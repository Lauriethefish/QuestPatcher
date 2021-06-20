using Newtonsoft.Json;
using QuestPatcher.Core.Models;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using QuestPatcher.Axml;

namespace QuestPatcher.Core.Patching
{
    /// <summary>
    /// Handles checking if the selected app is modded, alongside patching it if not
    /// </summary>
    public class PatchingManager : INotifyPropertyChanged
    {
        private const string ManifestPath = "AndroidManifest.xml";
        private static readonly Uri AndroidNamespaceUri = new("http://schemas.android.com/apk/res/android");
        private const int NameAttributeResourceId = 16842755;
        private const int RequiredAttributeResourceId = 16843406;
        private const int DebuggableAttributeResourceId = 16842767;
        private const int LegacyStorageAttributeResourceId = 16844291;

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

        private readonly string _storedApkPath;
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

            _storedApkPath = Path.Combine(specialFolders.PatchingFolder, "currentlyInstalled.apk");
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
            await _debugBridge.DownloadApk(_config.AppId, _storedApkPath);

            bool isModded = false;
            bool is64Bit = false;
            bool is32Bit = false;
            // Unfortunately, zip files do not support async, so we Task.Run this operation to avoid blocking
            _logger.Information("Checking APK modding status . . .");
            await Task.Run(() =>
            {
                using ZipArchive apkArchive = ZipFile.OpenRead(_storedApkPath);

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

        private async Task<bool> AttemptCopyUnstrippedUnity(string libsPath, ZipArchive apkArchive)
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
            string tempDownloadPath = _specialFolders.GetTempFilePath();
            try
            {
                await _filesDownloader.DownloadUrl(
                    $"https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/versions/{correctVersion}.so",
                    tempDownloadPath, "libunity.so");

                await apkArchive.AddFileAsync(tempDownloadPath, Path.Combine(libsPath, "libunity.so"), true);
            }
            finally
            {
                if (File.Exists(tempDownloadPath)) { File.Delete(tempDownloadPath); }
            }

            return true;
        }

        /// <summary>
        /// Patches the manifest of the APK to add the permissions/features specified in <see cref="PatchingPermissions"/> in the <see cref="Config"/>.
        /// </summary>
        /// <param name="apkArchive">The archive of the APK to patch</param>
        /// <exception cref="PatchingException">If the given archive does not contain an <code>AndroidManifest.xml</code> file</exception>
        private async Task PatchManifest(ZipArchive apkArchive)
        {
            ZipArchiveEntry? manifestEntry = apkArchive.GetEntry(ManifestPath);
            if (manifestEntry == null)
            {
                throw new PatchingException($"APK missing {ManifestPath} to patch");
            }

            // The AMXL loader requires a seekable stream
            MemoryStream ms = new();
            await Task.Run(() =>
            {
                using Stream stream = manifestEntry.Open();
                stream.CopyTo(ms);
            });

            ms.Position = 0;
            _logger.Information("Loading manifest as AXML . . .");
            AxmlElement manifest = AxmlLoader.LoadDocument(ms);

            // First we add permissions and features to the APK for modding
            List<string> addingPermissions = new();
            List<string> addingFeatures = new();
            PatchingPermissions permissions = _config.PatchingPermissions;
            if (permissions.ExternalFiles)
            {
                // Technically, we only need READ_EXTERNAL_STORAGE and WRITE_EXTERNAL_STORAGE, but we also add MANAGE_EXTERNAL_STORAGE as this is what Android 11 needs instead
                addingPermissions.AddRange(new[] {
                    "android.permission.READ_EXTERNAL_STORAGE", 
                    "android.permission.WRITE_EXTERNAL_STORAGE",
                    "android.permission.MANAGE_EXTERNAL_STORAGE"
                });
            }

            if (permissions.HandTracking)
            {
                // For some reason these are separate permissions, but we need both of them
                addingPermissions.AddRange(new[]
                {
                    "oculus.permission.handtracking",
                    "com.oculus.permission.HAND_TRACKING"
                });
                // Tell Android (and thus Oculus home) that this app supports hand tracking and we can launch the app with it
                addingFeatures.Add("oculus.software.handtracking");
            }

            // Find which features and permissions already exist to avoid adding existing ones
            ISet<string> existingPermissions = GetExistingChildren(manifest, "uses-permission");
            ISet<string> existingFeatures = GetExistingChildren(manifest, "uses-feature");

            foreach (string permission in addingPermissions)
            {
                if(existingPermissions.Contains(permission)) { continue; } // Do not add existing permissions

                _logger.Information($"Adding permission {permission}");
                AxmlElement permElement = new(0, "uses-permission", null);
                AddNameAttribute(permElement, permission);
                manifest.Children.Add(permElement);
            }

            foreach (string feature in addingFeatures)
            {
                if(existingFeatures.Contains(feature)) { continue; } // Do not add existing features

                _logger.Information($"Adding feature {feature}");
                AxmlElement featureElement = new(0, "uses-feature", null);
                AddNameAttribute(featureElement, feature);
                
                // TODO: User may want the feature to be required instead of suggested
                featureElement.Attributes.Add(new AxmlAttribute("required", AndroidNamespaceUri, RequiredAttributeResourceId, false));
                manifest.Children.Add(featureElement);
            }

            // Now we need to add the legacyStorageSupport and debuggable flags
            AxmlElement appElement = manifest.Children.Single(element => element.Name == "application");
            if (permissions.Debuggable && !appElement.Attributes.Any(attribute => attribute.Name == "debuggable"))
            {
                _logger.Information("Adding debuggable flag . . .");
                appElement.Attributes.Add(new AxmlAttribute("debuggable", AndroidNamespaceUri, DebuggableAttributeResourceId, true));
            }

            if (permissions.ExternalFiles && !appElement.Attributes.Any(attribute => attribute.Name == "requestLegacyExternalStorage"))
            {
                _logger.Information("Adding legacy external storage flag . . .");
                appElement.Attributes.Add(new AxmlAttribute("requestLegacyExternalStorage", AndroidNamespaceUri, LegacyStorageAttributeResourceId, true));
            }
            
            // Save the manifest using our AXML library
            // TODO: The AXML library is missing some features such as styles.
            _logger.Information("Saving manifest as AXML . . .");
            manifestEntry.Delete(); // Remove old manifest
            
            // No async ZipArchive implementation, so Task.Run is used
            await Task.Run(() =>
            {
                manifestEntry = apkArchive.CreateEntry(ManifestPath);
                using Stream saveStream = manifestEntry.Open();
                AxmlSaver.SaveDocument(saveStream, manifest);
            });
        }

        /// <summary>
        /// Scans the attributes of the children of the given element for their "name" attribute.
        /// </summary>
        /// <param name="manifest"></param>
        /// <param name="childNames"></param>
        /// <returns>A set of the values of the "name" attributes of children (does not error on children without this attribute)</returns>
        private ISet<string> GetExistingChildren(AxmlElement manifest, string childNames)
        {
            HashSet<string> result = new();
            
            foreach (AxmlElement element in manifest.Children)
            {
                if(element.Name != childNames) { continue; }

                List<AxmlAttribute> nameAttributes = element.Attributes.Where(attribute => attribute.Namespace == AndroidNamespaceUri && attribute.Name == "name").ToList();
                // Only add children with the name attribute
                if (nameAttributes.Count > 0) { result.Add((string) nameAttributes[0].Value); }
            }

            return result;
        }

        /// <summary>
        /// Adds a "name" attribute to the given element, with the given value.
        /// </summary>
        /// <param name="element">The element to add the attribute to</param>
        /// <param name="name">The value to put in the name attribute</param>
        private void AddNameAttribute(AxmlElement element, string name)
        {
            element.Attributes.Add(new AxmlAttribute("name", AndroidNamespaceUri, NameAttributeResourceId, name));
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
            string patchedApkPath = Path.Combine(_specialFolders.PatchingFolder, "patched.apk");

            // There is no async file copy method, so we Task.Run it (we could make our own with streams, that's another option)
            await Task.Run(() => { File.Copy(_storedApkPath, patchedApkPath, true); });
            
            _logger.Information("Copying files to patch APK . . .");

            PatchingStage = PatchingStage.Patching;
            ZipArchive apkArchive = ZipFile.Open(patchedApkPath, ZipArchiveMode.Update);
            try
            {
                string libsPath = InstalledApp.Is64Bit ? "lib/arm64-v8a" : "lib/armeabi-v7a";

                if (!InstalledApp.Is64Bit)
                {
                    _logger.Warning("App is 32 bit!");
                    if (
                        !await _prompter
                            .Prompt32Bit()) // Prompt the user to ask if they would like to continue, even though BS-hook doesn't work on 32 bit apps
                    {
                        return;
                    }
                }
                
                if (!await AttemptCopyUnstrippedUnity(libsPath, apkArchive))
                {
                    if (!await _prompter
                        .PromptUnstrippedUnityUnavailable()) // Prompt the user to ask if they would like to continue, since missing libunity is likely to break some mods
                    {
                        return;
                    }
                }

                // Replace libmain.so to load the modloader, then add libmodloader.so, which actually does the mod loading.
                _logger.Information("Copying libmain.so and libmodloader.so . . .");
                if (InstalledApp.Is64Bit)
                {
                    await apkArchive.AddFileAsync(await _filesDownloader.GetFileLocation(ExternalFileType.Main64), Path.Combine(libsPath, "libmain.so"), true);
                    await apkArchive.AddFileAsync(await _filesDownloader.GetFileLocation(ExternalFileType.Modloader64), Path.Combine(libsPath, "libmodloader.so"));
                }
                else
                {
                    _logger.Warning("Using 32 bit versions!");
                    await apkArchive.AddFileAsync(await _filesDownloader.GetFileLocation(ExternalFileType.Main32), Path.Combine(libsPath, "libmain.so"), true);
                    await apkArchive.AddFileAsync(await _filesDownloader.GetFileLocation(ExternalFileType.Modloader32), Path.Combine(libsPath, "libmodloader.so"));
                }
                

                // Add permissions to the manifest
                _logger.Information("Patching manifest . . .");
                await PatchManifest(apkArchive);

                _logger.Information("Adding tag . . .");
                // The disk IO while opening the APK as a zip archive causes a UI freeze, so we run it on another thread
                // We cannot just create this tag before compiling - apktool will remove it as it isn't a normal part of the APK
                apkArchive.CreateEntry("modded");

                _logger.Information("Closing APK archive . . .");
            }
            finally
            {
                await Task.Run(() => { apkArchive.Dispose(); });
            }

            // Pause patching before compiling the APK in order to give a developer the chance to modify it.
            if(_config.PauseBeforeCompile && !await _prompter.PromptPauseBeforeCompile())
            {
                return;
            }

            _logger.Information("Signing APK (this might take a while) . . .");
            PatchingStage = PatchingStage.Signing;

            await _apkTools.SignApk(patchedApkPath);
            try
            {
                File.Delete(patchedApkPath); // The patched APK takes up space, and we don't need it now
            }
            catch (IOException) // Sometimes a developer might be using the APK, so avoid failing the whole patching process
            {
                _logger.Warning("Failed to delete patched APK");
            }

            string signedPath = Path.Combine(_specialFolders.PatchingFolder, "patched-aligned-debugSigned.apk");
            // Avoid uninstalling the APK then failing during installation. Fail here instead to preserve the vanilla game
            if (!File.Exists(signedPath))
            {
                throw new PatchingException($"Signed APK ({signedPath}) not found! Signing must have failed");
            }
            
            _logger.Information("Uninstalling the default APK . . .");
            PatchingStage = PatchingStage.UninstallingOriginal;

            await _debugBridge.UninstallApp(_config.AppId);

            _logger.Information("Installing the modded APK . . .");
            PatchingStage = PatchingStage.InstallingModded;

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
