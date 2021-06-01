using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
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
        PlatformTools,
        Jre,
    }

    /// <summary>
    /// Manages the downloading and verification of external files that QuestPatcher needs.
    /// For instance, the mod loader, libmain.so and platform-tools.
    /// 
    /// Uses a file called completedDownloads.dat to store which files have fully downloaded - this avoids files partially downloading becoming corrupted.
    /// </summary>
    public class ExternalFilesDownloader : INotifyPropertyChanged
    {
        private class FileInfo
        {
            /// <summary>
            /// Name of the file when saved.
            /// Tools folder relative if RequiresExtraction is true, otherwise extraction folder relative.
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

            public string Name => ExtractionFolder ?? SaveName;

            public string? ExtractionFolder { get; set; }
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
                    SaveName = "platform-tools/adb.exe",
                    WindowsDownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip",
                    LinuxDownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-linux.zip",
                    MacDownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-darwin.zip",
                    ExtractionFolder = "platform-tools"
                }
            },
            {
                ExternalFileType.Jre,
                new FileInfo
                {
                    SaveName = "jdk-11.0.11+9-jre/bin/java.exe",
                    WindowsDownloadUrl = "https://github.com/AdoptOpenJDK/openjdk11-binaries/releases/download/jdk-11.0.11%2B9/OpenJDK11U-jre_x64_windows_hotspot_11.0.11_9.zip",
                    LinuxDownloadUrl = "https://github.com/AdoptOpenJDK/openjdk11-binaries/releases/download/jdk-11.0.11%2B9/OpenJDK11U-jre_x64_linux_hotspot_11.0.11_9.tar.gz",
                    MacDownloadUrl = "https://github.com/AdoptOpenJDK/openjdk11-binaries/releases/download/jdk-11.0.11%2B9/OpenJDK11U-jre_x64_mac_hotspot_11.0.11_9.tar.gz",
                    ExtractionFolder = "openjre"
                }
            }
        };

        /// <summary>
        /// The name of the downloading file, or null if no file is downloading
        /// </summary>
        public string? DownloadingFileName
        {
            get => _downloadingFileName;
            private set { if(_downloadingFileName != value) { _downloadingFileName = value; NotifyPropertyChanged(); } }
        }
        private string? _downloadingFileName;

        /// <summary>
        /// Current download progress as a percentage, or null if no file is downloading
        /// </summary>
        public double? DownloadProgress
        {
            get => _downloadProgress;
            private set { if(_downloadProgress != value) { _downloadProgress = value; NotifyPropertyChanged(); } }
        }
        private double? _downloadProgress = 0;

        /// <summary>
        /// Whether the currently downloading file is now being extracted
        /// </summary>
        public bool IsExtracting
        {
            get => _isExtracting;
            private set { if (_isExtracting != value) { _isExtracting = value; NotifyPropertyChanged(); } }
        }
        private bool _isExtracting;

        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly string _fullyDownloadedPath = "completedDownloads.dat"; // Path in tools where completed downloads are stored for error checking

        private readonly List<ExternalFileType> _fullyDownloaded = new();

        private readonly SpecialFolders _specialFolders;
        private readonly Logger _logger;
        private readonly WebClient _webClient = new();

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
            }
            catch (FileNotFoundException)
            {
                _fullyDownloaded = new();
            }

            _webClient.DownloadProgressChanged += (sender, args) =>
            {
                // Manually calculate the progress for better precision than the provided integer percentage
                DownloadProgress = ((double) args.BytesReceived / args.TotalBytesToReceive) * 100.0;
            };
        }
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task SaveFullyDownloaded()
        {
            await File.WriteAllLinesAsync(_fullyDownloadedPath, _fullyDownloaded.ConvertAll(fileType => fileType.ToString()));
        }

        private async Task DownloadFile(ExternalFileType fileType, FileInfo fileInfo, string saveLocation)
        {
            try
            {
                _logger.Information($"Downloading {fileInfo.Name} . . .");
                DownloadProgress = 0.0;
                DownloadingFileName = fileInfo.Name;

                string url = fileInfo.PlatformSpecificUrl;
                if (fileInfo.ExtractionFolder != null)
                {
                    Uri uri = new(url);
                    byte[] archiveData = await _webClient.DownloadDataTaskAsync(uri);
                    using MemoryStream stream = new(archiveData); // Temporarily download the archive in order to extract it

                    // There is no way to asynchronously ExtractToDirectory (or ExtractContents with TAR archives), so we use Task.Run to avoid blocking
                    _logger.Information("Extracting . . .");
                    IsExtracting = true;
                    await Task.Run(() =>
                    {
                        string extractFolder = Path.Combine(_specialFolders.ToolsFolder, fileInfo.ExtractionFolder);

                        if(url.EndsWith(".tar.gz")) {
                            GZipStream zipStream = new(stream, CompressionMode.Decompress);

                            TarArchive archive = TarArchive.CreateInputTarArchive(zipStream, Encoding.UTF8);
                            archive.SetKeepOldFiles(false);
                            archive.ExtractContents(extractFolder, false);
                        }
                        else
                        {
                            ZipArchive archive = new(stream);
                            archive.ExtractToDirectory(extractFolder, true);
                        }
                    });
                }
                else
                {
                    // Directly download the file to the tools folder
                    await _webClient.DownloadFileTaskAsync(url, saveLocation);
                }

                // Write that the file has been fully downloaded
                // This is used instead of just checking that it exists to avoid exiting part way through causing a corrupted file
                _fullyDownloaded.Add(fileType);
                await SaveFullyDownloaded();
            }
            finally
            {
                DownloadingFileName = null;
                DownloadProgress = null;
                IsExtracting = false;
            }
        }

        /// <summary>
        /// Finds the location of the specified file, and downloads/extracts it if it does not exist.
        /// </summary>
        /// <param name="fileType">The type of file to download</param>
        /// <returns>The location of the file</returns>
        public async Task<string> GetFileLocation(ExternalFileType fileType)
        {
            FileInfo fileInfo = _fileTypes[fileType];

            // Remove .exe on non-windows
            if(Path.GetExtension(fileInfo.SaveName) == ".exe" && !OperatingSystem.IsWindows())
            {
                fileInfo.SaveName = fileInfo.SaveName[^4..];
            }

            // The save location is relative to the extract folder if requires extraction, otherwise it's just relative to the tools folder
            string saveLocation;
            if (fileInfo.ExtractionFolder == null)
            {
                saveLocation = Path.Combine(_specialFolders.ToolsFolder, fileInfo.SaveName);
            }
            else
            {
                saveLocation = Path.Combine(_specialFolders.ToolsFolder, fileInfo.ExtractionFolder, fileInfo.SaveName);
            }

            if(!_fullyDownloaded.Contains(fileType) || !File.Exists(saveLocation))
            {
                await DownloadFile(fileType, fileInfo, saveLocation);
            }

            return saveLocation;
        }

        /// <summary>
        /// Downloads the specified URL and saves the result to a file.
        /// This is used for the download progress indicator - instead of just using a WebClient directly.
        /// </summary>
        /// <param name="url">The URL to download from</param>
        /// <param name="saveName">Where to save the resultant file</param>
        /// <param name="overrideFileName">Used instead of the file name of saveName as the DownloadingFileName</param>
        public async Task DownloadUrl(string url, string saveName, string? overrideFileName = null)
        {
            try
            {
                DownloadProgress = 0.0;
                DownloadingFileName = overrideFileName ?? Path.GetFileName(saveName);

                Uri uri = new(url);
                await _webClient.DownloadFileTaskAsync(uri, saveName);
            }
            finally
            {
                DownloadingFileName = null;
                DownloadProgress = null;
            }
        }

        /// <summary>
        /// Clears all downloaded files.
        /// Used in quick fix in case the files are corrupted
        /// </summary>
        public async Task ClearCache()
        {
            _logger.Information("Clearing downloaded file cache . . .");
            await Task.Run(() =>
            {
                _logger.Debug($"Deleting {_specialFolders.ToolsFolder} . . .");
                Directory.Delete(_specialFolders.ToolsFolder, true); // Also deletes the saved downloaded files file
                _fullyDownloaded.Clear();
            });
            _logger.Information("Recreating tools folder . . .");
            Directory.CreateDirectory(_specialFolders.ToolsFolder);
        }
    }
}
