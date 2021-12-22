using ICSharpCode.SharpZipLib.Tar;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace QuestPatcher.Core
{
    public enum ExternalFileType
    {
        Modloader32,
        Modloader64,
        Main32,
        Main64,
        ApkTool,
        UberApkSigner,
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
        /// <summary>
        /// Stores information about each file type for extracting/running
        /// </summary>
        private class FileInfo
        {
            /// <summary>
            /// Name of the file when saved.
            /// Tools folder relative if RequiresExtraction is true, otherwise extraction folder relative.
            /// 
            /// If RequiresExtraction is true, this should be the name of a file within the ZIP
            /// May depend on operating system
            /// </summary>
            public SystemSpecificValue<string> SaveName { get; set; } = "".ForAllSystems();

            public string Name => ExtractionFolder ?? SaveName.Value;

            /// <summary>
            /// If this is true,the file will be made executable with chmod after it is downloaded (and extracted)
            /// </summary>
            public bool IsExecutable { get; set; }

            public string? ExtractionFolder { get; set; }
        }

        /// <summary>
        /// Represents a particular set of download links in the JSON pulled from the QP repository.
        /// </summary>
        private class DownloadSet
        {
#nullable disable
            /// <summary>
            /// Range of QuestPatcher versions supported by this set
            /// </summary>
            [JsonIgnore]
            public SemanticVersioning.Range SupportedVersions { get; set; }
            
            [JsonProperty(PropertyName = "supportedVersions")]
            public string SupportedVersion
            {
                get => SupportedVersions.ToString();
                set => SupportedVersions = SemanticVersioning.Range.Parse(value);
            }

            /// <summary>
            /// Download links as SystemSpecificValues
            /// </summary>
            public Dictionary<ExternalFileType, SystemSpecificValue<string>> Downloads { get; set; }
#nullable enable
        }
        
        private static readonly JsonSerializer Serializer = new()
        {
            Formatting = Formatting.Indented,
            ContractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            },
            NullValueHandling = NullValueHandling.Ignore
        };
        
        /// <summary>
        /// Index for file downloads. Used by default, but if it fails QP will fallback to resources
        /// </summary>
        private const string DownloadsUrl = "https://raw.githubusercontent.com/Lauriethefish/QuestPatcher/main/QuestPatcher.Core/Resources/file-downloads.json";

        private readonly Dictionary<ExternalFileType, FileInfo> _fileTypes = new()
        {
            {
                ExternalFileType.Modloader64,
                new FileInfo
                {
                    SaveName = "libmodloader64.so".ForAllSystems(),
                }
            },
            {
                ExternalFileType.Main64,
                new FileInfo
                {
                    SaveName = "libmain64.so".ForAllSystems(),
                }
            },
            {
                ExternalFileType.Modloader32,
                new FileInfo
                {
                    SaveName = "libmodloader32.so".ForAllSystems(),
                }
            },
            {
                ExternalFileType.Main32,
                new FileInfo
                {
                    SaveName = "libmain32.so".ForAllSystems(),
                }
            },
            {
                ExternalFileType.PlatformTools,
                new FileInfo
                {
                    SaveName = new()
                    {
                        Windows = "platform-tools/adb.exe",
                        Unix = "platform-tools/adb"
                    },
                    ExtractionFolder = "platform-tools",
                    IsExecutable = true
                }
            }
        };

        private Dictionary<ExternalFileType, SystemSpecificValue<string>>? _downloadUrls;

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

        private readonly string _fullyDownloadedPath; // Path in tools where completed downloads are stored for error checking

        private readonly List<ExternalFileType> _fullyDownloaded = new();

        private readonly SpecialFolders _specialFolders;
        private readonly Logger _logger;
        private readonly WebClient _webClient = new();
        private readonly bool _isUnix = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

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

            _webClient.DownloadProgressChanged += (_, args) =>
            {
                // Manually calculate the progress for better precision than the provided integer percentage
                DownloadProgress = (double) args.BytesReceived / args.TotalBytesToReceive * 100.0;
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

        /// <summary>
        /// Downloads the download URLs (meta I know) from the QP repository, and if that fails uses the JSON in resources.
        /// Only pulls the download URLs if they haven't been pulled already.
        /// </summary>
        /// <returns>The pulled or existing download URLs</returns>
        /// <exception cref="Exception">If no download URLs were found for the current QuestPatcher version</exception>
        private async Task<Dictionary<ExternalFileType, SystemSpecificValue<string>>> PrepareDownloadUrls()
        {
            // Only pull the download URLs if we haven't already
            if (_downloadUrls != null) { return _downloadUrls; }
            
            _logger.Debug("Preparing URLs to download files from . . .");
            List<DownloadSet> downloadSets;
            try
            {
                downloadSets = await LoadDownloadSetsFromWeb();
            }
            catch(Exception ex) {
                _logger.Debug($"Failed to download download URLs ({ex}), pulling from resources instead . . .");
                downloadSets = LoadDownloadSetsFromResources();
            }

            SemanticVersioning.Version qpVersion = VersionUtil.QuestPatcherVersion;

            // Download sets are in order, highest priority comes first
            foreach (DownloadSet downloadSet in downloadSets)
            {
                if (downloadSet.SupportedVersions.IsSatisfied(qpVersion))
                {
                    _logger.Debug($"Using download set for versions {downloadSet.SupportedVersions}");
                    _downloadUrls = downloadSet.Downloads;
                    return _downloadUrls;
                }
            }

            throw new Exception($"Unable to find download URLs suitable for this QuestPatcher version ({qpVersion})");
        }
        
        /// <summary>
        /// Loads the available download sets from a DLL resource
        /// </summary>
        /// <returns>The list of download sets</returns>
        /// <exception cref="NullReferenceException">If the resource is missing</exception>
        private List<DownloadSet> LoadDownloadSetsFromResources()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream("QuestPatcher.Core.Resources.file-downloads.json");
            if (stream == null)
            {
                throw new NullReferenceException("Could not find file-downloads.json in resources");
            }

            using TextReader textReader = new StreamReader(stream);
            using JsonReader jsonReader = new JsonTextReader(textReader);
            List<DownloadSet>? result = Serializer.Deserialize<List<DownloadSet>>(jsonReader);
            if (result == null)
            {
                throw new NullReferenceException("No download sets found in resources file");
            }

            return result;
        }

        /// <summary>
        /// Loads the available download sets from the QuestPatcher repository
        /// </summary>
        /// <returns>The available download sets</returns>
        /// <exception cref="NullReferenceException">If no download sets were in the pulled file, i.e. it was empty</exception>
        private async Task<List<DownloadSet>> LoadDownloadSetsFromWeb()
        {
            _logger.Debug($"Getting download URLs from {DownloadsUrl} . . .");
            string data = await _webClient.DownloadStringTaskAsync(DownloadsUrl);
            using StringReader stringReader = new(data);
            using JsonReader jsonReader = new JsonTextReader(stringReader);

            List<DownloadSet>? result = Serializer.Deserialize<List<DownloadSet>>(jsonReader);
            if (result == null)
            {
                throw new NullReferenceException("No download sets found in web pulled file");
            }

            return result;
        }

        private async Task DownloadFile(ExternalFileType fileType, FileInfo fileInfo, string downloadUrl, string saveLocation)
        {
            try
            {
                _logger.Information($"Downloading {fileInfo.Name} . . .");
                _logger.Debug($"Download URL: {downloadUrl}");
                DownloadProgress = 0.0;
                DownloadingFileName = fileInfo.Name;

                if (fileInfo.ExtractionFolder != null)
                {
                    Uri uri = new(downloadUrl);
                    byte[] archiveData = await _webClient.DownloadDataTaskAsync(uri);
                    using MemoryStream stream = new(archiveData); // Temporarily download the archive in order to extract it

                    // There is no way to asynchronously ExtractToDirectory (or ExtractContents with TAR archives), so we use Task.Run to avoid blocking
                    _logger.Information("Extracting . . .");
                    IsExtracting = true;
                    await Task.Run(() =>
                    {
                        string extractFolder = Path.Combine(_specialFolders.ToolsFolder, fileInfo.ExtractionFolder);

                        if(downloadUrl.EndsWith(".tar.gz")) {
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
                    await _webClient.DownloadFileTaskAsync(downloadUrl, saveLocation);
                }

                // chmod to make the downloaded executable actually usable if on mac or linux
                if(_isUnix && fileInfo.IsExecutable)
                {
                    await MakeExecutable(saveLocation);
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
        /// Uses chmod to make a downloaded file executable. Only necessary on Mac and Linux.
        /// </summary>
        private async Task MakeExecutable(string path)
        {
            Process process = new();

            string command = $"chmod +x {path}";

            process.StartInfo.FileName = "/bin/bash";
            process.StartInfo.Arguments = "-c \" " + command.Replace("\"", "\\\"") + " \"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            await process.WaitForExitAsync();
        }

        /// <summary>
        /// Finds the location of the specified file, and downloads/extracts it if it does not exist.
        /// </summary>
        /// <param name="fileType">The type of file to download</param>
        /// <returns>The location of the file</returns>
        public async Task<string> GetFileLocation(ExternalFileType fileType)
        {
            
            FileInfo fileInfo = _fileTypes[fileType];

            // The save location is relative to the extract folder if requires extraction, otherwise it's just relative to the tools folder
            string saveLocation;
            if (fileInfo.ExtractionFolder == null)
            {
                saveLocation = Path.Combine(_specialFolders.ToolsFolder, fileInfo.SaveName.Value);
            }
            else
            {
                saveLocation = Path.Combine(_specialFolders.ToolsFolder, fileInfo.ExtractionFolder, fileInfo.SaveName.Value);
            }

            if(!_fullyDownloaded.Contains(fileType) || !File.Exists(saveLocation))
            {
                await DownloadFile(fileType, fileInfo, (await PrepareDownloadUrls())[fileType].Value, saveLocation);
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
