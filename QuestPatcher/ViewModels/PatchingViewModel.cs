using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using QuestPatcher.Models;
using QuestPatcher.Resources;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels
{
    public class PatchingViewModel : ViewModelBase
    {
        public bool IsPatchingInProgress { get => _isPatchingInProgress; set { if (_isPatchingInProgress != value) { this.RaiseAndSetIfChanged(ref _isPatchingInProgress, value); } } }
        private bool _isPatchingInProgress;

        public string PatchingStageText { get; private set; } = "";

        public string? CustomSplashPath => Config.PatchingOptions.CustomSplashPath;

        public Config Config { get; }

        public OperationLocker Locker { get; }

        public ProgressViewModel ProgressBarView { get; }

        public ExternalFilesDownloader FilesDownloader { get; }

        private readonly PatchingManager _patchingManager;
        private readonly InstallManager _installManager;
        private readonly Window _mainWindow;

        public PatchingViewModel(Config config, OperationLocker locker, PatchingManager patchingManager, InstallManager installManager, Window mainWindow, ProgressViewModel progressBarView, ExternalFilesDownloader filesDownloader)
        {
            Config = config;
            Locker = locker;
            ProgressBarView = progressBarView;
            FilesDownloader = filesDownloader;

            _patchingManager = patchingManager;
            _installManager = installManager;
            _mainWindow = mainWindow;

            _patchingManager.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(_patchingManager.PatchingStage))
                {
                    OnPatchingStageChange(_patchingManager.PatchingStage);
                }
            };
        }

        public async void StartPatching()
        {
            IsPatchingInProgress = true;
            Locker.StartOperation();
            try
            {
                await _patchingManager.PatchApp();
            }
            catch (FileDownloadFailedException ex)
            {
                Log.Error("Patching failed as essential files could not be downloaded: {Message}", ex.Message);

                var builder = new DialogBuilder
                {
                    Title = Strings.Patching_FileDownloadFailed_Title,
                    Text = Strings.Patching_FileDownloadFailed_Text,
                    HideCancelButton = true
                };

                await builder.OpenDialogue(_mainWindow);
            }
            catch (Exception ex)
            {
                // Print troubleshooting information for debugging
                Log.Error(ex, "Patching failed!");
                var builder = new DialogBuilder
                {
                    Title = Strings.Patching_PatchingFailed_Title,
                    Text = Strings.Patching_PatchingFailed_Text,
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                IsPatchingInProgress = false;
                Locker.FinishOperation();
            }

            if (_installManager.InstalledApp?.IsModded ?? false)
            {
                // Display a dialogue to give the user some info about what to expect next, and to avoid them pressing restore app by mistake
                Log.Debug("Patching completed successfully, displaying info dialogue");
                var builder = new DialogBuilder
                {
                    Title = Strings.Patching_PatchingSuccess_Title,
                    Text = Strings.Patching_PatchingSuccess_Text,
                    HideCancelButton = true
                };
                await builder.OpenDialogue(_mainWindow);
            }
        }

        public async void SelectSplashPath()
        {
            try
            {
                var files = await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    FileTypeFilter = new[]
                    {
                        FilePickerFileTypes.ImagePng
                    }
                });
                Config.PatchingOptions.CustomSplashPath = files.FirstOrDefault()?.Path.LocalPath;
                this.RaisePropertyChanged(nameof(CustomSplashPath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to select splash screen path");
            }
        }

        /// <summary>
        /// Updates the patching stage text in the view
        /// </summary>
        /// <param name="stage">The new patching stage</param>
        private void OnPatchingStageChange(PatchingStage stage)
        {
            PatchingStageText = stage switch
            {
                PatchingStage.NotStarted => Strings.PatchingStage_NotStarted,
                PatchingStage.FetchingFiles => Strings.PatchingStage_FetchFiles,
                PatchingStage.MovingToTemp => Strings.PatchingStage_MoveToTemp,
                PatchingStage.Patching => Strings.PatchingStage_Patching,
                PatchingStage.Signing => Strings.PatchingStage_Signing,
                PatchingStage.UninstallingOriginal => Strings.PatchingStage_UninstallOriginal,
                PatchingStage.InstallingModded => Strings.PatchingStage_InstallModded,
                _ => throw new NotImplementedException()
            };
            this.RaisePropertyChanged(nameof(PatchingStageText));
        }
    }
}
