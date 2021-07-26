using Avalonia.Controls;
using Quatcher.Core;
using Quatcher.Models;
using Quatcher.Services;
using Quatcher.Views;
using ReactiveUI;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace Quatcher.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public LoadedViewModel LoadedView { get; }
        public LoadingViewModel LoadingView { get; }
        
        public string WindowName { get; }

        public QuatcherService MainService { get; }

        public MainWindowViewModel(LoadedViewModel loadedView, LoadingViewModel loadingView, QuatcherService mainService)
        {
            LoadedView = loadedView;
            LoadingView = loadingView;
            MainService = mainService;

            // Set the window name based on the Quatcher version
            WindowName = $"Quatcher v{VersionUtil.QuatcherVersion}";
        }
    }
}
