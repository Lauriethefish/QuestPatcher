using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using System.Globalization;

namespace QuestPatcher
{
    public class DebugBridge
    {
        private static readonly CompareInfo compareInfo = new CultureInfo((int) CultureTypes.AllCultures).CompareInfo;

        public string APP_ID { get; } = File.ReadAllText("appId.txt");

        private string handlePlaceholders(string command)
        {
            command = command.Replace("{app-id}", APP_ID);
            return command;
        }

        public string runCommand(string command)
        {
            return runCommandAsync(command).Result;
        }

        private bool containsIgnoreCase(string str, string lookingFor)
        {
            return compareInfo.IndexOf(str, lookingFor, CompareOptions.IgnoreCase) >= 0;
        }

        public async Task<string> runCommandAsync(string command)
        {
            Process process = new Process();
            process.StartInfo.FileName = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
            process.StartInfo.Arguments = handlePlaceholders(command);
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            await process.WaitForExitAsync();

            if (containsIgnoreCase(output, "error") || containsIgnoreCase(output, "failed"))
            {
                throw new Exception(output);
            }

            return output;
        }
    }
}
