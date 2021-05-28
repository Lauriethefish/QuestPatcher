using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Modding
{
    public class InstallationException : Exception
    {
        public InstallationException(string message) : base(message) { }
        public InstallationException(string? message, Exception cause) : base(message, cause) { }
    }
}
