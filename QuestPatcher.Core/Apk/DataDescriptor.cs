using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Apk
{
    public class DataDescriptor
    {
        public static readonly int SIGNATURE = 0x08074b50;

        public int CRC32 { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }

        public DataDescriptor(FileMemory memory)
        {
            int signature = memory.ReadInt();
            if(signature != SIGNATURE)
                throw new Exception("Invalid DataDescriptor signature " + signature.ToString("X4"));
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
}
