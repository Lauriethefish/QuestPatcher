using System;
using System.IO;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Manages where QuestPatcher application data, and temporary files, are stored.
    /// </summary>
    public class SpecialFolders
    {
        /// <summary>
        /// The app data folder for QuestPatcher.
        /// <code>%appdata%/QuestPatcher</code> on windows, <code>~/.config/QuestPatcher</code> on linux.
        /// </summary>
        public string DataFolder { get; }

        public string LogsFolder { get; }

        public string ToolsFolder { get; }

        /// <summary>
        /// Temporary data folder. Removed and then recreated upon startup.
        /// Also deleted upon closing, but sometimes this fails.
        /// </summary>
        public string TempFolder { get; }

        /// <summary>
        /// Stores APKs being processed during patching
        /// </summary>
        public string PatchingFolder { get; }

        /// <summary>
        /// Creates and sets all special folders
        /// </summary>
        public SpecialFolders()
        {
            // Make sure to create the AppData folder if it does not exist
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);

            DataFolder = Path.Combine(appDataPath, "QuestPatcher");
            Directory.CreateDirectory(DataFolder);

            LogsFolder = Path.Combine(DataFolder, "logs");
            Directory.CreateDirectory(LogsFolder);

            ToolsFolder = Path.Combine(DataFolder, "tools");
            Directory.CreateDirectory(ToolsFolder);

            TempFolder = Path.Combine(Path.GetTempPath(), "QuestPatcher");
            PatchingFolder = Path.Combine(TempFolder, "patching");

            PatchingFolder = Path.Combine(TempFolder, "patching");

            if (Directory.Exists(TempFolder)) // Sometimes windows fails to delete this upon closing, and we have to do it ourselves
            {
                Directory.Delete(TempFolder, true);
            }
            Directory.CreateDirectory(TempFolder);
            Directory.CreateDirectory(PatchingFolder);
        }
    }
}
