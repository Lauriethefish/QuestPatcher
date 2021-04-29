using System;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Diagnostics;
using Serilog.Core;
using System.Text.Json;

namespace QuestPatcher
{
    public class PatchingException : Exception
    {
        public PatchingException(string message) : base(message) { }
    }

    class ModdingHandler
    {
        private readonly string TEMP_PATH;
        private readonly string TOOLS_PATH; // Stores downloaded JARs used for modding
        private const string LIB_PATH = "lib/arm64-v8a/";

        public AppInfo AppInfo { get; private set; }

        private readonly MainWindow window;
        private readonly DebugBridge debugBridge;
        private readonly Logger logger;

        private readonly WebClient webClient = new();

        public ModdingHandler(MainWindow window)
        {
            this.window = window;
            this.debugBridge = window.DebugBridge;
            this.logger = window.Logger;

            TEMP_PATH = window.TEMP_PATH + "patching/";
            TOOLS_PATH = window.DATA_PATH + "tools/";
        }

        // We need some files for installation that we can't just distrubute with this, so we download them while we're running.
        // This also makes sure that we're always using the latest version of the modloader (QuestLoader)
        private async Task DownloadToolsFiles()
        {
            await DownloadFileIfNotExists("https://github.com/sc2ad/QuestLoader/releases/latest/download/libmodloader64.so", "libmodloader.so");
            await DownloadFileIfNotExists("https://github.com/sc2ad/QuestLoader/releases/latest/download/libmain64.so", "libmain.so");
            await DownloadFileIfNotExists("https://github.com/patrickfav/uber-apk-signer/releases/download/v1.2.1/uber-apk-signer-1.2.1.jar", "uber-apk-signer.jar");
            await DownloadFileIfNotExists("https://bitbucket.org/iBotPeaches/apktool/downloads/apktool_2.5.0.jar", "apktool.jar");
        }

        // Uses https://github.com/Lauriethefish/QuestUnstrippedUnity to download an appropriate unstripped libunity.so for this app, if there is one indexed.
        private async Task<bool> AttemptDownloadUnstrippedUnity()
        {
            Uri indexUrl = new("https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/index.json");

            logger.Information("Checking index for unstripped libunity.so . . .");
            string libUnityIndexString = await webClient.DownloadStringTaskAsync(indexUrl);
            logger.Debug("Contents of index: " + libUnityIndexString);
            JsonDocument document = JsonDocument.Parse(libUnityIndexString);

            if(document.RootElement.TryGetProperty(window.Config.AppId, out JsonElement packageMapElement))
            {
                if(packageMapElement.TryGetProperty(AppInfo.GameVersion, out JsonElement packageVersionElement)) {
                    logger.Information("Successfully found unstripped libunity.so");
                    string libUnityUrl = $"https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/versions/{packageVersionElement.GetString()}.so";
                    await DownloadFile(libUnityUrl, "libunity.so", true);
                    return true;
                }   else    {
                    logger.Information("libunity was available for other versions of your app, but not the one that you have installed");
                    return false;
                }
            }
            else
            {
                logger.Warning("No libunity found");
                return false;
            }
        }

        private async Task DownloadFileIfNotExists(string downloadLink, string savePath)
        {
            await DownloadFile(downloadLink, savePath, false);
        }

        private async Task DownloadFile(string downloadLink, string savePath, bool overwriteIfExists)
        {
            string actualPath = TOOLS_PATH + savePath;
            if(File.Exists(actualPath))
            {
                if (overwriteIfExists)
                {
                    File.Delete(actualPath);
                }
                else
                {
                    return;
                }
            }

            logger.Information($"Downloading {savePath} . . .");
            await webClient.DownloadFileTaskAsync(downloadLink, actualPath);
        }

        // Invokes a JAR in the tools directory with name jarName and args args.
        // Returns the error output and the standard output concatenated together when the proces exits.
        public async Task<string> InvokeJarAsync(string jarName, string args) {
            string command = $"-Xmx1024m -jar \"{TOOLS_PATH}/{jarName}\" {args}";

            string result = await InvokeJavaAsync(command);
            if(result.Contains("corrupt"))
            {
                // Sometimes the JAR files get corrupted, e.g. if the users exits while they're downloading.
                // To solve this, we delete the existing ones and then re-download.
                logger.Information("A JAR file was corrupted. Attempting to re-download JARs . . .");
                ClearJars();
                await DownloadToolsFiles();
                return await InvokeJavaAsync(command);
            }
            else
            {
                return result;
            }
        }

