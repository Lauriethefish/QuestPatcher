using System.Threading.Tasks;

namespace QuestPatcher.Zip.Data
{
    /// <summary>
    /// The data stored for each file in the ZIP's central directory.
    /// </summary>
    internal class CentralDirectoryFileHeader
    {
        private const uint Header = 0x02014b50;

        /// <summary>
        /// The OS that created the ZIP file.
        /// This data is not required by our parser, as we make no attempt to parse the external file attributes.
        /// </summary>
        public ushort VersionMadeBy { get; set; }

        /// <summary>
        /// The minimum version of the ZIP specification that must be implemented to extract this file
        /// </summary>
        public ZipVersion VersionNeededToExtract { get; set; }

        /// <summary>
        /// Flags relating to the compression/encryption method selected
        /// </summary>
        public EntryFlags Flags { get; set; }

        /// <summary>
        /// Method used to compress this file.
        /// </summary>
        public CompressionMethod CompressionMethod { get; set; }

        /// <summary>
        /// The timestamp at which this ZIP entry was last modified
        /// </summary>
        public Timestamp LastModified { get; set; }

        /// <summary>
        /// CRC32 (presumably) of the data inside this file.
        /// </summary>
        public uint Crc32 { get; set; }

        /// <summary>
        /// Size of the compressed entry.
        /// </summary>
        public uint CompressedSize { get; set; }

        /// <summary>
        /// Size of the entry when uncompressed.
        /// </summary>
        public uint UncompressedSize { get; set; }

        /// <summary>
        /// The name of the file/directory this entry represents
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// An optional extra data container.
        /// </summary>
        public byte[]? ExtraField { get; set; }

        /// <summary>
        /// An optional file comment
        /// </summary>
        public string? FileComment { get; set; }

        /// <summary>
        /// The disk number where this header starts.
        /// In practice, this should always be 0 as multiple-disk archives aren't supported by this implementation
        /// </summary>
        public ushort DiskNumberStart { get; set; }

        /// <summary>
        /// Various file attributes, e.g. could indicate if the file is a text file.
        /// We don't care about these, so will just copy them as found in the original file.
        /// </summary>
        public ushort InternalFileAttributes { get; set; }

        /// <summary>
        /// Attributes of the file this entry represents.
        /// These are OS dependent, and relate to the VersionMadeBy.
        /// For this reason, we do not implement parsing them.
        /// </summary>
        public uint ExternalFileAttributes { get; set; }

        /// <summary>
        /// The offset of the local header, in bytes, from the start of the ZIP file.
        /// </summary>
        public uint LocalHeaderOffset { get; set; }

        public static CentralDirectoryFileHeader Read(ZipMemory reader)
        {
            if (reader.ReadUInt32() != Header)
            {
                throw new ZipFormatException("Invalid CentralDirectoryFileHeader signature");
            }

            var inst = new CentralDirectoryFileHeader()
            {
                VersionMadeBy = reader.ReadUInt16(),
                VersionNeededToExtract = ApkZip.CheckVersionSupported(ZipVersion.Read(reader)),
                Flags = (EntryFlags) reader.ReadInt16(),
                CompressionMethod = (CompressionMethod) reader.ReadInt16(),
                LastModified = Timestamp.Read(reader),
                Crc32 = reader.ReadUInt32(),
                CompressedSize = reader.ReadUInt32(),
                UncompressedSize = reader.ReadUInt32(),
            };

            ushort fileNameLength = reader.ReadUInt16();
            ushort extraFieldLength = reader.ReadUInt16();
            ushort commentLength = reader.ReadUInt16();

            inst.DiskNumberStart = reader.ReadUInt16();
            if (inst.DiskNumberStart != 0)
            {
                throw new ZipFormatException("ZIP files split across multiple disks are not supported");
            }

            inst.InternalFileAttributes = reader.ReadUInt16();
            inst.ExternalFileAttributes = reader.ReadUInt32();
            inst.LocalHeaderOffset = reader.ReadUInt32();

            if (fileNameLength != 0)
            {
                inst.FileName = reader.ReadZipString(fileNameLength, inst.Flags);
            }

            if (extraFieldLength != 0)
            {
                inst.ExtraField = reader.ReadBytes(extraFieldLength);
            }

            if (commentLength != 0)
            {
                inst.FileComment = reader.ReadZipString(fileNameLength, inst.Flags);
            }


            return inst;
        }

        public void Write(ZipMemory writer)
        {
            writer.Write(Header);
            writer.Write(VersionMadeBy);
            VersionNeededToExtract.Write(writer);
            writer.Write((short) Flags);
            writer.Write((short) CompressionMethod);
            LastModified.Write(writer);
            writer.Write(Crc32);
            writer.Write(CompressedSize);
            writer.Write(UncompressedSize);

            byte[]? fileNameBytes = null;
            if (FileName != null)
            {
                fileNameBytes = Flags.GetStringEncoding().GetBytes(FileName);

                if (fileNameBytes.Length > ushort.MaxValue)
                {
                    throw new ZipDataException($"File name too long ({fileNameBytes.Length}). Max length {ushort.MaxValue}.");
                }

                writer.Write((ushort) FileName.Length);
            }
            else
            {
                writer.Write((ushort) 0);
            }

            if (ExtraField != null)
            {
                if (ExtraField.Length > ushort.MaxValue)
                {
                    throw new ZipDataException($"Extra field too long ({ExtraField.Length}). Max length {ushort.MaxValue}");
                }

                writer.Write((ushort) ExtraField.Length);
            }
            else
            {
                writer.Write((ushort) 0);
            }

            byte[]? fileCommentBytes = null;
            if (FileComment != null)
            {
                fileCommentBytes = Flags.GetStringEncoding().GetBytes(FileComment);

                if (fileCommentBytes.Length > ushort.MaxValue)
                {
                    throw new ZipDataException($"File comment too long ({fileCommentBytes.Length}). Max length {ushort.MaxValue}");
                }

                writer.Write((ushort) fileCommentBytes.Length);
            }
            else
            {
                writer.Write((ushort) 0);
            }

            writer.Write(DiskNumberStart);
            writer.Write(InternalFileAttributes);
            writer.Write(ExternalFileAttributes);
            writer.Write(LocalHeaderOffset);


            if (fileNameBytes != null)
            {
                writer.Write(fileNameBytes);
            }

            if (ExtraField != null)
            {
                writer.Write(ExtraField);
            }

            if (fileCommentBytes != null)
            {
                writer.Write(fileCommentBytes);
            }
        }

