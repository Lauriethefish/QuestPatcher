using QuestPatcher.Core;
using QuestPatcher.Core.Models;

namespace QuestPatcher.ViewModels
{
    public class LoadingViewModel : ViewModelBase
    {
        public ProgressViewModel ProgressView { get; }
        public LoggingViewModel LoggingView { get; }

        public string LoadingText => $"QuestPatcher v{VersionUtil.QuestPatcherVersion} loading . . .";

        public Config Config { get; }

        public LoadingViewModel(ProgressViewModel progressView, LoggingViewModel loggingView, Config config)
        {
            ProgressView = progressView;
            LoggingView = loggingView;
            Config = config;
        }
    }
}
