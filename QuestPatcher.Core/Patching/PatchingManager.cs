using Newtonsoft.Json;
using QuestPatcher.Core.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using QuestPatcher.Axml;
using QuestPatcher.Core.Modding;
using Serilog;

namespace QuestPatcher.Core.Patching
{
    /// <summary>
    /// Handles checking if the selected app is modded, alongside patching it if not
    /// </summary>
    public class PatchingManager : INotifyPropertyChanged
    {
        private static readonly Uri AndroidNamespaceUri = new("http://schemas.android.com/apk/res/android");
        private const string ManifestPath = "AndroidManifest.xml";
        private const string DataDirectoryTemplate = "/sdcard/Android/data/{0}/files";
        private const string DataBackupTemplate = "/sdcard/QuestPatcher/{0}/backup";
        
        // Attribute resource IDs, used during manifest patching
        private const int NameAttributeResourceId = 16842755;
        private const int RequiredAttributeResourceId = 16843406;
        private const int DebuggableAttributeResourceId = 16842767;
        private const int LegacyStorageAttributeResourceId = 16844291;
        private const int ValueAttributeResourceId = 16842788;

        /// <summary>
        /// Tag added during patching.
        /// </summary>
        private const string QuestPatcherTagName = "modded";
        
        /// <summary>
        /// Tags from other installers which use QuestLoader. QP detects these for cross-compatibility.
        /// </summary>
        private static readonly string[] OtherTagNames = { "BMBF.modded" };
        
        /// <summary>
        /// Permission to tag the APK with.
        /// This permission is added to the manifest, and can be easily read from <code>adb shell dumpsys package [packageId]</code> without having to pull the entire APK.
        /// This makes loading much faster, especially on larger apps.
        /// </summary>
        private const string TagPermission = "questpatcher.modded";

        public ApkInfo? InstalledApp { get => _installedApp; private set { if (_installedApp != value) { _installedApp = value; NotifyPropertyChanged(); } } }
        private ApkInfo? _installedApp;

        public PatchingStage PatchingStage { get => _patchingStage; private set { if(_patchingStage != value) { _patchingStage = value; NotifyPropertyChanged(); } } }
        private PatchingStage _patchingStage = PatchingStage.NotStarted;
        
        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler? PatchingCompleted;

        private readonly Config _config;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly SpecialFolders _specialFolders;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly IUserPrompter _prompter;
        private readonly ApkSigner _apkSigner;
        private readonly Action _quit;
        private readonly ModManager _modManager;

        private readonly string _storedApkPath;
        private Dictionary<string, Dictionary<string, string>>? _libUnityIndex;

