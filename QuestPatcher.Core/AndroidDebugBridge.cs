
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text;

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
        MultipleDevices,
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
    /// Abstraction over using ADB to interact with the Quest.
    /// </summary>
    public class AndroidDebugBridge
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
            "com.weloveoculus.BMBF"
        };

        /// <summary>
        /// Command length limit used for batch commands to avoid errors.
        /// This isn't based on any particular OS, I kept it fairly low so that it works everywhere
        /// </summary>
        private const int CommandLengthLimit = 1024;

        public event EventHandler? StoppedLogging;

        private readonly Logger _logger;
        private readonly ExternalFilesDownloader _filesDownloader;
        private readonly Func<DisconnectionType, Task> _onDisconnect;
        private readonly string _adbExecutableName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";

        private string? _adbPath;
        private Process? _logcatProcess;

        public AndroidDebugBridge(Logger logger, ExternalFilesDownloader filesDownloader, Func<DisconnectionType, Task> onDisconnect)
        {
            _logger = logger;
            _filesDownloader = filesDownloader;
            _onDisconnect = onDisconnect;
        }

        /// <summary>
        /// Checks if ADB is on PATH, and downloads it if not
        /// </summary>
        public async Task PrepareAdbPath()
        {
            try
            {
                await ProcessUtil.InvokeAndCaptureOutput(_adbExecutableName, "-version");
                // If the ADB EXE is already on PATH, we can just use that
                _adbPath = _adbExecutableName;
                _logger.Information("Located ADB install on PATH");
            }
            catch (Win32Exception) // Thrown if the file we attempted to execute does not exist (on mac & linux as well, despite saying Win32)
            {
                // Otherwise, we download the tool and make it executable (only necessary on mac & linux)
                _adbPath = await _filesDownloader.GetFileLocation(ExternalFileType.PlatformTools); // Download ADB if it hasn't been already
            }
        }

        /// <summary>
        /// Runs <code>adb (command)</code> and returns the result.
        /// AdbException is thrown if the return code is non-zero, unless the return code is in allowedExitCodes.
        /// </summary>
        /// <param name="command">The command to execute, without the <code>adb</code> executable name</param>
        /// <returns>The process output from executing the file</returns>
        public async Task<ProcessOutput> RunCommand(string command, params int[] allowedExitCodes)
        {
            if(_adbPath == null)
            {
                await PrepareAdbPath();
            }
            Debug.Assert(_adbPath != null);

            _logger.Debug($"Executing ADB command: adb {command}");
            while (true)
            {
                ProcessOutput output = await ProcessUtil.InvokeAndCaptureOutput(_adbPath, command);
                _logger.Verbose($"Standard output: \"{output.StandardOutput}\"");
                if (output.ErrorOutput.Length > 0)
                {
                    _logger.Verbose($"Error output: \"{output.ErrorOutput}\"");
                }
                _logger.Verbose($"Exit code: {output.ExitCode}");

                // Command execution was a success if the exit code was zero or an allowed exit code
                // -1073740940 is always allowed as some ADB installations return it randomly, even when commands are successful.
                if (output.ExitCode == 0 || allowedExitCodes.Contains(output.ExitCode) || output.ExitCode == -1073740940) { return output; }

                string allOutput = output.StandardOutput + output.ErrorOutput;

                // We repeatedly prompt the user to plug in their quest if it is not plugged in, or the device is offline, or if there are multiple devices
                if (allOutput.Contains("no devices/emulators found"))
                {
                    await _onDisconnect(DisconnectionType.NoDevice);
                }
                else if(allOutput.Contains("device offline"))
                {
                    await _onDisconnect(DisconnectionType.DeviceOffline);
                }   else if(allOutput.Contains("multiple devices") || output.ErrorOutput.Contains("more than one device/emulator"))
                {
                    await _onDisconnect(DisconnectionType.MultipleDevices);
                }   else if(allOutput.Contains("unauthorized"))
                {
                    await _onDisconnect(DisconnectionType.Unauthorized);
                }
                else
                {
                    // Throw an exception as ADB gave a non-zero exit code so the command must've failed
                    throw new AdbException(allOutput);
                }
            }
        }

        public async Task DownloadFile(string name, string destination)
        {
            await RunCommand($"pull \"{FixPath(name)}\" \"{destination}\"");
        }

        public async Task UploadFile(string name, string destination)
        {
            await RunCommand($"push \"{name}\" \"{FixPath(destination)}\"");
        }

        /// <summary>
        /// ADB does not like backward slashes in upload/download paths.
        /// This fixes the paths to not contain these slashes.
        /// The backslashes are added by Path.Combine on the windows side
        /// </summary>
        /// <param name="path">The path to fix</param>
        /// <returns>The fixed path</returns>
        private static string FixPath(string path)
        {
            return path.Replace('\\', '/');
        }

        public async Task DownloadApk(string packageId, string destination)
        {
            // Pull the path of the app from the Android package manager, then remove the formatting that ADB adds
            string rawAppPath = (await RunCommand($"shell pm path {packageId}")).StandardOutput;
            string appPath = rawAppPath.Remove(0, 8).Replace("\n", "").Replace("'", "").Replace("\r", "");

            await DownloadFile(appPath, destination);
        }

        public async Task UninstallApp(string packageId)
        {
            await RunCommand($"uninstall {packageId}");
        }

        public async Task<bool> IsPackageInstalled(string packageId)
        {
            string result = (await RunCommand($"shell pm list packages {packageId}")).StandardOutput; // List packages with the specified ID
            return result.Contains(packageId); // The result is "package:packageId", so we check if the packageId is within that result. If it isn't the result will be empty, so this will return false
        }

        public async Task<List<string>> ListPackages()
        {
            string output = (await RunCommand($"shell pm list packages")).StandardOutput;
            List<string> result = new();
            foreach(string package in output.Split("\n"))
            {
                string trimmed = package.Trim();
                if(trimmed.Length == 0) { continue; }
                result.Add(trimmed[8..]); // Remove the "package:" from the package ID
            }

            return result;
        }

        public async Task<List<string>> ListNonDefaultPackages()
        {
            return (await ListPackages()).Where(packageId => !DefaultPackagePrefixes.Any(packageId.StartsWith)).ToList();
        }

        public async Task InstallApp(string apkPath)
        {
            await RunCommand($"install \"{apkPath}\"");
        }

        public async Task CreateDirectory(string path)
        {
            await RunCommand($"shell \"mkdir -p '{FixPath(path)}'\"");
        }

        public async Task RemoveFile(string path)
        {
            await RunCommand($"shell \"rm '{FixPath(path)}'\"");
        }

        public async Task RemoveDirectory(string path)
        {
            await RunCommand($"shell \"rm -r '{FixPath(path)}'\"");
        }

        public async Task CopyFile(string path, string destination)
        {
            await RunCommand($"shell \"cp '{FixPath(path)}' '{FixPath(destination)}'\"");
        }

        /// <summary>
        /// Executes the shell commands given using one ADB shell call, or multiple calls if there are too many for one call.
        /// NOTE: The commands should use single ('here') quotes on arguments, since double quotes are used around the command.
        /// </summary>
        /// <param name="commands">The commands to execute</param>
        public async Task RunShellCommands(List<string> commands)
        {
            if(commands.Count == 0) { return; } // Return blank output if no commands to avoid errors

            const string initialCommmand = "shell \"";
            List<string> awaitingRun = new();
            StringBuilder result = new(initialCommmand);
            for(int i = 0; i < commands.Count; i++)
            {
                result.Append(commands[i]); // Add the next command
                // If the current batch command + the next command will be greater than our command length limit (or we're at the last command), we stop the current batch command and add the result to the list
                if ((commands.Count - i >= 2 && result.Length + commands[i + 1].Length + 4 >= CommandLengthLimit) || i == commands.Count - 1)
                {
                    result.Append('\"');
                    awaitingRun.Add(result.ToString());
                    result = new StringBuilder(initialCommmand);
                    continue;
                }

                // Otherwise, add an && for the next command
                result.Append(" && ");
            }

            // Run all the queued commands
            foreach(string command in awaitingRun)
            {
                await RunCommand(command);
            }
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

            foreach(KeyValuePair<string, string> path in paths)
            {
                commands.Add($"cp '{FixPath(path.Key)}' '{FixPath(path.Value)}'");
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
            foreach(string path in paths)
            {
                commands.Add($"mkdir -p '{FixPath(path)}'");
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
                commands.Add($"rm '{FixPath(path)}'");
            }

            await RunShellCommands(commands);
        }

        public async Task ExtractArchive(string path, string outputFolder)
        {
            await CreateDirectory(outputFolder);
            await RunCommand($"shell unzip \"{path}\" -o -d \"{outputFolder}\"");
        }

        public async Task<List<string>> ListDirectoryFiles(string path, bool onlyFileName = false)
        {
            path = FixPath(path);
            ProcessOutput output = await RunCommand($"shell \"ls -p \\\"{path}\\\" | grep -v /\"", 1);
            string filesNonSplit = output.StandardOutput;
            
            // Exit code 1 is only allowed if it is returned with no files, as this is what the LS command returns
            if (filesNonSplit.Trim().Length != 0 && output.ExitCode != 0)
            {
                throw new AdbException(output.AllOutput);
            }
            
            return ParsePaths(filesNonSplit, path, onlyFileName);
        }

        public async Task<List<string>> ListDirectoryFolders(string path, bool onlyFolderName = false)
        {
            path = FixPath(path);
            ProcessOutput output = await RunCommand($"shell \"ls -p \\\"{path}\\\" | grep /\"", 1);
            string foldersNonSplit = output.StandardOutput;

            // Exit code 1 is only allowed if it is returned with no folders, as this is what the LS command returns
            if (foldersNonSplit.Trim().Length != 0 && output.ExitCode != 0)
            {
                throw new AdbException(output.AllOutput);
            }
            
            return ParsePaths(foldersNonSplit, path, onlyFolderName);
        }

        private static List<string> ParsePaths(string str, string path, bool onlyNames)
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

            // We can't just use RunCommand, that would be very inefficient as we'd store the whole log in memory before saving
            // Instead, we redirect the standard output to the file as it is written
            _logcatProcess = new Process();
            ProcessStartInfo startInfo = _logcatProcess.StartInfo;
            startInfo.FileName = _adbPath;
            startInfo.Arguments = "logcat";
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
                    _logger.Debug("ADB attempted to send data after it was closed");
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

        public async Task KillServer()
        {
            await RunCommand($"kill-server");
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
            if(dirName is null)
            {
                throw new InvalidOperationException("Attempted to find if a file without a directory name exists");
            }

            List<string> directoryFiles = await ListDirectoryFiles(dirName, true);
            return directoryFiles.Contains(Path.GetFileName(path));
        }
    }
}
