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

        /// <summary>
        /// QuestPatcher and ADB logs
        /// </summary>
        public string LogsFolder { get; }

        /// <summary>
        /// Tools needed for QP to work, e.g. ADB, QuestLoader.
        /// </summary>

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
        /// Sets all the special folder paths.
        /// </summary>
        public SpecialFolders()
        {
            // Make sure to create the AppData folder if it does not exist
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);

            DataFolder = Path.Combine(appDataPath, "QuestPatcher");
            LogsFolder = Path.Combine(DataFolder, "logs");
            ToolsFolder = Path.Combine(DataFolder, "tools");

            TempFolder = Path.Combine(Path.GetTempPath(), "QuestPatcher");
            PatchingFolder = Path.Combine(TempFolder, "patching");
            PatchingFolder = Path.Combine(TempFolder, "patching");
        }

        /// <summary>
        /// Creates all special folders.
        /// Deletes the temporary directory and recreates if it already exists.
        /// </summary>
        public void CreateAndDeleteTemp()
        {
            Directory.CreateDirectory(DataFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(ToolsFolder);

            // This may not be deleted if QP crashed, so we do it just to make sure.
            if (Directory.Exists(TempFolder))
            {
                Directory.Delete(TempFolder, true);
            }
            Directory.CreateDirectory(TempFolder);
            Directory.CreateDirectory(PatchingFolder);
        }
    }
}
