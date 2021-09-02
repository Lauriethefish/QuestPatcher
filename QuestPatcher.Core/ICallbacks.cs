using System.Threading.Tasks;
using Serilog;

namespace QuestPatcher.Core
{
    public interface ICallbacks
    {
        Task<bool> PromptAppNotInstalled();

        Task<bool> PromptAdbDisconnect(DisconnectionType type);

        Task<bool> PromptUnstrippedUnityUnavailable();

        Task<bool> Prompt32Bit();

        Task<bool> PromptPauseBeforeCompile();

        Task PromptUpgradeFromOld();

        void Quit();
    }
}
