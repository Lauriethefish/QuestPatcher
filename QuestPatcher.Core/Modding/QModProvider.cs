using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using QuestPatcher.Core.Models;
using QuestPatcher.QMod;
using Serilog;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// The provider used to load/save QMOD files, the file format created for and preferred by QuestPatcher.
    /// </summary>
    public class QModProvider : ConfigModProvider
    {
        public override string ConfigSaveId => "qmod";

        public override string FileExtension => "qmod";

        /// <summary>
        /// The mods currently registered to this provider, sorted by mod ID.
        /// </summary>
        public Dictionary<string, QPMod> ModsById { get; } = new();

        private readonly ModManager _modManager;
        private readonly Config _config;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly ExternalFilesDownloader _filesDownloader;

        public QModProvider(ModManager modManager, Config config, AndroidDebugBridge debugBridge, ExternalFilesDownloader filesDownloader)
        {
            _modManager = modManager;
            _config = config;
            _debugBridge = debugBridge;
            _filesDownloader = filesDownloader;
        }

        public override async Task<IMod> LoadFromFile(string modPath)
        {
            await using Stream modStream = File.OpenRead(modPath);
            await using var qmod = await QMod.QMod.ParseAsync(modStream);

            // Check that the package ID is correct. We don't want people installing Beat Saber mods on Gorilla Tag!
            Log.Information("Mod ID: {ModId}, Version: {ModVersion}, Is Library: {IsLibrary}", qmod.Id, qmod.Version, qmod.IsLibrary);
            if (qmod.PackageId != null && qmod.PackageId != _config.AppId)
            {
                throw new InstallationException($"Mod is intended for app {qmod.PackageId}, but {_config.AppId} is selected");
            }

            var mod = new QPMod(this, qmod.GetManifest(), _debugBridge, _filesDownloader, _modManager);

            // Check if upgrading from a previous version is OK, or if we have to fail the import
            ModsById.TryGetValue(qmod.Id, out var existingInstall);
            bool needImmediateInstall = false;
            if (existingInstall != null)
            {
                if (existingInstall.Version == qmod.Version)
                {
                    Log.Warning("Version of existing {ModId} is the same as the installing version ({InstallingVersion})", existingInstall.Id, mod.Version);
                }
                if (existingInstall.Version > qmod.Version)
                {
                    throw new InstallationException($"Version of existing {existingInstall.Id} ({existingInstall.Version}) is greater than installing version ({mod.Version}). Direct version downgrades are not permitted");
                }
                // Uninstall the existing mod. May throw an exception if other mods depend on the older version
                needImmediateInstall = await PrepareVersionChange(existingInstall, mod);
            }

            string pushPath = Path.Combine("/data/local/tmp/", $"{qmod.Id}.temp.modextract");
            // Save the mod files to the quest for later installing
            Log.Information("Pushing & extracting on to quest . . .");
            await _debugBridge.UploadFile(modPath, pushPath);
            await _debugBridge.ExtractArchive(pushPath, GetExtractDirectory(qmod.Id));
            await _debugBridge.DeleteFile(pushPath);

            AddMod(mod);
            _modManager.ModLoadedCallback(mod);

            if (needImmediateInstall)
            {
                await mod.Install();
            }

            Log.Information("Import complete");
            return mod;
        }

        public override async Task DeleteMod(IMod genericMod)
        {
            var mod = AssertQMod(genericMod);

            if (mod.IsInstalled)
            {
                Log.Information("Uninstalling mod {ModId} to prepare for removal . . .", mod.Id);
                await genericMod.Uninstall();
            }

            Log.Information("Removing mod {ModId} . . .", mod.Id);
            await _debugBridge.RemoveDirectory(GetExtractDirectory(mod.Id));

            ModsById.Remove(mod.Id);
            _modManager.ModRemovedCallback(mod);

            if (!mod.Manifest.IsLibrary)
            {
                await CleanUnusedLibraries(false);
            }
        }

        /// <summary>
        /// Finds a list of mods which depend on this mod (i.e. ones with any dependency on this mod's ID)
        /// </summary>
        /// <param name="mod">The mod to check the dependant mods of</param>
        /// <param name="onlyInstalledMods">Whether to only include mods which are actually installed (enabled)</param>
        /// <returns>A list of all mods depending on the mod</returns>
        public List<QPMod> FindModsDependingOn(QPMod mod, bool onlyInstalledMods = false)
        {
            return ModsById.Values.Where(otherMod => otherMod.Manifest.Dependencies.Any(dependency => dependency.Id == mod.Id) && (!onlyInstalledMods || otherMod.IsInstalled)).ToList();
        }

        public override IMod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var manifest = JsonSerializer.Deserialize<QModManifest>(ref reader, options);
            if (manifest == null)
            {
                throw new NullReferenceException("Null manifest for mod");
            }
            var mod = new QPMod(this, manifest, _debugBridge, _filesDownloader, _modManager);

            AddMod(mod);
            return mod;
        }

        public override void Write(Utf8JsonWriter writer, IMod value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, AssertQMod(value).Manifest, options);
        }

        public override async Task LoadModsStatus()
        {
            var modFiles = await _debugBridge.ListDirectoryFiles(_modManager.ModsPath, true);
            var libFiles = await _debugBridge.ListDirectoryFiles(_modManager.LibsPath, true);
            var sl2EarlyModFiles = await _debugBridge.ListDirectoryFiles(_modManager.Sl2EarlyModsPath, true);
            var sl2LateModFiles = await _debugBridge.ListDirectoryFiles(_modManager.Sl2LateModsPath, true);
            var sl2LibFiles = await _debugBridge.ListDirectoryFiles(_modManager.Sl2LibsPath, true);

            foreach (var mod in ModsById.Values)
            {
                SetModStatus(mod, modFiles, libFiles, sl2EarlyModFiles, sl2LateModFiles, sl2LibFiles);
            }
        }

        public override void ClearMods()
        {
            ModsById.Clear();
        }

        public override async Task LoadLegacyMods()
        {
            var legacyFolders = await _debugBridge.ListDirectoryFolders(_modManager.ModsExtractPath);
            Log.Information("Attempting to load {LegacyModsCount} legacy mods", legacyFolders.Count);
            foreach (string legacyFolder in legacyFolders)
            {
                try
                {
                    Log.Debug("Loading legacy mod at {LegacyModPath}", legacyFolder);
                    string modJsonPath = Path.Combine(legacyFolder, "mod.json");
                    using var tmp = new TempFile();
                    await _debugBridge.DownloadFile(modJsonPath, tmp.Path);

                    await using var modJsonStream = File.OpenRead(tmp.Path);
                    var manifest = await QModManifest.ParseAsync(modJsonStream);
                    var mod = new QPMod(this, manifest, _debugBridge, _filesDownloader, _modManager);

                    AddMod(mod);
                    _modManager.ModLoadedCallback(mod);
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to parse legacy mod at {ModPath}. \nThe mod has been skipped and the folder will be deleted to avoid future issues.", legacyFolder);
                    await _debugBridge.RemoveDirectory(legacyFolder);
                    
                    Log.Debug(ex, "Mod load failure stack trace");
                }
            }
        }

        /// <summary>
        /// Uninstalls all libraries that are not depended on by another mod.
        /// <param name="onlyDisable">Whether to only uninstall (disable) the libraries. If this is true, only mods that are enabled count as dependant mods as well.</param>
        /// </summary>
        internal async Task CleanUnusedLibraries(bool onlyDisable)
        {
            bool actionPerformed = true;
            while (actionPerformed) // Keep attempting to remove libraries until none get removed this iteration
            {
                actionPerformed = false;
                var unused = ModsById.Values.Where(mod => mod.Manifest.IsLibrary && FindModsDependingOn(mod, onlyDisable).Count == 0).ToList();

                // Uninstall any unused libraries this iteration
                foreach (var mod in unused)
                {
                    try
                    {
                        if (mod.IsInstalled)
                        {
                            Log.Information("{ModId} is unused - " + (onlyDisable ? "uninstalling" : "unloading"), mod.Id);
                            actionPerformed = true;
                            await mod.Uninstall();
                        }
                        if (!onlyDisable)
                        {
                            actionPerformed = true;
                            await DeleteMod(mod);
                        }
                    }
                    catch (InstallationException ex)
                    {
                        Log.Warning(ex, "Failed to clean mod with ID {ModId}", mod.Id);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the location where a mod will be extracted.
        /// </summary>
        /// <param name="id">The ID of the mod.</param>
        /// <returns>The full path to where the mod will be extracted.</returns>
        internal string GetExtractDirectory(string id)
        {
            return _modManager.GetModExtractPath(id);
        }

        /// <summary>
        /// Adds a mod to <see cref="ModsById"/>.
        /// </summary>
        /// <param name="mod">The mod to add.</param>
        private void AddMod(QPMod mod)
        {
            ModsById[mod.Id] = mod;
        }

        /// <summary>
        /// Checks to see if upgrading from the installed version to the new version is safe.
        /// i.e. this will throw an install exception if a mod depends on the older version being present.
        /// If upgrading is safe, this will uninstall the currently installed version to prepare for the version upgrade.
        /// </summary>
        /// <param name="currentlyInstalled">The installed version of the mod.</param>
        /// <param name="newVersion">The version of the mod to be upgraded to.</param>
        /// <returns>True if the mod had installed dependants, and thus needs to be immediately installed.</returns>
        private async Task<bool> PrepareVersionChange(QPMod currentlyInstalled, QPMod newVersion)
        {
            Debug.Assert(currentlyInstalled.Id == newVersion.Id);
            Log.Information("Attempting to upgrade {ModId} v{CurrentVersion} to v{NewVersion}", currentlyInstalled.Id, currentlyInstalled.Version, newVersion.Version);

            bool didFailToMatch = false;
            StringBuilder errorBuilder = new();
            errorBuilder.AppendLine($"Failed to upgrade installation of mod {currentlyInstalled.Id} to {newVersion.Version}: ");
            bool installedDependants = false;
            foreach (var mod in ModsById.Values)
            {
                if (!mod.IsInstalled)
                {
                    continue;
                }

                foreach (var dependency in mod.Manifest.Dependencies)
                {
                    if (dependency.Id == currentlyInstalled.Id)
                    {
                        if (dependency.VersionRange.IsSatisfied(newVersion.Version))
                        {
                            installedDependants = true;
                        }
                        else
                        {
                            string errorLine = $"Dependency of mod {mod.Id} requires version range {dependency.VersionRange} of {currentlyInstalled.Id}, however the version of {currentlyInstalled.Id} being upgraded to ({newVersion.Version}) does not intersect this range";
                            errorBuilder.AppendLine(errorLine);

                            Log.Error(errorLine);
                            didFailToMatch = true;
                        }
                    }
                }
            }

            if (didFailToMatch)
            {
                throw new InstallationException(errorBuilder.ToString());
            }
            else
            {
                Log.Information("Deleting old version of {ModId} to prepare for upgrade . . .", newVersion.Id);
                await DeleteMod(currentlyInstalled);
                return installedDependants;
            }
        }

        private void SetModStatus(QPMod mod, List<string> modFiles, List<string> libFiles, List<string> sl2EarlyModFiles, List<string> sl2LateModFiles, List<string> sl2LibFiles)
        {
            bool hasAllMods;
            bool hasAllLibs;
            if (mod.ModLoader == Models.ModLoader.Scotland2)
            {
                // Check for both early and late mods if using SL2
                hasAllMods = mod.Manifest.ModFileNames.TrueForAll(sl2EarlyModFiles.Contains)
                    && mod.Manifest.LateModFileNames.TrueForAll(sl2LateModFiles.Contains);

                // Use the SL2 libs folder
                hasAllLibs = mod.Manifest.LibraryFileNames.TrueForAll(sl2LibFiles.Contains);
            }
            else
            {
                hasAllMods = mod.Manifest.ModFileNames.TrueForAll(modFiles.Contains);
                hasAllLibs = mod.Manifest.LibraryFileNames.TrueForAll(libFiles.Contains);
            }

            // TODO: Should we also check that file copies are present?
            // TODO: This would be more expensive as we would have to check the files in more directories
            // TODO: Should we check that the files in mods/libs actually match the ones within the mod?

            mod.IsInstalled = hasAllMods && hasAllLibs;
        }

        /// <summary>
        /// Checks that a mod is a QMOD.
        /// </summary>
        /// <param name="genericMod">The mod to cast.</param>
        /// <returns><paramref name="genericMod"/>, casted to a QMOD.</returns>
        /// <exception cref="ArgumentException">If <paramref name="genericMod"/> is not a QMOD.</exception>
        private QPMod AssertQMod(IMod genericMod)
        {
            if (genericMod is QPMod mod)
            {
                return mod;
            }
            else
            {
                throw new ArgumentException("Passed non-qmod to qmod provider function");
            }
        }
    }
}
