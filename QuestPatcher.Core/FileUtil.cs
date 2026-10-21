using System;
using System.IO;
using System.Security.Cryptography;
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

        /// <summary>
        /// Computes the SHA-256 hash of a file.
        /// </summary>
        /// <param name="filePath">The path to the file to hash.</param>
        /// <returns>The hash as a lowercase hex string.</returns>
        public static async Task<string> GetSha256Async(string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            return await GetSha256Async(stream);
        }

        /// <summary>
        /// Computes the SHA-256 hash of the remaining contents of a stream.
        /// </summary>
        /// <param name="stream">The stream to hash.</param>
        /// <returns>The hash as a lowercase hex string.</returns>
        public static async Task<string> GetSha256Async(Stream stream)
        {
            using var sha256 = SHA256.Create();
            return Convert.ToHexString(await sha256.ComputeHashAsync(stream)).ToLowerInvariant();
        }
    }
}
