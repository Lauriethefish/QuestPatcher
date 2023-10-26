using System.Collections.Generic;
using Avalonia.Controls;
using ReactiveUI;

namespace QuestPatcher.ViewModels
{
    public class SelectAppWindowViewModel : ViewModelBase
    {
        public List<string>? InstalledApps
        {
            get => _installedApps;
            set
            {
                if (value != _installedApps)
                {
                    _installedApps = value;
                    this.RaisePropertyChanged();
                    this.RaisePropertyChanged(nameof(IsLoading));
                }
            }
        }
        private List<string>? _installedApps;

        public bool IsLoading => InstalledApps == null;

        public bool DidConfirm { get; private set; } = false;

        public string SelectedApp { get; set; }

        private readonly Window _window;

        public SelectAppWindowViewModel(Window window, string currentlySelected)
        {
            _window = window;
            SelectedApp = currentlySelected;
        }

        public void ConfirmNewApp()
        {
            DidConfirm = true;
            _window.Close();
        }
    }
}
