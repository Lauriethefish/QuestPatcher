using System.Text.Json.Serialization;

namespace QuestPatcher.Core.Models
{
    /// <summary>
    /// Used to load the JSON file "modded.json" found inside the APK.
    /// </summary>
    public class ModdedTag
    {
        /// <summary>
        /// The name of the application which patched the APK
        /// </summary>
        public string PatcherName { get; set; }

        /// <summary>
        /// The version of the application which patched the APK
        /// </summary>
        public string? PatcherVersion { get; set; }

        /// <summary>
        /// The name of the modloader this APK was patched with
        /// </summary>
        public string ModloaderName { get; set; }

        /// <summary>
        /// The version of the modloader the APK was patched with
        /// </summary>
        public string? ModloaderVersion { get; set; }

        [JsonConstructor]
        public ModdedTag(string patcherName, string? patcherVersion, string modloaderName, string? modloaderVersion)
        {
            PatcherName = patcherName;
            PatcherVersion = patcherVersion;
            ModloaderName = modloaderName;
            ModloaderVersion = modloaderVersion;
        }
    }
}
