using System.Reflection;
using System;

namespace Quatcher.Core
{
    /// <summary>
    /// Utilities for handling versions
    /// </summary>
    public class VersionUtil
    {
        /// <summary>
        /// The current version of Quatcher
        /// </summary>
        public static SemVer.Version QuatcherVersion { get; }

        static VersionUtil()
        {
            Version? assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (assemblyVersion == null)
            {
                throw new NullReferenceException("Assembly version was null, unable to get version of Quatcher");
            }

            QuatcherVersion = new SemVer.Version(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
        }
    }
}