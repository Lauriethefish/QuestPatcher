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
using Serilog.Core;

namespace QuestPatcher.Core.Modding
{
    public class QModProvider : ConfigModProvider
    {
        public override string ConfigSaveId => "qmod";

        public override string FileExtension => "qmod";

        public Dictionary<string, QPMod> ModsById { get; } = new();

        private readonly ModManager _modManager;
        private readonly Config _config;
        private readonly Logger _logger;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly ExternalFilesDownloader _filesDownloader;

        public QModProvider(ModManager modManager, Config config, Logger logger, AndroidDebugBridge debugBridge, ExternalFilesDownloader filesDownloader)
        {
            _modManager = modManager;
            _config = config;
            _logger = logger;
            _debugBridge = debugBridge;
            _filesDownloader = filesDownloader;
        }
        
        internal string GetExtractDirectory(string id)
        {
            return _modManager.GetModExtractPath(id);
        }

        private void AddMod(QPMod mod)
        {
            ModsById[mod.Id] = mod;
        }
        
        public override async Task<IMod> LoadFromFile(string modPath)
        {
            await using Stream modStream = File.OpenRead(modPath);
            await using QMod.QMod qmod = await QMod.QMod.ParseAsync(modStream);
            
            // Check that the package ID is correct. We don't want people installing Beat Saber mods on Gorilla Tag!
            _logger.Information($"Mod ID: {qmod.Id}, Version: {qmod.Version}, Is Library: {qmod.IsLibrary}");
            if (qmod.PackageId != _config.AppId)
            {
                throw new InstallationException($"Mod is intended for app {qmod.PackageId}, but {_config.AppId} is selected");
            }
            
            QPMod mod = new(this, qmod.GetManifest(), _debugBridge, _logger, _filesDownloader, _modManager);

            // Check if upgrading from a previous version is OK, or if we have to fail the import
            ModsById.TryGetValue(qmod.Id, out QPMod? existingInstall);
            if (existingInstall != null)
            {
                if (existingInstall.Version == qmod.Version)
                {
                    _logger.Warning($"Version of existing {existingInstall.Id} is the same as the installing version ({mod.Version})");
                }
                if (existingInstall.Version > qmod.Version)
                {
                    throw new InstallationException($"Version of existing {existingInstall.Id} ({existingInstall.Version}) is greater than installing version ({mod.Version}). Direct version downgrades are not permitted");
                }
                // Uninstall the existing mod. May throw an exception if other mods depend on the older version
                await PrepareVersionChange(existingInstall, mod);
            }
            
            string pushPath = Path.Combine("/data/local/tmp/", $"{qmod.Id}.temp.modextract");
            // Save the mod files to the quest for later installing
            _logger.Information("Pushing & extracting on to quest . . .");
            await _debugBridge.UploadFile(modPath, pushPath);
            await _debugBridge.ExtractArchive(pushPath, GetExtractDirectory(qmod.Id));
            await _debugBridge.RemoveFile(pushPath);

            AddMod(mod);
            _modManager.ModLoadedCallback(mod);

            _logger.Information("Import complete");
            return mod;
        }
        
