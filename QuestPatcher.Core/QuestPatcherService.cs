using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
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
    public class QuestPatcherService : INotifyPropertyChanged, IDisposable
    {
        private readonly SpecialFolders _specialFolders;
        private readonly Logger _logger;
        private readonly PatchingManager _patchingManager;
        private readonly ModManager _modManager;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly ICallbacks _prompter;

        private Config Config => _configManager.GetOrLoadConfig();
        
        private readonly ApkTools _apkTools;
        private readonly ConfigManager _configManager;

        public bool HasLoaded { get => _hasLoaded; private set { if(_hasLoaded != value) { _hasLoaded = value; NotifyPropertyChanged(); } } }
        private bool _hasLoaded;

        private bool _disposed = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public QuestPatcherService(ICallbacks prompter, SpecialFolders specialFolders, Logger logger, ConfigManager configManager, ExternalFilesDownloader filesDownloader, ApkTools apkTools, AndroidDebugBridge debugBridge, PatchingManager patchingManager, ModManager modManager)
        {
            _prompter = prompter;
            _specialFolders = specialFolders;
            _logger = logger;
            _configManager = configManager;
            _filesDownloader = filesDownloader;
            _apkTools = apkTools;
            _debugBridge = debugBridge;
            _patchingManager = patchingManager;
            _modManager = modManager;

            _logger.Debug($"QuestPatcherService constructed (QuestPatcher version {VersionUtil.QuestPatcherVersion})");
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async Task RunStartup()
        {
            HasLoaded = false;
            _logger.Information("Starting QuestPatcher . . .");
            await _apkTools.PrepareJavaInstall();

            if(!await _debugBridge.IsPackageInstalled(Config.AppId))
            {
                if (await _prompter.PromptAppNotInstalled())
                {
                    return; // New app ID selected - we will later reload
                }
                _prompter.Quit();
            }
            _logger.Information("App is installed");

            await MigrateOldFiles();

            await _patchingManager.LoadInstalledApp();
            await _modManager.LoadInstalledMods();
            HasLoaded = true;
        }

        public async Task PrepareReload()
        {
            _modManager.OnReload();
            _patchingManager.ResetInstalledApp();
        }

        /// <summary>
        /// Migrates old mods and displays the migration prompt if there were mods to migrate.
        /// Also deletes the old platform-tools folder to save space, since this has now been moved.
        /// </summary>
        private async Task MigrateOldFiles()
        {
            _logger.Information("Deleting old files. . .");
            try
            {
                string oldPlatformToolsPath = Path.Combine(_specialFolders.DataFolder, "platform-tools");
                if (Directory.Exists(oldPlatformToolsPath))
                {
                    Directory.Delete(oldPlatformToolsPath, true);
                }

                string oldLogPath = Path.Combine(_specialFolders.DataFolder, "log.log");
                string oldAdbLogPath = Path.Combine(_specialFolders.DataFolder, "adb.log");
                string oldApkToolPath = Path.Combine(_specialFolders.ToolsFolder, "apktool.jar");
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
                _logger.Warning($"Failed to delete QP1 files: {ex}");
            }

            if(await _modManager.DetectAndRemoveOldMods())
            {
                await _prompter.PromptUpgradeFromOld();
            }
        }

        /// <summary>
        /// Clears cached QuestPatcher files.
        /// This really shouldn't be necessary, but often fixes issues.
        /// The "partially extracted download" or "partially downloaded file" causing issues shouldn't be an issue with the new file download system, however this is here just in case it still is.
        /// </summary>
        public async Task QuickFix()
        {
            await _debugBridge.KillServer(); // Allow ADB to be deleted

            // Sometimes files fail to download so we clear them. This shouldn't happen anymore but I may as well add it to be on the safe side
            await _filesDownloader.ClearCache();
            await _debugBridge.PrepareAdbPath(); // Re-download ADB if necessary
            await _apkTools.PrepareJavaInstall(); // Re-download Java if necessary
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
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
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
