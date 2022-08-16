using Newtonsoft.Json;
using QuestPatcher.Core.Models;
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Serialization;
using Serilog;

namespace QuestPatcher.Core
{
    public class ConfigManager
    {
        private static readonly JsonSerializer Serializer = new()
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };
        
        public string ConfigPath { get; }

        private Config? _loadedConfig;
        private readonly string _legacyAppIdPath;

        public ConfigManager(SpecialFolders specialFolders)
        {
            ConfigPath = Path.Combine(specialFolders.DataFolder, "config.json");
            _legacyAppIdPath = Path.Combine(specialFolders.DataFolder, "appId.txt");
        }

        /// <summary>
        /// Gets the currently loaded config file, or loads the config file if none is loaded.
        /// Will attempt to recover corrupted configs by overwriting them with the default config file.
        /// </summary>
        /// <returns>The loaded config</returns>
        public Config GetOrLoadConfig()
        {
            if (_loadedConfig == null)
            {
                try
                {
                    SaveDefaultConfig(false);
                    _loadedConfig = LoadConfig();
                }
                catch (Exception ex)
                {
                    if (ex is FormatException or JsonException)
                    {
                        // Attempt to respond to config load errors by overwriting with the default config file
                        Log.Warning($"Failed to load the config file, overwriting with default config instead! ({ex})");
                        SaveDefaultConfig(true);
                        _loadedConfig = LoadConfig();
                        Log.Information("Overwriting with default config fixed the issue, continuing");
                    }
                    else
                    {
                        // Unknown errors just get rethrown an treated as an unhandled load error
                        throw;
                    }
                }
            }

            return _loadedConfig;
        }

        /// <summary>
        /// Saves the default config file if no config file currently exists.
        /// </summary>
        /// <param name="overwrite">Whether to overwrite the config even if an existing config file exists</param>
        /// <exception cref="NullReferenceException"></exception>
        private void SaveDefaultConfig(bool overwrite)
        {
            // If not forcing an overwrite of the config, and the config exists, we don't need to save the default config.
            if (File.Exists(ConfigPath) && !overwrite) { return; }
            
            Log.Debug("Saving default config file . . .");
            using Stream? resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuestPatcher.Core.Resources.default-config.json");
            if(resourceStream == null)
            {
                throw new NullReferenceException("Unable to find default-config.json in resources!");
            }

            using FileStream destStream = new(ConfigPath, FileMode.Create, FileAccess.Write);
            resourceStream.CopyTo(destStream);
        }

        /// <summary>
        /// Loads the config from disk.
        /// </summary>
        /// <returns>The loaded config</returns>
        /// <exception cref="FormatException">If the loaded config did not contain a config object, i.e. it was empty</exception>
        private Config LoadConfig()
        {
            Log.Information("Loading config . . .");
            
            // Load the config
            using StreamReader streamReader = new(ConfigPath);
            using JsonTextReader reader = new(streamReader);
            Config? newConfig = Serializer.Deserialize<Config>(reader);
            if (newConfig == null)
            {
                throw new FormatException("Loaded config contained no config object");
            }
            
            // In the past, an appId.txt file was used to store the app ID
            // Load this into the config, then delete the old file
            if (File.Exists(_legacyAppIdPath))
            {
                Log.Information("Loading app ID from legacy appId.txt");
                newConfig.AppId = File.ReadAllText(_legacyAppIdPath);
                File.Delete(_legacyAppIdPath);
                SaveConfig();
            }

            return newConfig;
        }

        /// <summary>
        /// Saves the loaded config file.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the config has not been loaded yet</exception>
        public void SaveConfig()
        {
            if (_loadedConfig == null) { throw new InvalidOperationException("Cannot save the config as it has not been loaded yet"); }
            
            Log.Information("Saving config file . . .");
            using StreamWriter streamWriter = new(ConfigPath);
            using JsonTextWriter writer = new(streamWriter);
            Serializer.Serialize(writer, _loadedConfig);
        }
    }
}
