namespace QuestPatcher.Core.Patching
{
    /// <summary>
    /// Represents a part of the patching process
    /// </summary>
    public enum PatchingStage
    {
        NotStarted,
        MovingToTemp,
        Patching,
        Signing,
        UninstallingOriginal,
        InstallingModded
    }
}
