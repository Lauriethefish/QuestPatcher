using System.IO;
using System.Threading.Tasks;
using QuestPatcher.Zip.Data;

namespace QuestPatcher.Zip
{
    /// <summary>
    /// Replacement for BinaryReader that supports async operations.
    /// Is not disposable, so will NOT close the underlying stream.
    /// 
    /// All values read/written are little-endian.
    /// </summary>
    internal class ZipMemory
    {
        private readonly Stream _stream;
        private readonly byte[] _buffer = new byte[4];

        /// <summary>
        /// Creates a reader for the given stream.
        /// </summary>
        /// <param name="stream">The stream to read the data from</param>
        public ZipMemory(Stream stream)
        {
            _stream = stream;
        }

        private void FillBuffer(int bytes)
        {
            _stream.Read(_buffer, 0, bytes);
        }

        private void WriteBuffer(int bytes)
        {
            _stream.Write(_buffer, 0, bytes);
        }

        /// <summary>
        /// Reads a ushort from the stream.
        /// </summary>
        /// <returns>The ushort read.</returns>
        public ushort ReadUInt16()
        {
            FillBuffer(2);
            return (ushort) (_buffer[0] | (_buffer[1] << 8));
        }

        /// <summary>
        /// Reads a short from the stream.
        /// </summary>
        /// <returns>The short read.</returns>
        public short ReadInt16()
        {
            FillBuffer(2);
            return (short) (_buffer[0] | (_buffer[1] << 8));
        }

        /// <summary>
        /// Reads a uint from the stream.
        /// </summary>
        /// <returns>The uint read.</returns>
        public uint ReadUInt32()
        {
            FillBuffer(4);
            return (uint) (_buffer[0] | (_buffer[1] << 8) | (_buffer[2] << 16) | (_buffer[3] << 24));
        }

        /// <summary>
        /// Reads an int from the stream.
        /// </summary>
        /// <returns>The int read.</returns>
        public int ReadInt32()
        {
            FillBuffer(4);
            return (_buffer[0] | (_buffer[1] << 8) | (_buffer[2] << 16) | (_buffer[3] << 24));
        }

        /// <summary>
        /// Reads a byte array from the stream.
        /// </summary>
        /// <param name="length">The number of bytes to read.</param>
        /// <returns>The bytes read.</returns>
        public byte[] ReadBytes(int length)
        {
            byte[] buffer = new byte[length];
            _stream.Read(buffer, 0, length);

            return buffer;
        }

        /// <summary>
        /// Reads a string from a ZIP record.
        /// </summary>
        /// <param name="length">Length of the string, in bytes</param>
        /// <param name="flags">The general purpose flags from whichever record this string is being read.
        /// These are necessary to check if the string must be read in UTF-8 format</param>
        /// <returns>The string read.</returns>
        public string ReadZipString(int length, EntryFlags flags)
        {
            byte[] bytes = ReadBytes(length);

            return flags.GetStringEncoding().GetString(bytes);
        }

        /// <summary>
        /// Reads a byte from the stream.
        /// </summary>
        /// <returns>The byte read.</returns>
        public byte ReadByte()
        {
            return (byte) _stream.ReadByte();
        }

        /// <summary>
        /// Writes a ushort to the stream.
        /// </summary>
        /// <param name="value">ushort to write.</param>
        public void Write(ushort value)
        {
            _buffer[0] = (byte) value;
            _buffer[1] = (byte) (value >> 8);
            WriteBuffer(2);
        }

        /// <summary>
        /// Writes a short to the stream.
        /// </summary>
        /// <param name="value">short to write.</param>
        public void Write(short value)
        {
            _buffer[0] = (byte) value;
            _buffer[1] = (byte) (value >> 8);
            WriteBuffer(2);
        }

        /// <summary>
        /// Writes a uint to the stream.
        /// </summary>
        /// <param name="value">uint to write.</param>
        public void Write(uint value)
        {
            _buffer[0] = (byte) value;
            _buffer[1] = (byte) (value >> 8);
            _buffer[2] = (byte) (value >> 16);
            _buffer[3] = (byte) (value >> 24);
            WriteBuffer(4);
        }

        /// <summary>
        /// Writes an int to the stream.
        /// </summary>
        /// <param name="value">int to write.</param>
        public void Write(int value)
        {
            _buffer[0] = (byte) value;
            _buffer[1] = (byte) (value >> 8);
            _buffer[2] = (byte) (value >> 16);
            _buffer[3] = (byte) (value >> 24);
            WriteBuffer(4);
        }

        /// <summary>
        /// Writes a byte to the stream.
        /// </summary>
        /// <param name="value">The byte to write.</param>
        public void Write(byte value)
        {
            _stream.WriteByte(value);
        }

