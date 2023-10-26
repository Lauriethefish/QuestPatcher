namespace QuestPatcher.Core.Models
{
    /// <summary>
    /// Represents the modloader that the APK is patched with
    /// </summary>
    public enum ModLoader
    {
        /// <summary>
        /// QuestLoader, maintained by Sc2ad
        /// </summary>
        QuestLoader,
        /// <summary>
        /// Scotland2, maintained by Sc2ad
        /// </summary>
        Scotland2,
        /// <summary>
        /// Some other modloader, that QuestPatcher doesn't know about.
        /// In this case, it will give you the option to patch the APK, although doing so may overwrite the existing modloader or not work at all.
        /// </summary>
        Unknown
    }
}
