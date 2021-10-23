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
        }

        /// <summary>
        /// Finds a path to write a file to before using ADB to push it
        /// </summary>
        /// <returns>A wrapper around a file to write to</returns>
        public TempFile GetTempFile()
        {
            return new();
        }
    }
}