        /// <summary>
        /// Writes the given bytes to the stream.
        /// </summary>
        /// <param name="value">The bytes to write to the stream.</param>
        public void Write(byte[] value)
        {
            _stream.Write(value, 0, value.Length);
        }

        private async Task FillBufferAsync(int bytes)
        {
            await _stream.ReadAsync(_buffer, 0, bytes);
        }

        private async Task WriteBufferAsync(int bytes)
        {
            await _stream.WriteAsync(_buffer, 0, bytes);
        }

        /// <summary>
        /// Reads a ushort from the stream.
        /// </summary>
        /// <returns>The ushort read.</returns>
        public async Task<ushort> ReadUInt16Async()
        {
            await FillBufferAsync(2);
            return (ushort) (_buffer[0] | (_buffer[1] << 8));
        }

        /// <summary>
        /// Reads a short from the stream.
        /// </summary>
        /// <returns>The short read.</returns>
        public async Task<short> ReadInt16Async()
        {
            await FillBufferAsync(2);
            return (short) (_buffer[0] | (_buffer[1] << 8));
        }

        /// <summary>
        /// Reads a uint from the stream.
        /// </summary>
        /// <returns>The uint read.</returns>
        public async Task<uint> ReadUInt32Async()
        {
            await FillBufferAsync(4);
            return (uint) (_buffer[0] | (_buffer[1] << 8) | (_buffer[2] << 16) | (_buffer[3] << 24));
        }

        /// <summary>
        /// Reads an int from the stream.
        /// </summary>
        /// <returns>The int read.</returns>
        public async Task<int> ReadInt32Async()
        {
            await FillBufferAsync(4);
            return (_buffer[0] | (_buffer[1] << 8) | (_buffer[2] << 16) | (_buffer[3] << 24));
        }

        /// <summary>
        /// Reads a byte from the stream.
        /// </summary>
        /// <returns>The byte read.</returns>
        public async Task<byte> ReadByteAsync()
        {
            await FillBufferAsync(1);
            return _buffer[0];
        }

        /// <summary>
        /// Reads a byte array from the stream.
        /// </summary>
        /// <param name="length">The number of bytes to read.</param>
        /// <returns>The bytes read.</returns>
        public async Task<byte[]> ReadBytesAsync(int length)
        {
            byte[] buffer = new byte[length];
            await _stream.ReadAsync(buffer, 0, length);

            return buffer;
        }

        /// <summary>
        /// Reads a string from a ZIP record.
        /// </summary>
        /// <param name="length">Length of the string, in bytes</param>
        /// <param name="flags">The general purpose flags from whichever record this string is being read.
        /// These are necessary to check if the string must be read in UTF-8 format</param>
        /// <returns>The string read.</returns>
        public async Task<string> ReadZipStringAsync(int length, EntryFlags flags)
        {
            byte[] bytes = await ReadBytesAsync(length);

            return flags.GetStringEncoding().GetString(bytes);
        }

        /// <summary>
        /// Writes a ushort to the stream.
        /// </summary>
        /// <param name="value">ushort to write.</param>
        public async Task WriteAsync(ushort value)
        {
            _buffer[0] = (byte) value;
            _buffer[1] = (byte) (value >> 8);
            await WriteBufferAsync(2);
        }

        /// <summary>
        /// Writes a short to the stream.
        /// </summary>
        /// <param name="value">short to write.</param>
        public async Task WriteAsync(short value)
        {
            _buffer[0] = (byte) value;
            _buffer[1] = (byte) (value >> 8);
            await WriteBufferAsync(2);
        }

        /// <summary>
        /// Writes a uint to the stream.
        /// </summary>
        /// <param name="value">uint to write.</param>
        public async Task WriteAsync(uint value)
        {
            _buffer[0] = (byte) value;
            _buffer[1] = (byte) (value >> 8);
            _buffer[2] = (byte) (value >> 16);
            _buffer[3] = (byte) (value >> 24);
            await WriteBufferAsync(4);
        }

        /// <summary>
        /// Writes an int to the stream.
        /// </summary>
        /// <param name="value">int to write.</param>
        public async Task WriteAsync(int value)
        {
            _buffer[0] = (byte) value;
            _buffer[1] = (byte) (value >> 8);
            _buffer[2] = (byte) (value >> 16);
            _buffer[3] = (byte) (value >> 24);
            await WriteBufferAsync(4);
        }

        /// <summary>
        /// Writes a byte to the stream.
        /// </summary>
        /// <param name="value">byte to write.</param>
        public async Task WriteAsync(byte value)
        {
            _buffer[0] = value;
            await WriteBufferAsync(1);
        }

        /// <summary>
        /// Writes the given bytes to the stream.
        /// </summary>
        /// <param name="value">The bytes to write to the stream.</param>
        public async Task WriteAsync(byte[] value)
        {
            await _stream.WriteAsync(value, 0, value.Length);
        }
    }
}
