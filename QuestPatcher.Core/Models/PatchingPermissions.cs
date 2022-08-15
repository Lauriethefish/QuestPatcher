
namespace QuestPatcher.Core.Models
{
    /// <summary>
    /// Specifies which permissions will be added to the APK during patching
    /// </summary>
    public class PatchingPermissions
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
        
        public bool FlatScreenSupport { get; set; }

        public HandTrackingVersion HandTrackingType { get; set; }

    }
}
