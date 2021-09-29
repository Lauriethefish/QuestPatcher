using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Patching
{
    public static class ZipExtensions
    {
        /// <summary>
        /// Creates a new entry in the archive, deleting it if the name already exists, then opens it.
        /// </summary>
        /// <param name="archive">Archive to create the entry in</param>
        /// <param name="entryName">Name/path of the entry</param>
        /// <returns>Stream which can be used to read/write to the entry</returns>
        public static Stream CreateAndOpenEntry(this ZipArchive archive, string entryName)
        {
            ZipArchiveEntry? existing = archive.GetEntry(entryName);
            if(existing != null)
            {
                existing.Delete();
            }

            return archive.CreateEntry(entryName).Open();
        }
        
        /// <summary>
        /// Asynchronously copies a file to the archive, overwriting if <code>overwrite</code> is set to <code>true</code>.
        /// </summary>
        /// <param name="archive">Archive to copy the file to</param>
        /// <param name="sourcePath">Where to copy the file from</param>
        /// <param name="entryName">Where to copy the file in the archive</param>
        /// <param name="overwrite">Used if an entry already exists with the given <paramRef name="entryName"/>. If set to <code>true</code>, the existing entry will be removed and a new entry will be created. Otherwise, <see cref="InvalidOperationException"/> is thrown.</param>
        /// <param name="enforceForwardSlash">If true, any backward slashes in the <paramRef name="entryName"/> will be replaced with forward slashes.</param>
        /// <exception cref="InvalidOperationException">If an entry with name <paramRef name="entryName"/> already exists and <paramRef name="overwrite"/> is set to <code>false</code></exception>
        public static async Task AddFileAsync(this ZipArchive archive, string sourcePath, string entryName, bool overwrite = false, bool enforceForwardSlash = true)
        {
            if (enforceForwardSlash)
            {
                entryName = entryName.Replace("\\", "/");
            }
            
            ZipArchiveEntry? existingEntry = archive.GetEntry(entryName);
            if (existingEntry != null)
            {
                if (overwrite)
                {
                    existingEntry.Delete();
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Failed to copy file to archive: Entry {entryName} existed, and overwrite was set to false");
                }
            }

            // We do not use the built in CreateEntryFromFile method, as it is not async
            ZipArchiveEntry newEntry = archive.CreateEntry(entryName);
            await using Stream fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using Stream destinationStream = newEntry.Open();
            await fileStream.CopyToAsync(destinationStream);
        }
    }
}
