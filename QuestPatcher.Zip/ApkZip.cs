using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;
using QuestPatcher.Zip.Data;
using CompressionMethod = QuestPatcher.Zip.Data.CompressionMethod;

namespace QuestPatcher.Zip
{
    /// <summary>
    /// ZIP implementation used for reading APK files.
    /// Not thread safe; an ApkZip should only ever be called upon by one thread.
    /// Similarly, read streams must only be called upon by one thread. Multiple can be open at once, however.
    /// 
    /// Disposing this class will automatically sign the APK.
    /// </summary>
    public class ApkZip : IAsyncDisposable, IDisposable
    {
        internal static ZipVersion MaxSupportedVersion = new ZipVersion
        {
            Major = 2,
            Minor = 0
        };

        /// <summary>
        /// The PEM data of the certificate to sign the APK with.
        /// </summary>
        public string CertificatePem
        {
            set
            {
                var (cert, privateKey) = SigningUtility.LoadCertificate(value);
                if (cert == null)
                {
                    throw new ArgumentException("No certificate in given PEM data");
                }

                if (privateKey == null)
                {
                    throw new ArgumentException("No private key in given PEM data");
                }

                _certificate = cert;
                _privateKey = privateKey;
            }
        }

        /// <summary>
        /// The full file names of each entry inside the APK.
        /// </summary>
        public ICollection<string> Entries => _centralDirectoryRecords.Keys;

        private readonly Dictionary<string, CentralDirectoryFileHeader> _centralDirectoryRecords;
        private Dictionary<string, string>? _existingHashes;
        private long _postFilesOffset;
        private readonly Stream _stream;
        private readonly ZipMemory _memory;

        private AsymmetricKeyParameter? _privateKey;
        private X509Certificate? _certificate;

        private bool _disposed = false;

        private ApkZip(Dictionary<string, CentralDirectoryFileHeader> centralDirectoryRecords, long postFilesOffset, Stream stream, ZipMemory memory)
        {
            _centralDirectoryRecords = centralDirectoryRecords;
            _postFilesOffset = postFilesOffset;
            _stream = stream;
            _memory = memory;

            // Use the default certificate until another is assigned
            CertificatePem = Certificates.DefaultCertificatePem;
        }

        /// <summary>
        /// Opens an APK's ZIP file.
        /// </summary>
        /// <param name="stream">The stream to load the APK ZIP from. Must support seeking and reading.
        /// May support writing.</param>
        /// <returns>The opened ZIP file.</returns>
        /// <exception cref="ArgumentException">If the stream does not support seeking or reading</exception>
        /// <exception cref="ZipFormatException">If the ZIP file cannot be loaded by this ZIP implementation.</exception>
        public static ApkZip Open(Stream stream)
        {
            if (!stream.CanSeek || !stream.CanRead)
            {
                throw new ArgumentException("Must have seekable and readable stream.");
            }

            var memory = new ZipMemory(stream);

            // A ZIP file must end with an end of central directory record, which is, at minimum, 22 bytes long.
            // The record starts with 4 header bytes, so we will locate these first.
            stream.Position = stream.Length - 22;

            while (memory.ReadUInt32() != EndOfCentralDirectory.Header)
            {
                // If we have just read from the start of the stream and there is still no EOCD signature, then this isn't a valid ZIP file
                if (stream.Position == 0 + 4)
                {
                    throw new ZipFormatException("File is not a valid ZIP archive: No end of central directory record found");
                }

                // Move 1 byte before the 4 bytes we just read.
                stream.Position -= (4 + 1);
            }

            stream.Position -= 4;
            var eocd = EndOfCentralDirectory.Read(memory);

            stream.Position = eocd.CentralDirectoryOffset;

            // TODO: Possibly add support for duplicate files, and unnamed files, depending on whether android accepts then in APKs
            // Right now they will both trigger an exception.
            var centralDirectoryRecords = new Dictionary<string, CentralDirectoryFileHeader>();
            CentralDirectoryFileHeader? lastRecord = null;
            for (int i = 0; i < eocd.CentralDirectoryRecords; i++)
            {
                var record = CentralDirectoryFileHeader.Read(memory);
                ValidateCentralDirectoryRecord(centralDirectoryRecords, record);
                if (record.LocalHeaderOffset >= (lastRecord?.LocalHeaderOffset ?? uint.MinValue))
                {
                    lastRecord = record;
                }
            }

            // Find the position of the first byte after the last local header
            if (lastRecord == null)
            {
                stream.Position = 0;
            }
            else
            {
                SeekToEndOfEntry(stream, lastRecord);
            }
            long postFilesOffset = stream.Position;
            stream.Position = 0;


            var apkZip = new ApkZip(centralDirectoryRecords, postFilesOffset, stream, memory);
            if (stream.CanWrite)
            {
                // Load the digests of each file from the signature. When we re-sign the APK later, we then only need to hash the changed files.
                apkZip._existingHashes = JarSigner.CollectExistingHashes(apkZip);
            }

            // Delete the existing eocd and central directory.
            // This isn't strictly necessary, and we could also just append our new files and then a new central directory
            // BUT we might as well do it since it saves space and doesn't involve pushing too many bytes around
            // It is done NOW since we don't want to overwrite any new files that may be added later.
            if (stream.CanWrite)
            {
                stream.SetLength(postFilesOffset);
            }

            return apkZip;
        }

