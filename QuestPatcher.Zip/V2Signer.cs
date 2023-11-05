using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;
using QuestPatcher.Zip.Data;

namespace QuestPatcher.Zip
{
    /// <summary>
    /// Implements the APK signature scheme v2.
    /// </summary>
    internal static class V2Signer
    {
        /// <summary>
        /// The size of the chunks in which the APK is hashed.
        /// </summary>
        private const int ChunkSize = 1 << 20;

        /// <summary>
        /// Writes the signature block, central directory and EOCD for a v2 signed ZIP file.
        /// </summary>
        /// <param name="centralDirectoryRecords">The records to be written to the central directory</param>
        /// <param name="apkStream">The stream of the APK file.
        /// Must be positioned at the point where the signature block should be written. (i.e. after the last ZIP entry)</param>
        /// <param name="certificate">The signing certificate</param>
        /// <param name="privateKey">The private key to sign with</param>
        /// <exception cref="ZipDataException"></exception>
        internal static void SignAndCompleteZipFile(ICollection<CentralDirectoryFileHeader> centralDirectoryRecords, Stream apkStream, X509Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            // The signature block is placed after the data of the last ZIP entry
            long sigBlockPosition = apkStream.Position;

            using var cdStream = new MemoryStream();
            var cdMemory = new ZipMemory(cdStream);
            foreach (var record in centralDirectoryRecords)
            {
                record.Write(cdMemory);
            }

            if (centralDirectoryRecords.Count > ushort.MaxValue)
            {
                throw new ZipDataException($"Too many central directory records. Max Length {ushort.MaxValue}, got {centralDirectoryRecords.Count}");
            }
            ushort cdRecords = (ushort) centralDirectoryRecords.Count;

            var eocd = new EndOfCentralDirectory()
            {
                NumberOfThisDisk = 0,
                StartOfCentralDirectoryDisk = 0,
                CentralDirectoryRecordsOnDisk = cdRecords,
                CentralDirectoryRecords = cdRecords,
                CentralDirectorySize = (uint) cdStream.Length,
                // When calculating the digest, the central directory offset is set to the signature block position
                // This is to avoid a situation where the signature block data depends on the length of itself.
                CentralDirectoryOffset = (uint) sigBlockPosition,
                Comment = null
            };

            using var eocdStream = new MemoryStream();
            var eocdMemory = new ZipMemory(eocdStream);
            eocd.Write(eocdMemory);

            // Write the signature block
            byte[] apkDigest = CalculateApkDigest(eocdStream, cdStream, apkStream, apkStream.Position);
            WriteSignature(apkStream, apkDigest, certificate, privateKey);

            // Save the central directory
            if (apkStream.Position > uint.MaxValue)
            {
                throw new ZipDataException("ZIP file too large to save central directory");
            }
            eocd.CentralDirectoryOffset = (uint) apkStream.Position;

            var apkWriter = new ZipMemory(apkStream);
            foreach (var record in centralDirectoryRecords)
            {
                record.Write(apkWriter);
            }

            eocd.Write(apkWriter);
        }

