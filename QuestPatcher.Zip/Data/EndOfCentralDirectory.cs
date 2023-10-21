namespace QuestPatcher.Zip.Data
{
    /// <summary>
    /// The record that marks the end of the central directory of a ZIP file.
    /// Found at the end of the ZIP file
    /// </summary>
    internal class EndOfCentralDirectory
    {
        public const uint Header = 0x06054b50;

        /// <summary>
        /// The number of the disk that this record marks the end of.
        /// Multiple disks aren't supported, so this should always be 0.
        /// </summary>
        public ushort NumberOfThisDisk { get; set; }

        /// <summary>
        /// Multiple disks are not supported, this should always be 0.
        /// </summary>
        public ushort StartOfCentralDirectoryDisk { get; set; }

        /// <summary>
        /// The number of central directory records on this disk.
        /// This should equal the total number of central directory records, since multiple disks aren't supported.
        /// </summary>
        public ushort CentralDirectoryRecordsOnDisk { get; set; }

        /// <summary>
        /// The total number of central directory records.
        /// </summary>
        public ushort CentralDirectoryRecords { get; set; }

        /// <summary>
        /// The size, in bytes, of the whole central directory. (all records)
        /// </summary>
        public uint CentralDirectorySize { get; set; }

        /// <summary>
        /// The offset of the first central directory record from the start of the file.
        /// </summary>
        public uint CentralDirectoryOffset { get; set; }

        /// <summary>
        /// An arbitrary comment.
        /// </summary>
        public byte[]? Comment { get; set; }

        public static EndOfCentralDirectory Read(BinaryReader reader)
        {
            if(reader.ReadUInt32() != Header)
            {
                throw new FormatException("Invalid EndOfCentralDirectory signature");
            }

            var inst = new EndOfCentralDirectory()
            {
                NumberOfThisDisk = reader.ReadUInt16(),
                StartOfCentralDirectoryDisk = reader.ReadUInt16(),
                CentralDirectoryRecordsOnDisk = reader.ReadUInt16(),
                CentralDirectoryRecords = reader.ReadUInt16(),
                CentralDirectorySize = reader.ReadUInt32(),
                CentralDirectoryOffset = reader.ReadUInt32(),
            };

            if(inst.CentralDirectoryRecords != inst.CentralDirectoryRecordsOnDisk || inst.NumberOfThisDisk != 0 || inst.StartOfCentralDirectoryDisk != 0)
            {
                throw new ZipFormatException("ZIP files split across multiple disks are not supported");
            }

            var commentLength = reader.ReadUInt16();

            // NB: At this point we have no general flags short, so we don't know what the encoding is for the comment.
            // For this reason, we will keep it as a byte array.
            if(commentLength != 0)
            {
                inst.Comment = reader.ReadBytes(commentLength);
            }

            return inst;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Header);
            writer.Write(NumberOfThisDisk);
            writer.Write(StartOfCentralDirectoryDisk);
            writer.Write(CentralDirectoryRecordsOnDisk);
            writer.Write(CentralDirectoryRecords);
            writer.Write(CentralDirectorySize);
            writer.Write(CentralDirectoryOffset);

            if(Comment != null)
            {
                if(Comment.Length > ushort.MaxValue)
                {
                    throw new ZipDataException($"End of central directory comment too long: max length: {ushort.MaxValue}, got {Comment.Length}");
                }
                writer.Write((ushort) Comment.Length);
                writer.Write(Comment);
            }
            else
            {
                writer.Write((ushort) 0);
            }
        }
    }
}
