using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Modding
{
    public class FileCopyType
    {
#nullable disable
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
#nullable enable

        public ObservableCollection<string> ExistingFiles { get; } = new();


        private readonly AndroidDebugBridge _debugBridge;


        public FileCopyType(AndroidDebugBridge debugBridge)
        {
            _debugBridge = debugBridge;
        }

        /// <summary>
        /// Loads the contents of this destination, replacing the old contents
        /// </summary>
        public async Task LoadContents()
        {
            await _debugBridge.CreateDirectory(Path); // Create the destination if it does not exist

            List<string> currentFiles = await _debugBridge.ListDirectoryFiles(Path);
            ExistingFiles.Clear();
            foreach(string file in currentFiles)
            {
                ExistingFiles.Add(file);
            }
        }

        /// <summary>
        /// Copies a file to this destination
        /// </summary>
        /// <param name="localPath">The path of the file on the PC</param>
        public async Task PerformCopy(string localPath)
        {
            await _debugBridge.CreateDirectory(Path); // Create the destination if it does not exist

            string destinationPath = System.IO.Path.Combine(Path, System.IO.Path.GetFileName(localPath));

            await _debugBridge.UploadFile(localPath, destinationPath);
            ExistingFiles.Add(destinationPath);
        }
    }
}