        /// <summary>
        /// Opens an APK's ZIP file, asynchronously.
        /// </summary>
        /// <param name="stream">The stream to load the APK ZIP from. Must support seeking and reading.
        /// May support writing.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The opened ZIP file.</returns>
        /// <exception cref="ArgumentException">If the stream does not support seeking or reading</exception>
        /// <exception cref="ZipFormatException">If the ZIP file cannot be loaded by this ZIP implementation.</exception>
        /// <exception cref="OperationCanceledException">If operation cancelled.</exception>
        public static async Task<ApkZip> OpenAsync(Stream stream, CancellationToken ct = default)
        {
            if (!stream.CanSeek || !stream.CanRead)
            {
                throw new ArgumentException("Must have seekable and readable stream.");
            }

            var memory = new ZipMemory(stream);
            stream.Position = stream.Length - 22;
            while (await memory.ReadUInt32Async() != EndOfCentralDirectory.Header)
            {
                if (stream.Position == 0 + 4)
                {
                    throw new ZipFormatException("File is not a valid ZIP archive: No end of central directory record found");
                }

                stream.Position -= (4 + 1);
                ct.ThrowIfCancellationRequested();
            }
            stream.Position -= 4;

            var eocd = await EndOfCentralDirectory.ReadAsync(memory);
            stream.Position = eocd.CentralDirectoryOffset;

            var centralDirectoryRecords = new Dictionary<string, CentralDirectoryFileHeader>();
            CentralDirectoryFileHeader? lastRecord = null;
            for (int i = 0; i < eocd.CentralDirectoryRecords; i++)
            {
                var record = await CentralDirectoryFileHeader.ReadAsync(memory);
                ValidateCentralDirectoryRecord(centralDirectoryRecords, record);
                if (record.LocalHeaderOffset >= (lastRecord?.LocalHeaderOffset ?? uint.MinValue))
                {
                    lastRecord = record;
                }
                ct.ThrowIfCancellationRequested();
            }

            if (lastRecord == null)
            {
                stream.Position = 0;
            }
            else
            {
                await SeekToEndOfEntryAsync(stream, lastRecord);
            }
            long postFilesOffset = stream.Position;
            stream.Position = 0;

            var apkZip = new ApkZip(centralDirectoryRecords, postFilesOffset, stream, memory);
            if (stream.CanWrite)
            {
                apkZip._existingHashes = await JarSigner.CollectExistingHashesAsync(apkZip, ct);
            }

            if (stream.CanWrite)
            {
                stream.SetLength(postFilesOffset);
            }

            return apkZip;
        }

        /// <summary>
        /// Checks whether or not the ZIP contains a file.
        /// </summary>
        /// <param name="fileName">The file name</param>
        /// <returns>True if the ZIP contains an entry with this name</returns>
        public bool ContainsFile(string fileName)
        {
            ThrowIfDisposed();

            return _centralDirectoryRecords.ContainsKey(NormaliseFileName(fileName));
        }

