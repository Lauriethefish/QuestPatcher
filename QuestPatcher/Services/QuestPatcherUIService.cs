using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using QuestPatcher.Models;
using QuestPatcher.ViewModels;
using QuestPatcher.Views;
using Serilog;
using Serilog.Events;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using QuestPatcher.ViewModels.Modding;
using QuestPatcher.Core;
using Avalonia.Input;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace QuestPatcher.Services
{
    /// <summary>
    /// Implementation of QuestPatcherService that uses UI message boxes and creates the viewmodel for displaying in UI
    /// </summary>
    public class QuestPatcherUIService : QuestPatcherService
    {
        private readonly Window _mainWindow;

        private readonly IClassicDesktopStyleApplicationLifetime _appLifetime;

        private LoggingViewModel? _loggingViewModel;
        private OperationLocker? _operationLocker;
        private BrowseImportManager? _browseManager;

        public QuestPatcherUIService(IClassicDesktopStyleApplicationLifetime appLifetime) : base(new UIPrompter())
        {
            _appLifetime = appLifetime;

            _mainWindow = InitialiseUI();

            _appLifetime.MainWindow = _mainWindow;
            UIPrompter prompter = (UIPrompter) _prompter;
            prompter.Init(_mainWindow, Config, this, _patchingManager);

            _mainWindow.Opened += OnMainWindowOpen;
            _mainWindow.Closing += OnMainWindowClosing;
        }

        private Window InitialiseUI()
        {
            _loggingViewModel = new LoggingViewModel();
            MainWindow window = new();
            window.Width = 900;
            window.Height = 550;
            _operationLocker = new();
            _browseManager = new(_otherFilesManager, _modManager, window, _logger, _patchingManager, _operationLocker);
            
            MainWindowViewModel mainWindowViewModel = new(
                new LoadedViewModel(
                    new PatchingViewModel(Config, _operationLocker, _patchingManager, window, _logger, ExitApplication),
                    new ManageModsViewModel(_modManager, _patchingManager, window, _operationLocker, _browseManager),
                    _loggingViewModel,
                    new ToolsViewModel(Config, _operationLocker, window, _specialFolders, _logger, _patchingManager, _filesDownloader, _androidDebugBridge, this),
                    Config,
                    _patchingManager,
                    _browseManager,
                    _logger
                ),
                new LoadingViewModel(_loggingViewModel, Config),
                this
            );
            window.DataContext = mainWindowViewModel;

            return window;
        }

        private async void OnMainWindowOpen(object? sender, EventArgs args)
        {
            try
            {
                await RunStartup();
            }
            catch (Exception ex)
            {
                DialogBuilder builder = new()
                {
                    Title = "Unhandled Load Error",
                    Text = "An error occured while loading",
                    HideCancelButton = true
                };
                builder.OkButton.ReturnValue = false;
                builder.WithException(ex);
                builder.WithButtons(
                    new ButtonInfo()
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
                    ExitApplication();
                }
            }
        }

        private async void OnMainWindowClosing(object? sender, CancelEventArgs args)
        {
            Debug.Assert(_operationLocker != null);

            // Closing while operations are in progress is a bad idea, so we warn the user
            if (!_operationLocker.IsFree)
            {
                // We must set this to true at first, even if the user might press OK later.
                // This is since the caller of the event will not wait for our async handler to finish
                args.Cancel = true;
                DialogBuilder builder = new()
                {
                    Title = "Operations still in progress!",
                    Text = "QuestPatcher still has ongoing operations. Closing QuestPatcher before these finish may lead to corruption of your install!"
                };
                builder.OkButton.Text = "Close Anyway";

                // Now we can exit the application if the user decides to
                if (await builder.OpenDialogue(_mainWindow))
                {
                    ExitApplication();
                }
            }
        }

        /// <summary>
        /// Opens a menu which allows the user to change app ID
        /// </summary>
        public async Task OpenChangeAppMenu(bool quitIfNotSelected)
        {
            Window menuWindow = new SelectAppWindow();
            SelectAppWindowViewModel viewModel = new(menuWindow, Config.AppId);
            menuWindow.DataContext = viewModel;

            Task windowCloseTask = menuWindow.ShowDialog(_mainWindow);
            DialogBuilder.CenterWindow(menuWindow, _mainWindow);

            viewModel.InstalledApps = await _androidDebugBridge.ListNonDefaultPackages();

            await windowCloseTask;
            if(viewModel.SelectedApp == Config.AppId || !viewModel.DidConfirm)
            {
                if(quitIfNotSelected)
                {
                    ExitApplication();
                }
            }
            else
            {
                Config.AppId = viewModel.SelectedApp;
                Reload();
            }
        }

        public void Reload()
        {
            if (_loggingViewModel != null)
            {
                _loggingViewModel.LoggedText = ""; // Avoid confusing people by not showing existing logs
            }

            _modManager.OnReload();
            _patchingManager.ResetInstalledApp();
            OnMainWindowOpen(this, new EventArgs());
        }

        protected override void SetLoggingOptions(LoggerConfiguration configuration)
        {
            configuration.MinimumLevel.Verbose()
                .WriteTo.Console(LogEventLevel.Verbose)
                .WriteTo.File(Path.Combine(_specialFolders.LogsFolder, "log.log"), LogEventLevel.Verbose, "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Sink(
                new StringDelegateSink((str) =>
                {
                    if (_loggingViewModel != null)
                    {
                        _loggingViewModel.LoggedText += str + "\n";
                    }
                }),
                LogEventLevel.Information
            );
        }

        protected override void ExitApplication()
        {
            _appLifetime.Shutdown();
        }
    }
}
