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
    class ModdingHandler
    {
        private readonly string TEMP_PATH;
        private readonly string TOOLS_PATH; // Stores downloaded JARs used for modding
        private const string LIB_PATH = "lib/arm64-v8a/";

        public AppInfo AppInfo { get; private set; }

        private DebugBridge debugBridge;
        private Logger logger;

        private WebClient webClient = new WebClient();

        public ModdingHandler(MainWindow window)
        {
            this.debugBridge = window.DebugBridge;
            this.logger = window.Logger;

            TEMP_PATH = window.TEMP_PATH + "patching/";
            TOOLS_PATH = window.DATA_PATH + "tools/";
        }

        // We need some files for installation that we can't just distrubute with this
        private async Task downloadFiles()
        {
            await downloadIfNotExists("https://github.com/sc2ad/QuestLoader/releases/latest/download/libmodloader.so", "libmodloader.so");
            await downloadIfNotExists("https://github.com/RedBrumbler/QuestAppPatcher/blob/master/extraFiles/libmain.so?raw=true", "libmain.so");
            await downloadIfNotExists("https://github.com/patrickfav/uber-apk-signer/releases/download/v1.2.1/uber-apk-signer-1.2.1.jar", "uber-apk-signer.jar");
            await downloadIfNotExists("https://bitbucket.org/iBotPeaches/apktool/downloads/apktool_2.5.0.jar", "apktool.jar");
        }

        // Uses https://github.com/Lauriethefish/QuestUnstrippedUnity to download an appropriate unstripped libunity.so for this app, if there is one indexed.
        private async Task<bool> attemptDownloadUnstrippedUnity()
        {
            Uri indexUrl = new Uri("https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/index.json");

            logger.Information("Checking index for unstripped libunity.so . . .");
            string libUnityIndexString = await webClient.DownloadStringTaskAsync(indexUrl);
            logger.Debug("Contents of index: " + libUnityIndexString);
            JsonDocument document = JsonDocument.Parse(libUnityIndexString);

            JsonElement packageMapElement;
            if(document.RootElement.TryGetProperty(debugBridge.APP_ID, out packageMapElement))
            {
                JsonElement packageVersionElement;
                if(packageMapElement.TryGetProperty(AppInfo.GameVersion, out packageVersionElement)) {
                    logger.Information("Successfully found unstripped libunity.so");
                    string libUnityUrl = "https://raw.githubusercontent.com/Lauriethefish/QuestUnstrippedUnity/main/versions/" + packageVersionElement.GetString() + ".so";
                    await download(libUnityUrl, "libunity.so", true);
                    return true;
                }   else    {
                    logger.Information("libunity was available for other versions of your app, but not the one that you have installed");
                    return true;
                }
            }
            else
            {
                logger.Information("No libunity found");
                return false;
            }
        }

        private async Task downloadIfNotExists(string downloadLink, string savePath)
        {
            await download(downloadLink, savePath, false);
        }

        private async Task download(string downloadLink, string savePath, bool overwriteIfExists)
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

            logger.Information("Downloading " + savePath + " . . .");
            await webClient.DownloadFileTaskAsync(downloadLink, actualPath);
        }

        public async Task<string> InvokeJarAsync(string jarName, string args) {
            string command = "-Xmx1024m -jar \"" + TOOLS_PATH + "/" + jarName + "\" " + args;

            string result = await InvokeJavaAsync(command);
            if(result.Contains("corrupt"))
            {
                // Sometimes the JAR files get corrupted, e.g. if the users exits while they're downloading.
                // To solve this, we delete the existing ones and then re-download.
                logger.Information("A JAR file was corrupted. Attempting to re-download JARs . . .");
                clearJARs();
                await downloadFiles();
                return await InvokeJavaAsync(command);
            }
            else
            {
                return result;
            }
        }

        private void clearJARs()
        {
            foreach(string fileName in Directory.GetFiles(TOOLS_PATH))
            {
                string extension = Path.GetExtension(fileName).ToUpper();
                if (extension == ".JAR")
                {
                    logger.Information("Deleting JAR " + fileName);
                    File.Delete(fileName);
                }
            }
        }


        // Invokes a JAR file in the tools directory with name jarName, passing it args
        public async Task<string> InvokeJavaAsync(string args)
        {
            Process process = new Process();
            process.StartInfo.FileName = OperatingSystem.IsWindows() ? "java.exe" : "java";
            process.StartInfo.Arguments = args;
            logger.Verbose("Running Java command: " + "java " + process.StartInfo.Arguments);

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string errorOutput = await process.StandardError.ReadToEndAsync();

            logger.Verbose("Standard output: " + output);
            logger.Verbose("Error output: " + errorOutput);

            await process.WaitForExitAsync();

            return errorOutput + output;
        }

        // Adds read and write permissions to the given manifest string
        private string modifyManifest(string manifest)
        {
            /*
            This is futureproofing as in Android 11 WRITE and READ is replaced by MANAGE
            otherwise Storage access would be limited to scoped-storage like an app-specific directory or a public shared directory, 
            can be removed until any device updates to Android 11 would keep for compatability though.
            */
            const string MANAGE_PERMISSIONS = "<uses-permission android:name=\"android.permission.MANAGE_EXTERNAL_STORAGE\"/>";

            const string WRITE_PERMISSIONS = "<uses-permission android:name=\"android.permission.WRITE_EXTERNAL_STORAGE\"/>";
            const string READ_PERMISSIONS = "<uses-permission android:name=\"android.permission.READ_EXTERNAL_STORAGE\"/>";

            // Required for Apps that target Android 10 API Level 29 or higher as that uses scoped storage see: https://developer.android.com/training/data-storage/use-cases#opt-out-scoped-storage
            const string LegacyExternalStorage = "<application android:requestLegacyExternalStorage = \"true\"";

            const string ApplicationStr = "<application";

              int newLineIndex = manifest.IndexOf('\n');
            string newManifest = manifest.Substring(0, newLineIndex) + "\n";
            if (manifest.IndexOf(MANAGE_PERMISSIONS) == -1)
            {
                newManifest += "    " + MANAGE_PERMISSIONS + "\n";
            }

            if (manifest.IndexOf(WRITE_PERMISSIONS) == -1)
            {
                newManifest += "    " + WRITE_PERMISSIONS + "\n";
            }

            if (manifest.IndexOf(READ_PERMISSIONS) == -1)
            {
                newManifest += "    " + READ_PERMISSIONS + "\n";
            }

            if (manifest.IndexOf(LegacyExternalStorage) == -1)
            {
                logger.Information("Adding LegacyStorageSupport");
                manifest = manifest.Replace(ApplicationStr, LegacyExternalStorage);
            }

            newManifest += manifest.Substring(newLineIndex + 1);

            return newManifest;
        }

        // Copies a library file to the correct folder in the APK. If failOnExists is true, then the installer will complain that the game is already modded and exit.
        private void copyLibraryFile(string name, bool failOnExists)
        {
            logger.Information("Copying library " + name + " . . .");
            string destPath = TEMP_PATH + "app/" + LIB_PATH + name;
            if(File.Exists(destPath) && failOnExists)
            {
                throw new Exception("Your game is already modded!");
            }

            File.Copy(TOOLS_PATH + name, destPath, true);
        }

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
            string appPath = await debugBridge.runCommandAsync("shell pm path {app-id}");
            appPath = appPath.Remove(0, 8).Replace("\n", "").Replace("'", "").Replace("\r", ""); // Remove the "package:" from the start and the new line from the end
            await debugBridge.runCommandAsync("pull \"" + appPath + "\" \"" + TEMP_PATH + "/unmodded.apk\"");

            // Unfortunately apktool doesn't extract the tag file, so we manually open the APK
            ZipArchive apkArchive = ZipFile.OpenRead(TEMP_PATH + "unmodded.apk");
            bool isModded = apkArchive.GetEntry("modded") != null || apkArchive.GetEntry("BMBF.modded") != null;
            apkArchive.Dispose();

            string gameVersion = await debugBridge.runCommandAsync("shell dumpsys \"package {app-id} | grep versionName\"");
            // Remove the version= and the \n
            gameVersion = gameVersion.Remove(0, 16);
            gameVersion = gameVersion.Trim();

            AppInfo = new AppInfo(isModded, gameVersion);
            logger.Information(AppInfo.IsModded ? "APK is modded" : "App is not modded");
            logger.Information("APK version: \"" + AppInfo.GameVersion + "\"");
        }

        public async Task startModdingProcess()
        {
            await downloadFiles();
            bool replaceUnity = await attemptDownloadUnstrippedUnity();

            // Decompile the APK using apktool. We have to do this to read the manifest since it's AXML, which is nasty
            logger.Information("Decompiling APK . . .");
            string cmd = "d -f -o \"" + TEMP_PATH + "app\" \"" + TEMP_PATH + "unmodded.apk\"";
            await InvokeJarAsync("apktool.jar", cmd);

            logger.Information("Decompiled APK");

            // Add permissions to access (read/write) external files.
            logger.Information("Modding manifest . . .");
            string oldManifest = File.ReadAllText(TEMP_PATH + "app/AndroidManifest.xml");
            string newManifest = modifyManifest(oldManifest);
            File.WriteAllText(TEMP_PATH + "app/AndroidManifest.xml", newManifest);

            // Add the modloader, and the modified libmain.so that loads it.
            logger.Information("Adding library files . . .");
            copyLibraryFile("libmain.so", false);
            copyLibraryFile("libmodloader.so", true);
            if (replaceUnity)
            {
                copyLibraryFile("libunity.so", false);
            }

            // Recompile the modified APK using apktool
            logger.Information("Compiling modded APK . . .");
            cmd = "b -f -o \"" + TEMP_PATH + "modded.apk\" \"" + TEMP_PATH + "app\"";
            await InvokeJarAsync("apktool.jar", cmd);

            logger.Information("Adding tag . . .");
            ZipArchive apkArchive = ZipFile.Open(TEMP_PATH + "modded.apk", ZipArchiveMode.Update);
            apkArchive.CreateEntry("modded");
            apkArchive.Dispose();

            // Sign it so that Android will install it
            logger.Information("Signing the modded APK . . .");
            cmd = "--apks \"" + TEMP_PATH + "modded.apk\"";
            await InvokeJarAsync("uber-apk-signer.jar", cmd);

            File.Move(TEMP_PATH + "modded-aligned-debugSigned.apk", TEMP_PATH + "modded-and-signed.apk");

            // Uninstall the original app with ADB first, ADB doesn't have a better way of doing this
            logger.Information("Uninstalling unmodded app . . .");
            string output = await debugBridge.runCommandAsync("uninstall {app-id}");
            logger.Information(output);
            
            // Install the modified APK
            logger.Information("Installing modded app . . .");
            output = await debugBridge.runCommandAsync("install \"" + TEMP_PATH + "modded-and-signed.apk\"");
            logger.Information(output);

            logger.Information("Modding complete!");
        }
    }
}
