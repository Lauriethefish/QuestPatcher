using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using Serilog;
using Serilog.Core;
using System;
using System.ComponentModel;
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
        protected SpecialFolders SpecialFolders { get; }
        protected Logger Logger { get; }
        protected PatchingManager PatchingManager { get; }
        protected ModManager ModManager { get; }
        protected AndroidDebugBridge DebugBridge { get; }
        protected ExternalFilesDownloader FilesDownloader { get; }
        protected OtherFilesManager OtherFilesManager { get; }
        protected IUserPrompter Prompter { get; }
        
        protected InfoDumper InfoDumper { get; }

        protected Config Config => _configManager.GetOrLoadConfig();
        
        private readonly ApkTools _apkTools;
        private readonly ConfigManager _configManager;

        public bool HasLoaded { get => _hasLoaded; private set { if(_hasLoaded != value) { _hasLoaded = value; NotifyPropertyChanged(); } } }
        private bool _hasLoaded;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected QuestPatcherService(IUserPrompter prompter)
        {
            Prompter = prompter;
            SpecialFolders = new SpecialFolders(); // Load QuestPatcher application folders

            Logger = SetupLogging();
            _configManager = new ConfigManager(Logger, SpecialFolders);
            _configManager.GetOrLoadConfig(); // Load the config file
            FilesDownloader = new ExternalFilesDownloader(SpecialFolders, Logger);
            _apkTools = new ApkTools(Logger, FilesDownloader);
            DebugBridge = new AndroidDebugBridge(Logger, FilesDownloader, OnAdbDisconnect);
            PatchingManager = new PatchingManager(Logger, Config, DebugBridge, _apkTools, SpecialFolders, FilesDownloader, Prompter, ExitApplication);
            ModManager = new ModManager(Logger, DebugBridge, SpecialFolders, PatchingManager, Config, FilesDownloader);
            OtherFilesManager = new OtherFilesManager(Config, DebugBridge);
            InfoDumper = new InfoDumper(SpecialFolders, DebugBridge, ModManager, Logger, _configManager, PatchingManager);

            Logger.Debug("QuestPatcherService constructed");
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets up basic logging to the logs folder and the console.
        /// Also calls the subclass to allow inheritors to add extra logging options
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
            Logger.Debug("Closing QuestPatcher . . .");
            _configManager.SaveConfig();
            try
            {
                Directory.Delete(SpecialFolders.TempFolder, true);
            }
            catch (Exception)
            {
                Logger.Warning("Failed to delete temporary directory");
            }
            Logger.Debug("Goodbye!");
        }

        protected async Task RunStartup()
        {
            HasLoaded = false;
            Logger.Information("Starting QuestPatcher . . .");
            await _apkTools.PrepareJavaInstall();

            if(!await DebugBridge.IsPackageInstalled(Config.AppId))
            {
                if (await Prompter.PromptAppNotInstalled())
                {
                    return; // New app ID selected - we will later reload
                }
                else
                {
                    ExitApplication();
                }
            }
            Logger.Information("App is installed");

            await MigrateOldFiles();

            await PatchingManager.LoadInstalledApp();
            await ModManager.LoadInstalledMods();
            HasLoaded = true;
        }

        /// <summary>
        /// Migrates old mods and displays the migration prompt if there were mods to migrate.
        /// Also deletes the old platform-tools folder to save space, since this has now been moved.
        /// </summary>
        private async Task MigrateOldFiles()
        {
            Logger.Information("Deleting old files. . .");
            try
            {
                string oldPlatformToolsPath = Path.Combine(SpecialFolders.DataFolder, "platform-tools");
                if (Directory.Exists(oldPlatformToolsPath))
                {
                    Directory.Delete(oldPlatformToolsPath, true);
                }

                string oldLogPath = Path.Combine(SpecialFolders.DataFolder, "log.log");
                string oldAdbLogPath = Path.Combine(SpecialFolders.DataFolder, "adb.log");
                string oldApkToolPath = Path.Combine(SpecialFolders.ToolsFolder, "apktool.jar");
                if (File.Exists(oldLogPath))
                {
                    File.Delete(oldLogPath);
                }
                if (File.Exists(oldAdbLogPath))
                {
                    File.Delete(oldAdbLogPath);
                }
                if (File.Exists(oldApkToolPath))
                {
                    File.Delete(oldApkToolPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to delete QP1 files: {ex}");
            }

            if(await ModManager.DetectAndRemoveOldMods())
            {
                await Prompter.PromptUpgradeFromOld();
            }
        }

        /// <summary>
        /// Repeatedly called while ADB is disconnected until it connects again
        /// </summary>
        /// <param name="type">What caused the disconnection</param>
        private async Task OnAdbDisconnect(DisconnectionType type)
        {
            if(!await Prompter.PromptAdbDisconnect(type))
            {
                ExitApplication();
            }
        }

        /// <summary>
        /// Clears cached QuestPatcher files.
        /// This really shouldn't be necessary, but often fixes issues.
        /// The "partially extracted download" or "partially downloaded file" causing issues shouldn't be an issue with the new file download system, however this is here just in case it still is.
        /// </summary>
        public async Task QuickFix()
        {
            await DebugBridge.KillServer(); // Allow ADB to be deleted

            // Sometimes files fail to download so we clear them. This shouldn't happen anymore but I may as well add it to be on the safe side
            await FilesDownloader.ClearCache();
            await DebugBridge.PrepareAdbPath(); // Re-download ADB if necessary
            await _apkTools.PrepareJavaInstall(); // Re-download Java if necessary
        }
    }
}
