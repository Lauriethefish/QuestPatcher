using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using Serilog;

namespace QuestPatcher.Core
{
    /// <summary>
    /// Used to create a ZIP file containing a bunch of info about the current state of QuestPatcher and mods.
    /// </summary>
    public class InfoDumper
    {
        private readonly SpecialFolders _specialFolders;
        private readonly AndroidDebugBridge _debugBridge;
        private readonly ModManager _modManager;
        private readonly ConfigManager _configManager;
        private readonly Config _config;
        private readonly InstallManager _installManager;

        private const string LogsDirectory = "logs";
        private string GameLogsDirectory => $"logs/{_config.AppId}";

        private string GameConfigsPath => $"/sdcard/ModData/{_config.AppId}/Configs/";
        private string GameConfigsDirectory => $"configs/{_config.AppId}";

        public InfoDumper(SpecialFolders specialFolders, AndroidDebugBridge debugBridge, ModManager modManager, ConfigManager configManager, InstallManager installManager)
        {
            _specialFolders = specialFolders;
            _debugBridge = debugBridge;
            _modManager = modManager;
            _configManager = configManager;
            _config = configManager.GetOrLoadConfig();
            _installManager = installManager;
        }

        /// <summary>
        /// Creates a dump ZIP containing lots of information about the current state of QuestPatcher.
        /// e.g. installed mods, config file, logs, ADB logs
        /// </summary>
        /// <param name="overrideLocation">A place to save the ZIP to instead of the default location of (data folder)/infoDump.zip.</param>
        /// <returns>The location of the saved dump</returns>
        public async Task<string> CreateInfoDump(string? overrideLocation = null)
        {
            string location = overrideLocation ?? Path.Combine(_specialFolders.DataFolder, "infoDump.zip");
            Log.Information("Starting dump at {DumpLocation} . . .", location);

            if (File.Exists(location))
            {
                Log.Information("Deleting existing dump . . .");
                File.Delete(location);
            }

            await using var stream = File.Open(location, FileMode.Create);
            using ZipArchive dumpArchive = new(stream, ZipArchiveMode.Update);

            try { await SaveQuestPatcherLogs(dumpArchive); }
            catch (Exception ex) { Log.Warning(ex, "Failed to save QuestPatcher logs to dump"); }

            try { await SaveModLogs(dumpArchive); }
            catch (Exception ex) { Log.Warning(ex, "Failed to save mod logs to dump"); }

            try { await SaveConfig(dumpArchive); }
            catch (Exception ex) { Log.Warning(ex, "Failed to save config to dump"); }

            try { await SaveModConfigs(dumpArchive); }
            catch (Exception ex) { Log.Warning(ex, "Failed to save mod configs to dump"); }

            try { await SaveInfoFile(dumpArchive); }
            catch (Exception ex) { Log.Warning(ex, "Failed to save information file to dump"); }

            Log.Information("Dump complete!");

            return location;
        }

        private async Task CreateLogEntry(ZipArchive dump, string sourcePath, string logsFolder, string? overrideLogName = null)
        {
            if (File.Exists(sourcePath))
            {
                string logName = overrideLogName ?? Path.GetFileName(sourcePath);
                Log.Information("Saving log {LogName} . . .", logName);
                await dump.AddFileAsync(sourcePath, Path.Combine(logsFolder, logName));
            }
        }

        /// <summary>
        /// Saves log files from BS-hook loggers in mods to a folder in the dump with the select app name
        /// </summary>
        /// <param name="dump">The dump to save to</param>
        private async Task SaveModLogs(ZipArchive dump)
        {
            string gameLogsPath = $"/sdcard/Android/data/{_config.AppId}/files/logs";
            Log.Information("Saving mod logs from {GameLogsPath} . . .", gameLogsPath);

            foreach (string logPath in await _debugBridge.ListDirectoryFiles(gameLogsPath))
            {
                try
                {
                    using var tempFile = new TempFile();
                    Log.Information("Downloading {LogPath} to {TempPath} . . .", logPath, tempFile);
                    await _debugBridge.DownloadFile(logPath, tempFile.Path);
                    await CreateLogEntry(dump, tempFile.Path, GameLogsDirectory, Path.GetFileName(logPath));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to download log {LogPath}", logPath);
                }
            }
        }