        // Clears all .JAR files in the tools directory - used when they are corrupted.
        private void ClearJars()
        {
            foreach(string fileName in Directory.GetFiles(TOOLS_PATH))
            {
                string extension = Path.GetExtension(fileName).ToUpper();
                if (extension == ".JAR")
                {
                    logger.Information($"Deleting JAR {fileName}");
                    File.Delete(fileName);
                }
            }
        }


        // Invokes a JAR file in the tools directory with name jarName, passing it args
        // Returns the error output and the standard output concatenated together when the process exits.
        public async Task<string> InvokeJavaAsync(string args)
        {
            Process process = new();
            process.StartInfo.FileName = OperatingSystem.IsWindows() ? "java.exe" : "java";
            process.StartInfo.Arguments = args;
            logger.Verbose($"Running Java command: java {process.StartInfo.Arguments}");

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string errorOutput = await process.StandardError.ReadToEndAsync();

            logger.Verbose($"Standard output: {output}");
            logger.Verbose($"Error output: {errorOutput}");

            await process.WaitForExitAsync();

            return errorOutput + output;
        }

        // Modifies this manifest string with the attributes necessary for modding
        // This includes adding read and write permissions to the given manifest string
        private string ModifyManifest(string manifest)
        {
            
            // This is futureproofing as in Android 11 WRITE and READ is replaced by MANAGE.
            // Otherwise Storage access would be limited to scoped-storage like an app-specific directory or a public shared directory.
            // Can be removed until any device updates to Android 11, however it's best to keep for compatability.
            const string MANAGE_PERMISSIONS = "<uses-permission android:name=\"android.permission.MANAGE_EXTERNAL_STORAGE\"/>";

            const string WRITE_PERMISSIONS = "<uses-permission android:name=\"android.permission.WRITE_EXTERNAL_STORAGE\"/>";
            const string READ_PERMISSIONS = "<uses-permission android:name=\"android.permission.READ_EXTERNAL_STORAGE\"/>";

            // Required for Apps that target Android 10 API Level 29 or higher as that uses scoped storage see: https://developer.android.com/training/data-storage/use-cases#opt-out-scoped-storage
            const string LegacyExternalStorage = "android:requestLegacyExternalStorage = \"true\"";
            const string ApplicationDebuggable = "android:debuggable = \"true\"";

            const string ApplicationStr = "<application";

            int newLineIndex = manifest.IndexOf('\n');
            string newManifest = manifest.Substring(0, newLineIndex) + "\n";
            if (!manifest.Contains(MANAGE_PERMISSIONS))
            {
                newManifest += "    " + MANAGE_PERMISSIONS + "\n";
            }

            if (!manifest.Contains(WRITE_PERMISSIONS))
            {
                newManifest += "    " + WRITE_PERMISSIONS + "\n";
            }

            if (!manifest.Contains(READ_PERMISSIONS))
            {
                newManifest += "    " + READ_PERMISSIONS + "\n";
            }

            if (!manifest.Contains(LegacyExternalStorage))
            {
                logger.Debug("Adding legacy storage support . . .");
                manifest = manifest.Replace(ApplicationStr, $"{ApplicationStr} {LegacyExternalStorage}");
            }

            if (!manifest.Contains(ApplicationDebuggable))
            {
                logger.Debug("Adding debuggable flag . . .");
                manifest = manifest.Replace(ApplicationStr, $"{ApplicationStr} {ApplicationDebuggable}");
            }

            newManifest += manifest[(newLineIndex + 1)..];

            return newManifest;
        }

        // Copies a library file to the correct folder in the APK. If failOnExists is true, then the installer will complain that the game is already modded and exit.
        private void CopyLibraryFile(string name, bool failOnExists)
        {
            logger.Information($"Copying library {name} . . .");
            string destPath = $"{TEMP_PATH}app/{LIB_PATH}{name}";
            if(File.Exists(destPath) && failOnExists)
            {
                throw new PatchingException("Your game is already modded!");
            }

            File.Copy(TOOLS_PATH + name, destPath, true);
        }

