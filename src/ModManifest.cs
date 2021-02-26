
using System.Collections.Generic;
using System.Text.Json;

namespace QuestPatcher {
    public class DependencyInfo {
        public string Id { get; set; }

        public string Version { get; set; }

        public string? DownloadPath { get; set; } = null;
    }

    public class ModManifest {
        public string _QPVersion { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }

        public string Author { get; set; }

        public string Version { get; set; }

        public string GameId { get; set; }
        public string GameVersion { get; set; }

        public string Type { get; set; }

        public bool AllowMultipleInstalls { get; set; } = false; // If true, multiple different versions of this mod ID can be installed.

        public List<string> ModFiles { get; set; } = new List<string>();
        public List<string> LibraryFiles { get; set; } = new List<string>();

        public List<DependencyInfo> Dependencies { get; set; } = new List<DependencyInfo>();

        public static ModManifest Load(string str) {
            JsonSerializerOptions options = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<ModManifest>(str, options);
        }
    }
}