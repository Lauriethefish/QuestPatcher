using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;

namespace QuestPatcher
{
    public class DebugBridge
    {
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

        public async Task<string> runCommandAsync(string command)
        {
            Process process = new Process();
            process.StartInfo.FileName = "adb.exe";
            process.StartInfo.Arguments = handlePlaceholders(command);
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            await process.WaitForExitAsync();

            if(output.Contains("error") || output.Contains("failed"))
            {
                throw new Exception(output);
            }

            return output;
        }
    }
}
