using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;

namespace QuestPatcher.Core
{
    public enum ExternalFileType
    {
        Modloader32,
        Modloader64,
        Main32,
        Main64,
        UberApkSigner,
        ApkTool,
        PlatformTools
    }

    /// <summary>
    /// Manages the downloading and verification of external files that QuestPatcher needs.
    /// For instance, the mod loader, libmain.so and platform-tools.
    /// 
    /// Uses a file called completedDownloads.dat to store which files have fully downloaded - this avoids files partially downloading becoming corrupted.
    /// </summary>
    public class ExternalFilesDownloader
    {
        private class FileInfo
        {
            /// <summary>
            /// Name of the file when saved.
            /// Tools folder relative if RequiresExtraction is true, otherwise data folder relative.
            /// 
            /// If RequiresExtraction is true, this should be the name of a file within the ZIP
            /// </summary>
            public string SaveName { get; set; } = "";

            /// <summary>
            /// Gets the correct download URL for the platform we are running on
            /// </summary>
            public string PlatformSpecificUrl
            {
                get
                {
                    if(DownloadUrl != null)
                    {
                        return DownloadUrl;
                    }

                    if(OperatingSystem.IsWindows())
                    {
                        return WindowsDownloadUrl;
                    }   else if(OperatingSystem.IsLinux())
                    {
                        return LinuxDownloadUrl;
                    }   else
                    {
                        return MacDownloadUrl;
                    }
                }
            }
            public string? DownloadUrl { get; set; } = null;

            // Platform specific download URLs, used if DownloadUrl is null
            public string WindowsDownloadUrl { get; set; } = "";
            public string LinuxDownloadUrl { get; set; } = "";
            public string MacDownloadUrl { get; set; } = "";

            public bool RequiresExtraction { get; set; } = false; // Will be extracted at the data path
        }

        private readonly Dictionary<ExternalFileType, FileInfo> _fileTypes = new()
        {
            {
                ExternalFileType.ApkTool,
                new FileInfo
                {
                    SaveName = "apktool.jar",
                    DownloadUrl = "https://bitbucket.org/iBotPeaches/apktool/downloads/apktool_2.5.0.jar"
                }
            },
            {
                ExternalFileType.UberApkSigner,
                new FileInfo
                {
                    SaveName = "uber-apk-signer.jar",
                    DownloadUrl = "https://github.com/patrickfav/uber-apk-signer/releases/download/v1.2.1/uber-apk-signer-1.2.1.jar"
                }
            },
            {
                ExternalFileType.Modloader64,
                new FileInfo
                {
                    SaveName = "libmodloader64.so",
                    DownloadUrl = "https://github.com/sc2ad/QuestLoader/releases/latest/download/libmodloader64.so"
                }
            },
            {
                ExternalFileType.Main64,
                new FileInfo
                {
                    SaveName = "libmain64.so",
                    DownloadUrl = "https://github.com/sc2ad/QuestLoader/releases/latest/download/libmain64.so"
                }
            },
            {
                ExternalFileType.Modloader32,
                new FileInfo
                {
                    SaveName = "libmodloader32.so",
                    DownloadUrl = "https://github.com/sc2ad/QuestLoader/releases/latest/download/libmodloader32.so"
                }
            },
            {
                ExternalFileType.Main32,
                new FileInfo
                {
                    SaveName = "libmain32.so",
                    DownloadUrl = "https://github.com/sc2ad/QuestLoader/releases/latest/download/libmain32.so"
                }
            },
            {
                ExternalFileType.PlatformTools,
                new FileInfo
                {
                    SaveName = "platform-tools/adb.exe", // Purely used to check if the download finished in the case of upgrading from a version where completedDownloads.dat is not a thing.
                    WindowsDownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip",
                    LinuxDownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-linux.zip",
                    MacDownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-darwin.zip",
                    RequiresExtraction = true
                }
            }
        };

        private readonly string _fullyDownloadedPath = "completedDownloads.dat"; // Path in tools where completed downloads are stored for error checking

        private readonly List<ExternalFileType> _fullyDownloaded = new();

        private readonly SpecialFolders _specialFolders;
        private readonly Logger _logger;
        private readonly WebClient _webClient = new();

        private bool _downloadsFileMissing;

