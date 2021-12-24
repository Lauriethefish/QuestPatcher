using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using QuestPatcher.Core.Modding;
using QuestPatcher.Core.Models;
using QuestPatcher.Core.Patching;
using Serilog.Core;

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
        private readonly Logger _logger;
        private readonly ConfigManager _configManager;
        private readonly Config _config;
        private readonly PatchingManager _patchingManager;

        private const string LogsDirectory = "logs";
        private string GameLogsDirectory => $"logs/{_config.AppId}";

        private string GameConfigsPath => $"/sdcard/ModData/{_config.AppId}/Configs/";
        private string GameConfigsDirectory => $"configs/{_config.AppId}";

        public InfoDumper(SpecialFolders specialFolders, AndroidDebugBridge debugBridge, ModManager modManager, Logger logger, ConfigManager configManager, PatchingManager patchingManager)
        {
            _specialFolders = specialFolders;
            _debugBridge = debugBridge;
            _modManager = modManager;
            _logger = logger;
            _configManager = configManager;
            _config = configManager.GetOrLoadConfig();
            _patchingManager = patchingManager;
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
            _logger.Information($"Starting dump at {location} . . .");

            if (File.Exists(location))
            {
                _logger.Information("Deleting existing dump . . .");
                File.Delete(location);
            }

            await using FileStream stream = File.Open(location, FileMode.Create);
            using ZipArchive dumpArchive = new(stream, ZipArchiveMode.Update);

            try { await SaveQuestPatcherLogs(dumpArchive); }
            catch (Exception ex) { _logger.Warning($"Failed to save QuestPatcher logs to dump: {ex}"); }

            try { await SaveModLogs(dumpArchive); }
            catch (Exception ex) { _logger.Warning($"Failed to save mod logs to dump: {ex}"); }
            
            try { await SaveConfig(dumpArchive); }
            catch (Exception ex) { _logger.Warning($"Failed to save config to dump: {ex}"); }
            
            try { await SaveModConfigs(dumpArchive); }
            catch (Exception ex) { _logger.Warning($"Failed to save mod configs to dump: {ex}"); }
            
            try { await SaveInfoFile(dumpArchive); }
            catch (Exception ex) { _logger.Warning($"Failed to save information file to dump: {ex}"); }
            
            _logger.Information("Dump complete!");

            return location;
        }

        private async Task CreateLogEntry(ZipArchive dump, string sourcePath, string logsFolder, string? overrideLogName = null)
        {
            if (File.Exists(sourcePath))
            {
                string logName = overrideLogName ?? Path.GetFileName(sourcePath);
                _logger.Information($"Saving log {logName} . . .");
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
            _logger.Information($"Saving mod logs from {gameLogsPath} . . .");

            foreach (string logPath in await _debugBridge.ListDirectoryFiles(gameLogsPath))
            {
                try
                {
                    using TempFile tempPath = _specialFolders.GetTempFile();
                    _logger.Information($"Downloading {logPath} to {tempPath} . . .");
                    await _debugBridge.DownloadFile(logPath, tempPath.Path);
                    await CreateLogEntry(dump, tempPath.Path, GameLogsDirectory, Path.GetFileName(logPath));
                }
                catch(Exception ex)
                {
                    _logger.Warning($"Failed to download log {logPath}: {ex}");
                }
            }
        }

        private async Task SaveModConfigs(ZipArchive dump)
        {
            _logger.Information($"Saving configs in {GameConfigsPath} . . .");
            foreach (string configPath in await _debugBridge.ListDirectoryFiles(GameConfigsPath))
            {
                try
                {
                    using TempFile tempPath = _specialFolders.GetTempFile();
                    _logger.Information($"Downloading {configPath} to {tempPath} . . .");
                    await _debugBridge.DownloadFile(configPath, tempPath.Path);
                    await dump.AddFileAsync(tempPath.Path, Path.Combine(GameConfigsDirectory, Path.GetFileName(configPath)));
                } catch (Exception ex) { 
                    _logger.Warning($"Failed to download config {configPath}: {ex}");
                }
            }
        }
        
        /// <summary>
        /// Saves QuestPatcher (and ADB) logs to the given dump.
        /// </summary>
        /// <param name="dump">The dump to save to</param>
        private async Task SaveQuestPatcherLogs(ZipArchive dump)
        {
            _logger.Information("Saving QP logs to dump . . .");
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
            _logger.Information("Saving state/information file . . .");
            ZipArchiveEntry infoEntry = dump.CreateEntry("status.txt");
            await using Stream stream = infoEntry.Open();
            await using StreamWriter writer = new(stream);

            await writer.WriteLineAsync("QuestPatcher Information dump");
            await writer.WriteLineAsync("=============================");

            ApkInfo? app = _patchingManager.InstalledApp;
            if (app != null)
            {
                await writer.WriteLineAsync(
                    $"Application: {_config.AppId} v{app.Version}. Is Modded: {app.IsModded}. 64 bit: {app.Is64Bit}");
            }
            else
            {
                await writer.WriteLineAsync("Application not yet loaded");
            }
            
            await writer.WriteLineAsync($"Total loaded mods: {_modManager.AllMods.Count}. {_modManager.AllMods.Count(mod => mod.IsInstalled)} Installed");

            foreach (IMod mod in _modManager.AllMods)
            {
                string authorText = mod.Porter == null ? $"by {mod.Author}" : $"by {mod.Author} ported by {mod.Porter}";
                await writer.WriteLineAsync($"{mod.Id} v{mod.Version} {authorText}. Installed: {mod.IsInstalled}. Is Library: {mod.IsLibrary}. Package version: {mod.PackageVersion}");
            }
            
            await writer.WriteLineAsync("=============================");

            try
            {
                List<string> modFiles = await _debugBridge.ListDirectoryFiles(_modManager.ModsPath);
                List<string> libFiles = await _debugBridge.ListDirectoryFiles(_modManager.LibsPath);

                await writer.WriteLineAsync($"Mod files (contents of {_modManager.ModsPath}):");
                foreach (string str in modFiles)
                {
                    await writer.WriteLineAsync(Path.GetFileName(str));
                }

                await writer.WriteLineAsync($"Library files (contents of {_modManager.LibsPath}):");
                foreach (string str in libFiles)
                {
                    await writer.WriteLineAsync(Path.GetFileName(str));
                }
            }
            catch (Exception ex)
            {
                await writer.WriteLineAsync($"Failed to load mods/libs from quest");
                _logger.Warning($"Failed to load mods/libs from quest: {ex}");
            }
        }
    }
}
