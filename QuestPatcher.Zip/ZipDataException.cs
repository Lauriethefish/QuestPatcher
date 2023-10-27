using System;

namespace QuestPatcher.Zip
{
    /// <summary>
    /// Thrown if the data to write to the ZIP file has a format problem
    /// </summary>
    public class ZipDataException : Exception
    {
        public ZipDataException(string message) : base(message) { }
    }
}
