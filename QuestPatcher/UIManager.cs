using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Models;
using QuestPatcher.ViewModels;
using QuestPatcher.Views;
using Serilog.Core;

namespace QuestPatcher
{
    public class UIManager
    {
        private readonly OperationLocker _locker;
        private readonly Logger _logger;
        private readonly UIPrompter _prompter;
        private readonly QuestPatcherService _qpService;
        private readonly Window _mainWindow;
        private readonly IControlledApplicationLifetime _appLifetime;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly Config _config;
        private readonly MainWindowViewModel _mainViewModel;
        
        private bool _inducedShutdownOngoing;

        public UIManager(OperationLocker locker, Logger logger, ICallbacks prompter, QuestPatcherService qpService, MainWindow mainWindow,
            MainWindowViewModel mainViewModel, IControlledApplicationLifetime appLifetime, AndroidDebugBridge debugBridge, ConfigManager configManager)
        {
            _locker = locker;
            _logger = logger;
            _prompter = (UIPrompter) prompter;
            _qpService = qpService;
            _mainWindow = mainWindow;
            _appLifetime = appLifetime;
            _debugBridge = debugBridge;
            _config = configManager.GetOrLoadConfig();
            _mainViewModel = mainViewModel;

            _mainWindow.DataContext = mainViewModel;
            _mainWindow.Opened += OnMainWindowOpened;
            _mainWindow.Closing += OnMainWindowClosing;
            _appLifetime.Exit += OnAppLifetimeExit;

            // TODO: These are recursive dependencies which we want to avoid
            _prompter.OnQuit = SafeExit;
            _prompter.ChangeApp = OpenChangeAppMenu;
            _mainViewModel.LoadedView.ToolsView.OnChangeApp = OpenChangeAppMenu;
            
            
            _locker.StartOperation(); // Still starting up
        }

        /// <summary>
        /// Opens a menu which allows the user to change app ID
        /// </summary>
        public async Task OpenChangeAppMenu(bool quitIfNotSelected)
        {
            Window menuWindow = new SelectAppWindow();
            SelectAppWindowViewModel viewModel = new(menuWindow, _config.AppId);
            menuWindow.DataContext = viewModel;
            menuWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Task windowCloseTask = menuWindow.ShowDialog(_mainWindow);

            viewModel.InstalledApps = await _debugBridge.ListNonDefaultPackages();

            await windowCloseTask;
            if(viewModel.SelectedApp == _config.AppId || !viewModel.DidConfirm)
            {
                if(quitIfNotSelected)
                {
                    _prompter.Quit();
                }
            }
            else
            {
                _config.AppId = viewModel.SelectedApp;
                await Reload();
            }
        }

        private async Task Reload()
        {
            // Reset logging to avoid confusing the user
            _mainViewModel.LoadedView.LoggingView.LoggedText = "";
            
            await _qpService.PrepareReload();
            await LoadAndHandleErrors();
        }
        
        private async Task LoadAndHandleErrors()
        {
            if (_locker.IsFree) // Necessary since the operation may have started earlier if this is the first load. Otherwise, we need to start the operation on subsequent loads
            {
                _locker.StartOperation();
            }
            try
            {
                await _qpService.RunStartup();
                // Files are not loaded during startup, since we need to check ADB status first
                // So instead, we refresh the currently selected file copy after starting, if there is one
                var otherItemsView = _mainViewModel.LoadedView.OtherItemsView;
                otherItemsView.OnCurrentDestinationsChanged();
                await otherItemsView.RefreshFiles();
            }
            catch (Exception ex)
            {
                DialogBuilder builder = new()
                {
                    Title = "Unhandled Load Error",
                    Text = "An error occured while loading",
                    HideCancelButton = true,
                    OkButton = { ReturnValue = false }
                };
                builder.WithException(ex);
                builder.WithButtons(
                    new ButtonInfo
                    {
                        Text = "Change App",
                        CloseDialogue = true,
                        ReturnValue = true,
                        OnClick = async () =>
                        {
                            await OpenChangeAppMenu(true);
                        }
                    }
                );

                // If the user did not select to change app, or closed the dialogue, we exit due to the error
                if (!await builder.OpenDialogue(_mainWindow))
                {
                    _logger.Error($"Exiting QuestPatcher due to unhandled load error: {ex}");
                    _prompter.Quit();
                }
            }   finally   {
                _locker.FinishOperation();
            }
        }

        private async void OnMainWindowOpened(object? sender, EventArgs args)
        {
            await LoadAndHandleErrors();
        }
        
        private async void OnMainWindowClosing(object? sender, CancelEventArgs args)
        {
            // Avoid showing this prompt if not in an operation, or if we are closing the window from exiting the application
            if (_locker.IsFree || _inducedShutdownOngoing || sender == null) return;

            // Closing while operations are in progress is a bad idea, so we warn the user
            // We must set this to true at first, even if the user might press OK later.
            // This is since the caller of the event will not wait for our async handler to finish
            args.Cancel = true;
            DialogBuilder builder = new()
            {
                Title = "Operations still in progress!",
                Text =
                    "QuestPatcher still has ongoing operations. Closing QuestPatcher before these finish may lead to corruption of your install!",
                OkButton = { Text = "Close Anyway" }
            };

            MainWindow mainWindow = (MainWindow) sender;
            // Now we can exit the application if the user decides to
            if (await builder.OpenDialogue(mainWindow))
            {
                _prompter.Quit();
            }
        }

        private void SafeExit()
        {
            _inducedShutdownOngoing = true;
            _appLifetime.Shutdown();
        }

        private void OnAppLifetimeExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
        {
            _qpService.Dispose(); // Clean things up, delete temporary folders, etc.
            if (args.ApplicationExitCode != 0)
            {
                _logger.Error($"QuestPatcher quit with non-zero exit code ({args.ApplicationExitCode})");
            }
            else
            {
                _logger.Verbose($"QuestPatcher quit with exit code {args.ApplicationExitCode}");
            }
        }

    }
}