        public ExternalFilesDownloader(SpecialFolders specialFolders, Logger logger)
        {
            _specialFolders = specialFolders;
            _logger = logger;
            _fullyDownloadedPath = Path.Combine(_specialFolders.ToolsFolder, "completedDownloads.dat");

            // Load which dependencies have been fully downloaded from disk
            try
            {
                foreach(string fileType in File.ReadAllLines(_fullyDownloadedPath))
                {
                    _fullyDownloaded.Add(Enum.Parse<ExternalFileType>(fileType));
                }
                _downloadsFileMissing = false;
            }
            catch (FileNotFoundException)
            {
                _downloadsFileMissing = true;
                _fullyDownloaded = new();
            }
        }

        private async Task SaveFullyDownloaded()
        {
            await File.WriteAllLinesAsync(_fullyDownloadedPath, _fullyDownloaded.ConvertAll(fileType => fileType.ToString()));
            _downloadsFileMissing = false;
        }

        private async Task DownloadFile(ExternalFileType fileType, FileInfo fileInfo, string saveLocation)
        {
            _logger.Information($"Downloading {(fileInfo.RequiresExtraction ? Path.GetDirectoryName(fileInfo.SaveName) : fileInfo.SaveName)} . . .");
            if (fileInfo.RequiresExtraction)
            {
                using Stream stream = _webClient.OpenRead(fileInfo.PlatformSpecificUrl); // Temporarily download the archive in order to extract it

                // There is no way to asynchronously ExtractToDirectory, so we use Task.Run to avoid blocking
                _logger.Information("Extracting . . .");
                await Task.Run(() =>
                {
                    ZipArchive archive = new(stream);
                    archive.ExtractToDirectory(_specialFolders.DataFolder, true);
                });
            }
            else
            {
                // Directly download the file to the tools folder
                await _webClient.DownloadFileTaskAsync(fileInfo.PlatformSpecificUrl, saveLocation);
            }

            // Write that the file has been fully downloaded
            // This is used instead of just checking that it exists to avoid exiting part way through causing a corrupted file
            _fullyDownloaded.Add(fileType);
            await SaveFullyDownloaded();
        }

        public async Task<string> GetFileLocation(ExternalFileType fileType)
        {
            FileInfo fileInfo = _fileTypes[fileType];
            // The save location is data folder relative if requires extraction, otherwise it is tools folder relative
            string saveLocation = Path.Combine(fileInfo.RequiresExtraction ? _specialFolders.DataFolder : _specialFolders.ToolsFolder, fileInfo.SaveName);

            // If the downloads file existed upon startup, we redownload if the file is missing in the downloads file.
            // Otherwise, we only redownload if the file is missing, which is not ideal.
            if(!_downloadsFileMissing && !_fullyDownloaded.Contains(fileType) || (_downloadsFileMissing && !File.Exists(fileInfo.SaveName)))
            {
                await DownloadFile(fileType, fileInfo, saveLocation);
            }

            return saveLocation;
        }

        public async Task ClearCache()
        {
            _logger.Information("Clearing downloaded file cache . . .");
            await Task.Run(() =>
            {
                _logger.Debug($"Deleting {_specialFolders.ToolsFolder} . . .");
                Directory.Delete(_specialFolders.ToolsFolder, true); // Also deletes the saved downloaded files file
                _fullyDownloaded.Clear();

                foreach (FileInfo fileInfo in _fileTypes.Values)
                {
                    // RequiresExtraction files are expected to be a folder in the data folder and the SaveName's folder should be the path of this folder
                    // TODO: RequiresExtraction files should probably be in the ToolsFolder as well, this just hasn't been done to avoid downloading platform-tools multiple times if somebody is upgrading from an older version
                    if (fileInfo.RequiresExtraction)
                    {
                        string? relativeName = Path.GetDirectoryName(fileInfo.SaveName);
                        Debug.Assert(relativeName != null);

                        string absolutePath = Path.Combine(_specialFolders.DataFolder, relativeName);
                        _logger.Debug($"Deleting {absolutePath} . . .");
                        if (Directory.Exists(absolutePath))
                        {
                            Directory.Delete(absolutePath, true);
                        }
                    }
                }

            });
            _logger.Information("Recreating tools folder . . .");
            Directory.CreateDirectory(_specialFolders.ToolsFolder);
        }
    }
}
