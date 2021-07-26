using System;

namespace Quatcher.Core.Patching
{
    public class PatchingException : Exception
    {
        public PatchingException(string message) : base(message) { }
        public PatchingException(string? message, Exception cause) : base(message, cause) { }
    }
}
