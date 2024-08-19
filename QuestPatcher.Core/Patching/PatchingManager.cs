using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using QuestPatcher.Axml;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Models;
using QuestPatcher.Zip;
using Serilog;

namespace QuestPatcher.Core.Patching
{
    /// <summary>
    /// Handles patching an app with the modloader.
    /// </summary>
    public class PatchingManager : INotifyPropertyChanged
    {
        private static readonly Uri AndroidNamespaceUri = new("http://schemas.android.com/apk/res/android");
        private static readonly string Scotland2LocationTemplate = "/sdcard/ModData/{0}/Modloader/libsl2.so";

        // Attribute resource IDs, used during manifest patching
        private const int NameAttributeResourceId = 16842755;
        private const int RequiredAttributeResourceId = 16843406;
        private const int DebuggableAttributeResourceId = 16842767;
        private const int LegacyStorageAttributeResourceId = 16844291;
        private const int ValueAttributeResourceId = 16842788;
        private const int AuthoritiesAttributeResourceId = 16842776;

        /// <summary>
        /// Compression level to use when adding files to the APK during patching.
        /// * Most asset files added should use no compression, as most already use a compressed format.
        /// </summary>
        private const CompressionLevel PatchingCompression = CompressionLevel.Optimal;

        public PatchingStage PatchingStage { get => _patchingStage; private set { if (_patchingStage != value) { _patchingStage = value; NotifyPropertyChanged(); } } }
        private PatchingStage _patchingStage = PatchingStage.NotStarted;

        public event PropertyChangedEventHandler? PropertyChanged;

        private ApkInfo? InstalledApp => _installManager.InstalledApp;

        private readonly Config _config;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly IUserPrompter _prompter;
        private readonly ModManager _modManager;
        private readonly InstallManager _installManager;

        private readonly string _patchedApkPath;
        private Dictionary<string, Dictionary<string, string>>? _libUnityIndex;