        public PatchingManager(Config config, AndroidDebugBridge debugBridge, SpecialFolders specialFolders, ExternalFilesDownloader filesDownloader, IUserPrompter prompter, ApkSigner apkSigner, Action quit, ModManager modManager)
        {
            _config = config;
            _debugBridge = debugBridge;
            _specialFolders = specialFolders;
            _filesDownloader = filesDownloader;
            _prompter = prompter;
            _apkSigner = apkSigner;
            _quit = quit;
            _modManager = modManager;

            _storedApkPath = Path.Combine(specialFolders.PatchingFolder, "currentlyInstalled.apk");
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
            bool is32Bit = false;
            bool is64Bit = false;
            bool isModded = false;
            
            string packageDump = (await _debugBridge.RunShellCommand($"dumpsys package {_config.AppId}")).StandardOutput;
            string version = GetPackageDumpValue(packageDump, "versionName");
            Log.Information($"App Version: {version}");

            int beginPermissionsIdx = packageDump.IndexOf("requested permissions:", StringComparison.Ordinal);
            int endPermissionsIdx = packageDump.IndexOf("install permissions:", StringComparison.Ordinal);
            string permissionsString = packageDump.Substring(beginPermissionsIdx, endPermissionsIdx - beginPermissionsIdx);

            Log.Information("Attempting to check modding status from package dump");
            // If the APK's permissions include the modded tag permission, then we know the APK is modded
            // This avoids having to pull the APK from the quest to check it if it's modded
            if(permissionsString.Split("\n").Skip(1).Select(perm => perm.Trim()).Contains(TagPermission))
            {
                Log.Information("Modded permission found in dumpsys output.");
                string cpuAbi = GetPackageDumpValue(packageDump, "primaryCpuAbi");
                // Currently, these are the only CPU ABIs supported
                is64Bit = cpuAbi == "arm64-v8a";
                is32Bit = cpuAbi == "armeabi-v7a";
                isModded = true;
            }
            else
            {
                // If the modded permission is not found, it is still possible that the APK is modded
                // Older QuestPatcher versions did not use a modded permission, and instead used a "modded" file in APK root
                // (which is still added during patching for backwards compatibility, and so that BMBF can see that the APK is patched)
                Log.Information("Modded permission not found, downloading APK from the Quest instead . . .");
                await _debugBridge.DownloadApk(_config.AppId, _storedApkPath);

                // Unfortunately, zip files do not support async, so we Task.Run this operation to avoid blocking
                Log.Information("Checking APK modding status . . .");
                await Task.Run(() =>
                {
                    using ZipArchive apkArchive = ZipFile.OpenRead(_storedApkPath);

                    // QuestPatcher adds a tag file to determine if the APK is modded later on
                    isModded = apkArchive.GetEntry(QuestPatcherTagName) != null || OtherTagNames.Any(tagName => apkArchive.GetEntry(tagName) != null);
                    is64Bit = apkArchive.GetEntry("lib/arm64-v8a/libil2cpp.so") != null;
                    is32Bit = apkArchive.GetEntry("lib/armeabi-v7a/libil2cpp.so") != null;
                });
            }


            if (!is64Bit && !is32Bit)
            {
                throw new PatchingException("The loaded APK did not contain a 32 or 64 bit libil2cpp for patching. This either means that it is of an unsupported architecture, or it is not an il2cpp unity app."
                    + " Please complain to Laurie if you're annoyed that QuestPatcher doesn't support unreal.");
            }
            Log.Information((isModded ? "APK is modded" : "APK is not modded") + " and is " + (is64Bit ? "64" : "32") + " bit");

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
                Log.Debug("Downloading libunity index for the first time . . .");
                JsonSerializer serializer = new();

                string data = await client.DownloadStringTaskAsync("https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/index.json");
                using StringReader stringReader = new(data);
                using JsonReader reader = new JsonTextReader(stringReader);

                _libUnityIndex = serializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(reader);
            }

            Log.Information("Checking index for unstripped libunity.so . . .");
            Debug.Assert(InstalledApp != null);
            Debug.Assert(_libUnityIndex != null);

            // The versions are separated per version of each app, since apps may change their unity version
            _libUnityIndex.TryGetValue(_config.AppId, out var availableVersions);

            if (availableVersions == null)
            {
                Log.Warning("Unstripped libunity not found for this app");
                return false;
            }

            availableVersions.TryGetValue(InstalledApp.Version, out string? correctVersion);

            if (correctVersion == null)
            {
                Log.Warning($"Unstripped libunity found for other versions of this app, but not {InstalledApp.Version}");
                return false;
            }

            Log.Information("Unstripped libunity found. Downloading . . .");
            using TempFile tempDownloadPath = _specialFolders.GetTempFile();
            
            await _filesDownloader.DownloadUrl(
                    $"https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/versions/{correctVersion}.so",
                    tempDownloadPath.Path, "libunity.so");

