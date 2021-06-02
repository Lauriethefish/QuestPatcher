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
        protected ConfigManager ConfigManager { get; }
        protected PatchingManager PatchingManager { get; }
        protected ModManager ModManager { get; }
        protected AndroidDebugBridge DebugBridge { get; }
        protected ExternalFilesDownloader FilesDownloader { get; }
        protected OtherFilesManager OtherFilesManager { get; }
        protected IUserPrompter Prompter { get; }

        protected Config Config => ConfigManager.Config;
        protected string AppId => ConfigManager.Config.AppId;

        private readonly ApkTools _apkTools;

        public bool HasLoaded { get => hasLoaded; private set { if(hasLoaded != value) { hasLoaded = value; NotifyPropertyChanged(); } } }
        private bool hasLoaded = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public QuestPatcherService(IUserPrompter prompter)
        {
            Prompter = prompter;
            SpecialFolders = new SpecialFolders(); // Load QuestPatcher application folders

            Logger = SetupLogging();
            ConfigManager = new ConfigManager(Logger, SpecialFolders);
            FilesDownloader = new ExternalFilesDownloader(SpecialFolders, Logger);
            _apkTools = new ApkTools(Logger, FilesDownloader);
            DebugBridge = new AndroidDebugBridge(Logger, FilesDownloader, OnAdbDisconnect);
            PatchingManager = new PatchingManager(Logger, Config, DebugBridge, _apkTools, SpecialFolders, FilesDownloader, Prompter, ExitApplication);
            ModManager = new ModManager(Logger, DebugBridge, SpecialFolders, PatchingManager, Config, FilesDownloader);
            OtherFilesManager = new OtherFilesManager(Config, DebugBridge);

            Logger.Debug("QuestPatcherService constructed");
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
            Logger.Debug("Closing QuestPatcher . . .");
            ConfigManager.SaveConfig();
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

        public async Task RunStartup()
        {
            HasLoaded = false;
            Logger.Information("Starting QuestPatcher . . .");
            await _apkTools.PrepareJavaInstall();

            if(!await DebugBridge.IsPackageInstalled(AppId))
            {
                if (await Prompter.PromptAppNotInstalled())
                {
                    ExitApplication();
                }
                else
                {
                    return; // New app ID selected - we reload
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
                if (File.Exists(oldLogPath))
                {
                    File.Delete(oldLogPath);
                }
                if (File.Exists(oldAdbLogPath))
                {
                    File.Delete(oldAdbLogPath);
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
        /// Clears cached QuestPatcher files, and deletes APKtool temporary files.
        /// This really shouldn't be necessary, but often fixes issues.
        /// The "partially extracted download" or "partially downloaded file" causing issues shouldn't be a thing with the new file download system, however this is here just in case it still is.
        /// </summary>
        public async Task QuickFix()
        {
            Logger.Information("Deleting apktool temp files . . .");
            // Apktool temporary files sometimes get corrupted, so we delete them
            string apkToolFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "apktool");
            if (Directory.Exists(apkToolFilesPath))
            {
                Directory.Delete(apkToolFilesPath, true);
            }

            await DebugBridge.KillServer(); // Allow ADB to be deleted

            // Sometimes files fail to download so we clear them. This shouldn't happen anymore but I may as well add it to be on the safe side
            await FilesDownloader.ClearCache();
            await DebugBridge.PrepareAdbPath(); // Redownload ADB if necessary
            await _apkTools.PrepareJavaInstall(); // Redownload Java if necessary
        }
    }
}
