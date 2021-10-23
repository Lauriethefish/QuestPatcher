using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using QuestPatcher.Core;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;

namespace QuestPatcher.CLI
{
    [Command("patch")]
    public class PatchCommand : QuestPatcherCommand
    {
        [CommandParameter(0, Name = "apkPath", Description = "Path to the APK to patch")]
        public string ApkPath { get; init; } = null!; // null! is to silence the compiler, but hopefully we can find a better way to tell it that this value is assigned by CliFx
        
        [CommandOption("resultPath", 'd', Description = "Path to save the APK to instead of patching in place")]
        public string? DestinationPath { get; init; }
        
        [CommandOption("overwrite", 'o', Description = "If passed, the existing APK will be deleted if an APK already exists in the destination path.")]
        public bool Overwrite { get; init; }
        
        [CommandOption("noFilePermissions", Description = "Disable adding external file permissions to the APK")]
        public bool DisableExternalFiles { get; init; }
        
        [CommandOption("handTracking", Description = "Enable adding hand tracking permissions to the APK")]
        public bool AddHandTracking { get; init; }

        [CommandOption("debuggable", Description = "Enable adding attributes to the APK to make it debuggable")]
        public bool AddDebuggable { get; init; }
        
        [CommandOption("noSign", Description = "Skips signing the APK")]
        public bool DisableSign { get; init; }
        
        [CommandOption("noTag", Description = "Disable adding a QuestPatcher tag to the APK. NOTE: This will mean that tools such as BMBF and QuestPatcher will not detect the APK as modded")]
        public bool DisableTag { get; init; }
        
        public override async ValueTask ExecuteAsync(IConsole console)
        {
            if(!File.Exists(ApkPath))
            {
                throw new CommandException($"The specified APK path (\"{ApkPath}\") did not exist!");
            }
            
            ZipArchive apkArchive;
            if(DestinationPath == null)
            {
                Logger.Information("Starting patch (in-place) . . .");
                apkArchive = ZipFile.Open(ApkPath, ZipArchiveMode.Update);
            }
            else
            {
                Logger.Information($"Starting patch to {DestinationPath}");
                if(File.Exists(DestinationPath))
                {
                    if(Overwrite)
                    {
                        File.Delete(DestinationPath);
                    }
                    else
                    {
                        throw new CommandException("Destination APK exists and is not being overwritten!");
                    }
                }
                
                File.Copy(ApkPath, DestinationPath);
                apkArchive = ZipFile.Open(DestinationPath, ZipArchiveMode.Update);
            }

            AppPatcher patcher = new(Logger, FilesDownloader);
            if(!await patcher.Patch(apkArchive, () => Task.FromResult(true), new PatchingPermissions
            {
                ExternalFiles = !DisableExternalFiles,
                HandTracking = AddHandTracking,
                Debuggable = AddDebuggable
            }, !DisableTag))
            {
                // Patching cancelled
                return;
            }

            if(DisableSign)
            {
                Logger.Warning("Skipping signing the APK, it must be signed manually before installing");
            }
            else
            {
                Logger.Information("Signing and saving APK . . .");
                ApkSigner signer = new();
                await signer.SignApkWithPatchingCertificate(apkArchive);
            }

            apkArchive.Dispose();
            console.ForegroundColor = ConsoleColor.Green;
            await console.Output.WriteLineAsync("APK Saved");
            console.ResetColor();
        }
    }
}
