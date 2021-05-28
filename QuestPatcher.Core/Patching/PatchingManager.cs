using Newtonsoft.Json;
using QuestPatcher.Core.Models;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Patching
{
    /// <summary>
    /// Handles checking if the selected app is modded, alongside patching it if not
    /// </summary>
    public class PatchingManager : INotifyPropertyChanged
    {
        public ApkInfo? InstalledApp { get => _installedApp; private set { if (_installedApp != value) { _installedApp = value; NotifyPropertyChanged(); } } }
        private ApkInfo? _installedApp;

        public PatchingStage PatchingStage { get => _patchingStage; private set { if(_patchingStage != value) { _patchingStage = value; NotifyPropertyChanged(); } } }
        private PatchingStage _patchingStage = PatchingStage.NotStarted;

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler? PatchingCompleted;

        private readonly Logger _logger;
        private readonly Config _config;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly ApkTools _apkTools;
        private readonly SpecialFolders _specialFolders;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly IUserPrompter _prompter;
        private readonly Action _quit;

        private readonly string _currentApkPath;
        private readonly string _decompPath;
        private Dictionary<string, Dictionary<string, string>>? _libUnityIndex;

        public PatchingManager(Logger logger, Config config, AndroidDebugBridge debugBridge, ApkTools apkTools, SpecialFolders specialFolders, ExternalFilesDownloader filesDownloader, IUserPrompter prompter, Action quit)
        {
            _logger = logger;
            _config = config;
            _debugBridge = debugBridge;
            _apkTools = apkTools;
            _specialFolders = specialFolders;
            _filesDownloader = filesDownloader;
            _prompter = prompter;
            _quit = quit;

            _currentApkPath = Path.Combine(specialFolders.PatchingFolder, "currentlyInstalled.apk");
            _decompPath = Path.Combine(specialFolders.PatchingFolder, "decompiledApp");
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async Task LoadInstalledApp()
        {
            string version = await _debugBridge.GetPackageVersion(_config.AppId);
            _logger.Information($"App Version: {version}");

            _logger.Information("Downloading APK from the Quest . . .");
            await _debugBridge.DownloadApk(_config.AppId, _currentApkPath);

            bool isModded = false;
            bool is64Bit = false;
            bool is32Bit = false;
            // Unfortunately, zip files do not support async, so we Task.Run this operation to avoid blocking
            _logger.Information("Checking APK modding status . . .");
            await Task.Run(() =>
            {
                using ZipArchive apkArchive = ZipFile.OpenRead(_currentApkPath);

                // QuestPatcher adds a tag file to determine if the APK is modded later on
                isModded = apkArchive.GetEntry("modded") != null || apkArchive.GetEntry("BMBF.modded") != null;
                is64Bit = apkArchive.GetEntry("lib/arm64-v8a/libil2cpp.so") != null;
                is32Bit = apkArchive.GetEntry("lib/armeabi-v7a/libil2cpp.so") != null;
            });

            if (!is64Bit && !is32Bit)
            {
                throw new PatchingException("The loaded APK did not contain a 32 or 64 bit libil2cpp for patching. This either means that it is of an unsupported architecture, or it is not an il2cpp unity app."
                    + " Please complain to Laurie if you're annoyed that QuestPatcher doesn't support unreal.");
            }
            _logger.Information((isModded ? "APK is modded" : "APK is not modded") + " and is " + (is64Bit ? "64" : "32") + " bit");

            InstalledApp = new ApkInfo(version, isModded, is64Bit);
        }

        public void ResetInstalledApp()
        {
            InstalledApp = null;
        }

        private async Task<bool> AttemptCopyUnstrippedUnity(string libsPath)
        {
            WebClient client = new();
            // Only download the index once
            if (_libUnityIndex == null)
            {
                _logger.Debug("Downloading libunity index for the first time . . .");
                JsonSerializer serializer = new();

                string data = await client.DownloadStringTaskAsync("https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/index.json");
                using StringReader stringReader = new(data);
                using JsonReader reader = new JsonTextReader(stringReader);

                _libUnityIndex = serializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(reader);
            }

            _logger.Information("Checking index for unstripped libunity.so . . .");
            Debug.Assert(InstalledApp != null);
            Debug.Assert(_libUnityIndex != null);

            // The versions are separated per version of each app, since apps may change their unity version
            _libUnityIndex.TryGetValue(_config.AppId, out var availableVersions);

            if (availableVersions == null)
            {
                _logger.Warning("Unstripped libunity not found for this app");
                return false;
            }

            availableVersions.TryGetValue(InstalledApp.Version, out string? correctVersion);

            if (correctVersion == null)
            {
                _logger.Warning($"Unstripped libunity found for other versions of this app, but not {InstalledApp.Version}");
                return false;
            }

            _logger.Information("Unstripped libunity found. Downloading . . .");
            await client.DownloadFileTaskAsync($"https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/versions/{correctVersion}.so", Path.Combine(libsPath, "libunity.so"));
            return true;
        }

        /// <summary>
        /// Adds permissions for modding, as specified in the config.
        /// This does not parse the XML, simply uses string manipulation because it was easier and it's a strange form of XML that nothing will read.
        /// </summary>
        /// <param name="manifest">The manifest to add the permissions to</param>
        /// <returns>The modified manifest</returns>
        private string PatchManifest(string manifest)
        {

            // This is futureproofing as in Android 11 WRITE and READ is replaced by MANAGE.
            // Otherwise storage access would be limited to scoped-storage like an app-specific directory or a public shared directory.
            // Can be removed until any device updates to Android 11, however it's best to keep for compatability.

            const string ReadPermissions = "<uses-permission android:name=\"android.permission.READ_EXTERNAL_STORAGE\"/>";
            const string WritePermissions = "<uses-permission android:name=\"android.permission.WRITE_EXTERNAL_STORAGE\"/>";
            const string ManagePermissions = "<uses-permission android:name=\"android.permission.MANAGE_EXTERNAL_STORAGE\"/>";

            // Required for Apps that target Android 10 API Level 29 or higher as that uses scoped storage see: https://developer.android.com/training/data-storage/use-cases#opt-out-scoped-storage
            const string LegacyExternalStorage = "android:requestLegacyExternalStorage = \"true\"";
            const string ApplicationDebuggable = "android:debuggable = \"true\"";

            // Hand tracking features and permissions
            const string OvrFeatureHandTracking = "<uses-feature android:name=\"oculus.software.handtracking\" android:required=\"false\"/>";
            const string OvrPermissionHandTracking = "<uses-permission android:name=\"oculus.permission.handtracking\"/>\n<uses-permission android:name=\"com.oculus.permission.HAND_TRACKING\"/>";

            const string ApplicationStr = "<application";

            int newLineIndex = manifest.IndexOf('\n');
            string newManifest = manifest.Substring(0, newLineIndex) + "\n";

            if (_config.PatchingPermissions.ExternalFiles)
            {
                _logger.Information("Adding storage permissions . . .");
                if (!manifest.Contains(ReadPermissions))
                {
                    newManifest += "    " + ReadPermissions + "\n";
                }

                if (!manifest.Contains(WritePermissions))
                {
                    newManifest += "    " + WritePermissions + "\n";
                }

                if (!manifest.Contains(ManagePermissions))
                {
                    newManifest += "    " + ManagePermissions + "\n";
                }

                if (!manifest.Contains(LegacyExternalStorage))
                {
                    _logger.Debug("Adding legacy storage support . . .");
                    manifest = manifest.Replace(ApplicationStr, $"{ApplicationStr} {LegacyExternalStorage}");
                }
            }
            else
            {
                _logger.Warning("No external file permissions granted - many mods will not work!");
            }

            if (_config.PatchingPermissions.Debuggable)
            {
                if (!manifest.Contains(ApplicationDebuggable))
                {
                    _logger.Information("Adding debuggable flag . . .");
                    manifest = manifest.Replace(ApplicationStr, $"{ApplicationStr} {ApplicationDebuggable}");
                }
            }

            if(_config.PatchingPermissions.HandTracking)
            {
                _logger.Information("Adding hand tracking . . .");
                if (!manifest.Contains(OvrFeatureHandTracking))
                {
                    newManifest += "    " + OvrFeatureHandTracking + "\n";
                }

                if (!manifest.Contains(OvrPermissionHandTracking))
                {
                    newManifest += "    " + OvrPermissionHandTracking + "\n";
                }
            }

            newManifest += manifest[(newLineIndex + 1)..]; // Add the rest of the original manifest
            return newManifest;
        }

        public async Task PatchApp()
        {
            _logger.Information("Decompiling APK . . .");
            PatchingStage = PatchingStage.Decompiling;

            Directory.CreateDirectory(_decompPath);
            await _apkTools.DecompileApk(_currentApkPath, _decompPath);
            _logger.Information("Decompiled APK");

            _logger.Information("Copying library files to patch APK . . .");
            PatchingStage = PatchingStage.Patching;
            if (InstalledApp == null)
            {
                throw new NullReferenceException("Cannot patch before installed app has been checked");
            }
            string libsPath = Path.Combine(_decompPath, InstalledApp.Is64Bit ? "lib/arm64-v8a" : "lib/armeabi-v7a");

            if (!InstalledApp.Is64Bit)
            {
                _logger.Warning("App is 32 bit!");
                if (!await _prompter.Prompt32Bit()) // Prompt the user to ask if they would like to continue, even though BS-hook doesn't work on 32 bit apps
                {
                    return;
                }
            }

            if (!await AttemptCopyUnstrippedUnity(libsPath))
            {
                if (!await _prompter.PromptUnstrippedUnityUnavailable()) // Prompt the user to ask if they would like to continue, since missing libunity is likely to break some mods
                {
                    return;
                }
            }

            // Replace libmain.so to load the modloader, then add libmodloader.so, which actually does the mod loading.
            _logger.Information("Copying libmain.so and libmodloader.so . . .");
            if (InstalledApp.Is64Bit)
            {
                File.Copy(await _filesDownloader.GetFileLocation(ExternalFileType.Main64), Path.Combine(libsPath, "libmain.so"), true);
                File.Copy(await _filesDownloader.GetFileLocation(ExternalFileType.Modloader64), Path.Combine(libsPath, "libmodloader.so"));
            }
            else
            {
                _logger.Warning("Using 32 bit versions!");
                File.Copy(await _filesDownloader.GetFileLocation(ExternalFileType.Main32), Path.Combine(libsPath, "libmain.so"), true);
                File.Copy(await _filesDownloader.GetFileLocation(ExternalFileType.Modloader32), Path.Combine(libsPath, "libmodloader.so"));
            }

            // Add permissions to the manifest
            _logger.Information("Patching manifest . . .");
            string manifestPath = Path.Combine(_decompPath, "AndroidManifest.xml");
            string modifiedManifest = PatchManifest(File.ReadAllText(manifestPath));
            File.WriteAllText(manifestPath, modifiedManifest);

            // Pause patching before compiling the APK in order to give a developer the chance to modify it.
            if(_config.PauseBeforeCompile && !await _prompter.PromptPauseBeforeCompile())
            {
                return;
            }

            _logger.Information("Recompiling APK . . .");
            PatchingStage = PatchingStage.Recompiling;

            string patchedPath = Path.Combine(_specialFolders.PatchingFolder, "patched.apk");
            await _apkTools.CompileApk(_decompPath, patchedPath);

            try
            {
                await Task.Run(() =>
                {
                    Directory.Delete(_decompPath, true); // The decompiled APK takes up a LOT of space (800-900 MB for Beat Saber!), so we clear this now
                });
            } 
            catch (IOException) // Sometimes a developer might be using the APK, so avoid failing the whole patching process
            {
                _logger.Warning("Failed to delete decompiled APK");
            }

            _logger.Information("Adding tag . . .");
            // The disk IO while opening the APK as a zip archive causes a UI freeze, so we run it on another thread
            // We cannot just create this tag before compiling - apktool will remove it as it isn't a normal part of the APK
            await Task.Run(() =>
            {
                using ZipArchive apkArchive = ZipFile.Open(patchedPath, ZipArchiveMode.Update);
                apkArchive.CreateEntry("modded");
            });

            _logger.Information("Signing APK (this might take a while) . . .");
            PatchingStage = PatchingStage.Signing;

            await _apkTools.SignApk(patchedPath);
            try
            {
                File.Delete(patchedPath); // The patched APK takes up space, and we don't need it now
            }
            catch (IOException) // Sometimes a developer might be using the APK, so avoid failing the whole patching process
            {
                _logger.Warning("Failed to delete patched APK");
            }

            _logger.Information("Uninstalling the default APK . . .");
            PatchingStage = PatchingStage.UninstallingOriginal;

            await _debugBridge.UninstallApp(_config.AppId);

            _logger.Information("Installing the modded APK . . .");
            PatchingStage = PatchingStage.InstallingModded;

            string signedPath = Path.Combine(_specialFolders.PatchingFolder, "patched-aligned-debugSigned.apk");
            await _debugBridge.InstallApp(signedPath);
            File.Delete(signedPath);

            _logger.Information("Patching complete!");
            InstalledApp.IsModded = true;
            PatchingCompleted?.Invoke(this, new EventArgs());
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
