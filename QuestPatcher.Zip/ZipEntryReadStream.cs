namespace QuestPatcher.Zip
{
    internal class ZipEntryReadStream : Stream
    {
        // The stream the ZIP file is read from
        private readonly Stream _stream;
        private readonly ApkZip _zip;

        private readonly long _entryDataOffset;
        private readonly uint _entryDataLength;

        internal ZipEntryReadStream(Stream stream, ApkZip zip, long entryDataOffset, uint entryDataLength)
        {
            _stream = stream;
            _zip = zip;
            _entryDataOffset = entryDataOffset;
            _entryDataLength = entryDataLength;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _entryDataLength;

        public override long Position
        {
            get => _stream.Position - _entryDataOffset;
            set
            {
                if (value < 0 || value >= _entryDataLength)
                {
                    throw new ArgumentException("Attempted to seek to position outside of ZIP entry");
                }

                _stream.Position = value + _entryDataOffset;
            }
        }

        public override void Flush()
        {
            // Necessary data will be flushed when the ZIP file is saved
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long bytesLeftInStream = _entryDataLength - Position;

            // Do not permit reading beyond the end of the entry
            return _stream.Read(buffer, offset, (int) Math.Min(bytesLeftInStream, count));
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
            _zip.FinishReading();
        }
    }
}