        /// <summary>
        /// Gets the CRC-32 digest of the file with the given name within the APK.
        /// </summary>
        /// <param name="fileName">The full path to the file</param>
        /// <exception cref="ArgumentException">If no file with the given name exists within the APK</exception>
        /// <returns>The CRC-32 of the file</returns>
        public uint GetCrc32(string fileName)
        {
            ThrowIfDisposed();

            fileName = NormaliseFileName(fileName);
            if (!ContainsFile(fileName))
            {
                throw new ArgumentException($"No file with name {fileName} exists within the ZIP");
            }

            return _centralDirectoryRecords[fileName].Crc32;
        }

        /// <summary>
        /// Removes a file from the ZIP.
        /// </summary>
        /// <param name="fileName">The file name to remove</param>
        /// <returns>true if the file existed in the ZIP and was thus removed, false otherwise</returns>
        /// <exception cref="InvalidOperationException">If this ZIP file is readonly</exception>
        public bool RemoveFile(string fileName)
        {
            ThrowIfDisposed();

            fileName = NormaliseFileName(fileName);
            if (!_stream.CanWrite)
            {
                throw new InvalidOperationException("Attempted to delete a file in a readonly ZIP file");
            }
            _existingHashes?.Remove(fileName);
            return _centralDirectoryRecords.Remove(fileName);
        }

        /// <summary>
        /// Opens a stream to read a file in the APK.
        /// </summary>
        /// <param name="fileName">The name of the file to open.</param>
        /// <returns>A stream that can be used to read from the file. Must only be read from the thread that opened it.</returns>
        /// <exception cref="ArgumentException">If no file with the given name exists within the APK</exception>
        /// <exception cref="ZipFormatException">If an unsupported compression mode is encountered. Currently only STORE and DEFLATE are supported.</exception>
        public Stream OpenReader(string fileName)
        {
            ThrowIfDisposed();

            var centralDirectoryHeader = GetRecordOrThrow(fileName);
            _stream.Position = centralDirectoryHeader.LocalHeaderOffset;
            var _ = LocalFileHeader.Read(_memory); // LocalFileHeader currently doesn't contain any information we need

            var entryStream = new ZipEntryReadStream(_stream, this, _stream.Position, centralDirectoryHeader.CompressedSize);

            return GetDecompressor(centralDirectoryHeader.CompressionMethod, entryStream);
        }

        /// <summary>
        /// Opens a stream to read a file in the APK.
        /// </summary>
        /// <param name="fileName">The name of the file to open.</param>
        /// <returns>A stream that can be used to read from the file. Must only be read from the thread that opened it.</returns>
        /// <exception cref="ArgumentException">If no file with the given name exists within the APK</exception>
        /// <exception cref="ZipFormatException">If an unsupported compression mode is encountered. Currently only STORE and DEFLATE are supported.</exception>
        public async Task<Stream> OpenReaderAsync(string fileName)
        {
            ThrowIfDisposed();

            var centralDirectoryHeader = GetRecordOrThrow(fileName);
            _stream.Position = centralDirectoryHeader.LocalHeaderOffset;
            var _ = await LocalFileHeader.ReadAsync(_memory);

            var entryStream = new ZipEntryReadStream(_stream, this, _stream.Position, centralDirectoryHeader.CompressedSize);

            return GetDecompressor(centralDirectoryHeader.CompressionMethod, entryStream);
        }

