
using System.Collections.Generic;

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

        public List<string> ModFiles { get; set; }
        public List<string> LibraryFiles { get; set; }
    }
}