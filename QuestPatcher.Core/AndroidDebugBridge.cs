
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using Version = SemanticVersioning.Version;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Thrown whenever the standard or error output of ADB contains "failed" or "error"
    /// </summary>
    public class AdbException : Exception
    {
        public AdbException(string message) : base(message) { }
    }

    public enum DisconnectionType
    {
        NoDevice,
        DeviceOffline,
        Unauthorized
    }

    public static class ContainsExtensions
    {
        public static bool ContainsIgnoreCase(this string str, string other)
        {
            return str.IndexOf(other, 0, StringComparison.CurrentCultureIgnoreCase) != -1;
        }
    }

    /// <summary>
    /// A particular android debug bridge device.
    /// </summary>
    public class AdbDevice
    {
        /// <summary>
        /// The device ID.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The device model.
        /// </summary>
        public string Model { get; set; }

        public AdbDevice(string id, string model)
        {
            Id = id;
            Model = model;
        }
    }

    /// <summary>
    /// Abstraction over using ADB to interact with the Quest.
    /// </summary>
    public class AndroidDebugBridge : IDisposable
    {
        /// <summary>
        /// Package names that will not be included in the apps to patch list
        /// </summary>
        private static readonly string[] DefaultPackagePrefixes =
        {
            "com.oculus",
            "com.android",
            "android",
            "com.qualcomm",
            "com.facebook",
            "oculus",
            "com.weloveoculus.BMBF",
            "com.meta",
            "com.whatsapp"
        };

        /// <summary>
        /// Command length limit used for batch commands to avoid errors.
        /// This isn't based on any particular OS, I kept it fairly low so that it works everywhere
        /// </summary>
        private const int CommandLengthLimit = 1024;

        /// <summary>
        /// The minimum ADB version required by QuestPatcher.
        /// </summary>
        private static readonly Version MinAdbVersion = new(1, 0, 39);

        public event EventHandler? StoppedLogging;

        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly IUserPrompter _prompter;
        private readonly string _adbExecutableName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        private readonly Action _quit;
        private readonly ProcessUtil _processUtil = new();

        /// <summary>
        /// The minimum time between checks for the currently open ADB daemon.
        /// QP will automatically switch to a different ADB install if a daemon is started with that install. (and the install is new enough to work with QP.)
        /// This allows QP to avoid killing an ADB daemon another app is using. (which would happen if it tried to use its own install and the versions were different.)
        /// </summary>
        private static readonly TimeSpan DaemonCheckInterval = TimeSpan.FromSeconds(5.0);

        private string? _adbPath;
        private DateTime _lastDaemonCheck; // The last time at which QP checked for existing ADB daemons
        private Process? _logcatProcess;

        private string? _selectedDevice;

        public AndroidDebugBridge(ExternalFilesDownloader filesDownloader, IUserPrompter prompter, Action quit)
        {
            _filesDownloader = filesDownloader;
            _prompter = prompter;
            _quit = quit;
        }

        /// <summary>
        /// Checks if a valid ADB installation is found on PATH or in an installation of SideQuest.
        /// Using an ADB installation from SideQuest helps avoid the issue where QuestPatcher and SideQuest
        /// keep trying to kill each other's ADB server, resulting in neither working properly.
        /// ADB executables for daemons already running will also be prioritised.
        /// </summary>
        public async Task PrepareAdbPath()
        {
            // Use existing ADB daemon if there is one of the correct version
            if (await FindExistingAdbServer())
            {
                return;
            }

            // Next check PATH
            Log.Debug("Checking installation on PATH");
            if (await SetAdbPathIfValid(_adbExecutableName))
            {
                Log.Information("Using ADB installation on PATH");
                return;
            }

            // Otherwise, download ADB
            string downloadedAdb = await _filesDownloader.GetFileLocation(ExternalFileType.PlatformTools);
            if (!await SetAdbPathIfValid(downloadedAdb))
            {
                // Redownloading ADB - existing installation was not valid
                Log.Information("Existing downloaded ADB was out of date or corrupted - fetching again");
                await _processUtil.InvokeAndCaptureOutput(downloadedAdb, "kill-server"); // Kill server first, otherwise directory will be in use, so can't be deleted.
                _adbPath = await _filesDownloader.GetFileLocation(ExternalFileType.PlatformTools, true);
            }
            else
            {
                Log.Information("Using downloaded ADB");
            }
        }

        /// <summary>
        /// Checks if the ADB executable at the given path exists and is up-to-date.
        /// If it is, then it will be set as the ADB path for the instance.
        /// </summary>
        /// <param name="adbExecutablePath">The relative or absolute path of the ADB executable.</param>
        /// <returns>True if and only if the ADB installation is present and up-to-date</returns>
        private async Task<bool> SetAdbPathIfValid(string adbExecutablePath)
        {
            const string VersionPrefix = "Android Debug Bridge version";

            try
            {
                Log.Verbose("Checking if ADB at {AdbPath} is present and up-to-date", adbExecutablePath);
                var outputInfo = await _processUtil.InvokeAndCaptureOutput(adbExecutablePath, "version");
                string output = outputInfo.AllOutput;

                Log.Debug("Output from checking ADB version: {VerisonOutput}", output);

                int prefixPos = output.IndexOf(VersionPrefix);
                if (prefixPos == -1)
                {
                    Log.Verbose("No version code could be found in the output. ADB executable is NOT valid");
                    return false;
                }

                int versionPos = prefixPos + VersionPrefix.Length;
                int nextNewline = output.IndexOf('\n', versionPos);

                string version;
                if (nextNewline == -1)
                {
                    version = output.Substring(versionPos).Trim();
                }
                else
                {
                    int versionLen = nextNewline - versionPos;
                    version = output.Substring(versionPos, versionLen).Trim();
                }

                Log.Debug($"Parsed ADB version as {version}");
                if (Version.TryParse(version, out var semver))
                {
                    if (semver >= MinAdbVersion)
                    {
                        _adbPath = outputInfo.FullPath ?? adbExecutablePath;
                        return true;
                    }
                }
                else
                {
                    Log.Debug("ADB version was not valid semver, assuming out of date");
                }

                return false;
            }
            catch (Win32Exception)
            {
                return false; // Executable not present
            }
        }

        /// <summary>
        /// Lists the devices connected to ADB.
        /// </summary>
        /// <returns>A list of the ADB devices.</returns>
        private async Task<List<AdbDevice>> ListDevices()
        {
            if (_adbPath == null)
            {
                await PrepareAdbPath();
            }

            var output = await _processUtil.InvokeAndCaptureOutput(_adbPath!, "devices -l");
            Log.Debug("Listing devices output {Output}", output.AllOutput);

            string[] lines = output.StandardOutput.Trim().Split('\n');

            var devices = new List<AdbDevice>();
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];

                int endIdIdx = line.IndexOf(' ');
                if (endIdIdx == -1)
                {
                    continue;
                }

                string id = line.Substring(0, endIdIdx);
                int modelIdx = line.IndexOf("model:");
                if (modelIdx == -1)
                {
                    continue;
                }

                int endModelIdx = line.IndexOf(' ', modelIdx);
                if (endModelIdx == -1)
                {
                    continue;
                }

                string model = line.Substring(modelIdx + 6, endModelIdx - modelIdx - 6);

                devices.Add(new AdbDevice(id, model));
            }

            return devices;
        }

        /// <returns>The device ID of one of the Quest devices connected, or a non-quest device if no quest is present.</returns>
        private async Task<List<AdbDevice>> GetDevicesInPreferredOrder()
        {
            return (await ListDevices()).OrderBy(device =>
                device.Id.Contains("quest", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ToList();
        }

        private async Task<bool> FindExistingAdbServer()
        {
            _lastDaemonCheck = DateTime.UtcNow;

            Log.Debug("Checking for existing daemon");
            foreach (string adbPath in FindRunningAdbPath())
            {
                Log.Debug("Found existing ADB daemon. Checking if it's valid for us to use");
                if (await SetAdbPathIfValid(adbPath))
                {
                    Log.Information("Using ADB from existing daemon at path {AdbPath}", adbPath);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Finds the full path to any ADB server currently running.
        /// </summary>
        /// <returns>A list of the full paths to all running ADB servers.</returns>
        private IEnumerable<string> FindRunningAdbPath()
        {
            return Process.GetProcessesByName("adb") // No .exe, process name is without the extension
                .Select(process =>
                {
                    try
                    {
                        return process.MainModule?.FileName;
                    }
                    catch (Win32Exception ex)
                    {
                        Log.Warning(ex, "Could not check process filename");
                        return null;
                    }
                })
                .Where(fullPath => fullPath != null && fullPath != _adbPath &&
                    Path.GetFileName(fullPath).Equals(_adbExecutableName, StringComparison.OrdinalIgnoreCase))! /* fullPath definitely not null */;
        }

        /// <summary>
        /// Determines the device to use. Allows the user to choose a device if multiple are connected.
        /// </summary>
        /// <returns></returns>
        private async Task<string> GetAdbDeviceId()
        {
            if (_selectedDevice != null)
            {
                return _selectedDevice;
            }

            // List devices until at least one is found, or the user gives up
            List<AdbDevice>? devices = null;
            do
            {
                // Wait until the user is ready if this is an attempt after the first.
                if (devices != null && !await _prompter.PromptAdbDisconnect(DisconnectionType.NoDevice))
                {
                    _quit();
                    return "quitting";
                }

                devices = await GetDevicesInPreferredOrder();
            } while (devices.Count == 0);


            if (devices.Count == 1)
            {
                // Only one device, just use that one
                _selectedDevice = devices[0].Id;
                return devices[0].Id;
            }
            else
            {
                // Allow the user to select a device if multiple are connected.
                var device = await _prompter.PromptSelectDevice(devices);
                if (device == null)
                {
                    _quit();
                    return "<no device selected, quitting>";
                }
                else
                {
                    Log.Verbose("Using device {DeviceId}", device.Id);
                    _selectedDevice = device.Id;
                    return device.Id;
                }
            }
        }

        /// <summary>
        /// Runs <code>adb (command)</code> and returns the result.
        /// AdbException is thrown if the return code is non-zero, unless the return code is in allowedExitCodes.
        /// </summary>
        /// <param name="command">The command to execute, without the <code>adb</code> executable name</param>
        /// <param name="allowedExitCodes">Non-zero exit codes that will be ignored and will not produce an <see cref="AdbException"/></param>
        /// <exception cref="AdbException">If a non-zero exit code is returned by ADB that is not within <paramref name="allowedExitCodes"/></exception>
        /// <returns>The process output from executing the file</returns>
        public async Task<ProcessOutput> RunCommand(string command, params int[] allowedExitCodes)
        {
            if (_adbPath == null)
            {
                await PrepareAdbPath();
            }
            else
            {
                var now = DateTime.UtcNow;
                if ((now - _lastDaemonCheck) > DaemonCheckInterval)
                {
                    await FindExistingAdbServer();
                }
            }
            Debug.Assert(_adbPath != null);

            // Allow the user to select a device if multiple are present.
            string chosenDeviceId = await GetAdbDeviceId();
            Log.Debug("Executing ADB command: {Command}", $"adb {command}");
            while (true)
            {
                var output = await _processUtil.InvokeAndCaptureOutput(_adbPath, $"-s {chosenDeviceId} " + command);
                if (output.StandardOutput.Length > 0)
                {
                    Log.Verbose("Standard output: {StandardOutput}", output.StandardOutput);
                }
                if (output.ErrorOutput.Length > 0)
                {
                    Log.Verbose("Error output: {ErrorOutput}", output.ErrorOutput);
                }
                if (output.ExitCode != 0)
                {
                    Log.Verbose("Exit code: {ExitCode}", output.ExitCode);
                }

                // Command execution was a success if the exit code was zero or an allowed exit code
                // -1073740940 is always allowed as some ADB installations return it randomly, even when commands are successful.
                if (output.ExitCode == 0 || allowedExitCodes.Contains(output.ExitCode) || output.ExitCode == -1073740940) { return output; }

                string allOutput = (output.StandardOutput + output.ErrorOutput).Trim();

                // We repeatedly prompt the user to plug in their quest if it is not plugged in, or the device is offline
                if (allOutput.Contains("device offline"))
                {
                    if (!await _prompter.PromptAdbDisconnect(DisconnectionType.DeviceOffline)) _quit();
                }
                else if (allOutput.Contains("unauthorized"))
                {
                    if (!await _prompter.PromptAdbDisconnect(DisconnectionType.Unauthorized)) _quit();
                }
                else if (allOutput.Contains("not found") && allOutput.Contains(chosenDeviceId))
                {
                    // Device with selected ID no longer exists.
                    Log.Warning("Selected device no longer found. Choosing a new device");
                    _selectedDevice = null;
                    chosenDeviceId = await GetAdbDeviceId(); // Find a new device to use.
                }
                else
                {
                    // Throw an exception as ADB gave a non-zero exit code so the command must've failed
                    // Add the exit code to the error message for debugging purposes.
                    throw new AdbException($"Code {output.ExitCode}: {allOutput}");
                }
            }
        }

        /// <summary>
        /// Executes the shell commands given using one ADB shell call, or multiple calls if there are too many for one call.
        /// </summary>
        /// <param name="commands">The commands to execute</param>
        public async Task RunShellCommands(List<string> commands)
        {
            if (commands.Count == 0) { return; } // Return blank output if no commands to avoid errors

            var currentCommand = new StringBuilder();
            for (int i = 0; i < commands.Count; i++)
            {
                currentCommand.Append(commands[i]); // Add the next command
                // If the current batch command + the next command will be greater than our command length limit (or we're at the last command), we stop the current batch command and add the result to the list
                if ((commands.Count - i >= 2 && currentCommand.Length + commands[i + 1].Length + 4 >= CommandLengthLimit) || i == commands.Count - 1)
                {
                    await RunShellCommand(currentCommand.ToString());
                    currentCommand.Clear();
                }
                else
                {
                    // Otherwise, add an && for the next command
                    currentCommand.Append(" && ");
                }
            }
        }

        public async Task<ProcessOutput> RunShellCommand(string command, params int[] allowedExitCodes)
        {
            return await RunCommand($"shell {command.EscapeProc()}", allowedExitCodes);
        }

        public async Task DownloadFile(string name, string destination)
        {
            await RunCommand($"pull {name.WithForwardSlashes().EscapeProc()} {destination.EscapeProc()}");
        }

        public async Task UploadFile(string name, string destination)
        {
            await RunCommand($"push {name.EscapeProc()} {destination.WithForwardSlashes().EscapeProc()}");
        }

        public async Task DownloadApk(string packageId, string destination)
        {
            // Pull the path of the app from the Android package manager, then remove the formatting that ADB adds
            string rawAppPath = (await RunShellCommand($"pm path {packageId}")).StandardOutput;
            string appPath = rawAppPath.Remove(0, 8).Replace("\n", "").Replace("'", "").Replace("\r", "");

            await DownloadFile(appPath, destination);
        }

        /// <summary>
        /// Uninstalls the app with package ID <paramref name="packageId"/>, if an app with this ID exists.
        /// </summary>
        /// <param name="packageId">The package ID of the app to uninstall.</param>
        /// <returns>True if the app existed and was uninstalled, false otherwise.</returns>
        public async Task<bool> UninstallApp(string packageId)
        {
            // Allow exit code 1 as this is returned in the case of the app not being installed in the first place.
            var output = await RunCommand($"uninstall {packageId}", 1);
            if(output.ExitCode == 1)
            {
                // Check if the failure was due to the app being uninstalled already (this causes DELETE_FAILED_INTERNAL_ERROR, which is terrible design)
                if (output.AllOutput.Contains("[DELETE_FAILED_INTERNAL_ERROR]"))
                {
                     return false;
                }
                else
                {
                    throw new AdbException("Received exit code 1 when uninstalling and was not because app was already uninstalled: "
                        + output.AllOutput);
                }
            }
            else
            {
                return true;
            }
        }

        public async Task<bool> IsPackageInstalled(string packageId)
        {
            string result = (await RunShellCommand($"pm list packages {packageId}")).StandardOutput; // List packages with the specified ID
            return result.Contains(packageId); // The result is "package:packageId", so we check if the packageId is within that result. If it isn't the result will be empty, so this will return false
        }

        public async Task<List<string>> ListPackages()
        {
            string output = (await RunShellCommand("pm list packages")).StandardOutput;
            List<string> result = new();
            foreach (string package in output.Split("\n"))
            {
                string trimmed = package.Trim();
                if (trimmed.Length == 0) { continue; }
                result.Add(trimmed[8..]); // Remove the "package:" from the package ID
            }

            return result;
        }

        public async Task<List<string>> ListNonDefaultPackages()
        {
            return (await ListPackages()).Where(packageId => !DefaultPackagePrefixes.Any(packageId.StartsWith)).ToList();
        }

        /// <summary>
        /// Kills all activities relating to an app.
        /// </summary>
        /// <param name="appId">The app to force-stop</param>
        public async Task ForceStop(string appId)
        {
            await RunShellCommand($"am force-stop {appId}");
        }

        /// <summary>
        /// Starts the given app by running the unity player activity.
        /// </summary>
        /// <param name="appId">The app to start</param>
        public async Task RunUnityPlayerActivity(string appId)
        {
            await RunShellCommand($"am start {appId}/com.unity3d.player.UnityPlayerActivity");
        }

        public async Task InstallApp(string apkPath)
        {
            string pushPath = $"/data/local/tmp/{Guid.NewGuid()}.apk";

            await RunCommand($"push {apkPath.EscapeProc()} {pushPath}");
            await RunShellCommand($"pm install {pushPath}");
            await RunShellCommand($"rm {pushPath}");
        }

        public async Task<bool> Exists(string path)
        {
            var output = await RunShellCommand($"stat {path.WithForwardSlashes().EscapeBash()}");

            return output.ExitCode == 0;
        }

        public async Task CreateDirectory(string path)
        {
            await RunShellCommand($"mkdir -p {path.WithForwardSlashes().EscapeBash()}");
        }

        public async Task Move(string from, string to)
        {
            await RunShellCommand(
                $"mv {from.WithForwardSlashes().EscapeBash()} {to.WithForwardSlashes().EscapeBash()}");
        }

        public async Task DeleteFile(string path)
        {
            await RunShellCommand($"rm -f {path.WithForwardSlashes().EscapeBash()}");
        }

        public async Task RemoveDirectory(string path)
        {
            await RunShellCommand($"rm -rf {path.WithForwardSlashes().EscapeBash()}");
        }

        public async Task CopyFile(string path, string destination)
        {
            await RunShellCommand(
                $"cp {path.WithForwardSlashes().EscapeBash()} {destination.WithForwardSlashes().EscapeBash()}");
        }

        /// <summary>
        /// Copies multiple files all at once using && and one single adb shell call.
        /// This makes copying files much faster, but lumps all of the errors together into one, i.e. if one file fails they all fail.
        /// For mod installs, this is fine, because the existence of the files copied by the mod is verified way earlier when it is loaded
        /// </summary>
        /// <param name="paths">The paths to copy. Key is from, Value is to</param>
        public async Task CopyFiles(List<KeyValuePair<string, string>> paths)
        {
            List<string> commands = new();

            foreach (var path in paths)
            {
                commands.Add($"cp {path.Key.WithForwardSlashes().EscapeBash()} {path.Value.WithForwardSlashes().EscapeBash()}");
            }

            await RunShellCommands(commands);
        }

        /// <summary>
        /// Creates multiple directories using one ADB command.
        /// Faster for quickly creating numbers of directories.
        /// </summary>
        /// <param name="paths">Paths of the directories to create</param>
        public async Task CreateDirectories(List<string> paths)
        {
            List<string> commands = new();
            foreach (string path in paths)
            {
                commands.Add($"mkdir -p {path.WithForwardSlashes().EscapeBash()}");
            }

            await RunShellCommands(commands);
        }

        /// <summary>
        /// Runs chmod on the given paths.
        /// </summary>
        /// <param name="paths">Paths to chmod</param>
        /// <param name="permissions">The permissions to assign to each file</param>
        public async Task Chmod(List<string> paths, string permissions)
        {
            List<string> commands = new();
            foreach (string path in paths)
            {
                Log.Verbose("Ran Chmod on {Path} with {Permissions}", path, permissions);
                commands.Add($"chmod {permissions} {path.WithForwardSlashes().EscapeBash()}");
            }

            await RunShellCommands(commands);
        }

        /// <summary>
        /// Deletes multiple files in one ADB command.
        /// Faster for quickly removing lots of files.
        /// </summary>
        /// <param name="paths">Paths of the files to delete</param>
        /// <returns></returns>
        public async Task DeleteFiles(List<string> paths)
        {
            List<string> commands = new();
            foreach (string path in paths)
            {
                commands.Add($"rm -f {path.WithForwardSlashes().EscapeBash()}");
            }

            await RunShellCommands(commands);
        }

        public async Task ExtractArchive(string path, string outputFolder)
        {
            await CreateDirectory(outputFolder);
            await RunShellCommand($"unzip {path.WithForwardSlashes().EscapeBash()} -o -d {outputFolder.WithForwardSlashes().EscapeBash()}");
        }

        public async Task<List<string>> ListDirectoryFiles(string path, bool onlyFileName = false)
        {
            var output = await RunShellCommand($"ls -p {path.WithForwardSlashes().EscapeBash()}", 1);
            string filesNonSplit = output.StandardOutput;

            // Exit code 1 is only allowed if it is returned with no files, as this is what the LS command returns
            if (filesNonSplit.Trim().Length != 0 && output.ExitCode != 0)
            {
                throw new AdbException(output.AllOutput);
            }

            return ParsePaths(filesNonSplit, path, onlyFileName, false);
        }

        public async Task<List<string>> ListDirectoryFolders(string path, bool onlyFolderName = false)
        {
            var output = await RunShellCommand($"ls -p {path.WithForwardSlashes().EscapeBash()}", 1);
            string foldersNonSplit = output.StandardOutput;

            // Exit code 1 is only allowed if it is returned with no folders, as this is what the LS command returns
            if (foldersNonSplit.Trim().Length != 0 && output.ExitCode != 0)
            {
                throw new AdbException(output.AllOutput);
            }

            return ParsePaths(foldersNonSplit, path, onlyFolderName, true);
        }

        public async Task KillServer()
        {
            await RunCommand("kill-server");
        }

        private static List<string> ParsePaths(string str, string path, bool onlyNames, bool directories)
        {
            // Remove unnecessary padding that ADB adds to get purely the paths
            string[] rawPaths = str.Split("\n");
            List<string> parsedPaths = new();
            for (int i = 0; i < rawPaths.Length - 1; i++)
            {
                string currentPath = rawPaths[i].Replace("\r", "");
                if (currentPath[^1] == ':') // Directories within this one that aren't the first index lead to this
                {
                    break;
                }

                // The directory listing passed to this method should be that from "ls -p"
                // This means that directories will end with a / and files will never end with a /
                if (currentPath.EndsWith("/"))
                {
                    // If only looking for files, and our path ends with a /, it must be a folder, so we skip it
                    if (!directories)
                    {
                        continue;
                    }
                }
                else
                {
                    // If only looking for directories, and our path doesn't end with a /, it must be a file, so we skip it
                    if (directories)
                    {
                        continue;
                    }
                }

                if (onlyNames)
                {
                    parsedPaths.Add(currentPath);
                }
                else
                {
                    parsedPaths.Add(Path.Combine(path, currentPath));
                }
            }

            return parsedPaths;
        }

        /// <summary>
        /// Starts an ADB log, saved to logFile as the logs are received
        /// </summary>
        /// <param name="logFile">The file to save the log to. Will be overwritten if it exists</param>
        public async Task StartLogging(string logFile)
        {
            if (_adbPath == null)
            {
                await PrepareAdbPath();
            }
            Debug.Assert(_adbPath != null);

            TextWriter outputWriter = new StreamWriter(File.OpenWrite(logFile));

            string chosenDevice = await GetAdbDeviceId();

            // We can't just use RunCommand, that would be very inefficient as we'd store the whole log in memory before saving
            // Instead, we redirect the standard output to the file as it is written
            _logcatProcess = new Process();
            var startInfo = _logcatProcess.StartInfo;
            startInfo.FileName = _adbPath;
            startInfo.Arguments = $"-s {chosenDevice} logcat";
            startInfo.RedirectStandardOutput = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            _logcatProcess.EnableRaisingEvents = true;

            _logcatProcess.OutputDataReceived += (_, args) =>
            {
                // Sometimes ADB attempts to send data after the process exists for whatever reason, so we need to handle that
                try
                {
                    if (args.Data != null) { outputWriter.WriteLine(args.Data); }
                }
                catch (ObjectDisposedException)
                {
                    Log.Debug("ADB attempted to send data after it was closed");
                }
            };

            _logcatProcess.Start();
            _logcatProcess.BeginOutputReadLine();

            _logcatProcess.Exited += (_, args) =>
            {
                outputWriter.Close();
                StoppedLogging?.Invoke(this, args); // Used to tell the UI to change back to normal instead of "Stop ADB log"
            };
        }

        /// <summary>
        /// Stops the currently running logcat, if there is one
        /// </summary>
        public void StopLogging()
        {
            _logcatProcess?.Kill();
        }

        /// <summary>
        /// Finds if a file with the given path exists
        /// </summary>
        /// <param name="path">File to find if exists</param>
        /// <returns>True if the file exists, false otherwise</returns>
        /// <exception cref="InvalidOperationException">If the given path did not contain a directory name</exception>
        public async Task<bool> FileExists(string path)
        {
            string? dirName = Path.GetDirectoryName(path);
            if (dirName is null)
            {
                throw new InvalidOperationException("Attempted to find if a file without a directory name exists");
            }

            var directoryFiles = await ListDirectoryFiles(dirName, true);
            return directoryFiles.Contains(Path.GetFileName(path));
        }

        public void Dispose()
        {
            _logcatProcess?.Dispose();
            _processUtil.Dispose(); // Ensure all ADB processes are killed.
        }
    }
}