        private static void WriteSignature(Stream apkStream, byte[] apkDigest, X509Certificate certificate, AsymmetricKeyParameter privateKey)
        {
            const uint SignatureAlgorithmId = 0x0103;
            const uint v2SignatureId = 0x7109871a;

            // This part of the signature block contains the digest of the APK and our certificate
            using var signedDataStream = new MemoryStream();
            using var signedDataWriter = new BinaryWriter(signedDataStream);

            int digestLength = 4 + 4 + 32; // 4 bytes for signature alg. ID, 4 bytes for digest length, 32 bytes for digest
            int digestSequenceLength = digestLength + 4; // 4 extra bytes for the digest length
            signedDataWriter.Write((uint) digestSequenceLength); // Write the length of the digests array, which includes the length written just below
            signedDataWriter.Write((uint) digestLength); // Write the length of our one digest
            signedDataWriter.Write(SignatureAlgorithmId);
            signedDataWriter.Write((uint) apkDigest.Length);
            signedDataWriter.Write(apkDigest);

            byte[] certData = certificate.GetEncoded();
            signedDataWriter.Write((uint) (certData.Length + 4)); // Length of certificates array
            signedDataWriter.Write((uint) certData.Length); // Length of our one certificate
            signedDataWriter.Write(certData);

            signedDataWriter.Write((uint) 0); // No additional attributes

            byte[] signedData = signedDataStream.ToArray();
            byte[] signature = SigningUtility.SignPKCS1(signedData, privateKey);

            byte[] publicKeyData = certificate.CertificateStructure.SubjectPublicKeyInfo.GetDerEncoded();

            // Calculate the total length of the signer section
            int signerLength = 4 + signedData.Length + 4 + 4 + 4 + 4 + signature.Length + 4 + publicKeyData.Length;

            // Calculate the total length of the signing block
            int v2SignatureValueLength = signerLength + 4 + 4;
            int v2SignaturePairLength = 4 + v2SignatureValueLength;
            int signingBlockLength = 8 + v2SignaturePairLength + 8 + 16;

            // Begin the APK signing block
            using var sigWriter = new BinaryWriter(apkStream, Encoding.ASCII, true);
            sigWriter.Write((ulong) signingBlockLength);
            sigWriter.Write((ulong) v2SignaturePairLength);
            sigWriter.Write(v2SignatureId);

            // Write the signer, which contains the actual signature
            sigWriter.Write((uint) (4 + signerLength)); // Length of signers array
            sigWriter.Write((uint) signerLength); // Length of first and only signer

            sigWriter.Write((uint) signedData.Length);
            sigWriter.Write(signedData);

            sigWriter.Write((uint) (4 + 4 + 4 + signature.Length)); // Length prefix for the signatures
            sigWriter.Write((uint) (4 + 4 + signature.Length)); // Length prefix for the first and only signature
            sigWriter.Write(SignatureAlgorithmId);
            sigWriter.Write((uint) signature.Length);
            sigWriter.Write(signature);

            sigWriter.Write((uint) publicKeyData.Length);
            sigWriter.Write(publicKeyData);

            // Finish the APK signing block
            sigWriter.Write((ulong) signingBlockLength);
            sigWriter.Write(Encoding.UTF8.GetBytes("APK Sig Block 42"));
        }

        private static byte[] CalculateApkDigest(Stream eocdStream, Stream cdStream, Stream apkStream, long apkEntriesLength)
        {
            using var topLevelDigestStream = new MemoryStream();
            using var topLevelDigestWriter = new BinaryWriter(topLevelDigestStream);
            topLevelDigestWriter.Write((byte) 0x5a); // Magic value for the top level digest
            topLevelDigestWriter.Write((uint) 0); // Write 0 for the chunk count, we will come back to this once we've written the chunks

            // Write the chunk digests of the zip contents, CD and EOCD
            // Must be in this order for the digest to be valid
            uint chunkCount = 0;
            byte[] chunkBuffer = new byte[ChunkSize];
            chunkCount += WriteChunkDigests(0, apkEntriesLength, apkStream, topLevelDigestWriter, chunkBuffer);
            chunkCount += WriteChunkDigests(0, cdStream.Length, cdStream, topLevelDigestWriter, chunkBuffer);
            chunkCount += WriteChunkDigests(0, eocdStream.Length, eocdStream, topLevelDigestWriter, chunkBuffer);
            topLevelDigestStream.Position = 1;
            topLevelDigestWriter.Write(chunkCount); // Write the correct chunk count

            topLevelDigestStream.Position = 0;

            var sha = SHA256.Create();
            return sha.ComputeHash(topLevelDigestStream.ToArray(), 0, (int) topLevelDigestStream.Length);
        }

        private static uint WriteChunkDigests(long sectionStart, long length, Stream sourceData, BinaryWriter output, byte[] chunkBuffer)
        {
            var chunkMagicStream = new MemoryStream();
            var chunkMagicWriter = new BinaryWriter(chunkMagicStream);

            // Compute the hash of each 1MB chunk and append it.
            long sectionEnd = sectionStart + length;
            sourceData.Position = sectionStart;
            uint chunkCount = 0;
            for (long i = sectionStart; i < sectionEnd; i += ChunkSize)
            {
                int bytesInChunk = (int) Math.Min(sectionEnd - i, ChunkSize);

                var sha = SHA256.Create();

                // Each 1MB chunk is computed over the concatenation of 0xa5, the chunk length, and the chunk data
                // To avoid copying all the chunk data into a byte array, we use a separate smaller array for the magic/length, and hash that . .
                chunkMagicWriter.Write((byte) 0xa5);
                chunkMagicWriter.Write(bytesInChunk);
                chunkMagicStream.Position = 0;
                sha.TransformBlock(chunkMagicStream.GetBuffer(), 0, 4 + 1, null, -1);

                // .. then finalise the hash with the full chunk data
                sourceData.Read(chunkBuffer, 0, bytesInChunk);
                sha.TransformFinalBlock(chunkBuffer, 0, bytesInChunk);

                output.Write(sha.Hash!);
                chunkCount += 1;
            }

            return chunkCount;
        }

    }
}
