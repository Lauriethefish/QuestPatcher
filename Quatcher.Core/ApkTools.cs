using Serilog.Core;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace Quatcher.Core
{
    /// <summary>
    /// Thrown if signing fails.
    /// </summary>
    public class SigningException : Exception
    {
        public SigningException(string message) : base(message) { }
    }
    
    /// <summary>
    /// Abstraction around uber-apk-signer used during patching.
    /// Used to abstract apktool as well, but Quatcher no longer uses this, relying on its own manifest decompiler instead.
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

            ProcessOutput output = await InvokeJava($"-jar \"{path}\" {args}");
            _logger.Verbose($"Standard output: {output.StandardOutput}");
            if (output.ErrorOutput.Length > 0)
            {
                _logger.Verbose($"Error output: {output.ErrorOutput}");
            }

            return output;
        }

        public async Task PrepareJavaInstall()
        {
            bool isInstalled = true;
            try
            {
                string javaVersion = (await InvokeJava("-version")).ErrorOutput;

                // Mac OS displays a special message when you try to use Java when it isn't installed
                if (javaVersion.StartsWith("The operation couldn’t be completed. Unable to locate a Java Runtime.") || javaVersion.StartsWith("No Java runtime present, requesting install."))
                {
                    isInstalled = false;
                }
                else
                {
                    _logger.Information("Located Java install on PATH");
                    _logger.Debug($"Java version: {javaVersion}");
                }
            }
            catch (Win32Exception) // Thrown if the executable is missing, even on Linux. Slight .NET quirk.
            {
                isInstalled = false;
            }

            if (!isInstalled)
            {
                // Download Java if it is not installed
                _javaExecutableName = await _filesDownloader.GetFileLocation(ExternalFileType.Jre);
            }
        }

        /// <summary>
        /// Signs the APK with the given path using uber-apk-signer.
        ///
        /// Sometimes signing with zipalign will fail, especially on macOS.
        /// Therefore it's best to attempt signing with zipalign, then fall back to without if signing fails.
        /// </summary>
        /// <param name="apkPath">The path of the APk to sign</param>
        /// <param name="useZipAlign">Whether or not to use zipalign to optimise the APK</param>
        /// <exception cref="SigningException">If the signer does not produce an APK at the output path, or if the output path already exists</exception>
        /// <returns>The path of the signed APK</returns>
        public async Task<string> SignApk(string apkPath, bool useZipAlign = true)
        {
            string signedPath = Path.GetFileNameWithoutExtension(apkPath);
            string? apkDirectory = Path.GetDirectoryName(apkPath);
            if (apkDirectory != null)
            {
                signedPath = Path.Combine(apkDirectory, signedPath);
            }

            // Uber APK signer uses -aligned-debugSigned for aligned APKs and -debugSigned for non-aligned APKs.
            signedPath += useZipAlign ? "-aligned-debugSigned.apk" : "-debugSigned.apk";
            // Sanity check this to avoid having to comb through the output of the Java signer
            if (File.Exists(signedPath))
            {
                throw new SigningException($"Destination file for signing {signedPath} already exists");
            }
            
            string command = "--debug ";
            if (!useZipAlign) { command += "--skipZipAlign "; }
            command += $"--apks \"{apkPath}\"";
            
            await InvokeJavaTool(ExternalFileType.UberApkSigner, command);

            // If the destination APK path is missing, then signing must have failed
            if (!File.Exists(signedPath))
            {
                throw new SigningException($"Signed APK at {signedPath} was missing, signing must have failed");
            }

            return signedPath;
        }
    }
}
