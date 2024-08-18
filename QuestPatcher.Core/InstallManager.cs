using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using QuestPatcher.Axml;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using QuestPatcher.Zip;
using Serilog;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Manages the current installation of the app.
    /// </summary>
    public class InstallManager : INotifyPropertyChanged
    {
        public const string JsonTagName = "modded.json";

        public static readonly IReadOnlyList<string> QuestLoaderTagNames = new List<string>
        {
            "BMBF.modded", // Legacy BMBF
            "modded", // Legacy QuestPatcher
        };
        public const string ManifestPath = "AndroidManifest.xml";

        public static readonly JsonSerializerOptions TagSerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private const string DataDirectoryTemplate = "/sdcard/Android/data/{0}/files";
        private const string ObbDirectoryTemplate = "/sdcard/Android/obb/{0}";
        private const string DataBackupTemplate = "/sdcard/QuestPatcher/{0}/backup";
        private const string ObbBackupTemplate = "/sdcard/QuestPatcher/{0}/obb";

        /// <summary>
        /// The APK currently installed on the quest
        /// </summary>
        public ApkInfo? InstalledApp { get => _installedApp; private set { if (_installedApp != value) { _installedApp = value; NotifyPropertyChanged(); } } }
        private ApkInfo? _installedApp;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// The /sdcard/Android/data/... directory for the installed app
        /// </summary>
        private string DataPath => string.Format(DataDirectoryTemplate, _config.AppId);

        /// <summary>
        /// The /sdcard/Android/obb/... directory for the installed app
        /// </summary>
        private string ObbPath => string.Format(ObbDirectoryTemplate, _config.AppId);

        private readonly string _currentlyInstalledPath;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly Config _config;
        private readonly Action _quit;

        public InstallManager(SpecialFolders specialFolders, AndroidDebugBridge debugBridge, Config config, Action quit)
        {
            _currentlyInstalledPath = Path.Combine(specialFolders.PatchingFolder, "currentlyInstalled.apk");
            _debugBridge = debugBridge;
            _config = config;
            _quit = quit;
        }

        /// <summary>
        /// Checks for tag files to find out which modloader an APK contains, if any.
        /// </summary>
        /// <param name="apk">The APK to check for modloaders</param>
        /// <returns>The modloader detected, if any</returns>
        private async Task<ModLoader?> GetModLoader(ApkZip apk)
        {
            // If APK is patched with a legacy questloader tag
            if (QuestLoaderTagNames.Any(tag => apk.ContainsFile(tag)))
            {
                Log.Information("Legacy tag indicates APK is modded with QuestLoader");
                return ModLoader.QuestLoader;
            }

            // Otherwise, we will check for the modern "modded.json" tag
            if (apk.ContainsFile(JsonTagName))
            {
                using var jsonStream = await apk.OpenReaderAsync(JsonTagName);

                var tag = await JsonSerializer.DeserializeAsync<ModdedTag>(jsonStream, TagSerializerOptions);
                if (tag != null)
                {
                    if (tag.ModloaderName.Equals("QuestLoader", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("APK is modded with QuestLoader");
                        return ModLoader.QuestLoader;
                    }
                    else if (tag.ModloaderName.Equals("Scotland2", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("APK is modded with Scotland2");
                        return ModLoader.Scotland2;
                    }
                    else
                    {
                        Log.Warning("Unknown modloader found in APK: {ModloaderName}", tag.ModloaderName);
                        return ModLoader.Unknown;
                    }
                }
            }

            Log.Information("APK is not modded");
            return null;
        }

        /// <summary>
        /// Finds the version of an APK, as specified in its manifest.
        /// </summary>
        /// <param name="apk">The APK to find the version of</param>
        /// <returns>The version of the APK</returns>
        private async Task<string> GetApkVersion(ApkZip apk)
        {
            // Need a seekable stream to load AXML
            using var memStream = new MemoryStream();
            using var manifestStream = await apk.OpenReaderAsync(ManifestPath);
            await manifestStream.CopyToAsync(memStream);
            memStream.Position = 0;

            var manifest = AxmlLoader.LoadDocument(memStream);
            return (string) manifest.Attributes.Single(attr => attr.Name == "versionName").Value;
        }


        /// <summary>
        /// Loads the currently installed APK from the Quest, and checks if it is patched.
        /// </summary>
        /// <exception cref="PatchingException">If the APK contained no 32-bit or 64-bit il2cpp.so, and so is not valid for use by QuestPatcher</exception>
        public async Task LoadInstalledApp()
        {
            Log.Information("Downloading APK from the Quest . . .");
            await _debugBridge.DownloadApk(_config.AppId, _currentlyInstalledPath);

            await CheckModdingStatus();
        }

        private async Task CheckModdingStatus()
        {
            Log.Information("Checking APK modding status");

            using var apkStream = File.OpenRead(_currentlyInstalledPath);
            await using var apk = await ApkZip.OpenAsync(apkStream);

            var modloader = await GetModLoader(apk);
            string version = await GetApkVersion(apk);

            bool is64Bit = apk.ContainsFile("lib/arm64-v8a/libil2cpp.so");
            bool is32Bit = apk.ContainsFile("lib/armeabi-v7a/libil2cpp.so");

            if (!is64Bit && !is32Bit)
            {
                throw new PatchingException("The loaded APK did not contain a 32 or 64 bit libil2cpp for patching. This either means that it is of an unsupported architecture, or it is not an il2cpp unity app."
                    + " Please complain to Laurie if you're annoyed that QuestPatcher doesn't support unreal.");
            }
            Log.Information("APK is " + (is64Bit ? "64" : "32") + " bit");

            InstalledApp = new ApkInfo(version, modloader, is64Bit, _currentlyInstalledPath);
        }

        /// <summary>
        /// Removes the downloaded APK and sets the installed APK to null.
        /// </summary>
        public void ResetInstalledApp()
        {
            if (File.Exists(_currentlyInstalledPath))
            {
                File.Delete(_currentlyInstalledPath);
            }
            InstalledApp = null;
        }


        /// <summary>
        /// Called to notify that the installed APK has changed.
        /// </summary>
        /// <param name="newApk">The path to the apk that was just installed. Will be moved upon calling</param>
        /// <exception cref="PatchingException">If the APK contained no 32-bit or 64-bit il2cpp.so, and so is not valid for use by QuestPatcher</exception>
        public async Task NewApkInstalled(string newApk)
        {
            if (File.Exists(_currentlyInstalledPath))
            {
                File.Delete(_currentlyInstalledPath);
            }

            File.Move(newApk, _currentlyInstalledPath);
            await CheckModdingStatus(); // Update the information about the app currently installed
        }

        /// <summary>
        /// Creates a backup of the /sdcard/Android/data/... directory for the current app.
        /// </summary>
        /// <returns>The path to the backup on the quest</returns>
        public async Task<string?> CreateDataBackup()
        {
            string backupPath = string.Format(DataBackupTemplate, _config.AppId);

            // Check if the data directory exists and skip if it doesn't.
            if (!await _debugBridge.Exists(DataPath))
            {
                return null;
            }

            // Remove the backup path if it already exists and then recreate it
            await _debugBridge.RemoveDirectory(backupPath);
            await _debugBridge.CreateDirectory(backupPath);
            // Copy all the files to the data backup
            await _debugBridge.Move(DataPath, backupPath);

            return backupPath;
        }

        /// <summary>
        /// Creates a backup of the /sdcard/Android/obb/... directory for the current app.
        /// </summary>
        /// <returns>The path to the backup on the quest</returns>
        public async Task<string?> CreateObbBackup()
        {
            string backupPath = string.Format(ObbBackupTemplate, _config.AppId);

            // Check if the data directory exists and skip if it doesn't.
            if (!await _debugBridge.Exists(ObbPath))
            {
                return null;
            }

            // Remove the backup path if it already exists
            await _debugBridge.RemoveDirectory(backupPath);
            // Copy all the files to the data backup
            await _debugBridge.Move(ObbPath, backupPath);

            return backupPath;
        }

        /// <summary>
        /// Restores a data backup created with CreateDataBackup.
        /// Will delete any mod/lib files in the backup to avoid old mods causing crashes.
        /// </summary>
        /// <param name="backupPath">The path to the backup on the quest</param>
        public async Task RestoreDataBackup(string backupPath)
        {
            string dataParentPath = Path.GetDirectoryName(DataPath)!;
            await _debugBridge.CreateDirectory(dataParentPath); // This is deleted upon uninstall

            // Move the "files" folder within the backup to the "data" folder for the app.
            await _debugBridge.Move(Path.Combine(backupPath, "files"), dataParentPath);
            // Delete mod/library files to avoid old mods causing crashes
            await _debugBridge.RemoveDirectory(Path.Combine(DataPath, "libs"));
            await _debugBridge.RemoveDirectory(Path.Combine(DataPath, "mods"));
        }

        /// <summary>
        /// Restores a obb backup created with CreateObbBackup.
        /// </summary>
        /// <param name="backupPath">The path to the backup on the quest</param>
        public async Task RestoreObbBackup(string backupPath)
        {
            // Remove the backup obb path if it already exists
            await _debugBridge.RemoveDirectory(ObbPath);
            // Move the obb backup to the original path.
            await _debugBridge.Move(backupPath, ObbPath);
        }

        /// <summary>
        /// Uninstalls the current app
        /// </summary>
        /// <returns></returns>
        public async Task UninstallApp()
        {
            if(!await _debugBridge.UninstallApp(_config.AppId))
            {
                Log.Warning("APK was already uninstalled. QuestPatcher is (was) out of sync.");
            }
            InstalledApp = null;
            _quit();
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
