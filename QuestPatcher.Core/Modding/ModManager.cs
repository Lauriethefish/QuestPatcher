using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using QuestPatcher.Core.Models;
using Serilog.Core;

namespace QuestPatcher.Core.Modding
{
    // TODO: Upgrade from old mod system
    // TODO: And old old mod system notice
    public class ModManager
    {
        public ObservableCollection<IMod> Mods { get; } = new();
        public ObservableCollection<IMod> Libraries { get; } = new();

        public List<IMod> AllMods => _modConfig?.Mods ?? EmptyModList;
        private static readonly List<IMod> EmptyModList = new();

        public string ModsPath => $"/sdcard/Android/data/{_config.AppId}/files/mods/";
        public string LibsPath => $"/sdcard/Android/data/{_config.AppId}/files/libs/";
        
        private string ConfigPath => $"/sdcard/QuestPatcher/{_config.AppId}/modsStatus.json";
        private string ModsExtractPath => $"/sdcard/QuestPatcher/{_config.AppId}/installedMods/";
        
        private readonly Dictionary<string, IModProvider> _modProviders = new();
        private readonly ModConverter _modConverter = new();
        private readonly Config _config;
        private readonly Logger _logger;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly JsonSerializerOptions _configSerializationOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private ModConfig? _modConfig;
        private bool _awaitingConfigSave;

        public ModManager(Config config, AndroidDebugBridge debugBridge, Logger logger)
        {
            _config = config;
            _debugBridge = debugBridge;
            _logger = logger;
            _configSerializationOptions.Converters.Add(_modConverter);
        }

        private string NormalizeFileExtension(string extension)
        {
            string lower = extension.ToLower(); // Enforce lower case
            if(lower.StartsWith(".")) // Remove periods at the beginning
            {
                return lower.Substring(1);
            }
            return lower;
        }

        public string GetModExtractPath(string id)
        {
            return Path.Combine(ModsExtractPath, id);
        }

        public void RegisterModProvider(IModProvider provider)
        {
            string extension = NormalizeFileExtension(provider.FileExtension);
            if(_modProviders.ContainsKey(extension))
            {
                throw new InvalidOperationException(
                    $"Attempted to add provider for extension {extension}, however a provider for this extension already existed");
            }

            if(provider is ConfigModProvider configProvider)
            {
                _modConverter.RegisterProvider(configProvider);
            }

            _modProviders[extension] = provider;
        }

        public async Task<IMod?> TryParseMod(string path)
        {
            string extension = NormalizeFileExtension(Path.GetExtension(path));

            if(_modProviders.TryGetValue(extension, out IModProvider? provider))
            {
                return await provider.LoadFromFile(path);
            }

            return null;
        }

        public async Task DeleteMod(IMod mod)
        {
            await mod.Provider.DeleteMod(mod);
        }

        public void Reset()
        {
            Mods.Clear();
            Libraries.Clear();
            _modConfig = null;
            foreach (IModProvider provider in _modProviders.Values)
            {
                provider.ClearMods();   
            }

            _awaitingConfigSave = false;
        }

        public async Task LoadModsForCurrentApp()
        {
            _logger.Information("Loading mods . . .");
            await _debugBridge.CreateDirectories(new List<string> {ModsPath, LibsPath, ModsExtractPath});

            // If a config file exists, we'll need to load our mods from it
            if(await _debugBridge.FileExists(ConfigPath))
            {
                _logger.Debug("Loading mods from quest mod config");
                using TempFile configTemp = new();
                await _debugBridge.DownloadFile(ConfigPath, configTemp.Path);

                try
                {
                    await using Stream configStream = File.OpenRead(configTemp.Path);
                    ModConfig? modConfig = await JsonSerializer.DeserializeAsync<ModConfig>(configStream, _configSerializationOptions);
                    if(modConfig != null)
                    {
                        modConfig.Mods.ForEach(ModLoadedCallback);
                        _modConfig = modConfig;
                        _logger.Debug($"{AllMods.Count()} mods loaded");
                    }
                }
                catch(Exception ex)
                {
                    _logger.Warning($"Failed to load mods from quest config: {ex}");
                }
            }
            else
            {
                _logger.Debug("No mod status config found, defaulting to no mods");
            }

            _modConfig ??= new();
            
            foreach(IModProvider provider in _modProviders.Values)
            {
                await provider.LoadMods();
            }
        }

        public async Task SaveMods()
        {
            if(!_awaitingConfigSave)
            {
                return;
            }
            
            if(_modConfig is null)
            {
                _logger.Warning("Could not save mods, mod config was null");
                return;
            }
            
            _logger.Information($"Saving {AllMods.Count} mods . . .");
            using TempFile configTemp = new();
            await using(Stream configStream = File.OpenWrite(configTemp.Path))
            {
                await JsonSerializer.SerializeAsync(configStream, _modConfig, _configSerializationOptions);
            }

            await _debugBridge.UploadFile(configTemp.Path, ConfigPath);
            _awaitingConfigSave = false;
        }
        
        internal void ModLoadedCallback(IMod mod)
        {
            (mod.IsLibrary ? Libraries : Mods).Add(mod);
            _modConfig?.Mods.Add(mod);
            _awaitingConfigSave = true;
        }
        
        internal void ModRemovedCallback(IMod mod)
        {
            (mod.IsLibrary ? Libraries : Mods).Remove(mod);
            _modConfig?.Mods.Remove(mod);
            _awaitingConfigSave = true;
        }
    }
}