        /// <summary>
        /// Copies the data from a stream to an entry on the ZIP.
        /// If the file exists, it will be deleted first and a new entry for the file appended to the ZIP.
        /// </summary>
        /// <param name="fileName">The name/path of the file to write to</param>
        /// <param name="sourceData">The stream containing data to copy to the file. Must support the Length property and reading.</param>
        /// <param name="compressionLevel">The (DEFLATE) compression level to use. If null, the STORE method will be used for the file.</param>
        public void AddFile(string fileName, Stream sourceData, CompressionLevel? compressionLevel)
        {
            ThrowIfDisposed();
            fileName = NormaliseFileName(fileName);

            RemoveFile(fileName);

            // Move to a position after the last ZIP entry
            _stream.Position = _postFilesOffset;
            long localHeaderOffset = _stream.Position;

            byte[] fileNameBytes = ((EntryFlags) 0).GetStringEncoding().GetBytes(fileName);

            // Move past where the local file header for this entry will go.
            _stream.Position += 30 + fileNameBytes.Length;
            long dataOffset = _stream.Position;

            // Copy the data into the entry, calculating the Crc32 at the same time.
            // TODO: Could align files using STORE to 4 bytes like zipalign does
            // However, this is likely unnecessary, as this is only important for large (e.g. media) files to allow them to be read with mmap.
            Stream? compressor = null;
            uint crc32;
            CompressionMethod compressionMethod;
            try
            {
                (compressor, compressionMethod) = GetCompressor(compressionLevel);
                crc32 = sourceData.CopyToCrc32(compressor);
            }
            finally
            {
                if (compressor != null && compressor != _stream)
                {
                    compressor.Dispose();
                }
            }

            long postEntryDataOffset = _stream.Position;
            long compressedSize = postEntryDataOffset - dataOffset;
            long uncompressedSize = sourceData.Length;
            var (centralDirectoryHeader, localHeader) = CreateFileHeaders(
                fileName,
                compressionMethod,
                (uint) compressedSize,
                (uint) uncompressedSize,
                crc32,
                localHeaderOffset
            );

            // Write the entry's local header
            _stream.Position = localHeaderOffset;
            localHeader.Write(_memory);
            _centralDirectoryRecords[fileName] = centralDirectoryHeader;

            // Update the position where the next file will be stored.
            _postFilesOffset = postEntryDataOffset;
        }

