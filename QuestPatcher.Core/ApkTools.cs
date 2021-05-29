using Serilog.Core;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Abstraction around APKtool and uber-apk-signer used during patching.
    /// </summary>
    public class ApkTools
    {
        private readonly Logger _logger;
        private readonly ExternalFilesDownloader _filesDownloader;
        private string _javaExecutableName;

        public ApkTools(Logger logger, ExternalFilesDownloader filesDownloader)
        {
            _logger = logger;
            _filesDownloader = filesDownloader;
            _javaExecutableName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        }

        private Task<ProcessOutput> InvokeJava(string args)
        {
            return ProcessUtil.InvokeAndCaptureOutput(_javaExecutableName, args);
        }

        private async Task<ProcessOutput> InvokeJavaTool(ExternalFileType type, string args)
        {
            string path = await _filesDownloader.GetFileLocation(type); // Downloads the file if it hasn't been already

            return await InvokeJava($"-jar {path} {args}");
        }

        public async Task<bool> PrepareJavaInstall()
        {
            try
            {
                string javaVersion = (await InvokeJava("-version")).ErrorOutput;
                _logger.Information("Located Java install on PATH");
                _logger.Debug($"Java version: {javaVersion}");
                return true;
            }
            catch (Win32Exception) // Thrown if the executable is missing, even on Linux. Slight .NET quirk.
            {
                // Download Java if it is not installed
                _javaExecutableName = await _filesDownloader.GetFileLocation(ExternalFileType.Jre);
                return false;
            }
        }

        public Task DecompileApk(string apkPath, string extractPath)
        {
            return InvokeJavaTool(ExternalFileType.ApkTool, $"d -f -o \"{extractPath}\" \"{apkPath}\"");
        }

        public Task CompileApk(string extractPath, string resultPath)
        {
            return InvokeJavaTool(ExternalFileType.ApkTool, $"b -f -o \"{resultPath}\" \"{extractPath}\"");
        }

        public Task SignApk(string apkPath)
        {
            return InvokeJavaTool(ExternalFileType.UberApkSigner, $"--apks \"{apkPath}\"");
        }
    }
}
