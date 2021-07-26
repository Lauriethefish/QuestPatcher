﻿using Quatcher.Core.Models;
using Quatcher.Core.Patching;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Quatcher.Core.Modding
{
    /// <summary>
    /// Handles installing and uninstalling mods and libraries.
    /// </summary>
    public class ModManager
    {
        /// <summary>
        /// Installed mods that are not libraries
        /// </summary>
        public ObservableCollection<Mod> Mods { get; } = new();

        /// <summary>
        /// Installed library mods
        /// </summary>
        public ObservableCollection<Mod> Libraries { get; } = new();

        private readonly Dictionary<string, Mod> _modsById = new(); // This is always updated to the contents of the above collections

        /// <summary>
        /// All installed mods, including libraries
        /// </summary>
        public Dictionary<string, Mod>.ValueCollection AllMods => _modsById.Values;

        private readonly Logger _logger;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly SpecialFolders _specialFolders;
        private readonly PatchingManager _patchingManager;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly Config _config;

        private string InstalledModsPath => $"/sdcard/Quatcher/{_config.AppId}/installedMods/";
        public string ModsPath => $"/sdcard/Android/data/{_config.AppId}/files/mods/";
        public string LibsPath => $"/sdcard/Android/data/{_config.AppId}/files/libs/";

        public ModManager(Logger logger, AndroidDebugBridge debugBridge, SpecialFolders specialFolders, PatchingManager patchingManager, Config config, ExternalFilesDownloader filesDownloader)
        {
            _logger = logger;
            _debugBridge = debugBridge;
            _specialFolders = specialFolders;
            _patchingManager = patchingManager;
            _config = config;
            _filesDownloader = filesDownloader;
        }

        public void OnReload()
        {
            Mods.Clear();
            Libraries.Clear();
            _modsById.Clear();
        }

        /// <summary>
        /// Downloads mod manifests from the quest directory and adds them as installed mods.
        /// </summary>
        public async Task LoadInstalledMods()
        {
            DateTime beforeStart = DateTime.UtcNow;
            _logger.Information("Loading mods . . .");

            await CreateModsDirectories();
            _logger.Debug("Listing files in mods, libs and installedMods . . .");
            Task<List<string>> modFoldersTask = _debugBridge.ListDirectoryFolders(InstalledModsPath);
            Task<List<string>> modFilesTask = _debugBridge.ListDirectoryFiles(ModsPath, true);
            Task<List<string>> libFilesTask = _debugBridge.ListDirectoryFiles(LibsPath, true);

            List<string> modFolders = await modFoldersTask;
            List<string> modFiles = await modFilesTask;
            List<string> libFiles = await libFilesTask;

            // Starting all the tasks then awaiting them makes this a fair bit faster
            _logger.Debug("Starting mod load tasks . . .");
            List<Task> loadTasks = new();
            foreach(string modFolder in modFolders)
            {
                loadTasks.Add(LoadInstalledMod(Path.Combine(modFolder, "mod.json"), modFiles, libFiles));
            }

            await Task.WhenAll(loadTasks);

            _logger.Information($"{AllMods.Count} mods loaded in {(DateTime.UtcNow - beforeStart).TotalMilliseconds}ms!");
        }

        /// <summary>
        /// Creates the mods, libs and installedMods folders for the current app if they do not already exist
        /// </summary>
        private async Task CreateModsDirectories()
        {
            await _debugBridge.CreateDirectories(new() { InstalledModsPath, ModsPath, LibsPath });
        }

        /// <summary>
        /// Quatcher used to just push the manifests of installs mods to /sdcard/Quatcher/installedMods, and didn't support mod toggling.
        /// With the system of pushing manifests, toggling cannot be supported since the mod files aren't available after the mod is turned off.
        /// While technically we could display old mods in UI as only uninstallable, with no togglging, this would be quite a lot of effort, and it isn't worth it.
        /// We could also detect old mods and libs that are installed and construct the full mod that way. However, the libs may be missing or overwritten with different versions (especially for unversioned libs) so this would be unreliable.
        /// File copies such as build in maps would also be deleted.
        /// 
        /// So instead, we just remove the old manifests and wipe the mod files to treat them as uninstalled.
        /// This works fine, and a UI window is shown to explain to users.
        /// </summary>
        /// <returns>True if old mods were found and deleted, false otherwise</returns>
        public async Task<bool> DetectAndRemoveOldMods()
        {
            await CreateModsDirectories();

            List<string> manifests = await _debugBridge.ListDirectoryFiles(InstalledModsPath);
            if(manifests.Count == 0) { return false; }

            _logger.Information("Upgrading from older Quatcher version! Removing old mods . . .");
            await _debugBridge.DeleteFiles(manifests);
            await _debugBridge.RemoveDirectory(ModsPath);
            await _debugBridge.RemoveDirectory(LibsPath);

            return true;
        }

        /// <summary>
        /// Attempts to load the manifest from the quest and will check that the mod files were actually copied correctly.
        /// Adds the mod to the installed list if so, does nothing if not.
        /// </summary>
        /// <param name="questPath">Path of the manifest on the quest</param>
        /// <param name="modFiles">Installed mod SOs</param>
        /// <param name="libFiles">Installed library SOs</param>
        private async Task LoadInstalledMod(string questPath, List<string> modFiles, List<string> libFiles)
        {
            try
            {
                Mod mod;
                using (TempFile localPath = _specialFolders.GetTempFile())
                {
                    // Download the manifest from the quest to local storage
                    _logger.Debug($"Downloading manifest {questPath} to {localPath} . . .");
                    await _debugBridge.DownloadFile(questPath, localPath.Path);

                    // Load the manifest
                    _logger.Debug($"Loading manifest from {localPath}");
                    await using Stream manifestStream = File.OpenRead(localPath.Path);
                    mod = Mod.Parse(manifestStream);
                }

                if (mod.CoverImagePath != null)
                {
                    _logger.Debug("Downloading cover image . . .");
                    string fullCoverImagePath = Path.Combine(GetExtractDirectory(mod), mod.CoverImagePath);
                    using TempFile localCoverFile = _specialFolders.GetTempFile();
                    
                    await _debugBridge.DownloadFile(fullCoverImagePath, localCoverFile.Path);
                    mod.CoverImage = await File.ReadAllBytesAsync(localCoverFile.Path);
                }

                // Next we need to check that the mod is actually installed correctly.
                // To do this, we check that each mod and lib file copied correctly.
                // This is useful to do, since otherwise mods would show as installed after an update, when the game folder was wiped so none of their files are actually copied.

                if (mod.IsInstalled)
                {
                    _logger.Debug("Mod is marked as installed. Checking that the install is correct . . .");
                    foreach (string modFile in mod.ModFiles)
                    {
                        if (!modFiles.Contains(modFile))
                        {
                            _logger.Debug($"Mod install not valid! Missing mod file {modFile}");
                            mod.IsInstalled = false;
                        }
                    }

                    foreach (string libraryFile in mod.LibraryFiles)
                    {
                        if (!libFiles.Contains(libraryFile))
                        {
                            _logger.Debug($"Mod install not valid! Missing library file {libraryFile}");
                            mod.IsInstalled = false;
                        }
                    }

                    if(!mod.IsInstalled)
                    {
                        await SaveManifest(mod); // Save that it isn't installed so that we don't check again next reload
                        _logger.Information($"{mod.Id} marked as disabled as the files are no longer copied.");
                    }
                }

                _logger.Information($"Mod {mod.Id} loaded");
                AddModToList(mod);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load manifest {questPath}: {ex}");
            }  
        }

        /// <summary>
        /// Checks to see if upgrading from the installed version to the new version is safe.
        /// i.e. this will throw an install exception if a mod depends on the older version being present.
        /// If upgrading is safe, this will uninstall the currently installed version to prepare for the version upgrade
        /// </summary>
        /// <param name="currentlyInstalled">The installed version of the mod</param>
        /// <param name="newVersion">The version of the mod to be upgraded to</param>
        private async Task PrepareVersionChange(Mod currentlyInstalled, Mod newVersion)
        {
            Debug.Assert(currentlyInstalled.Id == newVersion.Id);
            _logger.Information($"Attempting to upgrade {currentlyInstalled.Id} v{currentlyInstalled.Version} to {newVersion.Id} v{newVersion.Version}");

            bool didFailToMatch = false;

            StringBuilder errorBuilder = new();
            errorBuilder.AppendLine($"Failed to upgrade installation of mod {currentlyInstalled.Id} to {newVersion.Version}: ");
            foreach (Mod mod in AllMods)
            {

                foreach (Dependency dependency in mod.Dependencies)
                {
                    if (dependency.Id == currentlyInstalled.Id && !dependency.SemVersion.IsSatisfied(newVersion.Version))
                    {
                        string errorLine = $"Dependency of mod {mod.Id} requires version range {dependency.Version} of {currentlyInstalled.Id}, however the version of {currentlyInstalled.Id} being upgraded to ({newVersion.Version}) does not intersect this range";
                        errorBuilder.AppendLine(errorLine);
                        
                        _logger.Error(errorLine);
                        didFailToMatch = true;
                    }
                }
            }

            if(didFailToMatch)
            {
                throw new InstallationException(errorBuilder.ToString());
            }
            else
            {
                _logger.Information($"Deleting old version of {newVersion.Id} to prepare for upgrade . . .");
                await DeleteMod(currentlyInstalled);
            }
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
            _logger.Debug($"Preparing dependency of {dependency.Id} version {dependency.Version}");
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

            _modsById.TryGetValue(dependency.Id, out Mod? existing);
            // Could be significantly simpler but I want to do lots of logging since this behaviour can be confusing
            if (existing != null)
            {
                if (dependency.SemVersion.IsSatisfied(existing.SemVersion))
                {
                    _logger.Debug($"Dependency {dependency.Version} is already loaded and within the version range");
                    if(!existing.IsInstalled)
                    {
                        _logger.Information($"Installing dependency {dependency.Id} . . .");
                        await InstallMod(existing, new List<string>(installedInBranch));
                    }
                    return;
                }
                else if (dependency.DownloadIfMissing != null)
                {
                    _logger.Warning($"Dependency with ID {dependency.Id} is already installed but with an incorrect version ({existing.Version} does not intersect {dependency.Version}). Quatcher will attempt to upgrade the dependency");
                }
                else
                {
                    throw new InstallationException($"Dependency with ID {dependency.Id} is already installed but with an incorrect version ({existing.Version} does not intersect {dependency.Version}). Upgrading was not possible as there was no download link provided");
                }
            }
            else if (dependency.DownloadIfMissing == null)
            {
                throw new InstallationException($"Dependency {dependency.Id} is not installed, and the mod depending on it does not specify a download path if missing");
            }

            Mod installedDependency;
            using (TempFile downloadFile = _specialFolders.GetTempFile())
            {
                _logger.Information($"Downloading dependency {dependency.Id} . . .");
                try
                {
                    await _filesDownloader.DownloadUrl(dependency.DownloadIfMissing, downloadFile.Path, dependency.Id);
                }
                catch (WebException ex)
                {
                    // Print a nicer error message
                    throw new InstallationException($"Failed to download dependency from URL {dependency.DownloadIfMissing}: {ex.Message}", ex);
                }
                installedDependency = await LoadMod(downloadFile.Path);
            }

            await InstallMod(installedDependency, new List<string>(installedInBranch));

            // Sanity checks that the download link actually pointed to the right mod
            if (dependency.Id != installedDependency.Id)
            {
                await DeleteMod(installedDependency);
                throw new InstallationException($"Downloaded dependency had ID {installedDependency.Id}, whereas the dependency stated ID {dependency.Id}");
            }

            if (!dependency.SemVersion.IsSatisfied(installedDependency.SemVersion))
            {
                await DeleteMod(installedDependency);
                throw new InstallationException($"Downloaded dependency {installedDependency.Id} v{installedDependency.Version} was not within the version range stated in the dependency info ({dependency.Version})");
            }
        }

        /// <summary>
        /// Loads a mod from the input file and copies the files to the quest for later installing.
        /// Dependencies will not be installed just yet - that happens when the mod is actually installed.
        /// This could take a stream, however the mod has to be saved to file anyway to push to the quest, so there isn't much point.
        /// </summary>
        /// <param name="path">Path of the qmod to load</param>
        /// <returns></returns>
        public async Task<Mod> LoadMod(string path)
        {
            _logger.Information("Importing mod . . .");
            Mod mod;
            try
            {
                await using Stream modFileStream = File.OpenRead(path);
                using ZipArchive archive = new(modFileStream);
                ZipArchiveEntry? manifestEntry = archive.GetEntry("mod.json");
                if (manifestEntry == null)
                {
                    throw new InstallationException("Mod did not contain a mod.json manifest!");
                }

                await using (Stream stream = manifestEntry.Open())
                {
                    mod = Mod.Parse(stream);
                }

                // We could pull this from the quest after extracting (essentially sharing the code from loading the mods on startup), but this method is more efficient
                if(mod.CoverImagePath != null)
                {
                    ZipArchiveEntry? coverEntry = archive.GetEntry(mod.CoverImagePath);
                    if(coverEntry == null)
                    {
                        throw new InstallationException($"Mod specified cover image at {mod.CoverImagePath}, however this file was not found in the archive");
                    }

                    await using Stream coverStream = coverEntry.Open();
                    await using MemoryStream memoryStream = new();
                    await coverStream.CopyToAsync(memoryStream);

                    mod.CoverImage = memoryStream.ToArray();
                }

                // Check that the mod files in the manifest actually exist now, instead of just failing later when actually installing the mod
                // This is done since mod file copies are batched and the error messages aren't as clear
                _logger.Debug("Checking that mod contains the files stated in the manifest . . .");
                foreach(string modFile in mod.ModFiles)
                {
                    if(archive.GetEntry(modFile) == null) { throw new InstallationException($"Missing mod file {modFile} stated in the manifest"); }
                }

                foreach (string libFile in mod.LibraryFiles)
                {
                    if (archive.GetEntry(libFile) == null) { throw new InstallationException($"Missing library file {libFile} stated in the manifest"); }
                }

                foreach(FileCopy fileCopy in mod.FileCopies)
                {
                    if(archive.GetEntry(fileCopy.Name) == null) { throw new InstallationException($"Missing file copy origin file {fileCopy.Name} stated in the manifest"); }
                }
            }
            catch (InvalidDataException ex)
            {
                // Give this a more descriptive message than "End of central directory record could not be found"
                throw new InstallationException("Mod was not a valid ZIP archive. Please check that it fully downloaded", ex);
            }

            _logger.Debug("Manifest loaded.");

            // Check that the package ID is correct. We don't want people installing Beat Saber mods on Gorilla Tag!
            _logger.Information($"Mod ID: {mod.Id}, Version: {mod.Version}, Is Library: {mod.IsLibrary}");
            if (mod.PackageId != _config.AppId)
            {
                throw new InstallationException($"Mod is intended for app {mod.PackageId}, but {_config.AppId} is selected");
            }

            // Check if upgrading from a previous version is OK, or if we have to fail the import
            _modsById.TryGetValue(mod.Id, out Mod? existingInstall);
            if (existingInstall != null)
            {
                if (existingInstall.SemVersion == mod.SemVersion)
                {
                    _logger.Warning($"Version of existing {existingInstall.Id} is the same as the installing version ({mod.Version})");
                }
                if (existingInstall.SemVersion > mod.SemVersion)
                {
                    throw new InstallationException($"Version of existing {existingInstall.Id} ({existingInstall.Version}) is greater than installing version ({mod.Version}). Direct version downgrades are not permitted");
                }
                // Uninstall the existing mod. May throw an exception if other mods depend on the older version
                await PrepareVersionChange(existingInstall, mod);
            }

            string pushPath = Path.Combine(InstalledModsPath, $"{mod.Id}.temp");
            
            // Save the mod files to the quest for later installing
            _logger.Information("Pushing & extracting on to quest . . .");
            await _debugBridge.UploadFile(path, pushPath);
            await _debugBridge.ExtractArchive(pushPath, Path.Combine(InstalledModsPath, mod.Id));
            await _debugBridge.RemoveFile(pushPath);

            AddModToList(mod);
            _logger.Information("Import complete");
            return mod;
        }

        /// <summary>
        /// Removes the mod from the list and deletes the files from the quest.
        /// The mod should be uninstalled first.
        /// </summary>
        /// <param name="mod">The mod to unload</param>
        public async Task DeleteMod(Mod mod)
        {
            if(mod.IsInstalled)
            {
                _logger.Information($"Uninstalling mod {mod.Id} to prepare for removal . . .");
                await UninstallMod(mod);
            }
            _logger.Information($"Removing mod {mod.Id} . . .");

            RemoveModFromList(mod);
            await _debugBridge.RemoveDirectory(GetExtractDirectory(mod));

            if (!mod.IsLibrary)
            {
                await CleanUnusedLibraries(false);
            }
        }

        /// <summary>
        /// Installs the specified mod.
        /// The mod is extracted in order to push the files to the quest using ADB.
        /// Dependencies of the mod will be automatically downloaded if they specify a download path, otherwise this will fail.
        /// Makes sure that the mod isn't already installed with a newer version.
        /// </summary>
        /// <param name="mod">Mod to install</param>
        /// <param name="installedInBranch">The number of mods that are currently downloading down this branch of the install "tree", used to check for cyclic dependencies</param>
        public async Task InstallMod(Mod mod, List<string>? installedInBranch = null)
        {
            if (installedInBranch == null)
            {
                installedInBranch = new List<string>();
            }
            await CreateModsDirectories();

            _logger.Information($"Installing mod {mod.Id}");

            Debug.Assert(_patchingManager.InstalledApp != null); // We must be past the load app stage to install mods, so this is fine

            installedInBranch.Add(mod.Id); // Add to the installed tree so that dependencies further down on us will trigger a recursive install error

            foreach(Dependency dependency in mod.Dependencies)
            {
                await PrepareDependency(dependency, installedInBranch);
            }

            string extractPath = GetExtractDirectory(mod);

            // Copy files to actually install the mod

            List<KeyValuePair<string, string>> copyPaths = new();
            List<string> directoriesToCreate = new()
            {
                ModsPath,
                LibsPath
            };
            foreach(string libraryPath in mod.LibraryFiles)
            {
                _logger.Information($"Starting library file copy {libraryPath} . . .");
                copyPaths.Add(new(Path.Combine(extractPath, libraryPath), Path.Combine(LibsPath, Path.GetFileName(libraryPath))));
            }

            foreach(string modPath in mod.ModFiles)
            {
                _logger.Information($"Starting mod file copy {modPath} . . .");
                copyPaths.Add(new(Path.Combine(extractPath, modPath), Path.Combine(ModsPath, Path.GetFileName(modPath))));
            }

            foreach (FileCopy fileCopy in mod.FileCopies)
            {
                _logger.Information($"Starting file copy {fileCopy.Name} to {fileCopy.Destination}");
                string? directoryName = Path.GetDirectoryName(fileCopy.Destination);
                if(directoryName != null)
                {
                    directoriesToCreate.Add(directoryName);
                }
                copyPaths.Add(new(Path.Combine(extractPath, fileCopy.Name), fileCopy.Destination));
            }

            await _debugBridge.CreateDirectories(directoriesToCreate);
            await _debugBridge.CopyFiles(copyPaths);

            mod.IsInstalled = true;
            await SaveManifest(mod);

            _logger.Information("Done!");
        }

        /// <summary>
        /// Uninstalls the mod and clears unused libraries
        /// </summary>
        /// <param name="mod">The mod to uninstall</param>
        /// <param name="force">Whether to attempt an uninstall even if the mod is already uninstalled</param>
        public async Task UninstallMod(Mod mod, bool force = false)
        {
            if (!mod.IsInstalled && !force)
            {
                _logger.Debug($"Mod {mod.Id} is already uninstalled and we are not forcing the uninstall. Not uninstalling.");
                return;
            }
            
            _logger.Information($"Uninstalling mod {mod.Id} . . .");
            await CreateModsDirectories();

            List<string> filesToRemove = new();
            // Remove mod SOs so that the mod will not load
            foreach (string modFilePath in mod.ModFiles)
            {
                _logger.Information($"Removing mod file {modFilePath}");
                filesToRemove.Add(Path.Combine(ModsPath, Path.GetFileName(modFilePath)));
            }

            foreach (string libraryPath in mod.LibraryFiles)
            {
                // Only remove libraries if they aren't used by another mod
                bool isUsedElsewhere = false;
                foreach (Mod otherMod in AllMods)
                {
                    if (otherMod != mod && otherMod.IsInstalled && otherMod.LibraryFiles.Contains(libraryPath))
                    {
                        _logger.Information($"Other mod {otherMod.Id} still needs lib file {libraryPath}, not removing");
                        isUsedElsewhere = true;
                        break;
                    }
                }

                if (!isUsedElsewhere)
                {
                    _logger.Information("Removing library file " + libraryPath);
                    filesToRemove.Add(Path.Combine(LibsPath, Path.GetFileName(libraryPath)));
                }
            }

            foreach (FileCopy fileCopy in mod.FileCopies)
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
                _logger.Warning($"Failed to delete some of the files to uninstall a mod: {ex}. Were they manually deleted outside of Quatcher's knowledge?");
            }

            mod.IsInstalled = false;
            await SaveManifest(mod);

            if (!mod.IsLibrary)
            {
                // Only disable the unused libraries, don't completely remove them
                // This is to avoid redownloading dependencies if the mod is uninstalled then reinstalled without unloading
                await CleanUnusedLibraries(true);
            }

            _logger.Information("Done!");
        }

        /// <summary>
        /// Finds a list of mods which depend on this mod (i.e. ones with any dependency on this mod's ID)
        /// </summary>
        /// <param name="mod">The mod to check the dependant mods of</param>
        /// <param name="onlyInstalledMods">Whether to only include mods which are actually installed (enabled)</param>
        /// <returns>A list of all mods depending on the mod</returns>
        public List<Mod> FindModsDependingOn(Mod mod, bool onlyInstalledMods = false)
        {
            // Fun linq
            return AllMods.Where((otherMod) => otherMod.Dependencies.Where((dependency) => dependency.Id == mod.Id).Any() && (!onlyInstalledMods || otherMod.IsInstalled)).ToList();
        }

        private string GetExtractDirectory(Mod mod)
        {
            return Path.Combine(InstalledModsPath, mod.Id);
        }

        /// <summary>
        /// Saves the manifest of the mod to the quest.
        /// QP adds a field to the saved mods to identify if they're installed or uninstalled, so this saves that value.
        /// It could be stored in a separate file, but since we need to store the manifest on the quest anyway, we may as well put it in there.
        /// </summary>
        /// <param name="mod">The mod to save the manifest of</param>
        private async Task SaveManifest(Mod mod)
        {
            using TempFile stagingPath = _specialFolders.GetTempFile();
            await using (StreamWriter writer = new(stagingPath.Path))
            {
                mod.Save(writer);
            }

            string uploadPath = Path.Combine(GetExtractDirectory(mod), "mod.json");
            await _debugBridge.UploadFile(stagingPath.Path, uploadPath);
        }

        /// <summary>
        /// Uninstalls all libraries that are not depended on by another mod
        /// <param name="onlyDisable">Whether to only uninstall (disable) the libraries. If this is true, only mods that are enabled count as dependant mods as well</param>
        /// </summary>
        private async Task CleanUnusedLibraries(bool onlyDisable)
        {
            _logger.Information("Cleaning unused libraries . . .");

            bool actionPerformed = true;
            while (actionPerformed) // Keep attempting to remove libraries until none get removed this iteration
            {
                actionPerformed = false;
                List<Mod> unused = AllMods.Where((mod) => mod.IsLibrary && FindModsDependingOn(mod, onlyDisable).Count == 0).ToList();

                // Uninstall any unused libraries this iteration
                foreach (Mod mod in unused)
                {
                    if (mod.IsInstalled)
                    {
                        _logger.Information($"{mod.Id} is unused - uninstalling/unloading . . .");
                        actionPerformed = true;
                        await UninstallMod(mod);
                    }
                    if (!onlyDisable)
                    {
                        actionPerformed = true;
                        await DeleteMod(mod);
                    }
                }
            }
        }

        private void AddModToList(Mod mod)
        {
            if(mod.IsLibrary)
            {
                Libraries.Add(mod);
            }
            else
            {
                Mods.Add(mod);
            }
            _modsById[mod.Id] = mod;
        }

        private void RemoveModFromList(Mod mod)
        {
            if (mod.IsLibrary)
            {
                Libraries.Remove(mod);
            }
            else
            {
                Mods.Remove(mod);
            }
            _modsById.Remove(mod.Id);
        }
    }
}