        /// <summary>
        /// Copies the data from a stream to an entry on the ZIP.
        /// If the file exists, it will be deleted first and a new entry for the file appended to the ZIP.
        /// </summary>
        /// <param name="fileName">The name/path of the file to write to</param>
        /// <param name="sourceData">The stream containing data to copy to the file. Must support the Length property and reading.</param>
        /// <param name="compressionLevel">The (DEFLATE) compression level to use. If null, the STORE method will be used for the file.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="OperationCanceledException">If <paramref name="ct"/> is cancelled.</exception>
        public async Task AddFileAsync(string fileName, Stream sourceData, CompressionLevel? compressionLevel, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            fileName = NormaliseFileName(fileName);
            RemoveFile(fileName);

            _stream.Position = _postFilesOffset;
            long localHeaderOffset = _stream.Position;

            byte[] fileNameBytes = ((EntryFlags) 0).GetStringEncoding().GetBytes(fileName);
            _stream.Position += 30 + fileNameBytes.Length;
            long dataOffset = _stream.Position;

            Stream? compressor = null;
            uint crc32;
            CompressionMethod compressionMethod;

            try
            {
                try
                {
                    (compressor, compressionMethod) = GetCompressor(compressionLevel);
                    crc32 = await sourceData.CopyToCrc32Async(compressor, ct);
                }
                finally
                {
                    // This tryf block is necessary as we want to dispose the compressing stream BEFORE shortening the stream if cancelled.
                    // Otherwise, disposing the stream could cause more data to be written after truncating the data already written.
                    // This could lead to a corrupt archive.
                    if (compressor != null && compressor != _stream)
                    {
                        await compressor.DisposeAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // If the data copy was cancelled, delete the existing entry data/space for local header
                _stream.SetLength(localHeaderOffset);

                throw; // Then forward the cancellation up the call stack.
            }

            long postEntryDataOffset = _stream.Position;
            long compressedSize = postEntryDataOffset - dataOffset;
            long uncompressedSize = sourceData.Length;
            var (centralDirectoryHeader, localHeader) = CreateFileHeaders(
                fileName,
                compressionMethod,
                (uint) compressedSize,
                (uint) uncompressedSize,
                crc32,
                localHeaderOffset
            );

            _stream.Position = localHeaderOffset;
            await localHeader.WriteAsync(_memory);
            _centralDirectoryRecords[fileName] = centralDirectoryHeader;

            _postFilesOffset = postEntryDataOffset;
        }

        internal static ZipVersion CheckVersionSupported(ZipVersion version)
        {
            if (version.Major >= MaxSupportedVersion.Major)
            {
                if (version.Minor > MaxSupportedVersion.Minor)
                {
                    throw new ZipFormatException($"ZIP file uses version: {version}, but the maximum supported version is {MaxSupportedVersion}");
                }
            }

            return version;
        }

        /// <summary>
        /// Creates the local/central file headers to store a file in the ZIP.
        /// Assumes that no file flags are required.
        /// </summary>
        /// <param name="fileName">The full file name</param>
        /// <param name="compressionMethod">The compression method.</param>
        /// <param name="compressedSize">The compressed size of the file.</param>
        /// <param name="uncompressedSize">The uncompressed size of the file.</param>
        /// <param name="crc32">The CRC32 of uncompressed file data.</param>
        /// <param name="localHeaderOffset">The offset of the local header from the start of the ZIP file.</param>
        /// <returns>The local/central file headers</returns>
        private (CentralDirectoryFileHeader, LocalFileHeader) CreateFileHeaders(string fileName, CompressionMethod compressionMethod, uint compressedSize, uint uncompressedSize, uint crc32, long localHeaderOffset)
        {
            var lastModified = new Timestamp
            {
                DateTime = DateTime.Now // ZIP files use the local timestamp
            };

            var localHeader = new LocalFileHeader()
            {
                VersionNeededToExtract = MaxSupportedVersion,
                Flags = 0,
                CompressionMethod = compressionMethod,
                LastModified = lastModified,

                Crc32 = crc32,
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize,

                FileName = fileName,
                ExtraField = null
            };

            var centralDirectoryHeader = new CentralDirectoryFileHeader()
            {
                // The existing resources file and some other APK files use this ID without setting any external file attributes, so it should be safe for us
                VersionMadeBy = 0,
                VersionNeededToExtract = MaxSupportedVersion,
                Flags = 0,
                CompressionMethod = compressionMethod,
                LastModified = lastModified,

                Crc32 = crc32,
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize,

                FileName = fileName,
                ExtraField = null,
                FileComment = null,
                DiskNumberStart = 0,
                InternalFileAttributes = 0,
                ExternalFileAttributes = 0,
                LocalHeaderOffset = (uint) localHeaderOffset
            };

            return (centralDirectoryHeader, localHeader);
        }

        /// <summary>
        /// Gets a stream to compress with the given compression level.
        /// </summary>
        /// <param name="compressionLevel">The compression level to compress with.</param>
        /// <returns>A deflate stream, or just the main apk stream if <paramref name="compressionLevel"/> is null. Additionally, the compression method that should be stored in the APK.</returns>
        private (Stream, CompressionMethod) GetCompressor(CompressionLevel? compressionLevel)
        {
            if (compressionLevel != null)
            {
                return (new DeflateStream(_stream, (CompressionLevel) compressionLevel, true), CompressionMethod.Deflate);
            }
            else
            {
                return (_stream, CompressionMethod.Store);
            }
        }

        /// <summary>
        /// Gets a stream to read the decompressed data from a zip entry.
        /// </summary>
        /// <param name="compressionMethod">The compression method of the entry</param>
        /// <param name="stream">The stream to read the compressed data.</param>
        /// <returns>The decompressing stream.</returns>
        /// <exception cref="ZipFormatException">If an unsupported compression mode is encountered. Currently only DEFLATE and STORE are supported.</exception>
        private Stream GetDecompressor(CompressionMethod compressionMethod, ZipEntryReadStream stream)
        {
            if (compressionMethod == CompressionMethod.Deflate)
            {
                return new DeflateStream(stream, CompressionMode.Decompress);
            }
            if (compressionMethod == CompressionMethod.Store)
            {
                return stream;
            }

            throw new ZipFormatException("Invalid compression mode: the only supported modes are DEFLATE and STORE");
        }

        /// <summary>
        /// Gets the record with a particular name.
        /// </summary>
        /// <param name="fileName">The full file name of the record.</param>
        /// <exception cref="ArgumentException">If the file does not exist.</exception>
        /// <returns>The CD header of the file.</returns>
        private CentralDirectoryFileHeader GetRecordOrThrow(string fileName)
        {
            if (_centralDirectoryRecords.TryGetValue(NormaliseFileName(fileName), out var header))
            {
                return header;
            }
            else
            {
                throw new ArgumentException($"No file with name {fileName} exists within the ZIP");
            }
        }

        /// <summary>
        /// Adds a central directory record to a dictionary, checking that it meets the requirements for this ZIP implementation.
        /// </summary>
        /// <param name="records">The central directory records dictionary.</param>
        /// <param name="record">The record to add.</param>
        /// <exception cref="ZipFormatException">If the file is a duplicate, or has a zero length name.</exception>
        private static void ValidateCentralDirectoryRecord(Dictionary<string, CentralDirectoryFileHeader> records, CentralDirectoryFileHeader record)
        {
            if (record.FileName == null)
            {
                throw new ZipFormatException("Zero-length filenames are not supported");
            }

            if (records.TryGetValue(record.FileName, out _))
            {
                throw new ZipFormatException($"Duplicate file found in archive: \"{record.FileName}\"");
            }
            records[record.FileName] = record;
        }

        /// <summary>
        /// Seeks the stream to the first byte after the contents of a ZIP entry (including after the data descriptor if present).
        /// </summary>
        /// <param name="stream">The stream to seek.</param>
        /// <param name="header">The file's header.</param>
        private static void SeekToEndOfEntry(Stream stream, CentralDirectoryFileHeader header)
        {
            const uint DataDescriptorSignature = 0x08074b50;

            // Read the local header and skip past the compressed data.
            stream.Position = header.LocalHeaderOffset;
            var reader = new ZipMemory(stream);
            var _ = LocalFileHeader.Read(reader);
            stream.Position += header.CompressedSize;

            if (header.Flags.HasFlag(EntryFlags.UsesDataDescriptor))
            {
                // If the entry has stated that it has a data descriptor, read this too.

                // Some data descriptors contain the signature, some do not.
                // The signature was originally not part of the ZIP specification but has been widely adopted.
                uint _crc = reader.ReadUInt32();
                if (_crc == DataDescriptorSignature)
                {
                    // If the signature was present, and so we just read the signature, read the actual CRC here.
                    // In the situation that the CRC32 happened to equal the data descriptor signature, this will read invalid data.
                    // This case is not accounted for in the ZIP specification, so we will have to live in fear.
                    _crc = reader.ReadUInt32();
                }


                uint _compressedSize = reader.ReadUInt32();
                uint _uncompressedSize = reader.ReadUInt32();
            }
        }

        /// <summary>
        /// Seeks the stream to the first byte after the contents of a ZIP entry (including after the data descriptor if present).
        /// </summary>
        /// <param name="stream">The stream to seek.</param>
        /// <param name="header">The file's header.</param>
        private static async Task SeekToEndOfEntryAsync(Stream stream, CentralDirectoryFileHeader header)
        {
            const uint DataDescriptorSignature = 0x08074b50;

            stream.Position = header.LocalHeaderOffset;
            var memory = new ZipMemory(stream);
            var _ = await LocalFileHeader.ReadAsync(memory);
            stream.Position += header.CompressedSize;

            if (header.Flags.HasFlag(EntryFlags.UsesDataDescriptor))
            {
                uint _crc = await memory.ReadUInt32Async();
                if (_crc == DataDescriptorSignature)
                {
                    _crc = await memory.ReadUInt32Async();
                }
                uint _compressedSize = await memory.ReadUInt32Async();
                uint _uncompressedSize = await memory.ReadUInt32Async();
            }
        }

        private string NormaliseFileName(string fileName)
        {
            // Remove leading slash and normalise slashes to forward slashes
            fileName = fileName.Replace('\\', '/');
            if (fileName.StartsWith('/'))
            {
                fileName = fileName.Substring(1);
            }

            return fileName;
        }

        private void Save()
        {
            // _certificate and _privateKey are non-null due to the default certificate assigned in the constructor.
            JarSigner.SignApkFile(this, _certificate!, _privateKey!, _existingHashes);

            _stream.Position = _postFilesOffset;
            V2Signer.SignAndCompleteZipFile(_centralDirectoryRecords.Values, _stream, _certificate!, _privateKey!);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (_stream.CanWrite)
                {
                    Save();
                }
            }
            finally
            {
                // Do not dispose until this point, as JarSigner needs to be able to add the signature files to the APK, and for that the ApkZip must not be disposed.
                _disposed = true;
                _stream.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (_stream.CanWrite)
                {
                    // We have no support for asynchronous signing currently, so threading is used.
                    // Signing/calculating the APK digest is generally CPU-bound work anyway, so it doesn't make much sense to create an async implementation.
                    await Task.Run(() => Save());
                }
            }
            finally
            {
                _disposed = true;
                await _stream.DisposeAsync();
            }
        }
    }
}
