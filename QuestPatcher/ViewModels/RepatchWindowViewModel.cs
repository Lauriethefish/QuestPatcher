using Avalonia.Controls;
using QuestPatcher.Core.Models;

namespace QuestPatcher.ViewModels
{
    public class RepatchWindowViewModel : ViewModelBase
    {
        public Config Config { get; }

        private readonly PatchingViewModel _patchingViewModel;
        private readonly Window _window;

        public RepatchWindowViewModel(PatchingViewModel patchingViewModel, Config config, Window window)
        {
            _patchingViewModel = patchingViewModel;
            Config = config;
            _window = window;
        }

        public void RepatchApp()
        {
            _patchingViewModel.StartPatching();
            _window.Close();
        }
    }
}
