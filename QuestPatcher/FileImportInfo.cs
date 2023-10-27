using QuestPatcher.Core.Modding;

namespace QuestPatcher
{
    /// <summary>
    /// Information about a file to import.
    /// </summary>
    public class FileImportInfo
    {
        /// <summary>
        /// The path to the file.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// The file extension, if <see cref="Path"/> does not have the correct extension for the data in the file.
        /// Has a period prefix.
        /// </summary>
        public string? OverrideExtension { get; set; }

        /// <summary>
        /// The file copy type that will be assumed if this file could be imported as multiple file copy types.
        /// If null, a dialog will be shown allowing the user to choose which to use.
        /// </summary>
        public FileCopyType? PreferredCopyType { get; set; }

        public FileImportInfo(string path)
        {
            Path = path;
        }
    }
}
