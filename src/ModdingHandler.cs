using System;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Diagnostics;
using Serilog.Core;

namespace QuestPatcher
{
    class ModdingHandler
    {
        private readonly string TEMP_DIRECTORY = Path.GetTempPath() + "QuestPatcher/";
        private readonly string TOOLS_DIRECTORY = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/QuestPatcher/tools/"; // Stores downloaded JARs used for modding
        private const string LIB_PATH = "lib/arm64-v8a/";

        public AppInfo AppInfo { get; private set; }

        private DebugBridge debugBridge;
        private Logger logger;

        public ModdingHandler(MainWindow window)
        {
            this.debugBridge = window.DebugBridge;
            this.logger = window.Logger;
        }

        // We need some files for installation that we can't just distrubute with this
        private async Task downloadFiles()
        {
            await downloadIfNotExists("https://github.com/sc2ad/QuestLoader/releases/latest/download/libmodloader.so", "libmodloader.so");
            await downloadIfNotExists("https://github.com/RedBrumbler/QuestAppPatcher/blob/master/extraFiles/libmain.so?raw=true", "libmain.so");
            await downloadIfNotExists("https://github.com/patrickfav/uber-apk-signer/releases/download/v1.2.1/uber-apk-signer-1.2.1.jar", "uber-apk-signer.jar");
        }

        private async Task downloadIfNotExists(string downloadLink, string savePath)
        {
            savePath = TOOLS_DIRECTORY + savePath;

            if(File.Exists(savePath))
            {
                return;
            }

            logger.Information("Downloading " + savePath);
            await new WebClient().DownloadFileTaskAsync(downloadLink, savePath);
        }

        // Invokes a JAR file in the tools directory with name jarName, passing it args
        private async Task<string> invokeJavaAsync(string jarName, string args)
        {
            Process process = new Process();
            process.StartInfo.FileName = OperatingSystem.IsWindows() ? "java.exe" : "java";
            process.StartInfo.Arguments = "-Xmx1024m -jar " + TOOLS_DIRECTORY + "/" + jarName + " " + args;
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

            return output;
        }

        // Adds read and write permissions to the given manifest string
        private string modifyManifest(string manifest)
        {
            const string WRITE_PERMISSIONS = "<uses-permission android:name=\"android.permission.WRITE_EXTERNAL_STORAGE\"/>";
            const string READ_PERMISSIONS = "<uses-permission android:name=\"android.permission.READ_EXTERNAL_STORAGE\"/>";

            int newLineIndex = manifest.IndexOf('\n');
            string newManifest = manifest.Substring(0, newLineIndex) + "\n";
            if (manifest.IndexOf(WRITE_PERMISSIONS) == -1)
            {
                newManifest += "    " + WRITE_PERMISSIONS + "\n";
            }

            if (manifest.IndexOf(READ_PERMISSIONS) == -1)
            {
                newManifest += "    " + READ_PERMISSIONS + "\n";
            }

            newManifest += manifest.Substring(newLineIndex + 1);

            return newManifest;
        }

        // Copies a library file to the correct folder in the APK. If failOnExists is true, then the installer will complain that the game is already modded and exit.
        private void copyLibraryFile(string name, bool failOnExists)
        {
            string destPath = TEMP_DIRECTORY + "app/" + LIB_PATH + name;
            if(File.Exists(destPath) && failOnExists)
            {
                throw new Exception("Your game is already modded!");
            }

            File.Copy(TOOLS_DIRECTORY + name, destPath, true);
        }

        public void RemoveTemporaryDirectory()
        {
            if (Directory.Exists(TEMP_DIRECTORY))
            {
                Directory.Delete(TEMP_DIRECTORY, true);
            }
        }

        public async Task CheckInstallStatus()
        {
            if (Directory.Exists(TEMP_DIRECTORY))
            {
                logger.Information("Removing existing temporary directory . . .");
                Directory.Delete(TEMP_DIRECTORY, true);
            }
            logger.Information("Creating temporary directory . . .");
            Directory.CreateDirectory(TEMP_DIRECTORY);
            Directory.CreateDirectory(TOOLS_DIRECTORY);


            logger.Information("Pulling APK from Quest to check if modded . . .");
            string appPath = await debugBridge.runCommandAsync("shell pm path {app-id}");
            appPath = appPath.Remove(0, 8).Replace("\n", "").Replace("'", "").Replace("\r", ""); // Remove the "package:" from the start and the new line from the end
            await debugBridge.runCommandAsync("pull \"" + appPath + "\" \"" + TEMP_DIRECTORY + "/unmodded.apk\"");

            await downloadIfNotExists("https://bitbucket.org/iBotPeaches/apktool/downloads/apktool_2.5.0.jar", "apktool.jar");

            // Decompile the APK using apktool. We have to do this to read the manifest since it's AXML, which is nasty
            logger.Information("Decompiling APK . . .");
            string cmd = "d -f -o \"" + TEMP_DIRECTORY + "app\" \"" + TEMP_DIRECTORY + "unmodded.apk\"";
            await invokeJavaAsync("apktool.jar", cmd);

            logger.Information("Decompiled APK");
            AppInfo = new AppInfo(TEMP_DIRECTORY + "unmodded.apk", TEMP_DIRECTORY + "app/");

            logger.Information(AppInfo.IsModded ? "APK is modded" : "App is not modded");
        }

        public async Task startModdingProcess()
        {
            await downloadFiles();

            // Add permissions to access (read/write) external files.
            logger.Information("Modding manifest . . .");
            string oldManifest = File.ReadAllText(TEMP_DIRECTORY + "app/AndroidManifest.xml");
            string newManifest = modifyManifest(oldManifest);
            File.WriteAllText(TEMP_DIRECTORY + "app/AndroidManifest.xml", newManifest);

            // Add the modloader, and the modified libmain.so that loads it.
            logger.Information("Adding library files . . .");
            copyLibraryFile("libmain.so", false);
            copyLibraryFile("libmodloader.so", true);


            // Recompile the modified APK using apktool
            logger.Information("Compiling modded APK . . .");
            string cmd = "b -f -o \"" + TEMP_DIRECTORY + "modded.apk\" \"" + TEMP_DIRECTORY + "app\"";
            await invokeJavaAsync("apktool.jar", cmd);

            logger.Information("Adding tag . . .");
            ZipArchive apkArchive = ZipFile.Open(TEMP_DIRECTORY + "modded.apk", ZipArchiveMode.Update);
            apkArchive.CreateEntry("modded");
            apkArchive.Dispose();

            // Sign it so that Android will install it
            logger.Information("Signing the modded APK . . .");
            cmd = "--apks " + TEMP_DIRECTORY + "modded.apk";
            await invokeJavaAsync("uber-apk-signer.jar", cmd);

            File.Move(TEMP_DIRECTORY + "modded-aligned-debugSigned.apk", TEMP_DIRECTORY + "modded-and-signed.apk");

            // Uninstall the original app with ADB first, ADB doesn't have a better way of doing this
            logger.Information("Uninstalling unmodded app . . .");
            string output = await debugBridge.runCommandAsync("uninstall {app-id}");
            logger.Information(output);
            
            // Install the modified APK
            logger.Information("Installing modded app . . .");
            output = await debugBridge.runCommandAsync("install \"" + TEMP_DIRECTORY + "modded-and-signed.apk\"");
            logger.Information(output);

            logger.Information("Modding complete!");
        }
    }
}
