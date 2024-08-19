using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Represents the result of running a process.
    /// </summary>
    public struct ProcessOutput
    {
        /// <summary>
        /// All lines the process wrote to stdout
        /// </summary>
        public string StandardOutput { get; set; }

        /// <summary>
        /// All lines the process wrote to stderr
        /// </summary>
        public string ErrorOutput { get; set; }

        /// <summary>
        /// Returns the error output added to the end of the standard output.
        /// </summary>
        public string AllOutput => StandardOutput + ErrorOutput;

        /// <summary>
        /// The exit code of the process
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// The full path to the executable that was invoked.
        /// Useful if running an executable on the PATH environnment variable.
        /// May be null if this information was unavailable.
        /// </summary>
        public string? FullPath { get; set; }
    }

    /// <summary>
    /// A utility class for invoking processes and capturing their output.
    /// Automatically kills all running processes when the instance is disposed.
    /// This class is thread safe.
    /// </summary>
    public class ProcessUtil : IDisposable
    {
        private readonly HashSet<Process> _runningProcesses = new();
        private readonly object _processesLock = new object();
        private bool _disposed = false;
        
        /// <summary>
        /// Invokes an executable fileName with args and captures its standard and error output.
        /// Waits until the process exits before the task completes.
        /// </summary>
        /// <param name="fileName">File name of the application to call</param>
        /// <param name="arguments">Arguments to pass</param>
        /// <returns>The standard and error output of the process</returns>
        public async Task<ProcessOutput> InvokeAndCaptureOutput(string fileName, string arguments)
        {
            using Process process = new();

            var startInfo = process.StartInfo;
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            process.EnableRaisingEvents = true;

            // We use two builders and events for receiving data, since waiting on reading the error and standard output at the same time caused a deadlock for whatever reason.
            // If you can find a better method, PRs are open.
            StringBuilder standardOutputBuilder = new();
            StringBuilder errorOutputBuilder = new();

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null) { standardOutputBuilder.AppendLine(args.Data); }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null) { errorOutputBuilder.AppendLine(args.Data); }
            };

            process.Start();
            lock (_processesLock)
            {
                _runningProcesses.Add(process);
            }

            string? fullPath = null;
            try
            {
                fullPath = process.MainModule?.FileName;
            }
            catch (Exception ex)
            {
                if (ex is Win32Exception)
                {
                    Log.Warning(ex, "Failed to get full path to running ADB client. AntiVirus might be blocking this.");
                } else if(ex is InvalidOperationException)
                {
                    Log.Debug("ADB process exited too early to get full path");
                }
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            lock (_processesLock)
            {
                _runningProcesses.Remove(process);
            }

            if (_disposed)
            {
                throw new OperationCanceledException("QuestPatcher closing, process prematurely ended");
            }

            return new ProcessOutput
            {
                StandardOutput = standardOutputBuilder.ToString(),
                ErrorOutput = errorOutputBuilder.ToString(),
                ExitCode = process.ExitCode,
                FullPath = fullPath
            };
        }

        public void Dispose()
        {
            if(_disposed)
            {
                return;
            }
            _disposed = true;

            lock(_processesLock)
            {
                if(_runningProcesses.Count > 0)
                {
                    Log.Information("Killing {NumActiveProcesses} active processes", _runningProcesses.Count);
                }

                foreach (var process in _runningProcesses)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to kill process PID {ProcessId} upon exit", process.Id);
                    }
                }
            }
        }
    }
}
