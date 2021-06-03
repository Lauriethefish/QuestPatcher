using Newtonsoft.Json;
using QuestPatcher.Core.Models;
using Serilog.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace QuestPatcher.Core
{
    public class ConfigManager
    {
        private static readonly JsonSerializer Serializer = new();

        public Config Config
        {
            get
            {
                if(_config == null)
                {
                    LoadConfig();
                }
                Debug.Assert(_config != null);
                return _config;
            }
        }

        private Config? _config;
        private readonly Logger _logger;
        private readonly string _configPath;
        private readonly string _legacyAppIdPath;

        public ConfigManager(Logger logger, SpecialFolders specialFolders)
        {
            _logger = logger;
            _configPath = Path.Combine(specialFolders.DataFolder, "config.json");
            _legacyAppIdPath = Path.Combine(specialFolders.DataFolder, "appId.txt");
        }

        private void LoadConfig()
        {
            _logger.Debug("Loading config . . .");
            // Save the default config file from resources if a config was not found
            if (!File.Exists(_configPath))
            {
                _logger.Debug("Saving default config file . . .");
                Stream? resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuestPatcher.Core.Resources.default-config.json");
                if(resourceStream == null)
                {
                    throw new NullReferenceException("Unable to find default-config.json in resources!");
                }

                FileStream destStream = new(_configPath, FileMode.Create, FileAccess.Write);

                resourceStream.CopyTo(destStream);
                destStream.Close();
                resourceStream.Close();
            }

            // Load the config
            using (StreamReader streamReader = new(_configPath))
            using (JsonTextReader reader = new(streamReader)) {
                _config = Serializer.Deserialize<Config>(reader);
            }

            // In the past, an appId.txt file was used to store the app ID
            // Load this into the config, then delete the old file
            if (File.Exists(_legacyAppIdPath))
            {
                _logger.Information("Loading app ID from legacy appId.txt");
                Config.AppId = File.ReadAllText(_legacyAppIdPath);
                File.Delete(_legacyAppIdPath);
                SaveConfig();
            }
        }

        public void SaveConfig()
        {
            _logger.Information("Saving config file . . .");
            using StreamWriter streamWriter = new(_configPath);
            using JsonTextWriter writer = new(streamWriter);
            Serializer.Serialize(writer, _config);
        }
    }
}