        /// <summary>
        /// Checks to see if upgrading from the installed version to the new version is safe.
        /// i.e. this will throw an install exception if a mod depends on the older version being present.
        /// If upgrading is safe, this will uninstall the currently installed version to prepare for the version upgrade
        /// </summary>
        /// <param name="currentlyInstalled">The installed version of the mod</param>
        /// <param name="newVersion">The version of the mod to be upgraded to</param>
        private async Task PrepareVersionChange(QPMod currentlyInstalled, QPMod newVersion)
        {
            Debug.Assert(currentlyInstalled.Id == newVersion.Id);
            _logger.Information($"Attempting to upgrade {currentlyInstalled.Id} v{currentlyInstalled.Version} to {newVersion.Id} v{newVersion.Version}");

            bool didFailToMatch = false;

            StringBuilder errorBuilder = new();
            errorBuilder.AppendLine($"Failed to upgrade installation of mod {currentlyInstalled.Id} to {newVersion.Version}: ");
            foreach (QPMod mod in ModsById.Values)
            {

                foreach (Dependency dependency in mod.Manifest.Dependencies)
                {
                    if (dependency.Id == currentlyInstalled.Id && !dependency.VersionRange.IsSatisfied(newVersion.Version))
                    {
                        string errorLine = $"Dependency of mod {mod.Id} requires version range {dependency.VersionRange} of {currentlyInstalled.Id}, however the version of {currentlyInstalled.Id} being upgraded to ({newVersion.Version}) does not intersect this range";
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

        private QPMod AssertQMod(IMod genericMod)
        {
            if(genericMod is QPMod mod)
            {
                return mod;
            }
            else
            {
                throw new InvalidOperationException("Passed non-qmod to qmod provider function");
            }
        }

        public override async Task DeleteMod(IMod genericMod)
        {
            QPMod mod = AssertQMod(genericMod);
            
            if(mod.IsInstalled)
            {
                _logger.Information($"Uninstalling mod {mod.Id} to prepare for removal . . .");
                await genericMod.Uninstall();
            }
            
            _logger.Information($"Removing mod {mod.Id} . . .");
            await _debugBridge.RemoveDirectory(GetExtractDirectory(mod.Id));

            ModsById.Remove(mod.Id);
            _modManager.ModRemovedCallback(mod);
            
            if(!mod.Manifest.IsLibrary)
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
            // Fun linq
            return ModsById.Values.Where(otherMod => otherMod.Manifest.Dependencies.Any(dependency => dependency.Id == mod.Id) && (!onlyInstalledMods || otherMod.IsInstalled)).ToList();
        }

        /// <summary>
        /// Uninstalls all libraries that are not depended on by another mod
        /// <param name="onlyDisable">Whether to only uninstall (disable) the libraries. If this is true, only mods that are enabled count as dependant mods as well</param>
        /// </summary>
        internal async Task CleanUnusedLibraries(bool onlyDisable)
        {
            bool actionPerformed = true;
            while (actionPerformed) // Keep attempting to remove libraries until none get removed this iteration
            {
                actionPerformed = false;
                List<QPMod> unused = ModsById.Values.Where(mod => mod.Manifest.IsLibrary && FindModsDependingOn(mod, onlyDisable).Count == 0).ToList();

                // Uninstall any unused libraries this iteration
                foreach (QPMod mod in unused)
                {
                    if (mod.IsInstalled)
                    {
                        _logger.Information($"{mod.Id} is unused - " + (onlyDisable ? "uninstalling" : "unloading"));
                        actionPerformed = true;
                        await mod.Uninstall();
                    }
                    if (!onlyDisable)
                    {
                        actionPerformed = true;
                        await DeleteMod(mod);
                    }
                }
            }
        }
        
        public override IMod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            QModManifest? manifest = JsonSerializer.Deserialize<QModManifest>(ref reader, options);
            if(manifest == null)
            {
                throw new NullReferenceException("Null manifest for mod");
            }
            QPMod mod = new(this, manifest, _debugBridge, _logger, _filesDownloader, _modManager);
            
            AddMod(mod);
            return mod;
        }

        public override void Write(Utf8JsonWriter writer, IMod value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, AssertQMod(value).Manifest, options);
        }

        public override async Task LoadMods()
        {
            List<string> modFiles = await _debugBridge.ListDirectoryFiles(_modManager.ModsPath, true);
            List<string> libFiles = await _debugBridge.ListDirectoryFiles(_modManager.LibsPath, true);

            foreach(QPMod mod in ModsById.Values)
            {
                SetModStatus(mod, modFiles, libFiles);
            }
        }

        private void SetModStatus(QPMod mod, List<string> modFiles, List<string> libFiles)
        {
            bool hasAllMods = mod.Manifest.ModFileNames.TrueForAll(modFiles.Contains);
            bool hasAllLibs = mod.Manifest.LibraryFileNames.TrueForAll(libFiles.Contains);
            // TODO: Should we also check that file copies are present?
            // TODO: This would be more expensive as we would have to check the files in more directories
            // TODO: Should we check that the files in mods/libs actually match the ones within the mod?
            
            mod.IsInstalled = hasAllMods && hasAllLibs;
        }

        public override void ClearMods()
        {
            ModsById.Clear();
        }
    }
}
