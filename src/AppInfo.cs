namespace QuestPatcher
{
    public class AppInfo
    {
        public bool IsModded { get; }
        public string GameVersion { get; }

        public AppInfo(bool isModded, string gameVersion)
        {
            this.IsModded = isModded;
            this.GameVersion = gameVersion;
        }
    }
}
