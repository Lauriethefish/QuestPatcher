using System.Threading.Tasks;

namespace QuestPatcher.Zip.Data
{
    /// <summary>
    /// The data stored about a file just before the file's contents.
    /// Most of this data is redundant, as it is already stored in CentralDirectoryHeader.
    /// 
    /// To get the length/crc of the file data, one should use the properties in CentralDirectoryHeader,
    /// as the ones in this file may be 0 if the ZIP data was streamed and the length was not known before compressing.
    /// </summary>
    internal class LocalFileHeader
    {
        private const uint Header = 0x04034b50;

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
        /// May be 0 if UsesDataDescriptor is set in Flags.
        /// </summary>
        public uint Crc32 { get; set; }

        /// <summary>
        /// Size of the compressed entry.
        /// May be 0 if UsesDataDescriptor is set in Flags.
        /// </summary>
        public uint CompressedSize { get; set; }

        /// <summary>
        /// Size of the entry when uncompressed.
        /// May be 0 if UsesDataDescriptor is set in Flags.
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


        public static LocalFileHeader Read(ZipMemory reader)
        {
            if (reader.ReadUInt32() != Header)
            {
                throw new ZipFormatException("Invalid LocalFileHeader signature");
            }

            var inst = new LocalFileHeader()
            {
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

            if (fileNameLength != 0)
            {
                inst.FileName = reader.ReadZipString(fileNameLength, inst.Flags);
            }

            if (extraFieldLength != 0)
            {
                inst.ExtraField = reader.ReadBytes(extraFieldLength);
            }

            return inst;
        }

        public void Write(ZipMemory writer)
        {
            writer.Write(Header);
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

            if (fileNameBytes != null)
            {
                writer.Write(fileNameBytes);
            }

            if (ExtraField != null)
            {
                writer.Write(ExtraField);
            }
        }

        public static async Task<LocalFileHeader> ReadAsync(ZipMemory reader)
        {
            if (await reader.ReadUInt32Async() != Header)
            {
                throw new ZipFormatException("Invalid LocalFileHeader signature");
            }

            var inst = new LocalFileHeader()
            {
                VersionNeededToExtract = ApkZip.CheckVersionSupported(await ZipVersion.ReadAsync(reader)),
                Flags = (EntryFlags) await reader.ReadInt16Async(),
                CompressionMethod = (CompressionMethod) await reader.ReadInt16Async(),
                LastModified = await Timestamp.ReadAsync(reader),
                Crc32 = await reader.ReadUInt32Async(),
                CompressedSize = await reader.ReadUInt32Async(),
                UncompressedSize = await reader.ReadUInt32Async(),
            };

            ushort fileNameLength = await reader.ReadUInt16Async();
            ushort extraFieldLength = await reader.ReadUInt16Async();

            if (fileNameLength != 0)
            {
                inst.FileName = await reader.ReadZipStringAsync(fileNameLength, inst.Flags);
            }

            if (extraFieldLength != 0)
            {
                inst.ExtraField = await reader.ReadBytesAsync(extraFieldLength);
            }

            return inst;
        }

        public async Task WriteAsync(ZipMemory writer)
        {
            await writer.WriteAsync(Header);
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

            if (fileNameBytes != null)
            {
                await writer.WriteAsync(fileNameBytes);
            }

            if (ExtraField != null)
            {
                await writer.WriteAsync(ExtraField);
            }
        }
    }
}
