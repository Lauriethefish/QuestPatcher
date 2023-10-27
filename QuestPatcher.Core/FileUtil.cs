using System.IO;
using System.Threading.Tasks;

namespace QuestPatcher.Core
{
    public class FileUtil
    {
        /// <summary>
        /// Copies the contents of one file to another file asynchronously.
        /// If the destination file exists, it will be overwritten.
        /// </summary>
        /// <param name="from">The path to the file to copy.</param>
        /// <param name="to">The path to the file to copy the data to.</param>
        /// <exception cref="FileNotFoundException">If no file is found at <paramref name="from"/>.</exception>
        /// <exception cref="DirectoryNotFoundException">If the directory that would contain the file at <paramref name="to"/> does not exist.</exception>
        public static async Task CopyAsync(string from, string to)
        {
            await using var sourceStream = File.OpenRead(from);
            await using var targetStream = File.OpenWrite(to);

            await sourceStream.CopyToAsync(targetStream);
        }
    }
}
