using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using QuestPatcher.Core.Apk;

namespace QuestPatcher.Core
{

    public class ApkAligner
    {
        
        public static void AlignApk(string path)
        {
            using FileStream fs = new FileStream(path, FileMode.Open);
            using FileMemory memory = new FileMemory(fs);
            using FileMemory outMemory = new FileMemory(new MemoryStream());
            memory.Position = memory.Length() - 22;
            while(memory.ReadInt() != EndOfCentralDirectory.SIGNATURE)
            {
                memory.Position -= 4 + 1;
            }
            memory.Position -= 4;
            List<CentralDirectoryFileHeader> cDs = new List<CentralDirectoryFileHeader>();
            EndOfCentralDirectory eocd = new EndOfCentralDirectory(memory);
            if(eocd == null)
                return;
            memory.Position = eocd.OffsetOfCD;
            for(int i = 0; i < eocd.NumberOfCDsOnDisk; i++)
            {
                CentralDirectoryFileHeader cd = new CentralDirectoryFileHeader(memory);
                var nextCD = memory.Position;
                memory.Position = cd.Offset;
                LocalFileHeader lfh = new LocalFileHeader(memory);
                byte[] data = memory.ReadBytes(cd.CompressedSize);
                DataDescriptor? dd = null;
                if((lfh.GeneralPurposeFlag & 0x08) != 0) 
                    dd = new DataDescriptor(memory);
                if(lfh.CompressionMethod == 0) {
                    short padding = (short) ((outMemory.Position + 30 + FileMemory.StringLength(lfh.FileName) + lfh.ExtraField.Length) % 4);
                    if(padding > 0)
                    {
                        padding = (short) (4 - padding);
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
            foreach(CentralDirectoryFileHeader cd in cDs)
            {
                cd.Write(outMemory);
            }
            eocd.NumberOfCDs = (short) cDs.Count;
            eocd.NumberOfCDsOnDisk = (short) cDs.Count;
            eocd.SizeOfCD = (int) (outMemory.Position - eocd.OffsetOfCD);
            eocd.Write(outMemory);
            fs.SetLength(0);
            outMemory.Stream.Position = 0;
            outMemory.Stream.CopyTo(fs);
            fs.Close();
        }

    }
}
