using System.IO;
using System.Reflection;
using Serilog.Core;
using System.Text.Json;
using System.Threading.Tasks;

namespace QuestPatcher
{
    public class ConfigManager
    {
        private static readonly JsonSerializerOptions JSON_OPTIONS = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, // Allow appId instead of AppId in the config
            WriteIndented = true // Indent the config for manual editing
        };


        private Logger logger;
        private string CONFIG_PATH;
        private string enclosingFolder;

        public Config Config { get; private set; }
        public bool IsLoaded { get; private set; } = false;

        public ConfigManager(Logger logger, string enclosingFolder)
        {
            this.logger = logger;

            CONFIG_PATH = Path.Combine(enclosingFolder, "config.json");
            this.enclosingFolder = enclosingFolder;
        }

        public async Task LoadConfig()
        {
            logger.Debug("Loading config . . .");
            // Save the default config file from resources if a config was not found
            if (!File.Exists(CONFIG_PATH))
            {
                logger.Debug("Saving default config file . . .");
                Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuestPatcher.resources.default-config.json");
                FileStream destStream = new FileStream(CONFIG_PATH, FileMode.Create, FileAccess.Write);

                await resourceStream.CopyToAsync(destStream);
                destStream.Close();
                resourceStream.Close();
            }

            // Load the config asyncronously
            FileStream configStream = File.OpenRead(CONFIG_PATH);
            Config = await JsonSerializer.DeserializeAsync<Config>(configStream, JSON_OPTIONS);
            configStream.Close();

            // In the past, an appId.txt file was used to store the app ID
            // Load this into the config, then delete the old file
            string legacyAppIdPath = Path.Combine(enclosingFolder, "appId.txt");
            if(File.Exists(legacyAppIdPath)) {
                logger.Information("Loading app ID from legacy appId.txt");
                Config.AppId = await File.ReadAllTextAsync(legacyAppIdPath);
                File.Delete(legacyAppIdPath);
                await SaveConfig();
            }

            IsLoaded = true;
        }

        public async Task SaveConfig()
        {
            // Save the config asyncronously
            logger.Information("Saving config file . . .");
            FileStream configStream = File.Open(CONFIG_PATH, FileMode.Create);
            await JsonSerializer.SerializeAsync(configStream, Config, JSON_OPTIONS);
            configStream.Close();
        }
    }

    public class Config
    {
        public string AppId { get; set; } // Default is gorilla tag
    }
}
