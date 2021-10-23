using System.IO.Compression;
using System.Linq;

namespace QuestPatcher.Core.Patching
{
    /// <summary>
    /// Manages APK tagging, and checking an APK for the tag
    /// </summary>
    public static class ApkAnalyser
    {
        /// <summary>
        /// Tag added during patching.
        /// </summary>
        public const string QuestPatcherTagName = "modded";
        
        /// <summary>
        /// Tags from other installers which use QuestLoader. QP detects these for cross-compatibility.
        /// </summary>
        public static readonly string[] OtherTagNames = { "BMBF.modded" };
        
        /// <summary>
        /// Permission to tag the APK with.
        /// This permission is added to the manifest, and can be easily read from <code>adb shell dumpsys package [packageId]</code> without having to pull the entire APK.
        /// This makes loading much faster, especially on larger apps.
        /// </summary>
        public const string TagPermission = "questpatcher.modded";
        
        /// <summary>
        /// Loads whether or not the APK is modded, and whether it is 32 bit or 64 bit.
        /// </summary>
        /// <param name="apkArchive">APK archive to test</param>
        /// <param name="is64Bit">Whether the APK is 64 bit</param>
        /// <param name="isModded">Whether the APK is modded</param>
        /// <param name="libsPath">Path to the libraries inside the APK</param>
        /// <exception cref="PatchingException">If no 64 bit or 32 bit libil2cpp exists</exception>
        public static void GetApkInfo(ZipArchive apkArchive, out bool is64Bit, out bool isModded, out string libsPath)
        {
            const string libsPath32Bit = "lib/armeabi-v7a/";
            const string libsPath64Bit = "lib/arm64-v8a/";
            
            // QuestPatcher adds a tag file to determine if the APK is modded later on
            isModded = apkArchive.GetEntry(QuestPatcherTagName) != null || OtherTagNames.Any(tagName => apkArchive.GetEntry(tagName) != null);
            is64Bit = apkArchive.GetEntry(libsPath64Bit + "libil2cpp.so") != null;
            bool is32Bit = apkArchive.GetEntry(libsPath32Bit + "libil2cpp.so") != null;

            if(!is32Bit && !is64Bit)
            {
                throw new PatchingException(
                    "APK was of an unsupported architecture, or it was not a unity application");
            }

            libsPath = is64Bit ? libsPath64Bit : libsPath32Bit;
        }
    }
}
