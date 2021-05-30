using QuestPatcher.Core.Models;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Patching;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace QuestPatcher.Core
{
    /// <summary>
    /// The main class that manages most QuestPatcher services.
    /// Allows user prompts etc. to be abstracted through 
    /// </summary>
    public abstract class QuestPatcherService : INotifyPropertyChanged
    {
        protected readonly SpecialFolders _specialFolders;
        protected readonly Logger _logger;
        protected readonly ConfigManager _configManager;
        protected readonly PatchingManager _patchingManager;
        protected readonly ModManager _modManager;
        protected readonly AndroidDebugBridge _androidDebugBridge;
        protected readonly ExternalFilesDownloader _filesDownloader;
        protected readonly OtherFilesManager _otherFilesManager;
        protected readonly IUserPrompter _prompter;

        protected Config Config => _configManager.Config;
        protected string AppId => _configManager.Config.AppId;

        private readonly ApkTools _apkTools;

        public bool HasLoaded { get => hasLoaded; private set { if(hasLoaded != value) { hasLoaded = value; NotifyPropertyChanged(); } } }
        private bool hasLoaded = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public QuestPatcherService(IUserPrompter prompter)
        {
            _prompter = prompter;
            _specialFolders = new SpecialFolders(); // Load QuestPatcher application folders

            _logger = SetupLogging();
            _configManager = new ConfigManager(_logger, _specialFolders);
            _filesDownloader = new ExternalFilesDownloader(_specialFolders, _logger);
            _apkTools = new ApkTools(_logger, _filesDownloader);
            _androidDebugBridge = new AndroidDebugBridge(_logger, _filesDownloader, OnAdbDisconnect);
            _patchingManager = new PatchingManager(_logger, Config, _androidDebugBridge, _apkTools, _specialFolders, _filesDownloader, _prompter, ExitApplication);
            _modManager = new ModManager(_logger, _androidDebugBridge, _specialFolders, _patchingManager, Config, _filesDownloader);
            _otherFilesManager = new OtherFilesManager(Config, _androidDebugBridge);

            _logger.Debug("QuestPatcherService constructed");
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets up basic logging to the logs folder and the console.
        /// Also calls the subclass to allow inheriters to add extra logging options
        /// </summary>
        private Logger SetupLogging()
        {
            LoggerConfiguration configuration = new();

            SetLoggingOptions(configuration);
            return configuration.CreateLogger();
        }
        
        /// <summary>
        /// Adds extra logging options
        /// </summary>
        /// <param name="configuration">Logging configuration that will be used to create the logger</param>
        protected virtual void SetLoggingOptions(LoggerConfiguration configuration) { }

        /// <summary>
        /// Should exit the underlying application however the implementors see fit
        /// </summary>
        protected abstract void ExitApplication();

        /// <summary>
        /// Should be called upon application exit, cleans up temporary files.
        /// Note that this isn't called before Exit, since Exit just closes the underlying application, which should call this method.
        /// This is done to avoid a double call where we clean up, then exit is called, then the underlying application calls to clean up again.
        /// </summary>
        public void CleanUp()
        {
            _logger.Debug("Closing QuestPatcher . . .");
            _configManager.SaveConfig();
            try
            {
                Directory.Delete(_specialFolders.TempFolder, true);
            }
            catch (Exception)
            {
                _logger.Warning("Failed to delete temporary directory");
            }
            _logger.Debug("Goodbye!");
        }

        public async Task RunStartup()
        {
            HasLoaded = false;
            _logger.Information("Starting QuestPatcher . . .");
            await _apkTools.PrepareJavaInstall();

            if(!await _androidDebugBridge.IsPackageInstalled(AppId))
            {
                if (await _prompter.PromptAppNotInstalled())
                {
                    ExitApplication();
                }
                else
                {
                    return; // New app ID selected - we reload
                }
            }
            _logger.Information("App is installed");

            await _patchingManager.LoadInstalledApp();
            await _modManager.LoadInstalledMods();
            HasLoaded = true;
        }

        /// <summary>
        /// Repeatedly called while ADB is disconnected until it connects again
        /// </summary>
        /// <param name="type">What caused the disconnection</param>
        private async Task OnAdbDisconnect(DisconnectionType type)
        {
            if(!await _prompter.PromptAdbDisconnect(type))
            {
                ExitApplication();
            }
        }

        /// <summary>
        /// Clears cached QuestPatcher files, and deletes APKtool temporary files.
        /// This really shouldn't be necessary, but often fixes issues.
        /// The "partially extracted download" or "partially downloaded file" causing issues shouldn't be a thing with the new file download system, however this is here just in case it still is.
        /// </summary>
        public async Task QuickFix()
        {
            _logger.Information("Deleting apktool temp files . . .");
            // Apktool temporary files sometimes get corrupted, so we delete them
            string apkToolFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "apktool");
            if (Directory.Exists(apkToolFilesPath))
            {
                Directory.Delete(apkToolFilesPath, true);
            }

            await _androidDebugBridge.KillServer(); // Allow ADB to be deleted

            // Sometimes files fail to download so we clear them. This shouldn't happen anymore but I may as well add it to be on the safe side
            await _filesDownloader.ClearCache();
            await _androidDebugBridge.PrepareAdbPath(); // Redownload ADB if necessary
            await _apkTools.PrepareJavaInstall(); // Redownload Java if necessary
        }
    }
}
