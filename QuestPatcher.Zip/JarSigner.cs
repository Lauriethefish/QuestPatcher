using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;

namespace QuestPatcher.Zip
{
    /// <summary>
    /// Implements the V1 APK signature scheme (JAR signing)
    /// </summary>
    internal class JarSigner
    {
        private const string ManifestPath = "META-INF/MANIFEST.MF";
        private const string SigFilePath = "META-INF/BS.SF";
        private const string KeyFilePath = "META-INF/BS.RSA";

        /// <summary>
        /// Signs an APK file with a v1 JAR signature.
        /// </summary>
        /// <param name="apk">APK to sign</param>
        /// <param name="certificate">Certificate to sign with</param>
        /// <param name="privateKey">Private key to sign with</param>
        /// <param name="existingHashes">Digests scraped from the existing signature, to speed up signing</param>
        internal static void SignApkFile(ApkZip apk, X509Certificate certificate, AsymmetricKeyParameter privateKey, Dictionary<string, string>? existingHashes)
        {
            const CompressionLevel CompressionLevel = CompressionLevel.Optimal;
            var sha = SHA256.Create();

            using Stream manifestFile = new MemoryStream();
            using Stream sigFileBody = new MemoryStream();
            using (var manifestWriter = OpenStreamWriter(manifestFile))
            {
                manifestWriter.WriteLine("Manifest-Version: 1.0");
                manifestWriter.WriteLine("Created-By: QuestPatcher");
                manifestWriter.WriteLine();
            }

            // Write the digest for each APK entry
            foreach (string? fileName in apk.Entries.ToList())
            {
                if (fileName.StartsWith("META-INF"))
                {
                    apk.RemoveFile(fileName);
                }
                else
                {
                    WriteEntryHash(fileName, manifestFile, sigFileBody, existingHashes, apk, sha);
                }
            }

            // Find the hash of the manifest
            manifestFile.Position = 0;
            byte[] manifestHash = sha.ComputeHash(manifestFile);

            // Finally, copy it to the output file
            manifestFile.Position = 0;
            apk.AddFile(ManifestPath, manifestFile, CompressionLevel);

            // Write the signature information
            using var signatureFile = new MemoryStream();
            using (var signatureWriter = OpenStreamWriter(signatureFile))
            {
                signatureWriter.WriteLine("Signature-Version: 1.0");
                signatureWriter.WriteLine($"SHA-256-Digest-Manifest: {Convert.ToBase64String(manifestHash)}");
                signatureWriter.WriteLine("Created-By: QuestPatcher");
                signatureWriter.WriteLine("X-Android-APK-Signed: 2");
                signatureWriter.WriteLine();
            }

            // Copy the body of signatures for each file into the signature file
            sigFileBody.Position = 0;
            sigFileBody.CopyTo(signatureFile);
            signatureFile.Position = 0;
            apk.AddFile(SigFilePath, signatureFile, CompressionLevel);

            // Sign the signature file, and save the signature
            byte[] keyFile = SigningUtility.SignPKCS7(signatureFile.ToArray(), certificate, privateKey);
            var keyFileStream = new MemoryStream(keyFile);

            apk.AddFile(KeyFilePath, keyFileStream, CompressionLevel);
        }

        /// <summary>
        /// Writes the MANIFEST.MF and signature file hashes for the given entry
        /// </summary>
        private static void WriteEntryHash(string fileName, Stream manifestStream, Stream signatureStream, Dictionary<string, string>? existingHashes, ApkZip apk, SHA256 sha)
        {
            string hash;
            if (existingHashes != null &&
               existingHashes.TryGetValue(fileName, out string? prePatchHash))
            {
                hash = prePatchHash;
            }
            else
            {
                using var reader = apk.OpenReader(fileName);
                hash = Convert.ToBase64String(sha.ComputeHash(reader));
            }

            // First write the digest of the file to a section of the manifest file
            using var sectStream = new MemoryStream();
            using (var sectWriter = OpenStreamWriter(sectStream))
            {
                sectWriter.WriteLine($"Name: {fileName}");
                sectWriter.WriteLine($"SHA-256-Digest: {hash}");
                sectWriter.WriteLine();
            }

            // Then write the hash for the section of the manifest file to the signature file
            sectStream.Position = 0;
            string sectHash = Convert.ToBase64String(sha.ComputeHash(sectStream));
            using (var signatureWriter = OpenStreamWriter(signatureStream))
            {
                signatureWriter.WriteLine($"Name: {fileName}");
                signatureWriter.WriteLine($"SHA-256-Digest: {sectHash}");
                signatureWriter.WriteLine();
            }

            sectStream.Position = 0;
            sectStream.CopyTo(manifestStream);
        }