        private async Task SaveModConfigs(ZipArchive dump)
        {
            Log.Information("Saving configs in {GameConfigsPath} . . .", GameConfigsPath);
            foreach (string configPath in await _debugBridge.ListDirectoryFiles(GameConfigsPath))
            {
                try
                {
                    using var tempPath = new TempFile();
                    Log.Information("Downloading {ConfigPath} to {TempPath} . . .", configPath, tempPath);
                    await _debugBridge.DownloadFile(configPath, tempPath.Path);
                    await dump.AddFileAsync(tempPath.Path, Path.Combine(GameConfigsDirectory, Path.GetFileName(configPath)));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to download config {ConfigPath}", configPath);
                }
            }
        }

        /// <summary>
        /// Saves QuestPatcher (and ADB) logs to the given dump.
        /// </summary>
        /// <param name="dump">The dump to save to</param>
        private async Task SaveQuestPatcherLogs(ZipArchive dump)
        {
            Log.Information("Saving QP logs to dump . . .");
            string qpLogPath = Path.Combine(_specialFolders.LogsFolder, "log.log");
            string adbLogPath = Path.Combine(_specialFolders.LogsFolder, "adb.log");
            await CreateLogEntry(dump, qpLogPath, LogsDirectory);
            await CreateLogEntry(dump, adbLogPath, LogsDirectory);
        }

        /// <summary>
        /// Saves the config file to a dump.
        /// </summary>
        /// <param name="dump">The dump to save to</param>
        private async Task SaveConfig(ZipArchive dump)
        {
            _configManager.SaveConfig();
            await dump.AddFileAsync(_configManager.ConfigPath, "configs/QuestPatcher_config.json");
        }

        /// <summary>
        /// Saves an information file containing lots of information about QP state, e.g. installed mods, enabled/disabled mods, SO files in mods directories.
        /// </summary>
        /// <param name="dump"></param>
        private async Task SaveInfoFile(ZipArchive dump)
        {
            Log.Information("Saving state/information file . . .");
            var infoEntry = dump.CreateEntry("status.txt");
            await using var stream = infoEntry.Open();
            await using StreamWriter writer = new(stream);

            await writer.WriteLineAsync("QuestPatcher Information dump");
            await writer.WriteLineAsync("=============================");

            var app = _installManager.InstalledApp;
            if (app != null)
            {
                await writer.WriteLineAsync(
                    $"Application: {_config.AppId} v{app.Version}. Modloader: {app.ModLoader}. 64 bit: {app.Is64Bit}");
            }
            else
            {
                await writer.WriteLineAsync("Application not yet loaded");
            }

            await writer.WriteLineAsync($"Total loaded mods: {_modManager.AllMods.Count}. {_modManager.AllMods.Count(mod => mod.IsInstalled)} Installed");

            foreach (var mod in _modManager.AllMods)
            {
                string authorText = mod.Porter == null ? $"by {mod.Author}" : $"by {mod.Author} ported by {mod.Porter}";
                await writer.WriteLineAsync($"{mod.Id} v{mod.Version} {authorText}. Installed: {mod.IsInstalled}. Is Library: {mod.IsLibrary}. Package version: {mod.PackageVersion}");
            }

            await writer.WriteLineAsync("=============================");

            try
            {
                await WriteDirectoryDump(_modManager.ModsPath, "QuestLoader mod files", writer);
                await WriteDirectoryDump(_modManager.LibsPath, "QuestLoader library files", writer);
                await WriteDirectoryDump(_modManager.Sl2EarlyModsPath, "Scotland2 early mod files", writer);
                await WriteDirectoryDump(_modManager.Sl2LateModsPath, "Scotland2 late mod files", writer);
                await WriteDirectoryDump(_modManager.Sl2LibsPath, "Scotland2 library files", writer);
            }
            catch (Exception ex)
            {
                await writer.WriteLineAsync($"Failed to load mods/libs from quest");
                Log.Warning(ex, "Failed to load mods/libs from quest");
            }
        }

        private async Task WriteDirectoryDump(string path, string name, StreamWriter writer)
        {
            await writer.WriteLineAsync($"{name} (contents of {path}):");
            var files = await _debugBridge.ListDirectoryFiles(path, true);
            foreach (string fileName in files)
            {
                await writer.WriteLineAsync(fileName);
            }
        }
    }
}
