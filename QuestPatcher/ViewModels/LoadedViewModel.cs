using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Input;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Resources;
using QuestPatcher.ViewModels.Modding;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels
{
    public class LoadedViewModel : ViewModelBase
    {
        public string SelectedAppText => string.Format(Strings.Global_CurrentModding, Config.AppId);

        public PatchingViewModel PatchingView { get; }

        public ManageModsViewModel ManageModsView { get; }

        public LoggingViewModel LoggingView { get; }

        public ToolsViewModel ToolsView { get; }

        public OtherItemsViewModel OtherItemsView { get; }

        private string AppName
        {
            get
            {
                var now = DateTime.Now;
                bool isAprilFools = now.Month == 4 && now.Day == 1;
                return isAprilFools ? "QuestCorrupter" : "QuestPatcher";
            }
        }

        public string WelcomeText => string.Format(Strings.Global_WelcomeMessage, AppName);

        public string Version => VersionUtil.QuestPatcherVersion.ToString();

        public Config Config { get; }
        public ApkInfo AppInfo
        {
            get
            {
                Debug.Assert(_installManager.InstalledApp != null);
                return _installManager.InstalledApp;
            }
        }

        public bool NeedsPatchingView => PatchingView.IsPatchingInProgress || !AppInfo.IsModded;

        private readonly InstallManager _installManager;
        private readonly BrowseImportManager _browseManager;

        public LoadedViewModel(PatchingViewModel patchingView, ManageModsViewModel manageModsView, LoggingViewModel loggingView, ToolsViewModel toolsView, OtherItemsViewModel otherItemsView, Config config, InstallManager installManager, BrowseImportManager browseManager)
        {
            PatchingView = patchingView;
            LoggingView = loggingView;
            ToolsView = toolsView;
            ManageModsView = manageModsView;
            OtherItemsView = otherItemsView;

            Config = config;
            _installManager = installManager;
            _browseManager = browseManager;

            _installManager.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(_installManager.InstalledApp) && _installManager.InstalledApp != null)
                {
                    this.RaisePropertyChanged(nameof(AppInfo));
                    this.RaisePropertyChanged(nameof(NeedsPatchingView));
                    this.RaisePropertyChanged(nameof(SelectedAppText));
                }
            };

            patchingView.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(PatchingView.IsPatchingInProgress))
                {
                    this.RaisePropertyChanged(nameof(NeedsPatchingView));
                }
            };
        }

        private FileImportInfo GetImportInfoForUri(Uri fileUri)
        {
            return new FileImportInfo(fileUri.LocalPath) // No need to escape: using local path
            {
                PreferredCopyType = OtherItemsView.SelectedFileCopy,
            };
        }

        public async void OnDragAndDrop(object? sender, DragEventArgs args)
        {
            Log.Debug("Handling drag and drop on LoadedViewModel");

            // Sometimes a COMException gets thrown if the items can't be parsed for whatever reason.
            // We need to handle this to avoid crashing QuestPatcher.
            try
            {
                var filesToImport = new List<string>();

                string? text = args.Data.GetText();
                if (text != null)
                {
                    var creationOptions = new UriCreationOptions();
                    if (Uri.TryCreate(text, in creationOptions, out var uri))
                    {
                        string scheme = uri.Scheme.ToLower();

                        if (scheme == "file")
                        {
                            await _browseManager.AttemptImportFiles(new FileImportInfo[] { GetImportInfoForUri(uri) });
                        }
                        else if (uri.Scheme == "http" || uri.Scheme == "https")
                        {
                            await _browseManager.AttemptImportUri(uri);
                        }
                    }
                }
                else
                {
                    var files = args.Data.GetFiles();
                    if (files != null)
                    {
                        Log.Debug("Files found in drag and drop. Processing . . .");
                        await _browseManager.AttemptImportFiles(files.Select(file => GetImportInfoForUri(file.Path)).ToList());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse dragged items");
            }
        }
    }
}
