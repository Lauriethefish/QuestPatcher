using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using QuestPatcher.QMod;
using Serilog.Core;

namespace QuestPatcher.Core.Modding
{
    // ReSharper disable once InconsistentNaming
    public class QPMod : IMod
    {
        public IModProvider Provider => _provider;

        private readonly QModProvider _provider;

        public string Id => Manifest.Id;
        public string Name => Manifest.Name;
        public string? Description => Manifest.Description;
        public SemanticVersioning.Version Version => Manifest.Version;
        public string PackageVersion => Manifest.PackageVersion;
        public string Author => Manifest.Author;
        public string? Porter => Manifest.Porter;
        public bool IsLibrary => Manifest.IsLibrary;

        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if(_isInstalled != value)
                {
                    _isInstalled = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private bool _isInstalled;
        
        internal QModManifest Manifest { get; }
        private readonly AndroidDebugBridge _debugBridge;
        private readonly Logger _logger;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly ModManager _modManager;

        public event PropertyChangedEventHandler? PropertyChanged;

        public QPMod(QModProvider provider, QModManifest manifest, AndroidDebugBridge debugBridge, Logger logger, ExternalFilesDownloader filesDownloader, ModManager modManager)
        {
            _provider = provider;
            Manifest = manifest;
            _debugBridge = debugBridge;
            _logger = logger;
            _filesDownloader = filesDownloader;
            _modManager = modManager;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public Task Install()
        {
            return Install(new List<string>());
        }

        private async Task Install(List<string> installedInBranch)
        {
            if (IsInstalled)
            {
                _logger.Debug($"Mod {Id} is already installed. Not installing");
                return;
            }
            
            _logger.Information($"Installing mod {Id}");
            
            installedInBranch.Add(Id); // Add to the installed tree so that dependencies further down on us will trigger a recursive install error

            foreach(Dependency dependency in Manifest.Dependencies)
            {
                await PrepareDependency(dependency, installedInBranch);
            }

            string extractPath = _provider.GetExtractDirectory(Id);

            // Copy files to actually install the mod

            List<KeyValuePair<string, string>> copyPaths = new();
            List<string> directoriesToCreate = new();
            foreach(string libraryPath in Manifest.LibraryFileNames)
            {
                _logger.Information($"Starting library file copy {libraryPath} . . .");
                copyPaths.Add(new(Path.Combine(extractPath, libraryPath), Path.Combine(_modManager.LibsPath, Path.GetFileName(libraryPath))));
            }

            foreach(string modPath in Manifest.ModFileNames)
            {
                _logger.Information($"Starting mod file copy {modPath} . . .");
                copyPaths.Add(new(Path.Combine(extractPath, modPath), Path.Combine(_modManager.ModsPath, Path.GetFileName(modPath))));
            }

            foreach (FileCopy fileCopy in Manifest.FileCopies)
            {
                _logger.Information($"Starting file copy {fileCopy.Name} to {fileCopy.Destination}");
                string? directoryName = Path.GetDirectoryName(fileCopy.Destination);
                if(directoryName != null)
                {
                    directoriesToCreate.Add(directoryName);
                }
                copyPaths.Add(new(Path.Combine(extractPath, fileCopy.Name), fileCopy.Destination));
            }

            if(directoriesToCreate.Count > 0)
            {
                await _debugBridge.CreateDirectories(directoriesToCreate);
            }

            await _debugBridge.CopyFiles(copyPaths);
            IsInstalled = true;
            installedInBranch.Remove(Id);
        }

        public async Task Uninstall()
        {
            if (!IsInstalled)
            {
                _logger.Debug($"Mod {Id} is already uninstalled. Not uninstalling");
                return;
            }
            
            _logger.Information($"Uninstalling mod {Id} . . .");

            List<string> filesToRemove = new();
            // Remove mod SOs so that the mod will not load
            foreach (string modFilePath in Manifest.ModFileNames)
            {
                _logger.Information($"Removing mod file {modFilePath}");
                filesToRemove.Add(Path.Combine(_modManager.ModsPath, Path.GetFileName(modFilePath)));
            }

            foreach (string libraryPath in Manifest.LibraryFileNames)
            {
                // Only remove libraries if they aren't used by another mod
                bool isUsedElsewhere = false;
                foreach (QPMod otherMod in _provider.ModsById.Values)
                {
                    if (otherMod != this && otherMod.IsInstalled && otherMod.Manifest.LibraryFileNames.Contains(libraryPath))
                    {
                        _logger.Information($"Other mod {otherMod.Id} still needs lib file {libraryPath}, not removing");
                        isUsedElsewhere = true;
                        break;
                    }
                }

                if (!isUsedElsewhere)
                {
                    _logger.Information("Removing library file " + libraryPath);
                    filesToRemove.Add(Path.Combine(_modManager.LibsPath, Path.GetFileName(libraryPath)));
                }
            }

            foreach (FileCopy fileCopy in Manifest.FileCopies)
            {
                _logger.Information("Removing copied file " + fileCopy.Destination);
                filesToRemove.Add(fileCopy.Destination);
            }

            try
            {
                await _debugBridge.DeleteFiles(filesToRemove);
            }
            catch (AdbException ex)
            {
                _logger.Warning($"Failed to delete some of the files to uninstall a mod: {ex}. Were they manually deleted outside of QuestPatcher's knowledge?");
            }

            IsInstalled = false;

            if (!Manifest.IsLibrary)
            {
                // Only disable the unused libraries, don't completely remove them
                // This is to avoid redownloading dependencies if the mod is uninstalled then reinstalled without unloading
                await _provider.CleanUnusedLibraries(true);
            }
        }

        public async Task<Stream?> OpenCover()
        {
            if(Manifest.CoverImagePath == null)
            {
                return null;
            }
            
            string coverPath = Path.Combine(_provider.GetExtractDirectory(Id), Manifest.CoverImagePath);
            using TempFile tempFile = new();
            await _debugBridge.DownloadFile(coverPath, tempFile.Path);
            return new MemoryStream(await File.ReadAllBytesAsync(tempFile.Path));
        }

        /// <summary>
        /// Checks that a dependency is installed, and that the installed version is within the correct version range.
        /// If it's not installed, we will attempt to download the dependency if it specifies a download path, otherwise this fails.
        /// Does sanity checking for cyclical dependencies and will also attempt to upgrade installed versions via the download link where possible.
        /// </summary>
        /// <param name="dependency">The dependency to install</param>
        /// <param name="installedInBranch">The number of mods that are currently downloading down this branch of the install "tree", used to check for cyclic dependencies</param>
        private async Task PrepareDependency(Dependency dependency, List<string> installedInBranch)
        {
            _logger.Debug($"Preparing dependency of {dependency.Id} version {dependency.VersionRange}");
            int existingIndex = installedInBranch.FindIndex(downloadedDep => downloadedDep == dependency.Id);
            if (existingIndex != -1)
            {
                string dependMessage = "";
                for (int i = existingIndex; i < installedInBranch.Count; i++)
                {
                    dependMessage += $"{installedInBranch[i]} depends on ";
                }
                dependMessage += dependency.Id;

                throw new InstallationException($"Recursive dependency detected: {dependMessage}");
            }

            _provider.ModsById.TryGetValue(dependency.Id, out QPMod? existing);
            // Could be significantly simpler but I want to do lots of logging since this behaviour can be confusing
            if (existing != null)
            {
                if (dependency.VersionRange.IsSatisfied(existing.Version))
                {
                    _logger.Debug($"Dependency {dependency.VersionRange} is already loaded and within the version range");
                    if(!existing.IsInstalled)
                    {
                        _logger.Information($"Installing dependency {dependency.Id} . . .");
                        await existing.Install(installedInBranch);
                    }
                    return;
                }
                
                if (dependency.DownloadUrlString != null)
                {
                    _logger.Warning($"Dependency with ID {dependency.Id} is already installed but with an incorrect version ({existing.Version} does not intersect {dependency.VersionRange}). QuestPatcher will attempt to upgrade the dependency");
                }
                else
                {
                    throw new InstallationException($"Dependency with ID {dependency.Id} is already installed but with an incorrect version ({existing.Version} does not intersect {dependency.VersionRange}). Upgrading was not possible as there was no download link provided");
                }
            }
            else if (dependency.DownloadUrlString == null)
            {
                throw new InstallationException($"Dependency {dependency.Id} is not installed, and the mod depending on it does not specify a download path if missing");
            }

            QPMod installedDependency;
            using (TempFile downloadFile = new())
            {
                _logger.Information($"Downloading dependency {dependency.Id} . . .");
                try
                {
                    await _filesDownloader.DownloadUrl(dependency.DownloadUrlString, downloadFile.Path, dependency.Id);
                }
                catch (WebException ex)
                {
                    // Print a nicer error message
                    throw new InstallationException($"Failed to download dependency from URL {dependency.DownloadIfMissing}: {ex.Message}", ex);
                }

                installedDependency = (QPMod) await _provider.LoadFromFile(downloadFile.Path);
            }

            await installedDependency.Install(installedInBranch);

            // Sanity checks that the download link actually pointed to the right mod
            if (dependency.Id != installedDependency.Id)
            {
                await _provider.DeleteMod(installedDependency);
                throw new InstallationException($"Downloaded dependency had ID {installedDependency.Id}, whereas the dependency stated ID {dependency.Id}");
            }

            if (!dependency.VersionRange.IsSatisfied(installedDependency.Version))
            {
                await _provider.DeleteMod(installedDependency);
                throw new InstallationException($"Downloaded dependency {installedDependency.Id} v{installedDependency.Version} was not within the version range stated in the dependency info ({dependency.VersionRange})");
            }
        }
    }
}
