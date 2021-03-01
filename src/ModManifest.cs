
using Avalonia.Controls;
using System.Collections.Generic;
using System.Text.Json;
using Json.Schema;
using System;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;

namespace QuestPatcher {
    public class DependencyInfo {
        public string Id { get; set; }

        public string Version { get; set; }

        public SemVer.Range ParsedVersion { get; set; }

        public string? DownloadIfMissing { get; set; } = null;

        public void ParseRange()
        {
            ParsedVersion = SemVer.Range.Parse(Version);
        }
    }

    public class FileCopyInfo
    {
        public string Name { get; set; }
        public string Destination { get; set; }
    }

    public class ModManifest {
        private static JsonSchema? schema;

        public string _QPVersion { get; set; }
        public string Name { get; set; }
        public string Id { get; set; }

        public string Author { get; set; }

        public string Version { get; set; }

        public SemVer.Version ParsedVersion { get; private set; }

        public string PackageId { get; set; }
        public string PackageVersion { get; set; }

        public bool IsLibrary { get; set; }

        public List<string> ModFiles { get; set; } = new List<string>();
        public List<string> LibraryFiles { get; set; } = new List<string>();

        public List<FileCopyInfo> FileCopies { get; set; } = new List<FileCopyInfo>();

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

        public static async Task<ModManifest> Load(string str) {
            if(schema == null)
            {
                Assembly assembly = Assembly.GetExecutingAssembly();

                Stream? schemaStream = assembly.GetManifestResourceStream("QuestPatcher.resources.qmod.schema.json");
                if(schemaStream == null)
                {
                    throw new FileNotFoundException("Unable to find manifest schema in resources");
                }

                schema = await JsonSchema.FromStream(schemaStream);

            }

            JsonDocument document = JsonDocument.Parse(str);
            ValidationResults validity = schema.Validate(document.RootElement);
            if(!validity.IsValid)
            {
                validity.ToDetailed();
                throw new FormatException(validity.Message); // Unfortunately the message is always null, still trying to figure out why . . .
            }

            JsonSerializerOptions options = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };

            ModManifest? manifest = JsonSerializer.Deserialize<ModManifest>(str, options);
            if(manifest == null)
            {
                throw new NullReferenceException("Manifest was null");
            }

            manifest.ParsedVersion = SemVer.Version.Parse(manifest.Version);

            foreach(DependencyInfo dependency in manifest.Dependencies)
            {
                dependency.ParseRange();
            }

            return manifest;
        }
    }
}