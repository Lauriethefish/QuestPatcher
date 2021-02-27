
using Avalonia.Controls;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json;

namespace QuestPatcher {
    public class DependencyInfo {
        public string Id { get; set; }

        public string Version { get; set; }

        public string? DownloadIfMissing { get; set; } = null;
    }

    public class ModManifest {
        public string _QPVersion { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }

        public string Author { get; set; }

        public string Version { get; set; }

        public string GameId { get; set; }
        public string GameVersion { get; set; }

        public bool IsLibrary { get; set; }

        public List<string> ModFiles { get; set; } = new List<string>();
        public List<string> LibraryFiles { get; set; } = new List<string>();

        public List<DependencyInfo> Dependencies { get; set; } = new List<DependencyInfo>();

        public Control GuiElement { get; set; } // Used for removing this mod from the gui

        public bool DependsOn(string otherId)
        {
            foreach(DependencyInfo dependency in Dependencies)
            {
                if(dependency.Id == otherId)
                {
                    return true;
                }
            }

            return false;
        }

        public static ModManifest Load(string str) {
            JsonSerializerOptions options = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<ModManifest>(str, options);
        }
    }
}