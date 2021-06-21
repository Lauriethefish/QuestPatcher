using System;

namespace QuestPatcher.Axml
{
    public class AxmlParseException : Exception
    {
        public AxmlParseException(string? message) : base(message) { }
        public AxmlParseException(string? message, Exception? cause) : base(message, cause) { }
    }
}