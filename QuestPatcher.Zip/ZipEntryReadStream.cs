using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QuestPatcher.Zip
{
    internal class ZipEntryReadStream : Stream
    {
        // The stream the ZIP file is read from
        private readonly Stream _stream;
        private readonly ApkZip _zip;

        private readonly long _entryDataOffset;
        private readonly uint _entryDataLength;

        private long _streamPosition;

        internal ZipEntryReadStream(Stream stream, ApkZip zip, long entryDataOffset, uint entryDataLength)
        {
            _stream = stream;
            _zip = zip;
            _entryDataOffset = entryDataOffset;
            _entryDataLength = entryDataLength;
            _streamPosition = entryDataOffset; // Start reading from the beginning of the entry
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _entryDataLength;

        public override long Position
        {
            get => _streamPosition - _entryDataOffset;
            set
            {
                if (value < 0 || value >= _entryDataLength)
                {
                    throw new ArgumentException("Attempted to seek to position outside of ZIP entry");
                }

                _streamPosition = value + _entryDataOffset;
            }
        }

        public override void Flush()
        {
            // No need for Flush, as writing is unsupported.
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesLeftInEntry = PrepareToReadBytes(count);
            int bytesRead = _stream.Read(buffer, offset, bytesLeftInEntry);

            // Store the stream position for the next read call.
            _streamPosition = _stream.Position;
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int bytesLeftInEntry = PrepareToReadBytes(count);
            int bytesRead = await _stream.ReadAsync(buffer, offset, bytesLeftInEntry, ct);

            // Store the stream position for the next read call.
            _streamPosition = _stream.Position;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
            {
                Position = offset;
            }
            else if (origin == SeekOrigin.End)
            {
                Position = _entryDataLength - offset;
            }
            else if (origin == SeekOrigin.Current)
            {
                Position += offset;
            }

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("SetLength not supported for ZIP entry reading");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Write not supported for ZIP entry reading");
        }

        public override void Close()
        {
            // ApkZip handles disposing the underlying Stream.
        }

        /// <summary>
        /// Returns the stream to the correct position to read data from the entry.
        /// Calculates the maximum number of bytes that can be read based on the length of the entry.
        /// </summary>
        /// <param name="count">The caller-specified maximum bytes to read into the buffer.</param>
        /// <returns>The number of bytes that can be safely read.</returns>
        private int PrepareToReadBytes(int count)
        {
            // Seek to the right position to start reading the entry data
            // Multiple entry streams may be open at once, so we must seek each time we read.
            _stream.Position = _streamPosition;

            // Do not permit reading beyond the end of the entry
            long bytesLeftInEntry = _entryDataLength - Position;

            return Math.Min(count, (int) bytesLeftInEntry);
        }
    }
}
