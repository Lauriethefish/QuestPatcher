using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// A mod provider that loads and saves information about the mod to a config file.
    /// </summary>
    public abstract class ConfigModProvider : JsonConverter<IMod>, IModProvider
    {
        public abstract string ConfigSaveId { get; }

        public abstract string FileExtension { get; }
        public abstract Task<IMod> LoadFromFile(string modPath);
        public abstract Task DeleteMod(IMod mod);
        public abstract Task LoadModsStatus();
        public abstract void ClearMods();
        public abstract Task LoadLegacyMods();
    }
}
