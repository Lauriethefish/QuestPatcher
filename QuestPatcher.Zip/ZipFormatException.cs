using System;

namespace QuestPatcher.Zip
{
    /// <summary>
    /// Thrown if the ZIP file read has a format problem
    /// </summary>
    public class ZipFormatException : FormatException
    {
        public ZipFormatException(string message) : base(message) { }
    }
}
