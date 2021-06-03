using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Models;
using QuestPatcher.Services;
using QuestPatcher.Views;
using ReactiveUI;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace QuestPatcher.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public LoadedViewModel LoadedView { get; }
        public LoadingViewModel LoadingView { get; }

        public QuestPatcherService MainService { get; }

        public MainWindowViewModel(LoadedViewModel loadedView, LoadingViewModel loadingView, QuestPatcherService mainService)
        {
            LoadedView = loadedView;
            LoadingView = loadingView;
            MainService = mainService;
        }
    }
}
