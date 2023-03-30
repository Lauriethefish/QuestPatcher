using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Apk
{
    public class EndOfCentralDirectory
    {

        public static readonly int SIGNATURE = 0x06054b50;
        public short NumberOfDisk { get; set; }
        public short CDStartDisk { get; set; }
        public short NumberOfCDsOnDisk { get; set; }
        public short NumberOfCDs { get; set; }
        public int SizeOfCD { get; set; }
        public int OffsetOfCD { get; set; }
        public string Comment { get; set; }

        public EndOfCentralDirectory(FileMemory memory)
        {
            int signature = memory.ReadInt();
            if(signature != SIGNATURE)
                throw new Exception("Invalid EndOfCentralDirectory signature " + signature.ToString("X4"));
            NumberOfDisk = memory.ReadShort();
            CDStartDisk = memory.ReadShort();
            NumberOfCDsOnDisk = memory.ReadShort();
            NumberOfCDs = memory.ReadShort();
            SizeOfCD = memory.ReadInt();
            OffsetOfCD = memory.ReadInt();
            var commentLength = memory.ReadShort();
            Comment = memory.ReadString(commentLength);
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
            memory.WriteShort((short)FileMemory.StringLength(Comment));
            memory.WriteString(Comment);
        }

    }
}
