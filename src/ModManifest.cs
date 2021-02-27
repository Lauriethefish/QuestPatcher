
using Avalonia.Controls;
using System.Collections.Generic;
using System.Text.Json;
using System;

namespace QuestPatcher {
    public class DependencyInfo {
        public string Id { get; set; }

        public string Version { get; set; }

        public SemVer.Range ParsedVersion { get; set; }

        public string? DownloadIfMissing { get; set; } = null;

        public void ParseRange()
        {
            SemVer.Range? parsed;
            if(!SemVer.Range.TryParse(Version, out parsed))
            {
                throw new FormatException("Failed to parse version range \"" + Version + "\"");
            }

            ParsedVersion = parsed;
        }
    }

    public class ModManifest {
        public string _QPVersion { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }

        public string Author { get; set; }

        public string Version { get; set; }

        public SemVer.Version ParsedVersion { get; private set; }

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

            ModManifest? manifest = JsonSerializer.Deserialize<ModManifest>(str, options);
            if(manifest == null)
            {
                throw new NullReferenceException("Manifest was null");
            }

            // Check that the versions and version ranges are valid semver now, to avoid errors later on
            SemVer.Version? parsed;
            if(!SemVer.Version.TryParse(manifest.Version, out parsed))
            {
                throw new FormatException("Failed to parse version string \"" + manifest.Version + "\".");
            }
            manifest.ParsedVersion = parsed;

            foreach(DependencyInfo dependency in manifest.Dependencies)
            {
                dependency.ParseRange();
            }

            return manifest;
        }
    }
}