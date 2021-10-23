using System.IO;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Manages temporary folders
    /// </summary>
    public class TempFolders
    {
        /// <summary>
        /// Root temporary folder for QuestPatcher
        /// </summary>
        public string TempFolder { get; }
        
        /// <summary>
        /// Folder that stores temporary info during patching.
        /// </summary>
        public string PatchingFolder { get; }

        public TempFolders()
        {
            TempFolder = Path.Combine(Path.GetTempPath(), "QuestPatcher");
            
            // Remove existing temp files, this should fail if QuestPatcher is open already
            if(Directory.Exists(TempFolder))
            {
                Directory.Delete(TempFolder, true);
            }
            Directory.CreateDirectory(TempFolder);
            
            PatchingFolder = Path.Combine(TempFolder, "patching");
            Directory.CreateDirectory(PatchingFolder);
        }
    }
}