        private static StreamWriter OpenStreamWriter(Stream stream)
        {
            return new StreamWriter(stream, new UTF8Encoding(false) /* (disable BOM) */, 1024, true);
        }

        /// <summary>
        /// Parses the META-INF/MANIFEST.MF file within <paramref name="apk"/> and uses it to collect
        /// the hashes of the entries within the given APK.
        /// </summary>
        /// <param name="apk">The archive to get the entry hashes of</param>
        /// <returns>A dictionary of the full entry names and entry hashes, or null if parsing the manifest failed.</returns>
        internal static Dictionary<string, string>? CollectExistingHashes(ApkZip apk)
        {
            // Fallback failure if the APK isn't signed
            if (!apk.ContainsFile(ManifestPath))
            {
                return null;
            }

            using var manifestStream = apk.OpenReader(ManifestPath);
            using var manifestReader = new StreamReader(manifestStream);

            return CollectExistingHashesInternal(manifestReader);
        }

        /// <summary>
        /// Parses the META-INF/MANIFEST.MF file within <paramref name="apk"/> and uses it to collect
        /// the hashes of the entries within the given APK.
        /// </summary>
        /// <param name="apk">The archive to get the entry hashes of</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="OperationCanceledException">If operation is canceled.</exception>
        /// <returns>A dictionary of the full entry names and entry hashes, or null if parsing the manifest failed.</returns>
        internal static async Task<Dictionary<string, string>?> CollectExistingHashesAsync(ApkZip apk, CancellationToken ct)
        {
            // Fallback failure if the APK isn't signed
            if (!apk.ContainsFile(ManifestPath))
            {
                return null;
            }

            // Copy to a MemoryStream so that we can read the hashes without blocking.
            using var manifestStream = await apk.OpenReaderAsync(ManifestPath);
            using var memStream = new MemoryStream();
            await manifestStream.CopyToAsync(memStream, ct);
            memStream.Position = 0;
            using var manifestReader = new StreamReader(memStream);

            return CollectExistingHashesInternal(manifestReader);
        }

        private static Dictionary<string, string>? CollectExistingHashesInternal(StreamReader manifestReader)
        {
            // Fallback failure if the manifest version isn't what we're expecting.
            if (manifestReader.ReadLine() != "Manifest-Version: 1.0")
            {
                return null;
            }

            // Read the remaining lines of the MANIFEST.MF header, when we reach a blank line, the header is over
            // This skips information such as the piece of software that was doing the signing.
            while (manifestReader.ReadLine() != "") { }

            var result = new Dictionary<string, string>();
            while (true)
            {
                // Sometimes the names of files within a hash are formatted with multiple lines
                // In this case, the files will be formatted like:
                // |Name: myFileNameIsReallyReally
                // | LongItIsVeryLong.txt
                // So, each newline and space indicates an extension of the file name.
                var nameBuilder = new StringBuilder();
                string? firstLineOfName = manifestReader.ReadLine();
                // We have reached the end of the file, or there is a formatting issue, so we quit parsing
                if (firstLineOfName == null)
                {
                    return result;
                }
                // Skip the "Name: " prefix.
                nameBuilder.Append(firstLineOfName[6..]);

                string digest;
                // Now we will parse the remaining lines within the name of the file
                while (true)
                {
                    string? nextLineOfName = manifestReader.ReadLine();
                    if (nextLineOfName == null)
                    {
                        // We have reached the end of the file, or there is a formatting issue, so we quit parsing
                        return result;
                    }
                    if (nextLineOfName.StartsWith(" "))
                    {
                        // A space at the beginning of the line indicates that it is a continuation of the current file's name
                        nameBuilder.Append(nextLineOfName[1..]);
                    }
                    else if (nextLineOfName.StartsWith("SHA-256-Digest: "))
                    {
                        // We have now reached the end of the name of the file, and the start of the SHA-256 digest.
                        // Skip the "SHA-256-Digest: " prefix.
                        digest = nextLineOfName[16..];
                        break;
                    }
                    else
                    {
                        // If the next line does not start with a space, and is not a SHA-256 digest, then this manifest
                        // format/hash type is unsupported, so we will quit parsing here.
                        return result;
                    }
                }
                string entryName = nameBuilder.ToString();

                result[entryName] = digest;

                // Skip the newline after each entry.
                manifestReader.ReadLine();
            }
        }
    }
}
