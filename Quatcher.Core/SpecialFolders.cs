using System;
using System.IO;

namespace Quatcher.Core
{
    /// <summary>
    /// Manages where Quatcher application data, and temporary files, are stored.
    /// </summary>
    public class SpecialFolders
    {
        /// <summary>
        /// The app data folder for Quatcher.
        /// <code>%appdata%/Quatcher</code> on windows, <code>~/.config/Quatcher</code> on linux.
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
        /// Folder where files are temporarily stored to load as mods or upload/download to the quest.
        /// </summary>
        public string StagingArea { get; }

        private ulong _currentStagingNumber = 0;
        
        /// <summary>
        /// Creates and sets all special folders
        /// </summary>
        public SpecialFolders()
        {
            // Make sure to create the AppData folder if it does not exist
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
            
            DataFolder = Path.Combine(appDataPath, "Quatcher");
            Directory.CreateDirectory(DataFolder);

            LogsFolder = Path.Combine(DataFolder, "logs");
            Directory.CreateDirectory(LogsFolder);

            ToolsFolder = Path.Combine(DataFolder, "tools");
            Directory.CreateDirectory(ToolsFolder);

            TempFolder = Path.Combine(Path.GetTempPath(), "Quatcher");
            if(Directory.Exists(TempFolder)) // Sometimes windows fails to delete this upon closing, and we have to do it ourselves
            {
                Directory.Delete(TempFolder, true);
            }
            Directory.CreateDirectory(TempFolder);

            PatchingFolder = Path.Combine(TempFolder, "patching");
            Directory.CreateDirectory(PatchingFolder);

            StagingArea = Path.Combine(TempFolder, "stagingArea");
            Directory.CreateDirectory(StagingArea);
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
