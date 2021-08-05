using System;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Represents an exception while parsing AXML
    /// </summary>
    public class AxmlParseException : Exception
    {
        internal AxmlParseException(string? message) : base(message) { }
        internal AxmlParseException(string? message, Exception? cause) : base(message, cause) { }
    }
}