using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using QuestPatcher.Models;
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

                DialogBuilder builder = new()
                {
                    Title = "Could not download files",
                    Text = "QuestPatcher could not download files that it needs to patch the APK. Please check your internet connection, then try again.",
                    HideCancelButton = true
                };

                await builder.OpenDialogue(_mainWindow);
            }
            catch (Exception ex)
            {
                // Print troubleshooting information for debugging
                Log.Error(ex, $"Patching failed!");
                DialogBuilder builder = new()
                {
                    Title = "Patching Failed",
                    Text = "An unhandled error occured while attempting to patch the game",
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
                DialogBuilder builder = new()
                {
                    Title = "Patching Complete!",
                    Text = "Your installation is now modded!\nYou can now access installed mods, cosmetics, etc.\n\nNOTE: If you see a restore app prompt inside your headset, just press close. The chance of getting banned for modding is virtually zero, so it's nothing to worry about.",
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
                PatchingStage.NotStarted => "Not Started",
                PatchingStage.FetchingFiles => "Downloading files needed to mod the APK (patching stage 1/6)",
                PatchingStage.MovingToTemp => "Moving APK to temporary location (patching stage 2/6)",
                PatchingStage.Patching => "Modifying APK files to support mods (patching stage 3/6)",
                PatchingStage.Signing => "Signing APK (patching stage 4/6)",
                PatchingStage.UninstallingOriginal => "Uninstalling original APK to install modded APK (patching stage 5/6)",
                PatchingStage.InstallingModded => "Installing modded APK (patching stage 6/6)",
                _ => throw new NotImplementedException()
            };
            this.RaisePropertyChanged(nameof(PatchingStageText));
        }
    }
}
