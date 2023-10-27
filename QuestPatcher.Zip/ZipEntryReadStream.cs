using System;
using System.IO;

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
            // Seek to the right position to start reading the entry data
            // Multiple entry streams may be open at once, so we must seek each time we read.
            _stream.Position = _streamPosition;

            // Do not permit reading beyond the end of the entry
            long bytesLeftInStream = _entryDataLength - Position;
            int bytesRead = _stream.Read(buffer, offset, (int) Math.Min(bytesLeftInStream, count));

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
                Position = _entryDataLength - 1 - offset;
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
    }
}
