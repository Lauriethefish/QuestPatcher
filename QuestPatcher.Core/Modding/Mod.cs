using Newtonsoft.Json;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace QuestPatcher.Core.Modding
{
    public class FileCopy
    {
#nullable disable // We use a schema to make sure that all of these values exist, so there is no chance of them being null in reality

        /// <summary>
        /// Name of the file in the mod archive
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Destination on the quest to copy the file to
        /// </summary>
        public string Destination { get; set; }
    }

    public class Dependency
    {
        /// <summary>
        /// Mod ID of the dependency
        /// </summary>
        [JsonProperty]
        public string Id { get; set; }

        /// <summary>
        /// Version range for the dependency
        /// </summary>
        public string Version { get; set; }

#nullable enable
        /// <summary>
        /// Link to download the dependency if it is not installed (optional)
        /// </summary>
        public string? DownloadIfMissing { get; set; }
#nullable disable

        [JsonIgnore]
        public SemanticVersioning.Range SemVersion
        {
            get
            {
                if (_semVersion == null)
                {
                    _semVersion = SemanticVersioning.Range.Parse(Version);
                }
                return _semVersion;
            }
        }
        private SemanticVersioning.Range _semVersion;
    }
    public class Mod : INotifyPropertyChanged
    {
        /// <summary>
        /// Version of the QMOD format that this mod was designed for
        /// </summary>
        [JsonProperty(PropertyName = "_QPVersion")]
        public string SchemaVersion { get; set; }

        /// <summary>
        /// An ID for the mod.
        /// Two mods with the same ID cannot be installed
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// A human-readable name for the mod
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The author of the mod
        /// </summary>
        public string Author { get; set; }

#nullable enable

        /// <summary>
        /// A short description of what the mod does
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Path of the cover image inside the archive
        /// </summary>
        [JsonProperty(PropertyName = "coverImage")]
        public string? CoverImagePath { get; set; }

        /// <summary>
        /// Bytes of the cover image file for the mod.
        /// Not loaded here so that users can load it as they choose
        /// </summary>
        [JsonIgnore]
        public byte[]? CoverImage { get; set; }

        /// <summary>
        /// If the mod was ported from another platform, this is the author of the port.
        /// </summary>
        public string? Porter { get; set; }

#nullable disable
        /// <summary>
        /// The version number of the mod. Must be semver
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// The package ID of the app that the mod is designed for.
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The version of the app that the mod is designed for.
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// Whether or not the mod is a library mod.
        /// Library mods are automatically uninstalled whenever no mods that depend on them are installed
        /// </summary>
        public bool IsLibrary { get; set; }

        /// <summary>
        /// Files copied to QuestLoader's mods directory
        /// </summary>
        public List<string> ModFiles { get; set; } = new();

        /// <summary>
        /// Files copied to QuestLoader's libs directory
        /// </summary>
        public List<string> LibraryFiles { get; set; } = new();

        /// <summary>
        /// Files copied to arbitrary locations on the quest
        /// </summary>
        public List<FileCopy> FileCopies { get; set; } = new();

        /// <summary>
        /// Dependencies of the mod
        /// </summary>
        public List<Dependency> Dependencies { get; set; } = new();

        /// <summary>
        /// Whether or not the mod is installed
        /// </summary>
        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if(value != _isInstalled)
                {
                    _isInstalled = value;
                    NotifyPropertyChanged();
                }
            }
        }

        private bool _isInstalled;

#nullable enable

        [JsonIgnore]
        public SemanticVersioning.Version SemVersion
        {
            get
            {
                if(_semVersion == null)
                {
                    _semVersion = SemanticVersioning.Version.Parse(Version);
                }
                return _semVersion;
            }
        }
        private SemanticVersioning.Version? _semVersion;

        private static JSchema Schema
        {
            get
            {
                // Load the schema if it has not already been loaded
                if(_schema == null)
                {
                    using Stream? schemaStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuestPatcher.Core.Resources.qmod.schema.json");
                    
                    Debug.Assert(schemaStream != null);
                    using StreamReader reader = new(schemaStream);
                    using JsonReader jsonReader = new JsonTextReader(reader);

                    _schema = JSchema.Load(jsonReader);
                }

                return _schema;
            }
        }

        private static JSchema? _schema;
        private static readonly JsonSerializer Serializer = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Parses a mod from the specified stream and 
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static Mod Parse(Stream input)
        {
            using StreamReader reader = new(input);
            using JsonReader jsonReader = new JsonTextReader(reader);
            using JSchemaValidatingReader validatingReader = new(jsonReader)
            {
                Schema = Schema
            };

            Mod? mod = Serializer.Deserialize<Mod>(validatingReader);
            if(mod == null)
            {
                throw new NullReferenceException("No mod was contained within the mod manifest!");
            }

            return mod;
        }

        /// <summary>
        /// Saves this mod's manifest to the given output stream
        /// </summary>
        /// <param name="output">StreamWriter to save to</param>
        public void Save(StreamWriter output)
        {
            using JsonWriter writer = new JsonTextWriter(output);
            Serializer.Serialize(writer, this);
        }
    }
}
