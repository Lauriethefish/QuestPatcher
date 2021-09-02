using System;
using System.IO;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Manages where QuestPatcher application data, and temporary files, are stored.
    /// </summary>
    public class SpecialFolders
    {
#nullable disable
        /// <summary>
        /// The app data folder for QuestPatcher.
        /// <code>%appdata%/QuestPatcher</code> on windows, <code>~/.config/QuestPatcher</code> on linux.
        /// </summary>
        public string DataFolder { get; init; }

        public string LogsFolder { get; init; }

        public string ToolsFolder { get; init; }

        /// <summary>
        /// Temporary data folder. Removed and then recreated upon startup.
        /// Also deleted upon closing, but sometimes this fails.
        /// </summary>
        public string TempFolder { get; init; }

        /// <summary>
        /// Stores APKs being processed during patching
        /// </summary>
        public string PatchingFolder { get; init; }

        /// <summary>
        /// Folder where files are temporarily stored to load as mods or upload/download to the quest.
        /// </summary>
        public string StagingArea { get; init; }
        
#nullable enable

        private ulong _currentStagingNumber;

        /// <summary>
        /// Creates/removes existing special folders.
        /// </summary>
        public void CreateFolders()
        {
            Directory.CreateDirectory(DataFolder);
            Directory.CreateDirectory(LogsFolder);
            Directory.CreateDirectory(ToolsFolder);
            if(Directory.Exists(TempFolder)) // Sometimes windows fails to delete this upon closing, and we have to do it ourselves
            {
                Directory.Delete(TempFolder, true);
            }
            Directory.CreateDirectory(TempFolder);
            Directory.CreateDirectory(PatchingFolder);
            Directory.CreateDirectory(StagingArea);
        }

        /// <summary>
        /// Sets up QuestPatcher folders to be in appdata
        /// </summary>
        /// <returns>The created special folders</returns>
        public static SpecialFolders SetupStandardFolders()
        {
            // Make sure to create the AppData folder if it does not exist
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
            string dataFolder = Path.Combine(appDataPath, "QuestPatcher");
            string tempFolder = Path.Combine(Path.GetTempPath(), "QuestPatcher");
            
            SpecialFolders folders = new()
            {
                DataFolder = dataFolder,
                LogsFolder = Path.Combine(dataFolder, "logs"),
                ToolsFolder = Path.Combine(dataFolder, "tools"),
                TempFolder = tempFolder,
                PatchingFolder = Path.Combine(tempFolder, "patching"),
                StagingArea = Path.Combine(tempFolder, "stagingArea")
            };
            folders.CreateFolders();
            return folders;
        }

        /// <summary>
        /// Finds a path to write a file to before using ADB to push it
        /// </summary>
        /// <returns>A wrapper around a file to write to</returns>
        public TempFile GetTempFile()
        {
            string path = Path.Combine(StagingArea, $"{_currentStagingNumber}.temp");
            _currentStagingNumber++;
            return new TempFile(path);
        }
    }
}
