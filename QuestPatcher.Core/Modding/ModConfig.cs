using System.Collections.Generic;

namespace QuestPatcher.Core.Modding
{
    public class ModConfig
    {
        public List<IMod> Mods { get; set; } = new();
    }
}
