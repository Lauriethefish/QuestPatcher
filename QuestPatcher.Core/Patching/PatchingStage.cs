using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Patching
{
    /// <summary>
    /// Represents a part of the patching process
    /// </summary>
    public enum PatchingStage
    {
        NotStarted,
        Decompiling,
        Patching,
        Recompiling,
        Signing,
        UninstallingOriginal,
        InstallingModded
    }
}
