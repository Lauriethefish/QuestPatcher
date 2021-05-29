using Avalonia.Controls;
using QuestPatcher.Views;
using Serilog.Core;
using System;
using ReactiveUI;
using System.Diagnostics;
using QuestPatcher.Models;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using QuestPatcher.Core;

namespace QuestPatcher.ViewModels
{
    public class PatchingViewModel : ViewModelBase
    {
        public bool IsPatchingInProgress { get => _isPatchingInProgress; set { if(_isPatchingInProgress != value) { this.RaiseAndSetIfChanged(ref _isPatchingInProgress, value); } } }
        private bool _isPatchingInProgress = false;

        public string PatchingStageText { get; private set; } = "";

        public Config Config { get; }

        public OperationLocker Locker { get; }

        public ProgressViewModel ProgressBarView { get; }

        public ExternalFilesDownloader FilesDownloader { get; }

        private readonly PatchingManager _patchingManager;
        private readonly Window _mainWindow;
        private readonly Logger _logger;
        private readonly Action _quit;

        public PatchingViewModel(Config config, OperationLocker locker, PatchingManager patchingManager, Window mainWindow, Logger logger, Action quit, ProgressViewModel progressBarView, ExternalFilesDownloader filesDownloader)
        {
            Config = config;
            Locker = locker;
            ProgressBarView = progressBarView;
            FilesDownloader = filesDownloader;

            _patchingManager = patchingManager;
            _mainWindow = mainWindow;
            _logger = logger;
            _quit = quit;

            _patchingManager.PropertyChanged += (sender, args) =>
            {
                if(args.PropertyName == nameof(_patchingManager.PatchingStage))
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
                IsPatchingInProgress = false;

                Debug.Assert(_patchingManager.InstalledApp != null);
                if(_patchingManager.InstalledApp.IsModded)
                {
                    // Display a dialogue to give the user some info about what to expect next, and to avoid them pressing restore app by mistake
                    _logger.Debug("Patching completed successfully, displaying info dialogue");
                    DialogBuilder builder = new()
                    {
                        Title = "Patching Complete!",
                        Text = "Your installation is now modded!\nYou can now access installed mods, cosmetics, etc.\n\nNOTE: If you see a restore app prompt inside your headset, just press close. The chance of getting banned for modding is virtually zero, so it's nothing to worry about.",
                        HideCancelButton = true
                    };
                    await builder.OpenDialogue(_mainWindow);
                }
            }
            catch (Exception ex)
            {
                // Print troubleshooting information for debugging
                _logger.Error($"Patching failed!: {ex}");
                DialogBuilder builder = new()
                {
                    Title = "Patching Failed",
                    Text = "An unhandled error occured while attempting to patch the game",
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
                _quit();
            }   finally
            {
                Locker.FinishOperation();
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
                PatchingStage.Decompiling => "Decompiling APK (patching stage 1/6)",
                PatchingStage.Patching => "Modifying APK files to support mods (patching stage 2/6)",
                PatchingStage.Recompiling => "Compiling modded APK (patching stage 3/6)",
                PatchingStage.Signing => "Signing APK (patching stage 4/6)",
                PatchingStage.UninstallingOriginal => "Uninstalling original APK to install modded APK (patching stage 5/6)",
                PatchingStage.InstallingModded => "Installing modded APK (patching stage 6/6)",
                _ => throw new NotImplementedException()
            };
            this.RaisePropertyChanged(nameof(PatchingStageText));
        }
    }
}
