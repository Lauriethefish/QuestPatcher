using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuestPatcher.Core
{
    public interface IUserPrompter
    {
        Task<bool> PromptAppNotInstalled();

        Task<bool> PromptAdbDisconnect(DisconnectionType type);

        Task<AdbDevice?> PromptSelectDevice(List<AdbDevice> devices);

        Task<bool> PromptUnstrippedUnityUnavailable();

        Task<bool> Prompt32Bit();

        Task<bool> PromptUnknownModLoader();

        Task PromptUpgradeFromOld();
    }
}
