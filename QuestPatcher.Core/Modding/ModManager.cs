using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using QuestPatcher.Core.Models;
using Serilog;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// Oversees the installation, uninstallation and storage of mods.
    /// </summary>
    public class ModManager
    {
        /// <summary>
        /// The currently loaded mods.
        /// </summary>
        public ObservableCollection<IMod> Mods { get; } = new();

        /// <summary>
        /// The currently loaded libraries.
        /// </summary>
        public ObservableCollection<IMod> Libraries { get; } = new();

        /// <summary>
        /// All mods and libraries.
        /// </summary>
        public List<IMod> AllMods => _modConfig?.Mods ?? EmptyModList;
        private static readonly List<IMod> EmptyModList = new();

        /// <summary>
        /// Path where QuestLoader mod files reside.
        /// </summary>
        public string ModsPath => $"/sdcard/Android/data/{_config.AppId}/files/mods/";

        /// <summary>
        /// Path where QuestLoader library files reside.
        /// </summary>
        public string LibsPath => $"/sdcard/Android/data/{_config.AppId}/files/libs/";

        /// <summary>
        /// Path where Scotland2 late mods reside.
        /// </summary>
        public string Sl2LateModsPath => $"/sdcard/ModData/{_config.AppId}/Modloader/mods/";

        /// <summary>
        /// Path where Scotland2 early mods reside.
        /// </summary>
        public string Sl2EarlyModsPath => $"/sdcard/ModData/{_config.AppId}/Modloader/early_mods/";

        /// <summary>
        /// Path where Scotland2 library files reside.
        /// </summary>
        public string Sl2LibsPath => $"/sdcard/ModData/{_config.AppId}/Modloader/libs/";

        /// <summary>
        /// Path where mods are extracted. A subdirectory should be created for each mod ID that needs extraction.
        /// </summary>
        public string ModsExtractPath => $"/sdcard/QuestPatcher/{_config.AppId}/installedMods/";

        /// <summary>
        /// The path to the config file where mod data is stored.
        /// </summary>
        private string ConfigPath => $"/sdcard/QuestPatcher/{_config.AppId}/modsStatus.json";

        private readonly Dictionary<string, IModProvider> _modProviders = new();
        private readonly ModConverter _modConverter = new();
        private readonly Config _config;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly OtherFilesManager _otherFilesManager;
        private readonly JsonSerializerOptions _configSerializationOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private ModConfig? _modConfig;
        private bool _awaitingConfigSave;


        public ModManager(Config config, AndroidDebugBridge debugBridge, OtherFilesManager otherFilesManager)
        {
            _config = config;
            _debugBridge = debugBridge;
            _configSerializationOptions.Converters.Add(_modConverter);
            _otherFilesManager = otherFilesManager;
        }

        /// <summary>
        /// Registers a mod provider. The provider will then be called upon to load mods as necessary.
        /// </summary>
        /// <param name="provider">The provider to register.</param>
        /// <exception cref="ArgumentException">If the provider added used a file extension</exception>
        public void RegisterModProvider(IModProvider provider)
        {
            string extension = NormalizeFileExtension(provider.FileExtension);
            if (_modProviders.ContainsKey(extension))
            {
                throw new ArgumentException(
                    $"Attempted to add provider for extension {extension}, however a provider for this extension already existed");
            }

            if (provider is ConfigModProvider configProvider)
            {
                _modConverter.RegisterProvider(configProvider);
            }

            _modProviders[extension] = provider;
        }

        /// <summary>
        /// Attempts to parse the mod in a particular file.
        /// </summary>
        /// <param name="path">The path to the mod. File extension is important a this is used as an early check to see if a mod is valid.</param>
        /// <param name="overrideExtension">Can be used if the file extension of <see cref="path"/> does not match the mod type.</param>
        /// <returns>The parsed mod, or null if no provider could load a mod with the given path.</returns>
        public async Task<IMod?> TryParseMod(string path, string? overrideExtension = null)
        {
            string extension = NormalizeFileExtension(overrideExtension ?? Path.GetExtension(path));

            if (_modProviders.TryGetValue(extension, out var provider))
            {
                return await provider.LoadFromFile(path);
            }

            return null;
        }

        /// <summary>
        /// Deletes a mod, removing all mod files and uninstalling it if it was installed.
        /// </summary>
        /// <param name="mod">The mod to delete.</param>
        /// <exception cref="InstallationException">If uninstalling the mod failed, prior to removal.</exception>
        public async Task DeleteMod(IMod mod)
        {
            await mod.Provider.DeleteMod(mod);
        }

        /// <summary>
        /// Clears all mods from all providers.
        /// Can be useful if the currently selected app changes.
        /// </summary>
        public void Reset()
        {
            Mods.Clear();
            Libraries.Clear();
            _modConfig = null;
            foreach (var provider in _modProviders.Values)
            {
                provider.ClearMods();
            }

            _awaitingConfigSave = false;
        }

        /// <summary>
        /// Creates the directories where mod files are copied to.
        /// + grants external storage permissions (useful on quest 3).
        /// </summary>
        public async Task CreateModDirectories()
        {
            const string Permissions = "777";

            var modDirectories = new List<string> { ModsPath, LibsPath, Sl2LibsPath, Sl2EarlyModsPath, Sl2LateModsPath, ModsExtractPath };

            await _debugBridge.CreateDirectories(modDirectories);

            try
            {
                await _debugBridge.RunShellCommand($"appops set --uid {_config.AppId} MANAGE_EXTERNAL_STORAGE allow");
            }
            catch (AdbException ex)
            {
                Log.Error(ex, "Failed to grant external storage permission: mods may not load!");
            }

            try
            {
                await _debugBridge.Chmod(modDirectories, Permissions);
            }
            catch (AdbException ex)
            {
                Log.Warning(ex, "Failed to chmod mod directories, they will be deleted and recreated, this will disable all QuestLoader mods.");
                var modsAndLibs = new List<string> { ModsPath, LibsPath };

                await _debugBridge.RemoveDirectory(ModsPath);
                await _debugBridge.RemoveDirectory(LibsPath);
                await _debugBridge.CreateDirectories(modsAndLibs);

                try
                {
                    await _debugBridge.Chmod(modsAndLibs, Permissions);
                }
                catch (AdbException ex2)
                {
                    Log.Error(ex2, "Failed to chmod mod directories on the second attempt! Mods will not load!");
                }
            }
        }

        /// <summary>
        /// Loads/registers the mods for the currently selected app.
        /// </summary>
        public async Task LoadModsForCurrentApp()
        {
            Log.Information("Loading mods . . .");
            await CreateModDirectories();

            // If a config file exists, we'll need to load our mods from it
            if (await _debugBridge.FileExists(ConfigPath))
            {
                Log.Debug("Loading mods from quest mod config");
                using TempFile configTemp = new();
                await _debugBridge.DownloadFile(ConfigPath, configTemp.Path);

                try
                {
                    await using Stream configStream = File.OpenRead(configTemp.Path);
                    var modConfig = await JsonSerializer.DeserializeAsync<ModConfig>(configStream, _configSerializationOptions);
                    if (modConfig != null)
                    {
                        modConfig.Mods.ForEach(ModLoadedCallback);
                        _modConfig = modConfig;
                        Log.Debug("{ModsCount} mods loaded", AllMods.Count);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load mods from quest config");
                }
            }
            else
            {
                Log.Debug("No mod status config found, attempting to load legacy mods");

                _modConfig = new();
                foreach (var provider in _modProviders.Values)
                {
                    await provider.LoadLegacyMods();
                }

                await SaveMods();
            }

            await UpdateModsStatus();
        }

        /// <summary>
        /// Checks every mod to see if it is installed.
        /// May be called after an operation that could affect the mod file directories, and so may change mod status.
        /// </summary>
        public async Task UpdateModsStatus()
        {
            Log.Information("Checking if mods are installed");
            foreach (var provider in _modProviders.Values)
            {
                await provider.LoadModsStatus();
            }
        }

        /// <summary>
        /// Saves the current registered mods that use a <see cref="ConfigModProvider"/> to the mod config file.
        /// </summary>
        public async Task SaveMods()
        {
            if (!_awaitingConfigSave)
            {
                return;
            }

            if (_modConfig is null)
            {
                Log.Warning("Could not save mods, mod config was null");
                return;
            }

            Log.Information("Saving {ModsCount} mods . . .", AllMods.Count);
            using TempFile configTemp = new();
            await using (Stream configStream = File.OpenWrite(configTemp.Path))
            {
                await JsonSerializer.SerializeAsync(configStream, _modConfig, _configSerializationOptions);
            }

            await _debugBridge.UploadFile(configTemp.Path, ConfigPath);
            _awaitingConfigSave = false;
        }

        /// <summary>
        /// Gets the path where a mod should be extracted to.
        /// </summary>
        /// <param name="id">The ID of the mod.</param>
        /// <returns>The full path to the extract location on the quest.</returns>
        internal string GetModExtractPath(string id)
        {
            return Path.Combine(ModsExtractPath, id);
        }

        /// <summary>
        /// Registers a mod with the mod manager.
        /// Should be called whenever a mod is loaded by a provider.
        /// </summary>
        /// <param name="mod">The mod to register.</param>
        internal void ModLoadedCallback(IMod mod)
        {
            (mod.IsLibrary ? Libraries : Mods).Add(mod);
            _modConfig?.Mods.Add(mod);
            foreach (var copyType in mod.FileCopyTypes)
            {
                _otherFilesManager.RegisterFileCopy(_config.AppId, copyType);
            }
            _awaitingConfigSave = true;
        }

        /// <summary>
        /// Unregisters a mod with the mod manager.
        /// Should be called whenever a mod is unloaded/deleted by a provider.
        /// </summary>
        /// <param name="mod">The mod to unregister.</param>
        internal void ModRemovedCallback(IMod mod)
        {
            (mod.IsLibrary ? Libraries : Mods).Remove(mod);
            foreach (var copyType in mod.FileCopyTypes)
            {
                _otherFilesManager.RemoveFileCopy(_config.AppId, copyType);
            }
            _modConfig?.Mods.Remove(mod);
            _awaitingConfigSave = true;
        }

        /// <summary>
        /// Makes a file extension lowercase and removes the leading period, if there is one.
        /// </summary>
        /// <param name="extension">The file extension to normalize.</param>
        /// <returns>The normalized file extension.</returns>
        private string NormalizeFileExtension(string extension)
        {
            string lower = extension.ToLower(); // Enforce lower case
            if (lower.StartsWith(".")) // Remove periods at the beginning
            {
                return lower.Substring(1);
            }
            return lower;
        }
    }
}
