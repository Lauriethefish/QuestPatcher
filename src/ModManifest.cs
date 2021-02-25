
using System.Collections.Generic;
using System.Text.Json;

namespace QuestPatcher {
    public class ModManifest {
        public string _QPVersion { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }

        public string Author { get; set; }

        public string Version { get; set; }

        public string GameId { get; set; }
        public string GameVersion { get; set; }

        public string Type { get; set; }

        public List<string> ModFiles { get; set; } = new List<string>();
        public List<string> LibraryFiles { get; set; } = new List<string>();

        public static ModManifest Load(string str) {
            JsonSerializerOptions options = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<ModManifest>(str);
        }
    }
}