        public PatchingManager(Config config, AndroidDebugBridge debugBridge, SpecialFolders specialFolders, ExternalFilesDownloader filesDownloader, IUserPrompter prompter, ModManager modManager, InstallManager installManager)
        {
            _config = config;
            _debugBridge = debugBridge;
            _filesDownloader = filesDownloader;
            _prompter = prompter;
            _modManager = modManager;

            _patchedApkPath = Path.Combine(specialFolders.PatchingFolder, "patched.apk");
            _installManager = installManager;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task<TempFile?> GetUnstrippedUnityPath()
        {
            var client = new HttpClient();
            // Only download the index once
            if (_libUnityIndex == null)
            {
                Log.Debug("Downloading libunity index for the first time . . .");

                try
                {
                    _libUnityIndex = await client.GetFromJsonAsync<Dictionary<string, Dictionary<string, string>>>("https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/index.json");
                }
                catch (HttpRequestException ex)
                {
                    Log.Warning(ex, "Failed to download libunity index");
                    return null;
                }

            }

            Log.Information("Checking index for unstripped libunity.so . . .");
            Debug.Assert(InstalledApp != null);
            Debug.Assert(_libUnityIndex != null);

            // The versions are separated per version of each app, since apps may change their unity version
            _libUnityIndex.TryGetValue(_config.AppId, out var availableVersions);

            if (availableVersions == null)
            {
                Log.Warning("Unstripped libunity not found for this app");
                return null;
            }

            availableVersions.TryGetValue(InstalledApp.Version, out string? correctVersion);

            if (correctVersion == null)
            {
                Log.Warning("Unstripped libunity found for other versions of this app, but not {InstalledVersion}", InstalledApp.Version);
                return null;
            }

            Log.Information("Unstripped libunity found. Downloading . . .");

            var tempDownloadPath = new TempFile();
            try
            {
                await _filesDownloader.DownloadUri(
                    $"https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/versions/{correctVersion}.so",
                    tempDownloadPath.Path, "libunity.so");

                return tempDownloadPath;
            }
            catch
            {
                tempDownloadPath.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Patches the manifest of the APK to add the permissions/features specified in <see cref="PatchingOptions"/> in the <see cref="Config"/>.
        /// </summary>
        /// <param name="apk">The apk with the manifest to patch</param>
        /// <exception cref="PatchingException">If the given archive does not contain an <code>AndroidManifest.xml</code> file</exception>
        private async Task PatchManifest(ApkZip apk)
        {
            if (!apk.Entries.Contains(InstallManager.ManifestPath))
            {
                throw new PatchingException($"APK missing {InstallManager.ManifestPath} to patch");
            }

            // The AMXL loader requires a seekable stream
            using var ms = new MemoryStream();
            using (var stream = await apk.OpenReaderAsync(InstallManager.ManifestPath))
            {
                await stream.CopyToAsync(ms);
            }

            ms.Position = 0;
            Log.Information("Loading manifest as AXML . . .");
            var manifest = AxmlLoader.LoadDocument(ms);
            bool manifestModified = false;

            // First we add permissions and features to the APK for modding
            List<string> addingPermissions = new();
            List<string> addingFeatures = new();
            var permissions = _config.PatchingOptions;
            if (permissions.ExternalFiles)
            {
                // Technically, we only need READ_EXTERNAL_STORAGE and WRITE_EXTERNAL_STORAGE, but we also add MANAGE_EXTERNAL_STORAGE as this is what Android 11 needs instead
                addingPermissions.AddRange(new[] {
                    "android.permission.READ_EXTERNAL_STORAGE",
                    "android.permission.WRITE_EXTERNAL_STORAGE",
                    "android.permission.MANAGE_EXTERNAL_STORAGE",
                });
            }

            if (permissions.Microphone)
            {
                Log.Information("Adding microphone permission request . . .");
                addingPermissions.Add("android.permission.RECORD_AUDIO");
            }

            if (permissions.HandTrackingType != HandTrackingVersion.None)
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

            if (permissions.OpenXR)
            {
                Log.Information("Adding OpenXR permission . . .");

                addingPermissions.AddRange(new[] {
                    "org.khronos.openxr.permission.OPENXR",
                    "org.khronos.openxr.permission.OPENXR_SYSTEM",
                });

                AxmlElement providerElement = new("provider")
                {
                    Attributes = { new("authorities", AndroidNamespaceUri, AuthoritiesAttributeResourceId, "org.khronos.openxr.runtime_broker;org.khronos.openxr.system_runtime_broker") },
                };
                AxmlElement runtimeIntent = new("intent")
                {
                    Children = {
                        new("action") {
                            Attributes = {new("name", AndroidNamespaceUri, NameAttributeResourceId, "org.khronos.openxr.OpenXRRuntimeService")},
                        },
                    },
                };
                AxmlElement layerIntent = new("intent")
                {
                    Children = {
                        new("action") {
                            Attributes = {new("name", AndroidNamespaceUri, NameAttributeResourceId, "org.khronos.openxr.OpenXRApiLayerService")},
                        },
                    },
                };
                manifest.Children.Add(new("queries")
                {
                    Children = {
                        providerElement,
                        runtimeIntent,
                        layerIntent,
                    },
                });
            }

            if (permissions.Passthrough)
            {
                addingFeatures.Add("com.oculus.feature.PASSTHROUGH");
            }

            if (permissions.BodyTracking)
            {
                addingFeatures.Add("com.oculus.software.body_tracking");
                addingPermissions.Add("com.oculus.permission.BODY_TRACKING");
            }

            // Find which features and permissions already exist to avoid adding existing ones
            var existingPermissions = GetExistingChildren(manifest, "uses-permission");
            var existingFeatures = GetExistingChildren(manifest, "uses-feature");

            foreach (string permission in addingPermissions)
            {
                if (existingPermissions.Contains(permission)) { continue; } // Do not add existing permissions

                Log.Information("Adding permission {Permission}", permission);
                manifestModified = true;
                AxmlElement permElement = new("uses-permission");
                AddNameAttribute(permElement, permission);
                manifest.Children.Add(permElement);
            }

            foreach (string feature in addingFeatures)
            {
                if (existingFeatures.Contains(feature)) { continue; } // Do not add existing features

                Log.Information("Adding feature {Feature}", feature);
                manifestModified = true;
                AxmlElement featureElement = new("uses-feature");
                AddNameAttribute(featureElement, feature);

                // TODO: User may want the feature to be suggested instead of required
                featureElement.Attributes.Add(new AxmlAttribute("required", AndroidNamespaceUri, RequiredAttributeResourceId, false));
                manifest.Children.Add(featureElement);
            }

            // Now we need to add the legacyStorageSupport and debuggable flags
            var appElement = manifest.Children.Single(element => element.Name == "application");
            if (permissions.Debuggable && !appElement.Attributes.Any(attribute => attribute.Name == "debuggable"))
            {
                Log.Information("Adding debuggable flag . . .");
                manifestModified = true;
                appElement.Attributes.Add(new AxmlAttribute("debuggable", AndroidNamespaceUri, DebuggableAttributeResourceId, true));
            }

            if (permissions.ExternalFiles && !appElement.Attributes.Any(attribute => attribute.Name == "requestLegacyExternalStorage"))
            {
                Log.Information("Adding legacy external storage flag . . .");
                manifestModified = true;
                appElement.Attributes.Add(new AxmlAttribute("requestLegacyExternalStorage", AndroidNamespaceUri, LegacyStorageAttributeResourceId, true));
            }

            if (permissions.MrcWorkaround)
            {
                const string MrcLibName = "libOVRMrcLib.oculus.so";

                if(appElement.Children.Any(child => child.Name == "uses-native-library" &&
                    child.Attributes.Any(attr => attr.Name == "name" && (string) attr.Value == MrcLibName))) {
                    Log.Information("Not adding MRC workaround as it already exists");
                }
                else
                {
                    Log.Information("Adding MRC workaround");
                    AxmlElement nativeLibElement = new("uses-native-library");
                    AddNameAttribute(nativeLibElement, MrcLibName);
                    nativeLibElement.Attributes.Add(new AxmlAttribute("required", AndroidNamespaceUri, RequiredAttributeResourceId, false));

                    appElement.Children.Add(nativeLibElement);
                    manifestModified = true;
                }
            }

            // TODO: Modify an existing hand tracking element if one exists
            switch (permissions.HandTrackingType)
            {
                case HandTrackingVersion.None:
                case HandTrackingVersion.V1:
                    Log.Debug("No need for any extra hand tracking metadata (v1/no tracking)");
                    break;
                case HandTrackingVersion.V1HighFrequency:
                    Log.Information("Adding high-frequency V1 hand-tracking. . .");
                    AxmlElement frequencyElement = new("meta-data");
                    AddNameAttribute(frequencyElement, "com.oculus.handtracking.frequency");
                    frequencyElement.Attributes.Add(new AxmlAttribute("value", AndroidNamespaceUri, ValueAttributeResourceId, "HIGH"));
                    appElement.Children.Add(frequencyElement);
                    manifestModified = true;
                    break;
                case HandTrackingVersion.V2:
                    Log.Information("Adding V2 hand-tracking. . .");
                    frequencyElement = new("meta-data");
                    AddNameAttribute(frequencyElement, "com.oculus.handtracking.version");
                    frequencyElement.Attributes.Add(new AxmlAttribute("value", AndroidNamespaceUri, ValueAttributeResourceId, "V2.0"));
                    appElement.Children.Add(frequencyElement);
                    manifestModified = true;
                    break;
            }

            // Save the manifest using the AXML library
            if (manifestModified)
            {
                Log.Information("Saving manifest as AXML . . .");

                ms.SetLength(0);
                ms.Position = 0;
                AxmlSaver.SaveDocument(ms, manifest);
                ms.Position = 0;

                await apk.AddFileAsync(InstallManager.ManifestPath, ms, PatchingCompression);
            }
            else
            {
                // Yes, we could just overwrite the existing manifest
                // BUT doing this with QuestPatcher.Zip will not remove the existing manifest's contents from the actual file.
                Log.Information("Not saving manifest - no changes made");
            }
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

            foreach (var element in manifest.Children)
            {
                if (element.Name != childNames) { continue; }

                var nameAttributes = element.Attributes.Where(attribute => attribute.Namespace == AndroidNamespaceUri && attribute.Name == "name").ToList();
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
        /// Modifies the given APK to support normal android devices, not just VR headsets.
        /// </summary>
        /// <param name="apk">APK to modify</param>
        /// <param name="ovrPlatformSdkPath">Path to the ovrPlatformSdk ZIP file to fetch a necessary library from</param>
        /// <exception cref="PatchingException">If the APK file was missing essential files, or otherwise not a valid unity game for adding flatscreen support.</exception>
        private async Task AddFlatscreenSupport(ApkZip apk, string ovrPlatformSdkPath)
        {
            const string bootCfgPath = "assets/bin/Data/boot.config";
            const string globalGameManagersPath = "assets/bin/Data/globalgamemanagers";
            const string ovrPlatformLoaderPath = "lib/arm64-v8a/libovrplatformloader.so";

            if (!apk.ContainsFile(bootCfgPath))
            {
                throw new PatchingException("boot.config must exist on a Beat Saber installation");
            }


            string bootCfgContents;
            using (var bootCfgStream = await apk.OpenReaderAsync(bootCfgPath))
            using (var reader = new StreamReader(bootCfgStream))
            {
                bootCfgContents = (await reader.ReadToEndAsync())
                    .Replace("vr-enabled=1", "vr-enabled=0")
                    .Replace("vr-device-list=Oculus", "vr-device-list=");
            }

            using (var newBootCfg = new MemoryStream())
            using (var newBootCfgWriter = new StreamWriter(newBootCfg))
            {
                newBootCfgWriter.Write(bootCfgContents);
                newBootCfgWriter.Flush();

                newBootCfg.Position = 0;
                await apk.AddFileAsync(bootCfgPath, newBootCfg, PatchingCompression);

            }

            if (!apk.ContainsFile(globalGameManagersPath))
            {
                throw new PatchingException("globalgamemanagers must exist on a Beat Saber installation");
            }

            var am = new AssetsManager();

            using var replacementContents = new MemoryStream();
            using var writer = new AssetsFileWriter(replacementContents);
            using (var gameManagersStream = await apk.OpenReaderAsync(globalGameManagersPath))
            {
                using var gameManagersMs = new MemoryStream();
                await gameManagersStream.CopyToAsync(gameManagersMs);
                gameManagersMs.Position = 0;

                var inst = am.LoadAssetsFile(gameManagersMs, "globalgamemanagers", false);

                var inf = inst.table.GetAssetsOfType((int) AssetClassID.BuildSettings).Single();

                var assembly = Assembly.GetExecutingAssembly();
                using var classPkgStream = assembly.GetManifestResourceStream("QuestPatcher.Core.Resources.classdata.tpk");
                if (classPkgStream == null)
                {
                    throw new PatchingException("Could not find classdata.tpk in resources");
                }
                am.LoadClassPackage(classPkgStream);
                am.LoadClassDatabaseFromPackage(inst.file.typeTree.unityVersion);

                var type = am.GetTypeInstance(inst, inf);
                var baseField = type.GetBaseField();

                baseField.Get("enabledVRDevices").GetChildrenList()[0].SetChildrenList(Array.Empty<AssetTypeValueField>());
                byte[] newBytes = baseField.WriteToByteArray();

                var replacer = new AssetsReplacerFromMemory(0, inf.index, (int) inf.curFileType, 0xffff, newBytes);

                inst.file.Write(writer, 0, new List<AssetsReplacer> { replacer });
                am.UnloadAllAssetsFiles();
            }
            writer.Flush();
            replacementContents.Position = 0;
            await apk.AddFileAsync(globalGameManagersPath, replacementContents, PatchingCompression);

            using var sdkArchive = ZipFile.Open(ovrPlatformSdkPath, ZipArchiveMode.Update);
            var downgradedLoaderEntry = sdkArchive.GetEntry("Android/libs/arm64-v8a/libovrplatformloader.so")
                                        ?? throw new PatchingException("No libovrplatformloader.so found in downloaded OvrPlatformSdk");
            using var downloadedLoaderStream = downgradedLoaderEntry.Open();

            await apk.AddFileAsync(ovrPlatformLoaderPath, downloadedLoaderStream, PatchingCompression);
        }

        /// <summary>
        /// Makes the modifications to the APK to support mods.
        /// </summary>
        /// <param name="mainPath">Path of the libmain file to replace</param>
        /// <param name="modloaderPath">Path of the libmodloader to replace, or null if no modloader needs to be stored within the APK</param>
        /// <param name="unityPath">Optionally, a path to a replacement libunity.so</param>
        /// <param name="libsDirectory">The directory where the SO files are stored in the APK</param>
        /// <param name="ovrPlatformSdkPath">Path to the OVR platform SDK ZIP, used for patching with flatscreen support. Must be non-null if flatscreen support is enabled.</param>
        /// <param name="apk">The APK to patch</param>
        private async Task ModifyApk(string mainPath, string? modloaderPath, string? unityPath, string? ovrPlatformSdkPath, string libsDirectory, ApkZip apk)
        {
            Log.Information("Copying libmain.so and libmodloader.so . . .");
            await AddFileToApk(mainPath, Path.Combine(libsDirectory, "libmain.so"), apk);
            if (modloaderPath == null)
            {
                if (apk.RemoveFile(Path.Combine(libsDirectory, "libmodloader.so")))
                {
                    Log.Information("Removed QuestLoader from the APK");
                }
            }
            else
            {
                await AddFileToApk(modloaderPath, Path.Combine(libsDirectory, "libmodloader.so"), apk);
            }

            if (unityPath != null)
            {
                Log.Information("Adding unstripped libunity.so . . .");
                await AddFileToApk(unityPath, Path.Combine(libsDirectory, "libunity.so"), apk);
            }

            if (_config.PatchingOptions.FlatScreenSupport)
            {
                Log.Information("Adding flatscreen support . . .");
                await AddFlatscreenSupport(apk, ovrPlatformSdkPath!);
            }

            if (_config.PatchingOptions.CustomSplashPath != null)
            {
                Log.Information("Checking if Splash screen file exists");
                if (File.Exists(_config.PatchingOptions.CustomSplashPath))
                {
                    apk.RemoveFile("assets/vr_splash.png");
                    await AddFileToApk(_config.PatchingOptions.CustomSplashPath, "assets/vr_splash.png", apk, true);
                    Log.Information("Replaced Splash with custom Image");
                }
                else
                {
                    Log.Warning("Could not add custom splash screen: file did not exist.");
                }
            }

            Log.Information("Patching manifest . . .");
            await PatchManifest(apk);

            Log.Information("Adding tag");
            using var tagStream = new MemoryStream();

            string modloaderName = _config.PatchingOptions.ModLoader == ModLoader.QuestLoader ? "QuestLoader" : "Scotland2";
            var tag = new ModdedTag("QuestPatcher", VersionUtil.QuestPatcherVersion.ToString(), modloaderName, null);
            JsonSerializer.Serialize(tagStream, tag, InstallManager.TagSerializerOptions);
            tagStream.Position = 0;

            await apk.AddFileAsync(InstallManager.JsonTagName, tagStream, PatchingCompression);
        }

        /// <summary>
        /// Copies the file with the given path into the APK.
        /// </summary>
        /// <param name="filePath">The path to the file to copy into the APK</param>
        /// <param name="apkFilePath">The name of the file in the APK to create</param>
        /// <param name="apk">The apk to copy the file into</param>
        /// <param name="useStore">If enabled, compression is disabled and the STORE compression method is used.</param>
        /// <exception cref="PatchingException">If the file already exists in the APK, if configured to throw.</exception>
        private async Task AddFileToApk(string filePath, string apkFilePath, ApkZip apk, bool useStore = false)
        {
            using var fileStream = File.OpenRead(filePath);
            if (apk.ContainsFile(apkFilePath))
            {
                uint existingCrc = apk.GetCrc32(apkFilePath);
                uint newCrc = await fileStream.CopyToCrc32Async(null);
                if (existingCrc == newCrc)
                {
                    Log.Debug("Skipping adding file {FilePath} as the CRC-32 was identical", apkFilePath);
                    return;
                }
                fileStream.Position = 0;
            }

            await apk.AddFileAsync(apkFilePath, fileStream, useStore ? null : PatchingCompression);
        }

        /// <summary>
        /// Saves the scotland2 modloader to the appropriate location for the currently installed app.
        /// </summary>
        public async Task SaveScotland2(bool replaceIfPresent)
        {
            string sl2Path = await _filesDownloader.GetFileLocation(ExternalFileType.Scotland2);
            string sl2SavePath = string.Format(Scotland2LocationTemplate, _config.AppId);

            await _debugBridge.CreateDirectory(Path.GetDirectoryName(sl2SavePath)!);
            if (!await _debugBridge.FileExists(sl2SavePath) || replaceIfPresent)
            {
                Log.Information("Uploading scotland2 to the quest");
                Log.Debug("Saving to {Scotland2Path}", sl2SavePath);

                await _debugBridge.UploadFile(sl2Path, sl2SavePath);
                await _debugBridge.Chmod(new List<string> { sl2SavePath }, "+r"); // Sometimes necessary for the file to be accessed on Quest 3
            }
        }

        /// <summary>
        /// Begins patching the currently installed APK, then uninstalls it and installs the modded copy. (must be pulled before calling this)
        /// <exception cref="FileDownloadFailedException">If downloading files necessary to mod the APK fails</exception>
        /// </summary>
        public async Task PatchApp()
        {
            if (InstalledApp == null)
            {
                throw new NullReferenceException("Cannot patch before installed app has been checked");
            }

            bool scotland2 = _config.PatchingOptions.ModLoader == ModLoader.Scotland2;

            if (!InstalledApp.Is64Bit)
            {
                if (scotland2)
                {
                    Log.Error("App is 32 bit, cannot patch with scotland2");
                    throw new PatchingException("32 bit apps are not supported by scotland2");
                }
                else
                {
                    Log.Warning("App is 32 bit!");
                    if (!await _prompter.Prompt32Bit()) // Prompt the user to ask if they would like to continue, even though BS-hook doesn't work on 32 bit apps
                    {
                        return;
                    }
                }
            }

            if (InstalledApp.ModLoader == ModLoader.Unknown)
            {
                Log.Warning("APK contains unknown modloader");
                if (!await _prompter.PromptUnknownModLoader())
                {
                    return;
                }
            }

            Log.Information("Downloading files . . .");
            PatchingStage = PatchingStage.FetchingFiles;

            // First make sure that we have all necessary files downloaded, including the libmain and libmodloader
            string libsPath = InstalledApp.Is64Bit ? "lib/arm64-v8a" : "lib/armeabi-v7a";
            string mainPath;
            string? modloaderPath;
            string? ovrPlatformSdkPath = null;

            if (scotland2)
            {
                mainPath = await _filesDownloader.GetFileLocation(ExternalFileType.LibMainLoader);
                modloaderPath = null;

                // Scotland2 itself lives outside the APK, so save it to the required location
                await SaveScotland2(true); // As we patch the APK, we should update the sl2 version, in case some old version is breaking things
            }
            else
            {

                if (InstalledApp.Is64Bit)
                {
                    mainPath = await _filesDownloader.GetFileLocation(ExternalFileType.Main64);
                    modloaderPath = await _filesDownloader.GetFileLocation(ExternalFileType.Modloader64);
                }
                else
                {
                    mainPath = await _filesDownloader.GetFileLocation(ExternalFileType.Main32);
                    modloaderPath = await _filesDownloader.GetFileLocation(ExternalFileType.Modloader32);
                }
            }

            if (_config.PatchingOptions.FlatScreenSupport)
            {
                ovrPlatformSdkPath = await _filesDownloader.GetFileLocation(ExternalFileType.OvrPlatformSdk);
            }

            using var libUnityFile = await GetUnstrippedUnityPath();
            if (libUnityFile == null)
            {
                if (!await _prompter.PromptUnstrippedUnityUnavailable())
                {
                    return;
                }
            }

            PatchingStage = PatchingStage.MovingToTemp;
            Log.Information("Copying APK to temporary location . . .");
            if (File.Exists(_patchedApkPath))
            {
                File.Delete(_patchedApkPath);
            }

            await FileUtil.CopyAsync(InstalledApp.Path, _patchedApkPath);

            // Then actually do the patching, using the APK reader, which is synchronous
            PatchingStage = PatchingStage.Patching;
            Log.Information("Copying files to patch the apk . . .");
            using var apkStream = File.Open(_patchedApkPath, FileMode.Open);
            var apk = await ApkZip.OpenAsync(apkStream);
            try
            {
                await ModifyApk(mainPath, modloaderPath, libUnityFile?.Path, ovrPlatformSdkPath, libsPath, apk);

                Log.Information("Signing APK . . .");
                PatchingStage = PatchingStage.Signing;
            }
            finally
            {
                await apk.DisposeAsync();
            }

            // Close any running instance of the app.
            await _debugBridge.ForceStop(_config.AppId);

            Log.Information("Uninstalling the default APK . . .");
            Log.Information("Backing up data directory");
            string? dataBackupPath;
            try
            {
                dataBackupPath = await _installManager.CreateDataBackup();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create data backup");
                dataBackupPath = null; // Indicate that the backup failed
            }

            Log.Information("Backing up obb directory");
            string? obbBackupPath;
            try
            {
                obbBackupPath = await _installManager.CreateObbBackup();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create obb backup");
                obbBackupPath = null; // Indicate that the backup failed
            }

            PatchingStage = PatchingStage.UninstallingOriginal;
            try
            {
                if(!await _debugBridge.UninstallApp(_config.AppId))
                {
                    Log.Warning("APK was already uninstalled");
                }
            }
            catch (AdbException)
            {
                Log.Warning("Failed to remove the original APK, likely because it was already removed in a previous patching attempt");
                Log.Warning("Will continue with modding anyway");
            }

            Log.Information("Installing the modded APK . . .");
            PatchingStage = PatchingStage.InstallingModded;
            await _debugBridge.InstallApp(_patchedApkPath);

            if (dataBackupPath != null)
            {
                Log.Information("Restoring data backup");
                try
                {
                    await _installManager.RestoreDataBackup(dataBackupPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to restore data backup");
                }
            }

            if (obbBackupPath != null)
            {
                Log.Information("Restoring obb backup");
                try
                {
                    await _installManager.RestoreObbBackup(obbBackupPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to restore obb backup");
                }
            }

            // Recreate the mod directories as they will not be present after the uninstall/backup restore
            await _modManager.CreateModDirectories();
            // When repatching, certain mods may have been deleted when the app was uninstalled, so we will check for this
            await _modManager.UpdateModsStatus();

            await _installManager.NewApkInstalled(_patchedApkPath);

            Log.Information("Patching complete!");

        }
    }
}
