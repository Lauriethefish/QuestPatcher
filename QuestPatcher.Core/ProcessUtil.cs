using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher
{
    public struct ProcessOutput
    {
        public string StandardOutput { get; set; }
        public string ErrorOutput { get; set; }
    }

    public static class ProcessUtil
    {
        /// <summary>
        /// Invokes an executable fileName with args and captures its standard and error output.
        /// Waits until the process exits before the task completes.
        /// </summary>
        /// <param name="fileName">File name of the application to call</param>
        /// <param name="args">Arguments to pass</param>
        /// <returns>The standard and error output of the process</returns>
        public static async Task<ProcessOutput> InvokeAndCaptureOutput(string fileName, string args)
        {
            Process process = new();

            ProcessStartInfo startInfo = process.StartInfo;
            startInfo.FileName = fileName;
            startInfo.Arguments = args;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            process.EnableRaisingEvents = true;

            // We use two builders and events for receiving data, since waiting on reading the error and standard output at the same time caused a deadlock for whatever reason.
            // If you can find a better method, PRs are open.
            StringBuilder standardOutputBuilder = new();
            StringBuilder errorOutputBuilder = new();

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null) { standardOutputBuilder.AppendLine(args.Data); }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null) { errorOutputBuilder.AppendLine(args.Data); }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            return new ProcessOutput
            {
                StandardOutput = standardOutputBuilder.ToString(),
                ErrorOutput = errorOutputBuilder.ToString()
            };
        }
    }
}
