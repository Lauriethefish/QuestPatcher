using System;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// Exception thrown for errors while parsing, installing, and uninstalling mods, alongside the importing of file copies and other files.
    /// </summary>
    public class InstallationException : Exception
    {
        public InstallationException(string message) : base(message) { }
        public InstallationException(string? message, Exception cause) : base(message, cause) { }
    }
}