        public static async Task<CentralDirectoryFileHeader> ReadAsync(ZipMemory reader)
        {
            if (await reader.ReadUInt32Async() != Header)
            {
                throw new ZipFormatException("Invalid CentralDirectoryFileHeader signature");
            }

            var inst = new CentralDirectoryFileHeader()
            {
                VersionMadeBy = await reader.ReadUInt16Async(),
                VersionNeededToExtract = ApkZip.CheckVersionSupported(await ZipVersion.ReadAsync(reader)),
                Flags = (EntryFlags) reader.ReadInt16(),
                CompressionMethod = (CompressionMethod) reader.ReadInt16(),
                LastModified = await Timestamp.ReadAsync(reader),
                Crc32 = await reader.ReadUInt32Async(),
                CompressedSize = await reader.ReadUInt32Async(),
                UncompressedSize = await reader.ReadUInt32Async(),
            };

            ushort fileNameLength = await reader.ReadUInt16Async();
            ushort extraFieldLength = await reader.ReadUInt16Async();
            ushort commentLength = await reader.ReadUInt16Async();

            inst.DiskNumberStart = await reader.ReadUInt16Async();
            if (inst.DiskNumberStart != 0)
            {
                throw new ZipFormatException("ZIP files split across multiple disks are not supported");
            }

            inst.InternalFileAttributes = await reader.ReadUInt16Async();
            inst.ExternalFileAttributes = await reader.ReadUInt32Async();
            inst.LocalHeaderOffset = await reader.ReadUInt32Async();

            if (fileNameLength != 0)
            {
                inst.FileName = await reader.ReadZipStringAsync(fileNameLength, inst.Flags);
            }

            if (extraFieldLength != 0)
            {
                inst.ExtraField = await reader.ReadBytesAsync(extraFieldLength);
            }

            if (commentLength != 0)
            {
                inst.FileComment = await reader.ReadZipStringAsync(fileNameLength, inst.Flags);
            }


            return inst;
        }

        public async Task WriteAsync(ZipMemory writer)
        {
            await writer.WriteAsync(Header);
            await writer.WriteAsync(VersionMadeBy);
            await VersionNeededToExtract.WriteAsync(writer);
            await writer.WriteAsync((short) Flags);
            await writer.WriteAsync((short) CompressionMethod);
            await LastModified.WriteAsync(writer);
            await writer.WriteAsync(Crc32);
            await writer.WriteAsync(CompressedSize);
            await writer.WriteAsync(UncompressedSize);

            byte[]? fileNameBytes = null;
            if (FileName != null)
            {
                fileNameBytes = Flags.GetStringEncoding().GetBytes(FileName);

                if (fileNameBytes.Length > ushort.MaxValue)
                {
                    throw new ZipDataException($"File name too long ({fileNameBytes.Length}). Max length {ushort.MaxValue}.");
                }

                await writer.WriteAsync((ushort) FileName.Length);
            }
            else
            {
                await writer.WriteAsync((ushort) 0);
            }

            if (ExtraField != null)
            {
                if (ExtraField.Length > ushort.MaxValue)
                {
                    throw new ZipDataException($"Extra field too long ({ExtraField.Length}). Max length {ushort.MaxValue}");
                }

                await writer.WriteAsync((ushort) ExtraField.Length);
            }
            else
            {
                await writer.WriteAsync((ushort) 0);
            }

            byte[]? fileCommentBytes = null;
            if (FileComment != null)
            {
                fileCommentBytes = Flags.GetStringEncoding().GetBytes(FileComment);

                if (fileCommentBytes.Length > ushort.MaxValue)
                {
                    throw new ZipDataException($"File comment too long ({fileCommentBytes.Length}). Max length {ushort.MaxValue}");
                }

                await writer.WriteAsync((ushort) fileCommentBytes.Length);
            }
            else
            {
                await writer.WriteAsync((ushort) 0);
            }

            await writer.WriteAsync(DiskNumberStart);
            await writer.WriteAsync(InternalFileAttributes);
            await writer.WriteAsync(ExternalFileAttributes);
            await writer.WriteAsync(LocalHeaderOffset);


            if (fileNameBytes != null)
            {
                await writer.WriteAsync(fileNameBytes);
            }

            if (ExtraField != null)
            {
                await writer.WriteAsync(ExtraField);
            }

            if (fileCommentBytes != null)
            {
                await writer.WriteAsync(fileCommentBytes);
            }
        }
    }
}