        // Pulls the APK from the Quest to check if it is modded, and to check its version.
        public async Task CheckInstallStatus()
        {
            if (Directory.Exists(TEMP_PATH))
            {
                logger.Information("Removing existing temporary directory . . .");
                Directory.Delete(TEMP_PATH, true);
            }
            logger.Information("Creating temporary directory . . .");
            Directory.CreateDirectory(TEMP_PATH);
            Directory.CreateDirectory(TOOLS_PATH);


            logger.Information("Pulling APK from Quest to check if modded . . .");
            string appPath = await debugBridge.RunCommandAsync("shell pm path {app-id}");
            appPath = appPath.Remove(0, 8).Replace("\n", "").Replace("'", "").Replace("\r", ""); // Remove the "package:" from the start and the new line from the end
            await debugBridge.RunCommandAsync($"pull \"{appPath}\" \"{TEMP_PATH}/unmodded.apk\"");

            // Unfortunately apktool doesn't extract the tag file, so we manually open the APK
            ZipArchive apkArchive = ZipFile.OpenRead(TEMP_PATH + "unmodded.apk");
            bool isModded = apkArchive.GetEntry("modded") != null || apkArchive.GetEntry("BMBF.modded") != null;
            apkArchive.Dispose();

            string gameVersion = await debugBridge.RunCommandAsync("shell dumpsys \"package {app-id} | grep versionName\"");
            // Remove the version= and the \n
            gameVersion = gameVersion.Remove(0, 16);
            gameVersion = gameVersion.Trim();

            AppInfo = new AppInfo(isModded, gameVersion);
            logger.Information(AppInfo.IsModded ? "APK is modded" : "App is not modded");
            logger.Information($"APK version: \"{AppInfo.GameVersion}\"");
        }

        // Patches the app, including modding the manifest, and adding unstripped libunity and the modloader.
        // Then uninstalls the unmodded app and installs the modded one.
        public async Task StartModdingProcess()
        {
            await DownloadToolsFiles();
            bool replaceUnity = await AttemptDownloadUnstrippedUnity();

            // Decompile the APK using apktool. We have to do this to read the manifest since it's AXML, which is nasty
            logger.Information("Decompiling APK . . .");
            string cmd = $"d -f -o \"{TEMP_PATH}app\" \"{TEMP_PATH}unmodded.apk\"";
            await InvokeJarAsync("apktool.jar", cmd);

            logger.Information("Decompiled APK");

            // Add permissions to access (read/write) external files.
            logger.Information("Modding manifest . . .");
            string oldManifest = File.ReadAllText($"{TEMP_PATH}app/AndroidManifest.xml");
            string newManifest = ModifyManifest(oldManifest);
            File.WriteAllText($"{TEMP_PATH}app/AndroidManifest.xml", newManifest);

            // Add the modloader, and the modified libmain.so that loads it.
            logger.Information("Adding library files . . .");
            CopyLibraryFile("libmain.so", false);
            CopyLibraryFile("libmodloader.so", true);
            if (replaceUnity)
            {
                CopyLibraryFile("libunity.so", false);
            }

            // Recompile the modified APK using apktool
            logger.Information("Compiling modded APK . . .");
            cmd = $"b -f -o \"{TEMP_PATH}modded.apk\" \"{TEMP_PATH}app\"";
            await InvokeJarAsync("apktool.jar", cmd);

            logger.Information("Adding tag . . .");
            ZipArchive apkArchive = ZipFile.Open($"{TEMP_PATH}modded.apk", ZipArchiveMode.Update);
            apkArchive.CreateEntry("modded");
            apkArchive.Dispose();

            // Sign it so that Android will install it
            logger.Information("Signing the modded APK . . .");
            cmd = $"--apks \"{TEMP_PATH}modded.apk\"";
            await InvokeJarAsync("uber-apk-signer.jar", cmd);

            File.Move($"{TEMP_PATH}modded-aligned-debugSigned.apk", $"{TEMP_PATH}modded-and-signed.apk");

            // Uninstall the original app with ADB first, ADB doesn't have a better way of doing this
            logger.Information("Uninstalling unmodded app . . .");
            string output = await debugBridge.RunCommandAsync("uninstall {app-id}");
            logger.Information(output);
            
            // Install the modified APK
            logger.Information("Installing modded app . . .");
            output = await debugBridge.RunCommandAsync($"install \"{TEMP_PATH}modded-and-signed.apk\"");
            logger.Information(output);

            logger.Information("Modding complete!");
        }
    }
}
