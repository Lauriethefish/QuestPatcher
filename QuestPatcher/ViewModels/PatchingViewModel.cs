using System;
using System.IO;
using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using QuestPatcher.Models;
using QuestPatcher.Views;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels
{
    public class PatchingViewModel : ViewModelBase
    {
        public bool IsPatchingInProgress { get => _isPatchingInProgress; set { if (_isPatchingInProgress != value) { this.RaiseAndSetIfChanged(ref _isPatchingInProgress, value); } } }
        private bool _isPatchingInProgress;

        public string PatchingStageText { get; private set; } = "";

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

        public async void SetSplash()
        {
            string path = Config.PatchingOptions.CustomSplash;

            if (path.ToLower() == "none")
            {
                Config.PatchingOptions.EnableCustomSplash = false;
                DialogBuilder builder = new()
                {
                    Title = "Success",
                    Text = "The Default Splash screen will be used.",
                    HideCancelButton = true
                };
                await builder.OpenDialogue(_mainWindow);
                return;
            }
            
            if (File.Exists(path))
            {
                if (!path.EndsWith(".png"))
                {
                    Config.PatchingOptions.EnableCustomSplash = false;
                    DialogBuilder builder = new()
                    {
                        Title = "Error",
                        Text = "Meta only supports .png files as splash screens! Please specify a .png file.",
                        HideCancelButton = true
                    };
                    await builder.OpenDialogue(_mainWindow);
                    return;
                }
                
                Config.PatchingOptions.EnableCustomSplash = true;
                DialogBuilder builder2 = new()
                {
                    Title = "Success",
                    Text = "The Image (" + path + ") was set as the splash screen!\nTo use the default splash screen, set the value to \"None\" and click the button again.\n\nNote: Once Patching is finished you cant change the splash screen unless you redo the patching process!",
                    HideCancelButton = true
                };
                await builder2.OpenDialogue(_mainWindow);
                return;
            }
            
            Config.PatchingOptions.EnableCustomSplash = false;
            DialogBuilder builder3 = new()
            {
                Title = "Error",
                Text = "The Specified file in path \"" + path + "\" doesn't exist!",
                HideCancelButton = true
            };
            await builder3.OpenDialogue(_mainWindow);
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
