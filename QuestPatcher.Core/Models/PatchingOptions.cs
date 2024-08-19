
namespace QuestPatcher.Core.Models
{
    /// <summary>
    /// Specifies which permissions will be added to the APK during patching
    /// </summary>
    public class PatchingOptions
    {
        public bool ExternalFiles { get; set; } = true; // Not changeable in UI, since 90% of mods need this to work

        public bool Debuggable { get; set; } // Allows debugging with GDB or LLDB

        /// <summary>
        /// Used to support loading legacy configs
        /// </summary>
        public bool HandTracking
        {
            set
            {
                HandTrackingType = value ? HandTrackingVersion.V1 : HandTrackingVersion.None;
            }
        }

        public bool MrcWorkaround { get; set; }

        public bool Microphone { get; set; }

        public bool OpenXR { get; set; }

        public bool FlatScreenSupport { get; set; }

        public HandTrackingVersion HandTrackingType { get; set; }

        public ModLoader ModLoader { get; set; } = ModLoader.QuestLoader;

        public bool Passthrough { get; set; }

        public bool BodyTracking { get; set; }

        /// <summary>
        /// Path to a PNG file containing a custom splash screen.
        /// </summary>
        public string? CustomSplashPath { get; set; } = null;
    }
}
