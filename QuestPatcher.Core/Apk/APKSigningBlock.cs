using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Apk
{
    public class APKSigningBlock
    {
        public class IDValuePair
        {

            public uint ID { get; private set; }
            public int Value { get; private set; }
            public byte[]? Data { get; private set; }

            public IDValuePair(uint id, int value)
            {
                ID = id;
                Value = value;
                Data = null;
            }

            public IDValuePair(uint id, byte[] value)
            {
                ID = id;
                Value = value.Length;
                Data = value;
            }

            public int Length()
            {
                return 8 + 4 + Data?.Length ?? 4;
            }

            public void Write(FileMemory memory)
            {
                memory.WriteULong((ulong) Length() - 8);
                memory.WriteUInt(ID);
                if(Data == null)
                {
                    memory.WriteInt(Value);
                } else
                {
                    memory.WriteBytes(Data);
                }
            }

        }

        public static readonly string MAGIC = "APK Sig Block 42";

        public List<IDValuePair> Values { get; private set; }

        public APKSigningBlock()
        {
            Values = new List<IDValuePair>();
        }

        public void Write(FileMemory memory)
        {
            ulong size = (ulong) Values.Sum(values => values.Length()) + 8 + 16;
            memory.WriteULong(size);
            Values.ForEach(value => value.Write(memory));
            memory.WriteULong(size);
            memory.WriteString(MAGIC);
        }

    }
}
