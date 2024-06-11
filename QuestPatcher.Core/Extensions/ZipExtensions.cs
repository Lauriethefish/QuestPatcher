using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace QuestPatcher.Core.Extensions
{
    internal static class ZipExtensions
    {
        /// <summary>
        /// Adds a folder and its contents to a ZipArchive.
        /// </summary>
        /// <param name="archive">The ZipArchive to add entries to.</param>
        /// <param name="sourceFolder">The path to the source folder.</param>
        public static void AddFolder(this ZipArchive archive, string sourceFolder)
        {
            var stack = new Stack<string>();
            stack.Push(sourceFolder);

            while (stack.Count > 0)
            {
                var currentDir = stack.Pop();

                foreach (var subDirectory in Directory.GetDirectories(currentDir))
                {
                    var entryName = Path.GetRelativePath(sourceFolder, subDirectory);
                    archive.CreateEntry($"{entryName}/");
                    stack.Push(subDirectory);
                }

                foreach (var filePath in Directory.GetFiles(currentDir))
                {
                    var entryName = Path.GetRelativePath(sourceFolder, filePath);
                    archive.CreateEntryFromFile(filePath, entryName);
                }
            }
        }
    }
}
