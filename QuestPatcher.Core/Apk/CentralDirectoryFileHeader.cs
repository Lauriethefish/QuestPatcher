using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Apk
{
    public class CentralDirectoryFileHeader
    {
        public static readonly int SIGNATURE = 0x02014b50;
        public short VersionMadeBy { get; set; }
        public short VersionNeeded { get; set; }
        public short GeneralPurposeFlag { get; set; }
        public short CompressionMethod { get; set; }
        public short FileLastModificationTime { get; set; }
        public short FileLastModificationDate { get; set; }
        public int CRC32 { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }
        public short DiskNumberFileStart { get; set; }
        public short InternalFileAttributes { get; set; }
        public int ExternalFileAttributes { get; set; }
        public int Offset { get; set; }
        public string FileName { get; set; }
        public byte[] ExtraField { get; set; }
        public string FileComment { get; set; }

        public CentralDirectoryFileHeader(FileMemory memory)
        {
            int signature = memory.ReadInt();
            if(signature != SIGNATURE)
                throw new Exception("Invalid CentralDirectoryFileHeader signature " + signature.ToString("X4"));
            VersionMadeBy = memory.ReadShort();
            VersionNeeded = memory.ReadShort();
            GeneralPurposeFlag = memory.ReadShort();
            CompressionMethod = memory.ReadShort();
            FileLastModificationTime = memory.ReadShort();
            FileLastModificationDate = memory.ReadShort();
            CRC32 = memory.ReadInt();
            CompressedSize = memory.ReadInt();
            UncompressedSize = memory.ReadInt();
            var fileNameLength = memory.ReadShort();
            var extraFieldLength = memory.ReadShort();
            var fileCommentLength = memory.ReadShort();
            DiskNumberFileStart = memory.ReadShort();
            InternalFileAttributes = memory.ReadShort();
            ExternalFileAttributes = memory.ReadInt();
            Offset = memory.ReadInt();
            FileName = memory.ReadString(fileNameLength);
            ExtraField = memory.ReadBytes(extraFieldLength);
            FileComment = memory.ReadString(fileCommentLength);
        }

        public void Write(FileMemory memory)
        {
            memory.WriteInt(SIGNATURE);
            memory.WriteShort(VersionMadeBy);
            memory.WriteShort(VersionNeeded);
            memory.WriteShort(GeneralPurposeFlag);
            memory.WriteShort(CompressionMethod);
            memory.WriteShort(FileLastModificationTime);
            memory.WriteShort(FileLastModificationDate);
            memory.WriteInt(CRC32);
            memory.WriteInt(CompressedSize);
            memory.WriteInt(UncompressedSize);
            memory.WriteShort((short)FileMemory.StringLength(FileName));
            memory.WriteShort((short)ExtraField.Length);
            memory.WriteShort((short) FileMemory.StringLength(FileComment));
            memory.WriteShort(DiskNumberFileStart);
            memory.WriteShort(InternalFileAttributes);
            memory.WriteInt(ExternalFileAttributes);
            memory.WriteInt(Offset);
            memory.WriteString(FileName);
            memory.WriteBytes(ExtraField);
            memory.WriteString(FileComment);
        }
    }
}
