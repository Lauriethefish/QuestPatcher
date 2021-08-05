using System;
using Newtonsoft.Json;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Thrown if trying to get the value of a OS dependent value when one is not configured for the OS.
    /// </summary>
    public class UnknownOperatingSystemException : Exception
    {
        public UnknownOperatingSystemException() : base($"No value is configured for the current operating system") { }
    }
    
    /// <summary>
    /// Used to represent values which need to be changed depending on the operating system QuestPatcher is running on.
    /// </summary>
    /// <typeparam name="T">The type of the value that depends on the operating system</typeparam>
    public class SystemSpecificValue<T>
    {
        /// <summary>
        /// A value that works on any operating system.
        /// If the OS specific property is not null, that will be used instead.
        /// </summary>
        public T? Any { get; init; }

        /// <summary>
        /// Value to use on Microsoft Windows
        /// </summary>
        public T? Windows { get; init; }
        
        /// <summary>
        /// Value to use on Mac OS
        /// </summary>
        public T? Mac { get; init; }
        
        /// <summary>
        /// Value to use on Linux
        /// </summary>
        public T? Linux { get; init; }
        
        /// <summary>
        /// Sets both the linux and mac value at the same time.
        /// </summary>
        [JsonIgnore]
        public T? Unix {
            init
            {
                Mac = value;
                Linux = value;
            }
        }

        /// <summary>
        /// Finds the current value based on the operating system
        /// </summary>
        [JsonIgnore]
        public T Value => EvaluateBasedOnOS();

        private T EvaluateBasedOnOS()
        {
            if (Windows != null && OperatingSystem.IsWindows())
            {
                return Windows;
            }
            
            if (Linux != null && OperatingSystem.IsLinux())
            {
                return Linux;
            }

            if (Mac != null && OperatingSystem.IsMacOS())
            {
                return Mac;
            }

            if (Any != null)
            {
                return Any;
            }

            // This OS is not supported
            throw new UnknownOperatingSystemException();
        }
    }
}