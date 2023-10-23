using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// Used to deserialize file copy information from JSON.
    /// </summary>
    public class FileCopyInfo
    {
        /// <summary>
        /// Name of the file copy, singular. E.g. "gorilla tag hat"
        /// </summary>
        public string NameSingular { get; set; }

        /// <summary>
        /// Name of the file copy, plural. E.g. "gorilla tag hats"
        /// </summary>
        public string NamePlural { get; set; }

        /// <summary>
        /// Path to copy files to/list files from
        /// </summary>
        public string Path { get; set; }


        /// <summary>
        /// List of support file extensions for this file copy destination
        /// </summary>
        public List<string> SupportedExtensions { get; set; }

        [JsonConstructor]
        public FileCopyInfo(string nameSingular, string namePlural, string path, List<string> supportedExtensions)
        {
            NameSingular = nameSingular;
            NamePlural = namePlural;
            Path = path;
            SupportedExtensions = supportedExtensions;
        }
    }
}
