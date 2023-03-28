using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace QuestPatcher.Core
{

    public class ApkAligner
    {
        class FileMemory : IDisposable
        {
            public Stream Stream { get; private set; }

            public long Position
            {
                get { return Stream.Position; }
                set { Stream.Position = value; }
            }

            public FileMemory(Stream stream)
            {
                this.Stream = stream;
            }

            public void Dispose()
            {
                Stream.Dispose();
            }

            public long Length()
            {
                return Stream.Length;
            }

            public void WriteBytes(byte[] bytes)
            {
                Stream.Write(bytes, 0, bytes.Length);
            }

            public byte[] ReadBytes(int count)
            {
                byte[] bytes = new byte[count];
                Stream.Read(bytes, 0, count);
                return bytes;
            }

            public short ReadShort()
            {
                return BitConverter.ToInt16(ReadBytes(2), 0);
            }

            public void WriteShort(short value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public int ReadInt()
            {
                return BitConverter.ToInt32(ReadBytes(4), 0);
            }

            public void WriteInt(int value)
            {
                WriteBytes(BitConverter.GetBytes(value));
            }

            public string ReadString(int count)
            {
                return Encoding.UTF8.GetString(ReadBytes(count));
            }

            public void WriteString(string value)
            {
                WriteBytes(Encoding.UTF8.GetBytes(value));
            }
        }

        class DD
        {
            public static readonly int SIGNATURE = 0x08074b50;

            public int CRC32 { get; set; }
            public int CompressedSize { get; set; }
            public int UncompressedSize { get; set; }

            public DD(FileMemory memory)
            {
                int signature = memory.ReadInt();
                if(signature != SIGNATURE)
                    throw new Exception("Invalid DD signature " + signature.ToString("X4"));
                CRC32 = memory.ReadInt();
                CompressedSize = memory.ReadInt();
                UncompressedSize = memory.ReadInt();
            }

            public void Write(FileMemory memory)
            {
                memory.WriteInt(SIGNATURE);
                memory.WriteInt(CRC32);
                memory.WriteInt(CompressedSize);
                memory.WriteInt(UncompressedSize);
            }

        }

        class LFH {

            public static readonly int SIGNATURE = 0x04034b50;

            public short VersionNeeded { get; set; }
            public short GeneralPurposeFlag { get; set; }
            public short CompressionMethod { get; set; }
            public short FileLastModificationTime { get; set; }
            public short FileLastModificationDate { get; set; }
            public int CRC32 { get; set; }
            public int CompressedSize { get; set; }
            public int UncompressedSize { get; set; }
            public short FileNameLength { get; set; }
            public short ExtraFieldLength { get; set; }
            public string FileName { get; set; }
            public byte[] ExtraField { get; set; }

            public LFH(FileMemory memory)
            {
                int signature = memory.ReadInt();
                if(signature != SIGNATURE)
                    throw new Exception("Invalid LFH signature " + signature.ToString("X4"));
                VersionNeeded = memory.ReadShort();
                GeneralPurposeFlag = memory.ReadShort();
                CompressionMethod = memory.ReadShort();
                FileLastModificationTime = memory.ReadShort();
                FileLastModificationDate = memory.ReadShort();
                CRC32 = memory.ReadInt();
                CompressedSize = memory.ReadInt();
                UncompressedSize = memory.ReadInt();
                FileNameLength = memory.ReadShort();
                ExtraFieldLength = memory.ReadShort();
                FileName = memory.ReadString(FileNameLength);
                ExtraField = memory.ReadBytes(ExtraFieldLength);
            }

            public void Write(FileMemory memory)
            {
                memory.WriteInt(SIGNATURE);
                memory.WriteShort(VersionNeeded);
                memory.WriteShort(GeneralPurposeFlag);
                memory.WriteShort(CompressionMethod);
                memory.WriteShort(FileLastModificationTime);
                memory.WriteShort(FileLastModificationDate);
                memory.WriteInt(CRC32);
                memory.WriteInt(CompressedSize);
                memory.WriteInt(UncompressedSize);
                memory.WriteShort(FileNameLength);
                memory.WriteShort(ExtraFieldLength);
                memory.WriteString(FileName);
                memory.WriteBytes(ExtraField);
            }

        }
        class CD
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
            public short FileNameLength { get; set; }
            public short ExtraFieldLength { get; set; }
            public short FileCommentLength { get; set; }
            public short DiskNumberFileStart { get; set; }
            public short InternalFileAttributes { get; set; }
            public int ExternalFileAttributes { get; set; }
            public int Offset { get; set; }
            public string FileName { get; set; }
            public byte[] ExtraField { get; set; }
            public string FileComment { get; set; }

            public CD(FileMemory memory)
            {
                int signature = memory.ReadInt();
                if(signature != SIGNATURE)
                    throw new Exception("Invalid CD signature " + signature.ToString("X4"));
                VersionMadeBy = memory.ReadShort();
                VersionNeeded = memory.ReadShort();
                GeneralPurposeFlag = memory.ReadShort();
                CompressionMethod = memory.ReadShort();
                FileLastModificationTime = memory.ReadShort();
                FileLastModificationDate = memory.ReadShort();
                CRC32 = memory.ReadInt();
                CompressedSize = memory.ReadInt();
                UncompressedSize = memory.ReadInt();
                FileNameLength = memory.ReadShort();
                ExtraFieldLength = memory.ReadShort();
                FileCommentLength = memory.ReadShort();
                DiskNumberFileStart = memory.ReadShort();
                InternalFileAttributes = memory.ReadShort();
                ExternalFileAttributes = memory.ReadInt();
                Offset = memory.ReadInt();
                FileName = memory.ReadString(FileNameLength);
                ExtraField = memory.ReadBytes(ExtraFieldLength);
                FileComment = memory.ReadString(FileCommentLength);
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
                memory.WriteShort(FileNameLength);
                memory.WriteShort(ExtraFieldLength);
                memory.WriteShort(FileCommentLength);
                memory.WriteShort(DiskNumberFileStart);
                memory.WriteShort(InternalFileAttributes);
                memory.WriteInt(ExternalFileAttributes);
                memory.WriteInt(Offset);
                memory.WriteString(FileName);
                memory.WriteBytes(ExtraField);
                memory.WriteString(FileComment);
            }
        }

        class EOCD
        {

            public static readonly int SIGNATURE = 0x06054b50;
            public short NumberOfDisk { get; set; }
            public short CDStartDisk { get; set; }
            public short NumberOfCDsOnDisk { get; set; }
            public short NumberOfCDs { get; set; }
            public int SizeOfCD { get; set; }
            public int OffsetOfCD { get; set; }
            public short CommentLength { get; set; }
            public string Comment { get; set; }

            public EOCD(FileMemory memory)
            {
                int signature = memory.ReadInt();
                if(signature != SIGNATURE)
                    throw new Exception("Invalid EOCD signature " + signature.ToString("X4"));
                NumberOfDisk = memory.ReadShort();
                CDStartDisk = memory.ReadShort();
                NumberOfCDsOnDisk = memory.ReadShort();
                NumberOfCDs = memory.ReadShort();
                SizeOfCD = memory.ReadInt();
                OffsetOfCD = memory.ReadInt();
                CommentLength = memory.ReadShort();
                Comment = memory.ReadString(CommentLength);
            }
            public void Write(FileMemory memory)
            {
                memory.WriteInt(SIGNATURE);
                memory.WriteShort(NumberOfDisk);
                memory.WriteShort(CDStartDisk);
                memory.WriteShort(NumberOfCDsOnDisk);
                memory.WriteShort(NumberOfCDs);
                memory.WriteInt(SizeOfCD);
                memory.WriteInt(OffsetOfCD);
                memory.WriteShort(CommentLength);
                memory.WriteString(Comment);
            }
        }

        public static void AlignApk(string path)
        {
            using FileStream fs = new FileStream(path, FileMode.Open);
            using FileMemory memory = new FileMemory(fs);
            using FileMemory outMemory = new FileMemory(new MemoryStream());
            memory.Position = memory.Length() - 22;
            while(memory.ReadInt() != EOCD.SIGNATURE)
            {
                memory.Position -= 4 + 1;
            }
            memory.Position -= 4;
            List<CD> cDs = new List<CD>();
            EOCD eocd = new EOCD(memory);
            if(eocd == null)
                return;
            memory.Position = eocd.OffsetOfCD;
            for(int i = 0; i < eocd.NumberOfCDsOnDisk; i++)
            {
                CD cd = new CD(memory);
                var nextCD = memory.Position;
                memory.Position = cd.Offset;
                LFH lfh = new LFH(memory);
                byte[] data = memory.ReadBytes(cd.CompressedSize);
                DD? dd = null;
                if((lfh.GeneralPurposeFlag & 0x08) != 0) 
                    dd = new DD(memory);
                if(lfh.CompressionMethod == 0) {
                    short padding = (short) ((outMemory.Position + 30 + lfh.FileNameLength + lfh.ExtraFieldLength) % 4);
                    if(padding > 0)
                    {
                        padding = (short) (4 - padding);
                        lfh.ExtraFieldLength += padding;
                        lfh.ExtraField = lfh.ExtraField.Concat(new byte[padding]).ToArray();
                    }
                }
                cd.Offset = (int) outMemory.Position;
                lfh.Write(outMemory);
                outMemory.WriteBytes(data);
                if(dd != null)
                    dd.Write(outMemory);
                cDs.Add(cd);
                memory.Position = nextCD;
            }
            eocd.OffsetOfCD = (int) outMemory.Position;
            foreach(CD cd in cDs)
            {
                cd.Write(outMemory);
            }
            eocd.Write(outMemory);
            fs.SetLength(0);
            outMemory.Stream.Position = 0;
            outMemory.Stream.CopyTo(fs);
            fs.Close();
        }

    }
}
