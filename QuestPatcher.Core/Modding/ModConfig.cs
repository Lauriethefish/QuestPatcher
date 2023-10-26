using System.Collections.Generic;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// Represents the mod config file, which is serialized using <see cref="ModConverter"/> to instantiate each mod with its respective provider.
    /// </summary>
    public class ModConfig
    {
        /// <summary>
        /// The mods in the config file.
        /// </summary>
        public List<IMod> Mods { get; set; } = new();
    }
}
