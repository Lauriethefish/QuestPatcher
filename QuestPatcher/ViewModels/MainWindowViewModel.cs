using QuestPatcher.Core;

namespace QuestPatcher.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public LoadedViewModel LoadedView { get; }
        public LoadingViewModel LoadingView { get; }

        public string WindowName { get; }

        public QuestPatcherService MainService { get; }

        public MainWindowViewModel(LoadedViewModel loadedView, LoadingViewModel loadingView, QuestPatcherService mainService)
        {
            LoadedView = loadedView;
            LoadingView = loadingView;
            MainService = mainService;

            // Set the window name based on the QuestPatcher version
            WindowName = $"QuestPatcher v{VersionUtil.QuestPatcherVersion}";
        }
    }
}
