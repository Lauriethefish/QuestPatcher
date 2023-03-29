using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Zip
{
    public class FileMemory : IDisposable
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
}
