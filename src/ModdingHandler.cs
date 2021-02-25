using System;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Diagnostics;

namespace QuestPatcher
{
    class ModdingHandler
    {
        private const string TEMP_DIRECTORY = "./temp/";
        private const string LIB_PATH = "lib/arm64-v8a/";

        private MainWindow window;
        private DebugBridge debugBridge;

        public ModdingHandler(MainWindow window)
        {
            this.window = window;
            this.debugBridge = window.DebugBridge;
        }

        // We need some files for installation that we can't just distrubute with this
        private async Task downloadFiles()
        {
            window.log("Downloading libmodloader.so . . .");
            WebClient webClient = new WebClient();
            await webClient.DownloadFileTaskAsync("https://github.com/sc2ad/QuestLoader/releases/latest/download/libmodloader.so", TEMP_DIRECTORY + "libmodloader.so");

            window.log("Downloading libmain.so . . .");
            await webClient.DownloadFileTaskAsync("https://github.com/RedBrumbler/QuestAppPatcher/blob/master/extraFiles/libmain.so?raw=true", TEMP_DIRECTORY + "libmain.so");

            window.log("Downloading apktool . . .");
            await webClient.DownloadFileTaskAsync("https://bitbucket.org/iBotPeaches/apktool/downloads/apktool_2.5.0.jar", TEMP_DIRECTORY + "apktool.jar");

            window.log("Downloading uber-signer . . .");
            await webClient.DownloadFileTaskAsync("https://github.com/patrickfav/uber-apk-signer/releases/download/v1.2.1/uber-apk-signer-1.2.1.jar", TEMP_DIRECTORY + "uber-apk-signer.jar");
        }

        // Invokes a JAR file in the temporary directory with name jarName, passing it args
        private async Task<string> invokeJavaAsync(string jarName, string args)
        {
            Process process = new Process();
            process.StartInfo.FileName = "java.exe";
            process.StartInfo.Arguments = "-Xmx1024m -jar " + TEMP_DIRECTORY + "/" + jarName + " " + args;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
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

            File.Copy(TEMP_DIRECTORY + name, destPath, true);
        }

        public async Task startModdingProcess()
        {
            if(Directory.Exists(TEMP_DIRECTORY)) {
                window.log("Removing existing temporary directory . . .");
                Directory.Delete(TEMP_DIRECTORY, true); // Delete the temporary directory, since stuff from last time we modded will get in the way
            }
            window.log("Creating temporary directory . . .");
            Directory.CreateDirectory(TEMP_DIRECTORY);

            await downloadFiles();

            window.log("Downloading APK from the Quest . . .");
            string appPath = await debugBridge.runCommandAsync("shell pm path {app-id}");
            appPath = appPath.Remove(0, 8).Replace("\n", "").Replace("'", "").Replace("\r", ""); // Remove the package: from the start and the new line from the end
            await debugBridge.runCommandAsync("pull " + appPath + " " + TEMP_DIRECTORY + "unmodded.apk");

            // Decompile the APK using apktool
            window.log("Decompiling APK . . .");
            string cmd = "d -f -o \"" + TEMP_DIRECTORY + "app\" \"" + TEMP_DIRECTORY + "unmodded.apk\"";
            await invokeJavaAsync("apktool.jar", cmd);

            // Add permissions to access (read/write) external files.
            window.log("Modding manifest . . .");
            string oldManifest = File.ReadAllText(TEMP_DIRECTORY + "app/AndroidManifest.xml");
            string newManifest = modifyManifest(oldManifest);
            File.WriteAllText(TEMP_DIRECTORY + "app/AndroidManifest.xml", newManifest);

            // Add the modloader, and the modified libmain.so that loads it.
            window.log("Adding library files . . .");
            copyLibraryFile("libmain.so", false);
            copyLibraryFile("libmodloader.so", true);

            // Recompile the modified APK using apktool
            window.log("Compiling modded APK . . .");
            cmd = "b -f -o \"" + TEMP_DIRECTORY + "modded.apk\" \"" + TEMP_DIRECTORY + "app\"";
            await invokeJavaAsync("apktool.jar", cmd);

            // Sign it so that Android will install it
            window.log("Signing the modded APK . . .");
            cmd = "--apks " + TEMP_DIRECTORY + "modded.apk";
            await invokeJavaAsync("uber-apk-signer.jar", cmd);

            File.Move(TEMP_DIRECTORY + "modded-aligned-debugSigned.apk", TEMP_DIRECTORY + "modded-and-signed.apk");

            // Uninstall the original app with ADB first, ADB doesn't have a better way of doing this
            window.log("Uninstalling unmodded app . . .");
            string output = await debugBridge.runCommandAsync("uninstall {app-id}");
            window.log(output);
            
            // Install the modified APK
            window.log("Installing modded app . . .");
            output = await debugBridge.runCommandAsync("install " + TEMP_DIRECTORY + "modded-and-signed.apk");
            window.log(output);

            window.log("Modding complete!");
        }
    }
}
