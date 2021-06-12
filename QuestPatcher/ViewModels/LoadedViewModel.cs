using System.Diagnostics;
using ReactiveUI;
using QuestPatcher.ViewModels.Modding;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using Avalonia.Input;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Serilog.Core;
using System.Linq;

namespace QuestPatcher.ViewModels
{
    public class LoadedViewModel : ViewModelBase
    {
        public string SelectedAppText => $"Modding {Config.AppId}";

        public PatchingViewModel PatchingView { get; }

        public ManageModsViewModel ManageModsView { get; }

        public LoggingViewModel LoggingView { get; }

        public ToolsViewModel ToolsView { get; }

        public OtherItemsViewModel OtherItemsView { get; }

        public Config Config { get; }
        public ApkInfo AppInfo
        {
            get
            {
                Debug.Assert(_patchingManager.InstalledApp != null);
                return _patchingManager.InstalledApp;
            }
        }

        private readonly PatchingManager _patchingManager;
        private readonly BrowseImportManager _browseManager;
        private readonly Logger _logger;

        public LoadedViewModel(PatchingViewModel patchingView, ManageModsViewModel manageModsView, LoggingViewModel loggingView, ToolsViewModel toolsView, OtherItemsViewModel otherItemsView, Config config, PatchingManager patchingManager, BrowseImportManager browseManager, Logger logger)
        {
            PatchingView = patchingView;
            LoggingView = loggingView;
            ToolsView = toolsView;
            ManageModsView = manageModsView;
            OtherItemsView = otherItemsView;

            Config = config;
            _patchingManager = patchingManager;
            _browseManager = browseManager;
            _logger = logger;

            _patchingManager.PropertyChanged += (_, args) =>
            {
                if(args.PropertyName == nameof(_patchingManager.InstalledApp) && _patchingManager.InstalledApp != null)
                {
                    this.RaisePropertyChanged(nameof(AppInfo));
                    this.RaisePropertyChanged(nameof(SelectedAppText));
                }
            };
        }

        public async void OnDragAndDrop(object? sender, DragEventArgs args)
        {
            _logger.Debug("Handling drag and drop on LoadedViewModel");

            // Sometimes a COMException gets thrown if the items can't be parsed for whatever reason.
            // We need to handle this to avoid crashing QuestPatcher.
            try
            {
                IEnumerable<string>? fileNames = args.Data.GetFileNames();
                if (fileNames == null) // Non-file items dragged
                {
                    _logger.Debug("Drag and drop contained no file names");
                    return;
                }

                _logger.Debug("Files found in drag and drop. Processing . . .");
                await _browseManager.AttemptImportFiles(fileNames.ToList(), OtherItemsView.SelectedFileCopy);
            }
            catch (COMException)
            {
                _logger.Error("Failed to parse dragged items");
            }
        }
    }
}
