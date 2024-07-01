using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Models;
using QuestPatcher.Resources;
using QuestPatcher.ViewModels;
using QuestPatcher.ViewModels.Modding;
using QuestPatcher.Views;
using Serilog;
using Serilog.Events;

namespace QuestPatcher.Services
{
    /// <summary>
    /// Implementation of QuestPatcherService that uses UI message boxes and creates the viewmodel for displaying in UI
    /// </summary>
    public class QuestPatcherUiService : QuestPatcherService
    {
        private readonly Window _mainWindow;

        private readonly IClassicDesktopStyleApplicationLifetime _appLifetime;

        private LoggingViewModel? _loggingViewModel;
        private OperationLocker? _operationLocker;
        private BrowseImportManager? _browseManager;
        private OtherItemsViewModel? _otherItemsView;
        private PatchingViewModel? _patchingView;

        private readonly ThemeManager _themeManager;
        private bool _isShuttingDown;

        public QuestPatcherUiService(IClassicDesktopStyleApplicationLifetime appLifetime) : base(new UIPrompter())
        {
            _appLifetime = appLifetime;
            _themeManager = new ThemeManager(Config, SpecialFolders);

            // Deal with language configuration before we load the UI
            try
            {
                var language = Config.Language.ToCultureInfo();
                Strings.Culture = language;
            }
            catch (Exception)
            {
                Log.Warning("Failed to set language from config: {Code}", Config.Language);
                Config.Language = Language.Default;
                Strings.Culture = null;
            }

            _mainWindow = PrepareUi();

            _appLifetime.MainWindow = _mainWindow;
            var prompter = (UIPrompter) Prompter;
            prompter.Init(_mainWindow, Config, this, SpecialFolders);

            _mainWindow.Opened += async (_, _) => await LoadAndHandleErrors();
            _mainWindow.Closing += OnMainWindowClosing;
        }

        private Window PrepareUi()
        {
            _loggingViewModel = new LoggingViewModel();
            MainWindow window = new()
            {
                Width = 900,
                Height = 550
            };
            _operationLocker = new();
            _operationLocker.StartOperation(); // Still loading
            _browseManager = new(OtherFilesManager, ModManager, window, InstallManager, _operationLocker, this, FilesDownloader);
            ProgressViewModel progressViewModel = new(_operationLocker, FilesDownloader);
            _otherItemsView = new OtherItemsViewModel(OtherFilesManager, window, _browseManager, _operationLocker, progressViewModel);
            _patchingView = new PatchingViewModel(Config, _operationLocker, PatchingManager, InstallManager, window, progressViewModel, FilesDownloader);

            MainWindowViewModel mainWindowViewModel = new(
                new LoadedViewModel(
                    _patchingView,
                    new ManageModsViewModel(ModManager, InstallManager, window, _operationLocker, progressViewModel, _browseManager),
                    _loggingViewModel,
                    new ToolsViewModel(Config, progressViewModel, _operationLocker, window, SpecialFolders, InstallManager, DebugBridge, this, InfoDumper,
                        _themeManager, ExitApplication),
                    _otherItemsView,
                    Config,
                    InstallManager,
                    _browseManager
                ),
                new LoadingViewModel(progressViewModel, _loggingViewModel, Config),
                this
            );
            window.DataContext = mainWindowViewModel;

            return window;
        }

        private async Task LoadAndHandleErrors()
        {
            Debug.Assert(_operationLocker != null); // Main window has been loaded, so this is assigned
            if (_operationLocker.IsFree) // Necessary since the operation may have started earlier if this is the first load. Otherwise, we need to start the operation on subsequent loads
            {
                _operationLocker.StartOperation();
            }
            try
            {
                await RunStartup();
                // Files are not loaded during startup, since we need to check ADB status first
                // So instead, we refresh the currently selected file copy after starting, if there is one
                _otherItemsView?.RefreshFiles();
            }
            catch (Exception ex)
            {
                var builder = new DialogBuilder
                {
                    Title = Strings.Loading_UnhandledError_Title,
                    Text = Strings.Loading_UnhandledError_Text,
                    HideCancelButton = true,
                };
                builder.OkButton.ReturnValue = false;
                builder.WithException(ex);
                builder.WithButtons(
                    new ButtonInfo
                    {
                        Text = Strings.Loading_UnhandledError_ChangeApp,
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
                    Log.Error(ex, "Exiting QuestPatcher due to unhandled load error: {Ex}", ex.Message);
                    ExitApplication();
                }
            }
            finally
            {
                _operationLocker.FinishOperation();
            }
        }

        private async void OnMainWindowClosing(object? sender, CancelEventArgs args)
        {
            Debug.Assert(_operationLocker != null);

            // Avoid showing this prompt if not in an operation, or if we are closing the window from exiting the application
            if (_operationLocker.IsFree || _isShuttingDown) return;

            // Closing while operations are in progress is a bad idea, so we warn the user
            // We must set this to true at first, even if the user might press OK later.
            // This is since the caller of the event will not wait for our async handler to finish
            args.Cancel = true;
            var builder = new DialogBuilder
            {
                Title = Strings.Prompt_OperationInProgress_Title,
                Text = Strings.Prompt_OperationInProgress_Text
            };
            builder.OkButton.Text = Strings.Generic_CloseAnyway;

            // Now we can exit the application if the user decides to
            if (await builder.OpenDialogue(_mainWindow))
            {
                ExitApplication();
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
            menuWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var windowCloseTask = menuWindow.ShowDialog(_mainWindow);

            viewModel.InstalledApps = await DebugBridge.ListNonDefaultPackages();

            await windowCloseTask;
            if (viewModel.SelectedApp == Config.AppId || !viewModel.DidConfirm)
            {
                if (quitIfNotSelected)
                {
                    ExitApplication();
                }
            }
            else
            {
                Config.AppId = viewModel.SelectedApp;
                await Reload();
            }
        }

        /// <summary>
        /// Opens a window that allows the user to change the modloader they have installed by re-patching their app.
        /// </summary>
        /// <param name="preferredModloader">The modloader that will be selected for patching by default. The user can change this.</param>
        public async void OpenRepatchMenu(ModLoader? preferredModloader = null)
        {
            if (preferredModloader != null)
            {
                Config.PatchingOptions.ModLoader = (ModLoader) preferredModloader;
            }

            Window menuWindow = new RepatchWindow();
            menuWindow.DataContext = new RepatchWindowViewModel(_patchingView!, Config, menuWindow);
            menuWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await menuWindow.ShowDialog(_mainWindow);
        }

        private async Task Reload()
        {
            if (_loggingViewModel != null)
            {
                _loggingViewModel.LoggedText = ""; // Avoid confusing people by not showing existing logs
            }

            ModManager.Reset();
            InstallManager.ResetInstalledApp();
            await LoadAndHandleErrors();
        }

        protected override void SetLoggingOptions(LoggerConfiguration configuration)
        {
            configuration.MinimumLevel.Verbose()
                .WriteTo.File(Path.Combine(SpecialFolders.LogsFolder, "log.log"), LogEventLevel.Verbose, "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Console()
                .WriteTo.Sink(
                new StringDelegateSink(str =>
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
            _isShuttingDown = true;
            _appLifetime.Shutdown();
        }
    }
}
