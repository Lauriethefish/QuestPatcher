using System;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Thrown if downloading a file needed for QuestPatcher to function fails.
    /// </summary>
    public class FileDownloadFailedException : Exception
    {
        public FileDownloadFailedException(string? message) : base(message) { }

        public FileDownloadFailedException(string? message, Exception innerException) : base(message, innerException) { }

    }
}
