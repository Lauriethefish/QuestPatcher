using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Patching
{
    public class PatchingException : Exception
    {
        public PatchingException(string message) : base(message) { }
        public PatchingException(string? message, Exception cause) : base(message, cause) { }
    }
}
