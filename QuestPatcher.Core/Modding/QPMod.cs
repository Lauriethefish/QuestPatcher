using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using QuestPatcher.QMod;
using Serilog;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// A mod loaded from QMOD format.
    /// </summary>
    public class QPMod : IMod
    {
        public IModProvider Provider => _provider;

        private readonly QModProvider _provider;

        public string Id => Manifest.Id;
        public string Name => Manifest.Name;
        public string? Description => Manifest.Description;
        public SemanticVersioning.Version Version => Manifest.Version;
        public string? PackageVersion => Manifest.PackageVersion;
        public string Author => Manifest.Author;
        public string? Porter => Manifest.Porter;
        public bool IsLibrary => Manifest.IsLibrary;

        // Convert the QuestPatcher.QMod modloader to our own format, which supports an "Unknown" modloader in the case of an unrecognised modding tag.
        public Models.ModLoader ModLoader => Manifest.ModLoader switch
        {
            QMod.ModLoader.QuestLoader => Models.ModLoader.QuestLoader,
            QMod.ModLoader.Scotland2 => Models.ModLoader.Scotland2,
            _ => Models.ModLoader.Unknown
        };

        public IEnumerable<FileCopyType> FileCopyTypes { get; }

        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if (_isInstalled != value)
                {
                    _isInstalled = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        internal QModManifest Manifest { get; }


        private bool _isInstalled;

        private readonly AndroidDebugBridge _debugBridge;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly ModManager _modManager;

        public QPMod(QModProvider provider, QModManifest manifest, AndroidDebugBridge debugBridge, ExternalFilesDownloader filesDownloader, ModManager modManager)
        {
            _provider = provider;
            Manifest = manifest;
            _debugBridge = debugBridge;
            _filesDownloader = filesDownloader;
            _modManager = modManager;

            FileCopyTypes = manifest.CopyExtensions.Select(copyExt => new FileCopyType(debugBridge, new FileCopyInfo(
                $"{manifest.Name} .{copyExt.Extension} file",
                $"{manifest.Name} .{copyExt.Extension} files",
                copyExt.Destination,
                new List<string> { copyExt.Extension }
            ))).ToList();
        }

        public Task Install()
        {
            return InstallInternal(new List<string>());
        }

        public async Task Uninstall()
        {
            if (!IsInstalled)
            {
                Log.Debug("Mod {ModId} is already uninstalled. Not uninstalling", Id);
                return;
            }

            Log.Information("Uninstalling mod {ModId} . . .", Id);

            List<string> filesToRemove = new();
            // Remove mod SOs so that the mod will not load
            bool sl2 = ModLoader == Models.ModLoader.Scotland2;

            // When using questloader, early mods get put in the late mods directory (legacy)
            string modFilesPath = sl2 ? _modManager.Sl2EarlyModsPath : _modManager.ModsPath;
            foreach (string modFilePath in Manifest.ModFileNames)
            {
                Log.Information("Removing (early) mod file {EarlyModFilePath}", modFilePath);
                filesToRemove.Add(Path.Combine(modFilesPath, Path.GetFileName(modFilePath)));
            }

            // Remove late mods - SL2 only
            if (sl2)
            {
                foreach (string lateModFilePath in Manifest.LateModFileNames)
                {
                    Log.Information("Removing late mod file {LateModFilePath}", lateModFilePath);
                    filesToRemove.Add(Path.Combine(_modManager.Sl2LateModsPath, Path.GetFileName(lateModFilePath)));
                }
            }

            string libsPath = sl2 ? _modManager.Sl2LibsPath : _modManager.LibsPath;
            foreach (string libraryPath in Manifest.LibraryFileNames)
            {
                // Only remove libraries if they aren't used by another mod
                bool isUsedElsewhere = false;
                foreach (var otherMod in _provider.ModsById.Values)
                {
                    if (otherMod != this && otherMod.IsInstalled && otherMod.Manifest.LibraryFileNames.Contains(libraryPath))
                    {
                        Log.Information("Other mod {OtherModId} still needs lib file {LibraryFilePath}, not removing", otherMod.Id, libraryPath);
                        isUsedElsewhere = true;
                        break;
                    }
                }

                if (!isUsedElsewhere)
                {
                    Log.Information("Removing library file {LibraryFilePath}", libraryPath);
                    filesToRemove.Add(Path.Combine(libsPath, Path.GetFileName(libraryPath)));
                }
            }

            foreach (var fileCopy in Manifest.FileCopies)
            {
                Log.Information("Removing copied file {FileCopyDestination}", fileCopy.Destination);
                filesToRemove.Add(fileCopy.Destination);
            }

            try
            {
                await _debugBridge.DeleteFiles(filesToRemove);
            }
            catch (AdbException ex)
            {
                Log.Warning(ex, "Failed to delete some of the files to uninstall a mod. Were they manually deleted outside of QuestPatcher's knowledge?");
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
            if (Manifest.CoverImagePath == null)
            {
                return null;
            }

            try
            {
                string coverPath = Path.Combine(_provider.GetExtractDirectory(Id), Manifest.CoverImagePath);
                using TempFile tempFile = new();

                // TODO: Is loading into memory necessary here?
                await _debugBridge.DownloadFile(coverPath, tempFile.Path);
                return new MemoryStream(await File.ReadAllBytesAsync(tempFile.Path));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load cover image for {ModId}", Id);
                return null;
            }

        }

        /// <summary>
        /// Installs this mod, and any dependencies.
        /// </summary>
        /// <param name="installedInBranch">The mods that have been installed in the current operation. Used to detect and thwart cyclical dependencies</param>
        private async Task InstallInternal(List<string> installedInBranch)
        {
            if (IsInstalled)
            {
                Log.Debug("Mod {ModId} is already installed. Not installing", Id);
                return;
            }

            Log.Information("Installing mod {ModId}", Id);

            installedInBranch.Add(Id); // Add to the installed tree so that dependencies further down on us will trigger a recursive install error

            foreach (var dependency in Manifest.Dependencies)
            {
                await PrepareDependency(dependency, installedInBranch);
            }

            string extractPath = _provider.GetExtractDirectory(Id);

            // Copy files to actually install the mod

            List<KeyValuePair<string, string>> copyPaths = new();

            bool sl2 = ModLoader == Models.ModLoader.Scotland2;

            string libsPath = sl2 ? _modManager.Sl2LibsPath : _modManager.LibsPath;
            List<string> directoriesToCreate = new();
            foreach (string libraryPath in Manifest.LibraryFileNames)
            {
                Log.Information("Starting library file copy {LibraryFilePath} . . .", libraryPath);
                copyPaths.Add(new(Path.Combine(extractPath, libraryPath), Path.Combine(libsPath, Path.GetFileName(libraryPath))));
            }

            // When using sl2, (early) mod files are copied to early_mods
            // To support legacy mods, when QuestLoader is present in the APK, early mods will be written to the directory that now contains late mods.
            string modFilesPath = sl2 ? _modManager.Sl2EarlyModsPath : _modManager.ModsPath;
            foreach (string modPath in Manifest.ModFileNames)
            {
                Log.Information("Starting (early) mod file copy {EarlyModPath} . . .", modPath);
                copyPaths.Add(new(Path.Combine(extractPath, modPath), Path.Combine(modFilesPath, Path.GetFileName(modPath))));
            }

            if (sl2)
            {
                foreach (string lateModPath in Manifest.LateModFileNames)
                {
                    Log.Information("Starting late mod file copy {LateModPath} . . .", lateModPath);
                    copyPaths.Add(new(Path.Combine(extractPath, lateModPath), Path.Combine(_modManager.Sl2LateModsPath, Path.GetFileName(lateModPath))));
                }
            }

            foreach (var fileCopy in Manifest.FileCopies)
            {
                Log.Information("Starting file copy {FileCopyName} to {FileCopyDestination}", fileCopy.Name, fileCopy.Destination);
                string? directoryName = Path.GetDirectoryName(fileCopy.Destination);
                if (directoryName != null)
                {
                    directoriesToCreate.Add(directoryName);
                }
                copyPaths.Add(new(Path.Combine(extractPath, fileCopy.Name), fileCopy.Destination));
            }

            if (directoriesToCreate.Count > 0)
            {
                await _debugBridge.CreateDirectories(directoriesToCreate);
            }

            await _debugBridge.CopyFiles(copyPaths);

            var chmodPaths = copyPaths.AsEnumerable().Select(path => path.Value).ToList();
            await _debugBridge.Chmod(chmodPaths, "+r");

            IsInstalled = true;
            installedInBranch.Remove(Id);
        }

        /// <summary>
        /// Checks that a dependency is installed, and that the installed version is within the correct version range.
        /// If it's not installed, we will attempt to download the dependency if it specifies a download path, otherwise this fails.
        /// Does sanity checking for cyclical dependencies and will also attempt to upgrade installed versions via the download link where possible.
        /// </summary>
        /// <param name="dependency">The dependency to install.</param>
        /// <param name="installedInBranch">The number of mods that are currently downloading down this branch of the install "tree", used to check for cyclic dependencies.</param>
        /// <exception cref="InstallationException">If installing the dependency was not possible, or failed.</exception>
        private async Task PrepareDependency(Dependency dependency, List<string> installedInBranch)
        {
            Log.Debug("Preparing dependency of {DependencyId} version {DependencyVersionRange}", dependency.Id, dependency.VersionRange);
            int existingIndex = installedInBranch.FindIndex(downloadedDep => downloadedDep == dependency.Id);
            if (existingIndex != -1)
            {
                string dependMessage = "";
                for (int i = existingIndex; i < installedInBranch.Count; i++)
                {
                    dependMessage += $"{installedInBranch[i]} depends on ";
                }
                dependMessage += dependency.Id;

                throw new InstallationException($"Cylical dependency detected: {dependMessage}");
            }

            _provider.ModsById.TryGetValue(dependency.Id, out var existing);
            // Could be significantly simpler but I want to do lots of logging since this behaviour can be confusing
            if (existing != null)
            {
                if (dependency.VersionRange.IsSatisfied(existing.Version))
                {
                    Log.Debug("Dependency {DependencyVersionRange} is already loaded and within the version range", dependency.VersionRange);
                    if (!existing.IsInstalled)
                    {
                        Log.Information("Installing dependency {DependencyId} . . .", dependency.Id);
                        await existing.InstallInternal(installedInBranch);
                    }
                    return;
                }

                if (dependency.DownloadUrlString != null)
                {
                    Log.Warning("Dependency with ID {DependencyId} is already installed but with an incorrect version ({ExistingVersion} does not intersect {DependencyVersionRange}). QuestPatcher will attempt to upgrade the dependency", dependency.Id, existing.Version, dependency.VersionRange);
                }
                else
                {
                    throw new InstallationException($"Dependency with ID {dependency.Id} is already installed but with an incorrect version ({existing.Version} does not intersect {dependency.VersionRange}). Upgrading was not possible as there was no download link provided");
                }
            }
            else if (!dependency.Required)
            {
                // Dependency is optional so we can stop here since there is no existing install.
                return;
            }
            else if (dependency.DownloadUrlString == null)
            {
                throw new InstallationException($"Dependency {dependency.Id} is not installed, and the mod depending on it does not specify a download path if missing");
            }

            QPMod installedDependency;
            using (TempFile downloadFile = new())
            {
                Log.Information("Downloading dependency {DependencyId} . . .", dependency.Id);
                try
                {
                    await _filesDownloader.DownloadUri(dependency.DownloadUrlString, downloadFile.Path, dependency.Id);
                }
                catch (FileDownloadFailedException ex)
                {
                    // Print a nicer error message
                    throw new InstallationException($"Failed to download dependency from URL {dependency.DownloadIfMissing}: {ex.Message}", ex);
                }

                installedDependency = (QPMod) await _provider.LoadFromFile(downloadFile.Path);
            }

            await installedDependency.InstallInternal(installedInBranch);

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

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
