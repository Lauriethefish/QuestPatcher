using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Models;
using QuestPatcher.Resources;
using QuestPatcher.Services;
using ReactiveUI;
using Serilog;

namespace QuestPatcher.ViewModels
{
    public class ToolsViewModel : ViewModelBase
    {
        public Config Config { get; }

        public ProgressViewModel ProgressView { get; }

        public OperationLocker Locker { get; }

        public ThemeManager ThemeManager { get; }
        
        public Language SelectedLanguage
        {
            get => Config.Language;
            set
            {
                if (value != Config.Language)
                {
                    // Use ToCultureInfo to check if the new language is actually a different culture
                    // E.g. This is to account for the fact that a change from English to the system default 
                    // isn't actually a change in language if the system default IS English
                    string newCultureName = value.ToCultureInfo().Name;
                    string oldCultureName = Config.Language.ToCultureInfo().Name;

                    // If the culture name is different AND at least one of the cultures isn't English, make the change
                    // QuestPatcher doesn't differentiate between US and UK (real) English so we don't want to prompt
                    // an app reload if both are English.
                    if (newCultureName != oldCultureName && !(newCultureName.StartsWith("en-") && oldCultureName.StartsWith("en")))
                    {
                        ShowLanguageChangeDialog();
                    }

                    Config.Language = value;
                }
            }
        }

        public string AdbButtonText => _isAdbLogging ? Strings.Tools_Tool_ToggleADB_Stop : Strings.Tools_Tool_ToggleADB_Start;

        private bool _isAdbLogging;

        private readonly Window _mainWindow;
        private readonly SpecialFolders _specialFolders;
        private readonly InstallManager _installManager;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly QuestPatcherUiService _uiService;
        private readonly InfoDumper _dumper;
        private readonly Action _quit;

        public ToolsViewModel(Config config, ProgressViewModel progressView, OperationLocker locker, Window mainWindow, SpecialFolders specialFolders, InstallManager installManager, AndroidDebugBridge debugBridge, QuestPatcherUiService uiService, InfoDumper dumper, ThemeManager themeManager, Action quit)
        {
            Config = config;
            ProgressView = progressView;
            Locker = locker;
            ThemeManager = themeManager;

            _mainWindow = mainWindow;
            _specialFolders = specialFolders;
            _installManager = installManager;
            _debugBridge = debugBridge;
            _uiService = uiService;
            _dumper = dumper;
            _quit = quit;

            _debugBridge.StoppedLogging += (_, _) =>
            {
                Log.Information("ADB log exited");
                _isAdbLogging = false;
                this.RaisePropertyChanged(nameof(AdbButtonText));
            };
        }

        public async void UninstallApp()
        {
            try
            {
                var builder = new DialogBuilder
                {
                    Title = Strings.Tools_Tool_UninstallApp_Title,
                    Text = Strings.Tools_Tool_UninstallApp_Text,
                };
                builder.OkButton.Text = Strings.Tools_Tool_UninstallApp_Confirm;
                if (await builder.OpenDialogue(_mainWindow))
                {
                    Locker.StartOperation();
                    try
                    {
                        Log.Information("Uninstalling app . . .");
                        await _installManager.UninstallApp();
                    }
                    finally
                    {
                        Locker.FinishOperation();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to uninstall app");
            }
        }

        public void OpenLogsFolder()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _specialFolders.LogsFolder,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        public async void QuickFix()
        {
            Locker.StartOperation(true); // ADB is not available during a quick fix, as we redownload platform-tools
            try
            {
                await _uiService.QuickFix();
                Log.Information("Done!");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to clear cache");
                var builder = new DialogBuilder
                {
                    Title = Strings.Tools_Tool_QuickFix_Failed_Title,
                    Text = Strings.Tools_Tool_QuickFix_Failed_Text,
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        public async void ToggleAdbLog()
        {
            if (_isAdbLogging)
            {
                _debugBridge.StopLogging();
            }
            else
            {
                await _debugBridge.RunCommand("logcat -c");
                Log.Information("Starting ADB log");
                await _debugBridge.StartLogging(Path.Combine(_specialFolders.LogsFolder, "adb.log"));

                _isAdbLogging = true;
                this.RaisePropertyChanged(nameof(AdbButtonText));
            }
        }

        public async void CreateDump()
        {
            Locker.StartOperation();
            try
            {
                // Create the dump in the default location (the data directory)
                string dumpLocation = await _dumper.CreateInfoDump();

                string? dumpFolder = Path.GetDirectoryName(dumpLocation);
                if (dumpFolder != null)
                {
                    // Open the dump's directory for convenience
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dumpFolder,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch (Exception ex)
            {
                // Show a dialog with any errors
                Log.Error(ex, "Failed to create dump");
                var builder = new DialogBuilder
                {
                    Title = Strings.Tools_Tool_CreateDump_Failed_Title,
                    Text = Strings.Tools_Tool_CreateDump_Failed_Text,
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        public void RepatchApp()
        {
            _uiService.OpenRepatchMenu();
        }

        public async void ChangeApp()
        {
            await _uiService.OpenChangeAppMenu(false);
        }

        public async void RestartApp()
        {
            try
            {
                Log.Information("Restarting app");
                Locker.StartOperation();
                await _debugBridge.ForceStop(Config.AppId);

                // Run the app once, wait, and run again.
                // This bypasses the restore app prompt
                await _debugBridge.RunUnityPlayerActivity(Config.AppId);
                await Task.Delay(1000);
                await _debugBridge.RunUnityPlayerActivity(Config.AppId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to restart app");
                var builder = new DialogBuilder
                {
                    Title = Strings.Tools_Tool_RestartApp_Failed_Title,
                    Text = Strings.Tools_Tool_RestartApp_Failed_Text,
                    HideCancelButton = true
                };
                builder.WithException(ex);

                await builder.OpenDialogue(_mainWindow);
            }
            finally
            {
                Locker.FinishOperation();
            }
        }

        public void OpenThemesFolder()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ThemeManager.ThemesDirectory,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        
        private async void ShowLanguageChangeDialog()
        {
            Strings.Culture = Config.Language.ToCultureInfo(); // Update the resource language so the dialog is in the correct language 
            
            var builder = new DialogBuilder
            {
                Title = Strings.Tools_Option_Language_Title,
                Text = Strings.Tools_Option_Language_Text,
            };
            builder.OkButton.Text = Strings.Generic_OK;
            builder.CancelButton.Text = Strings.Generic_NotNow;
            builder.OkButton.CloseDialogue = false;
            builder.OkButton.OnClick = () =>
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if(exePath != null)
                {
                    Process.Start(exePath);
                }

                _quit();
            };
            

            await builder.OpenDialogue(_mainWindow);
        }
    }
}
