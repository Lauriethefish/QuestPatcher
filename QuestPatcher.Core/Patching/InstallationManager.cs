using QuestPatcher.Core.Models;
using Serilog.Core;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Patching
{
    /// <summary>
    /// Handles checking if the selected app is modded, alongside patching it if not
    /// </summary>
    public class InstallationManager : INotifyPropertyChanged
    {
        public ApkInfo? InstalledApp { get => _installedApp; private set { if (_installedApp != value) { _installedApp = value; NotifyPropertyChanged(); } } }
        private ApkInfo? _installedApp;

        public PatchingStage PatchingStage { get => _patchingStage; private set { if(_patchingStage != value) { _patchingStage = value; NotifyPropertyChanged(); } } }
        private PatchingStage _patchingStage = PatchingStage.NotStarted;

        public event PropertyChangedEventHandler? PropertyChanged;
        
        private readonly Logger _logger;
        private readonly Config _config;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly TempFolders _tempFolders;
        private readonly IUserPrompter _prompter;
        private readonly ApkSigner _apkSigner;
        private readonly Action _quit;
        private readonly AppPatcher _patcher;

        private readonly string _storedApkPath;

        public InstallationManager(Logger logger, Config config, AndroidDebugBridge debugBridge, TempFolders tempFolders, IUserPrompter prompter, ApkSigner apkSigner, Action quit, AppPatcher patcher)
        {
            _logger = logger;
            _config = config;
            _debugBridge = debugBridge;
            _tempFolders = tempFolders;
            _prompter = prompter;
            _apkSigner = apkSigner;
            _quit = quit;
            _patcher = patcher;

            _storedApkPath = Path.Combine(_tempFolders.PatchingFolder, "currentlyInstalled.apk");
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Finds a string beginning at <paramref name="idx"/> and continuing until a new line character is found.
        /// The resulting string is then trimmed.
        /// </summary>
        /// <param name="str">String to index in</param>
        /// <param name="idx">Starting index</param>
        /// <returns>Trimmed string beginning at <paramref name="idx"/> and continuing until a new line</returns>
        private string ContinueUntilNewline(string str, int idx)
        {
            StringBuilder result = new();
            while(str[idx] != '\n')
            {
                result.Append(str[idx]);
                idx++;
            }

            return result.ToString().Trim();
        }

        /// <summary>
        /// Gets the value in the package dump with the given name.
        /// </summary>
        /// <param name="packageDump">Dump of a package from adb shell dumpsys package</param>
        /// <param name="name">Name of the value to get</param>
        /// <returns>Value from the package dump, as a trimmed string</returns>
        private string GetPackageDumpValue(string packageDump, string name)
        {
            string fullName = $"{name}=";
            int idx = packageDump.IndexOf(fullName, StringComparison.Ordinal);
            return ContinueUntilNewline(packageDump, idx + fullName.Length);
        }

        public async Task LoadInstalledApp()
        {
            bool is64Bit = false;
            bool isModded = false;
            
            string packageDump = (await _debugBridge.RunCommand($"shell dumpsys \"package {_config.AppId}\"")).StandardOutput;
            string version = GetPackageDumpValue(packageDump, "versionName");
            _logger.Information($"App Version: {version}");

            int beginPermissionsIdx = packageDump.IndexOf("requested permissions:", StringComparison.Ordinal);
            int endPermissionsIdx = packageDump.IndexOf("install permissions:", StringComparison.Ordinal);
            string permissionsString = packageDump.Substring(beginPermissionsIdx, endPermissionsIdx - beginPermissionsIdx);

            _logger.Information("Attempting to check modding status from package dump");
            // If the APK's permissions include the modded tag permission, then we know the APK is modded
            // This avoids having to pull the APK from the quest to check it if it's modded
            if(permissionsString.Split("\n").Skip(1).Select(perm => perm.Trim()).Contains(ApkAnalyser.TagPermission))
            {
                _logger.Information("Modded permission found in dumpsys output.");
                string cpuAbi = GetPackageDumpValue(packageDump, "primaryCpuAbi");
                // Currently, these are the only CPU ABIs supported
                is64Bit = cpuAbi == "arm64-v8a";
                bool is32Bit = cpuAbi == "armeabi-v7a";
                if(!is32Bit && !is64Bit)
                {
                    throw new PatchingException(
                        "APK was of an unsupported architecture, only arm64-v8a and armeabi-v7a are supported");
                }
                
                isModded = true;
            }
            else
            {
                // If the modded permission is not found, it is still possible that the APK is modded
                // Older QuestPatcher versions did not use a modded permission, and instead used a "modded" file in APK root
                // (which is still added during patching for backwards compatibility, and so that BMBF can see that the APK is patched)
                _logger.Information("Modded permission not found, downloading APK from the Quest instead . . .");
                await _debugBridge.DownloadApk(_config.AppId, _storedApkPath);

                // Unfortunately, zip files do not support async, so we Task.Run this operation to avoid blocking
                _logger.Information("Checking APK modding status . . .");
                await Task.Run(() =>
                {
                    using ZipArchive apkArchive = ZipFile.OpenRead(_storedApkPath);

                    // QuestPatcher adds a tag file to determine if the APK is modded later on
                    ApkAnalyser.GetApkInfo(apkArchive, out is64Bit, out isModded, out _);
                });
            }
            
            _logger.Information((isModded ? "APK is modded" : "APK is not modded") + " and is " + (is64Bit ? "64" : "32") + " bit");

            InstalledApp = new ApkInfo(version, isModded, is64Bit);
        }

        public void ResetInstalledApp()
        {
            InstalledApp = null;
        }



        /// <summary>
        /// Begins patching the currently installed APK. (must be pulled before calling this)
        /// </summary>
        public async Task PatchApp()
        {
            if (InstalledApp == null)
            {
                throw new NullReferenceException("Cannot patch before installed app has been checked");
            }
            
            _patchingStage = PatchingStage.MovingToTemp;
            _logger.Information("Copying APK to patched location . . .");
            string patchedApkPath = Path.Combine(_tempFolders.PatchingFolder, "patched.apk");

            // There is no async file copy method, so we Task.Run it (we could make our own with streams, that's another option)
            await Task.Run(() => { File.Copy(_storedApkPath, patchedApkPath, true); });
            
            _logger.Information("Copying files to patch APK . . .");

            PatchingStage = PatchingStage.Patching;
            ZipArchive apkArchive = ZipFile.Open(patchedApkPath, ZipArchiveMode.Update);
            try
            {
                // Actually patch the APK
                if(!await _patcher.Patch(apkArchive, async () => await _prompter.PromptUnstrippedUnityUnavailable(),
                    _config.PatchingPermissions))
                {
                    return;
                }
            }
            finally
            {
                // The disk IO while opening the APK as a zip archive causes a UI freeze, so we run it on another thread
                _logger.Information("Closing APK archive . . .");
                await Task.Run(() => { apkArchive.Dispose(); });
            }
            
            // Pause patching before compiling the APK in order to give a developer the chance to modify it.
            if(_config.PauseBeforeCompile && !await _prompter.PromptPauseBeforeCompile())
            {
                return;
            }
            
            _logger.Information("Signing APK (this might take a while) . . .");
            PatchingStage = PatchingStage.Signing;

            await _apkSigner.SignApkWithPatchingCertificate(patchedApkPath);

            _logger.Information("Uninstalling the default APK . . .");
            PatchingStage = PatchingStage.UninstallingOriginal;

            await _debugBridge.UninstallApp(_config.AppId);

            _logger.Information("Installing the modded APK . . .");
            PatchingStage = PatchingStage.InstallingModded;

            await _debugBridge.InstallApp(patchedApkPath);
            
            try
            {
                File.Delete(patchedApkPath); // The patched APK takes up space, and we don't need it now
            }
            catch (IOException) // Sometimes a developer might be using the APK, so avoid failing the whole patching process
            {
                _logger.Warning("Failed to delete patched APK");
            }

            _logger.Information("Patching complete!");
            InstalledApp.IsModded = true;
        }

        /// <summary>
        /// Uninstalls the installed app.
        /// Quits QuestPatcher, since it relies on the app being installed
        /// </summary>
        public async Task UninstallCurrentApp()
        {
            await _debugBridge.UninstallApp(_config.AppId);
            _quit();
        }
    }
}