            await apkArchive.AddFileAsync(tempDownloadPath.Path, Path.Combine(libsPath, "libunity.so"), true);

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
            Log.Information("Loading manifest as AXML . . .");
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
                    "android.permission.MANAGE_EXTERNAL_STORAGE",
                    TagPermission
                });
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

            // Find which features and permissions already exist to avoid adding existing ones
            ISet<string> existingPermissions = GetExistingChildren(manifest, "uses-permission");
            ISet<string> existingFeatures = GetExistingChildren(manifest, "uses-feature");

            foreach (string permission in addingPermissions)
            {
                if(existingPermissions.Contains(permission)) { continue; } // Do not add existing permissions

                Log.Information($"Adding permission {permission}");
                AxmlElement permElement = new("uses-permission");
                AddNameAttribute(permElement, permission);
                manifest.Children.Add(permElement);
            }

            foreach (string feature in addingFeatures)
            {
                if(existingFeatures.Contains(feature)) { continue; } // Do not add existing features

                Log.Information($"Adding feature {feature}");
                AxmlElement featureElement = new("uses-feature");
                AddNameAttribute(featureElement, feature);
                
                // TODO: User may want the feature to be required instead of suggested
                featureElement.Attributes.Add(new AxmlAttribute("required", AndroidNamespaceUri, RequiredAttributeResourceId, false));
                manifest.Children.Add(featureElement);
            }

            // Now we need to add the legacyStorageSupport and debuggable flags
            AxmlElement appElement = manifest.Children.Single(element => element.Name == "application");
            if (permissions.Debuggable && !appElement.Attributes.Any(attribute => attribute.Name == "debuggable"))
            {
                Log.Information("Adding debuggable flag . . .");
                appElement.Attributes.Add(new AxmlAttribute("debuggable", AndroidNamespaceUri, DebuggableAttributeResourceId, true));
            }

            if (permissions.ExternalFiles && !appElement.Attributes.Any(attribute => attribute.Name == "requestLegacyExternalStorage"))
            {
                Log.Information("Adding legacy external storage flag . . .");
                appElement.Attributes.Add(new AxmlAttribute("requestLegacyExternalStorage", AndroidNamespaceUri, LegacyStorageAttributeResourceId, true));
            }


            switch(permissions.HandTrackingType)
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
                    break;
                case HandTrackingVersion.V2:
                    Log.Information("Adding V2 hand-tracking. . .");
                    frequencyElement = new("meta-data");
                    AddNameAttribute(frequencyElement, "com.oculus.handtracking.version");
                    frequencyElement.Attributes.Add(new AxmlAttribute("value", AndroidNamespaceUri, ValueAttributeResourceId, "V2.0"));
                    appElement.Children.Add(frequencyElement);
                    break;
            }

            // Save the manifest using our AXML library
            Log.Information("Saving manifest as AXML . . .");
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

        private async Task AddFlatscreenSupport(ZipArchive apkArchive)
        {
            const string bootCfgPath = "assets/bin/Data/boot.config";
            const string globalGameManagersPath = "assets/bin/Data/globalgamemanagers";
            const string ovrPlatformLoaderPath = "lib/arm64-v8a/libovrplatformloader.so";
            
            var bootCfgEntry = apkArchive.GetEntry(bootCfgPath)
                               ?? throw new PatchingException(
                                   "boot.config must exist on a Beat Saber installation");


            string bootCfgContents;
            using(var reader = new StreamReader(bootCfgEntry.Open()))
            {
                bootCfgContents = (await reader.ReadToEndAsync())
                    .Replace("vr-enabled=1", "vr-enabled=0")
                    .Replace("vr-device-list=Oculus", "vr-device-list=");
            }

            bootCfgEntry.Delete();
            bootCfgEntry = apkArchive.CreateEntry(bootCfgPath);
            await using(var writer = new StreamWriter(bootCfgEntry.Open()))
            {
                await writer.WriteAsync(bootCfgContents);
            }

            var gameManagersEntry = apkArchive.GetEntry(globalGameManagersPath) ?? throw new PatchingException(
                "globalgamemanagers must exist on a Beat Saber installation");

            var am = new AssetsManager();
            
            var replacementEntry = apkArchive.CreateEntry("_QP/replacementGameManagers");

            using(var gameManagersStream = gameManagersEntry.Open())
            {
                var inst = am.LoadAssetsFile(gameManagersStream, "globalgamemanagers", false);

                var inf = inst.table.GetAssetsOfType((int) AssetClassID.BuildSettings).Single();
                
                Assembly assembly = Assembly.GetExecutingAssembly();
                await using Stream? classPkgStream = assembly.GetManifestResourceStream("QuestPatcher.Core.Resources.classdata.tpk");
                if (classPkgStream == null)
                {
                    throw new NullReferenceException("Could not find classdata.tpk in resources");
                }
                am.LoadClassPackage(classPkgStream);
                am.LoadClassDatabaseFromPackage(inst.file.typeTree.unityVersion);
                
                var type = am.GetTypeInstance(inst, inf);
                var baseField = type.GetBaseField();

                baseField.Get("enabledVRDevices").GetChildrenList()[0].SetChildrenList(Array.Empty<AssetTypeValueField>());
                var newBytes = baseField.WriteToByteArray();
                
                var replacer = new AssetsReplacerFromMemory(0, inf.index, (int)inf.curFileType, 0xffff, newBytes);

                using var replacementStream = replacementEntry.Open();
                using var writer = new AssetsFileWriter(replacementStream);
                inst.file.Write(writer, 0, new List<AssetsReplacer> { replacer });
                am.UnloadAllAssetsFiles();
            }
            
            gameManagersEntry.Delete();
            gameManagersEntry = apkArchive.CreateEntry(globalGameManagersPath);
            using(var newGameManagers = gameManagersEntry.Open())
            using(var replacement = replacementEntry.Open())
            {
                await replacement.CopyToAsync(newGameManagers);
            }

            replacementEntry.Delete();

            using var sdkArchive = ZipFile.OpenRead(await _filesDownloader.GetFileLocation(ExternalFileType.OvrPlatformSdk));
            var downgradedLoaderEntry = sdkArchive.GetEntry("Android/libs/arm64-v8a/libovrplatformloader.so")
                                        ?? throw new PatchingException("No libovrplatformloader.so found in downloaded OvrPlatformSdk");
            using var downloadedLoaderStream = downgradedLoaderEntry.Open();

            apkArchive.GetEntry(ovrPlatformLoaderPath)?.Delete();
            var platformLoaderEntry = apkArchive.CreateEntry(ovrPlatformLoaderPath);
            using var platformLoaderStream = platformLoaderEntry.Open();

            await downloadedLoaderStream.CopyToAsync(platformLoaderStream);
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
            Log.Information("Copying APK to patched location . . .");
            string patchedApkPath = Path.Combine(_specialFolders.PatchingFolder, "patched.apk");

            // There is no async file copy method, so we Task.Run it (we could make our own with streams, that's another option)
            await Task.Run(() => { File.Copy(_storedApkPath, patchedApkPath, true); });
            Dictionary<string, ApkSigner.PrePatchHash>? prePatchHashes;
            
            Log.Information("Copying files to patch APK . . .");

            PatchingStage = PatchingStage.Patching;
            ZipArchive apkArchive = ZipFile.Open(patchedApkPath, ZipArchiveMode.Update);
            try
            {
                Log.Information("Preparing hashes for after signing");
                prePatchHashes = await _apkSigner.CollectPrePatchHashes(apkArchive);
                
                string libsPath = InstalledApp.Is64Bit ? "lib/arm64-v8a" : "lib/armeabi-v7a";

                if (!InstalledApp.Is64Bit)
                {
                    Log.Warning("App is 32 bit!");
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
                Log.Information("Copying libmain.so and libmodloader.so . . .");
                if (InstalledApp.Is64Bit)
                {
                    await apkArchive.AddFileAsync(await _filesDownloader.GetFileLocation(ExternalFileType.Main64), Path.Combine(libsPath, "libmain.so"), true);
                    await apkArchive.AddFileAsync(await _filesDownloader.GetFileLocation(ExternalFileType.Modloader64), Path.Combine(libsPath, "libmodloader.so"));
                }
                else
                {
                    Log.Warning("Using 32 bit versions!");
                    await apkArchive.AddFileAsync(await _filesDownloader.GetFileLocation(ExternalFileType.Main32), Path.Combine(libsPath, "libmain.so"), true);
                    await apkArchive.AddFileAsync(await _filesDownloader.GetFileLocation(ExternalFileType.Modloader32), Path.Combine(libsPath, "libmodloader.so"));
                }


                // Add permissions to the manifest
                Log.Information("Patching manifest . . .");
                await PatchManifest(apkArchive);

                if(_config.PatchingPermissions.FlatScreenSupport)
                {
                    Log.Information("Adding flatscreen support . . .");
                    await AddFlatscreenSupport(apkArchive);
                }

                Log.Information("Adding tag . . .");
                // The disk IO while opening the APK as a zip archive causes a UI freeze, so we run it on another thread
                apkArchive.CreateEntry(QuestPatcherTagName);
            }
            finally
            {
                Log.Information("Closing APK archive . . .");
                await Task.Run(() => { apkArchive.Dispose(); });
            }
            
            // Pause patching before compiling the APK in order to give a developer the chance to modify it.
            if(_config.PauseBeforeCompile && !await _prompter.PromptPauseBeforeCompile())
            {
                return;
            }
            
            Log.Information("Signing APK (this might take a while) . . .");
            PatchingStage = PatchingStage.Signing;

            await _apkSigner.SignApkWithPatchingCertificate(patchedApkPath, prePatchHashes);
            

            Log.Information("Uninstalling the default APK . . .");
            Log.Information("Backing up data directory");
            string dataPath = string.Format(DataDirectoryTemplate, _config.AppId);
            string? backupPath = string.Format(DataBackupTemplate, _config.AppId);
            try
            {
                // Avoid failing if no files are present in the data directory
                // TODO: Perhaps check if it exists first and then skip backup if missing? This is more complex.
                await _debugBridge.CreateDirectory(dataPath);
                // Remove the backup path if it already exists and then recreate it
                await _debugBridge.RemoveDirectory(backupPath);
                await _debugBridge.CreateDirectory(backupPath);
                // Copy all the files to the data backup
                await _debugBridge.Move(dataPath, backupPath);
            }
            catch(Exception ex)
            {
                Log.Error($"Failed to create data backup: {ex}");
                backupPath = null; // Indicate that the backup failed
            }

            PatchingStage = PatchingStage.UninstallingOriginal;

            await _debugBridge.UninstallApp(_config.AppId);

            Log.Information("Installing the modded APK . . .");
            PatchingStage = PatchingStage.InstallingModded;

            await _debugBridge.InstallApp(patchedApkPath);

            if(backupPath != null)
            {
                Log.Information("Restoring data backup");
                try
                {
                    string dataParentPath = Path.GetDirectoryName(dataPath)!;
                    await _debugBridge.CreateDirectory(dataParentPath); // This is deleted upon uninstall
                    // Move the "files" folder within the backup to the "data" folder for the app.
                    await _debugBridge.Move(Path.Combine(backupPath, "files"), dataParentPath);
                    // Delete mod/library files to avoid old mods causing crashes
                    await _debugBridge.RemoveDirectory(Path.Combine(dataPath, "libs"));
                    await _debugBridge.RemoveDirectory(Path.Combine(dataPath, "mods"));
                }
                catch(Exception ex)
                {
                    Log.Error($"Failed to restore data backup: {ex}");
                }
            }
            
            try
            {
                File.Delete(patchedApkPath); // The patched APK takes up space, and we don't need it now
            }
            catch (IOException) // Sometimes a developer might be using the APK, so avoid failing the whole patching process
            {
                Log.Warning("Failed to delete patched APK");
            }

            // Recreate the mod directories as they will not be present after the uninstall/backup restore
            await _modManager.CreateModDirectories();

            Log.Information("Patching complete!");
            InstalledApp.IsModded = true;
            PatchingCompleted?.Invoke(this, EventArgs.Empty);
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
