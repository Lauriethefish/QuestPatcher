using System.Reflection;
using System;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Utilities for handling versions
    /// </summary>
    public class VersionUtil
    {
        /// <summary>
        /// The current version of QuestPatcher
        /// </summary>
        public static SemanticVersioning.Version QuestPatcherVersion { get; }

        static VersionUtil()
        {
            Version? assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (assemblyVersion == null)
            {
                throw new NullReferenceException("Assembly version was null, unable to get version of QuestPatcher");
            }

            QuestPatcherVersion = new SemanticVersioning.Version(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
        }
    }
}
