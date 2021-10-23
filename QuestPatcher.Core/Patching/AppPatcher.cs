using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using QuestPatcher.Axml;
using QuestPatcher.Core.Models;
using Serilog.Core;
using UnityIndex = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>;

namespace QuestPatcher.Core.Patching
{
    public class AppPatcher
    {
        private static readonly Uri AndroidNamespaceUri = new("http://schemas.android.com/apk/res/android");
        private const string ManifestPath = "AndroidManifest.xml";
        
        // Attribute resource IDs, used during manifest patching
        private const int NameAttributeResourceId = 16842755;
        private const int RequiredAttributeResourceId = 16843406;
        private const int DebuggableAttributeResourceId = 16842767;
        private const int LegacyStorageAttributeResourceId = 16844291;
        
        private readonly Logger _logger;
        private readonly ExternalFilesDownloader _filesDownloader;

        public AppPatcher(Logger logger, ExternalFilesDownloader filesDownloader)
        {
            _logger = logger;
            _filesDownloader = filesDownloader;
        }
        
        /// <summary>
        /// Patches the manifest of the APK to add the permissions/features specified in <see cref="PatchingPermissions"/> in the <see cref="Config"/>.
        /// </summary>
        /// <param name="manifest">Root AXML element of the APK's manifest</param>
        /// <param name="permissions">Permissions to patch the manifest with</param>
        /// <exception cref="PatchingException">If the given archive does not contain an <code>AndroidManifest.xml</code> file</exception>
        private void PatchManifest(AxmlElement manifest, PatchingPermissions permissions, bool addTag)
        {
            // First we add permissions and features to the APK for modding
            List<string> addingPermissions = new();
            List<string> addingFeatures = new();
            if (permissions.ExternalFiles)
            {
                // Technically, we only need READ_EXTERNAL_STORAGE and WRITE_EXTERNAL_STORAGE, but we also add MANAGE_EXTERNAL_STORAGE as this is what Android 11 needs instead
                addingPermissions.AddRange(new[] {
                    "android.permission.READ_EXTERNAL_STORAGE", 
                    "android.permission.WRITE_EXTERNAL_STORAGE",
                    "android.permission.MANAGE_EXTERNAL_STORAGE",
                });

                if(addTag)
                {
                    addingPermissions.Add(ApkAnalyser.TagPermission);
                }
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

                _logger.Debug($"Adding permission {permission}");
                AxmlElement permElement = new("uses-permission");
                AddNameAttribute(permElement, permission);
                manifest.Children.Add(permElement);
            }

            foreach (string feature in addingFeatures)
            {
                if(existingFeatures.Contains(feature)) { continue; } // Do not add existing features

                _logger.Debug($"Adding feature {feature}");
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
                appElement.Attributes.Add(new AxmlAttribute("debuggable", AndroidNamespaceUri, DebuggableAttributeResourceId, true));
                _logger.Information("Added debuggable flag");
            }

            if (permissions.ExternalFiles && !appElement.Attributes.Any(attribute => attribute.Name == "requestLegacyExternalStorage"))
            {
                appElement.Attributes.Add(new AxmlAttribute("requestLegacyExternalStorage", AndroidNamespaceUri, LegacyStorageAttributeResourceId, true));
                _logger.Debug("Added legacy external storage flag");
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
        
        private async Task<bool> AttemptCopyUnstrippedUnity(string libsPath, ZipArchive apkArchive, string appVersion, string packageId)
        {
            WebClient client = new();

            // Only download the index once
            _logger.Debug("Downloading libunity index . . .");

            string data = await client.DownloadStringTaskAsync("https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/index.json");

            UnityIndex? libUnityIndex = JsonSerializer.Deserialize<UnityIndex>(data);
            if(libUnityIndex == null)
            {
                throw new ArgumentException("Unity index was null!");
            }
            

            _logger.Debug("Checking index for unstripped libunity.so . . .");

            // The versions are separated per version of each app, since apps may change their unity version
            libUnityIndex.TryGetValue(packageId, out var availableVersions);

            if (availableVersions == null)
            {
                _logger.Warning("Unstripped libunity not found for this app");
                return false;
            }

            availableVersions.TryGetValue(appVersion, out string? correctVersion);

            if (correctVersion == null)
            {
                _logger.Warning($"Unstripped libunity found for other versions of this app, but not {appVersion}");
                return false;
            }

            _logger.Information("Unstripped libunity found. Downloading . . .");
            using TempFile tempDownloadPath = new();
            
            await _filesDownloader.DownloadUrl(
                    $"https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/versions/{correctVersion}.so",
                    tempDownloadPath.Path, "libunity.so");

            await apkArchive.AddFileAsync(tempDownloadPath.Path, Path.Combine(libsPath, "libunity.so"), true);

            return true;
        }
        
        public async Task<bool> Patch(ZipArchive apkArchive, Func<Task<bool>> promptUnityUnavailable, PatchingPermissions patchingPermissions, bool addTag = true)
        {
            ApkAnalyser.GetApkInfo(apkArchive, out var is64Bit, out var alreadyModded, out string libsPath);
            if(alreadyModded)
            {
                _logger.Error("The APK has already been modded!");
                return false;
            }
            
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
            _logger.Debug("Loading manifest as AXML . . .");
            AxmlElement manifest = AxmlLoader.LoadDocument(ms);

            string packageId = (string) manifest.Attributes.Single(attr => attr.Name == "package").Value;
            string apkVersion = (string) manifest.Attributes.Single(attr => attr.Name == "versionName").Value;

            if (!await AttemptCopyUnstrippedUnity(libsPath, apkArchive, apkVersion, packageId))
            {
                if (!await promptUnityUnavailable()) // Prompt the user to ask if they would like to continue, since missing libunity is likely to break some mods
                {
                    return false;
                }
            }

            // Replace libmain.so to load the modloader, then add libmodloader.so, which actually does the mod loading.
            if (is64Bit)
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
            _logger.Information("Copied libmain.so and libmodloader.so");

            // Add permissions to the manifest
            PatchManifest(manifest, patchingPermissions, addTag);
            _logger.Information("Patched manifest");
            
            // Save the manifest using our AXML library
            // TODO: The AXML library is missing some features such as styles.
            _logger.Debug("Saving manifest as AXML . . .");
            manifestEntry.Delete(); // Remove old manifest
            
            // No async ZipArchive implementation, so Task.Run is used
            await Task.Run(() =>
            {
                manifestEntry = apkArchive.CreateEntry(ManifestPath);
                using Stream saveStream = manifestEntry.Open();
                AxmlSaver.SaveDocument(saveStream, manifest);
            });

            if(addTag)
            {
                // The disk IO while opening the APK as a zip archive causes a UI freeze, so we run it on another thread
                // We cannot just create this tag before compiling - apktool will remove it as it isn't a normal part of the APK
                apkArchive.CreateEntry(ApkAnalyser.QuestPatcherTagName);
                _logger.Information("Added tag");
            }

            _logger.Information("Patching complete!");
            return true;
        }
    }
}
