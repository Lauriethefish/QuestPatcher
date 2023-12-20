using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Tar;
using Serilog;

namespace QuestPatcher.Core
{
    public enum ExternalFileType
    {
        Modloader32,
        Modloader64,
        Main32,
        Main64,
        Scotland2,
        LibMainLoader,
        ApkTool,
        UberApkSigner,
        PlatformTools,
        Jre,
        OvrPlatformSdk
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

            [JsonPropertyName("supportedVersions")]
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

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {

            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
                ExternalFileType.LibMainLoader,
                new FileInfo
                {
                    SaveName = "libmainloader.so".ForAllSystems()
                }
            },
            {
                ExternalFileType.Scotland2,
                new FileInfo
                {
                    SaveName = "libsl2.so".ForAllSystems()
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
            },
            {
                ExternalFileType.OvrPlatformSdk,
                new FileInfo
                {
                    SaveName = "OVRPlatformSDK.zip".ForAllSystems()
                }
            }
        };

        private Dictionary<ExternalFileType, List<string>>? _downloadUrls;

        /// <summary>
        /// The name of the downloading file, or null if no file is downloading
        /// </summary>
        public string? DownloadingFileName
        {
            get => _downloadingFileName;
            private set { if (_downloadingFileName != value) { _downloadingFileName = value; NotifyPropertyChanged(); } }
        }
        private string? _downloadingFileName;

        /// <summary>
        /// Current download progress as a percentage. Value not guaranteed if HasDownloadProgress is false.
        /// </summary>
        public double DownloadProgress
        {
            get => _downloadProgress;
            private set { if (_downloadProgress != value) { _downloadProgress = value; NotifyPropertyChanged(); } }
        }
        private double _downloadProgress;

        /// <summary>
        /// Whether or not the currently downloading file's download progress is known.
        /// False if no file is being downloaded.
        /// </summary>
        public bool DownloadProgressKnown
        {
            get => _downloadProgressKnown;
            private set { if (_downloadProgressKnown != value) { _downloadProgressKnown = value; NotifyPropertyChanged(); } }
        }
        private bool _downloadProgressKnown;

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
        private readonly HttpClient _httpClient = new();
        private readonly bool _isUnix = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

        public ExternalFilesDownloader(SpecialFolders specialFolders)
        {
            _specialFolders = specialFolders;
            _fullyDownloadedPath = Path.Combine(_specialFolders.ToolsFolder, "completedDownloads.dat");

            // Load which dependencies have been fully downloaded from disk
            try
            {
                foreach (string fileType in File.ReadAllLines(_fullyDownloadedPath))
                {
                    _fullyDownloaded.Add(Enum.Parse<ExternalFileType>(fileType));
                }
            }
            catch (FileNotFoundException)
            {
                _fullyDownloaded = new();
            }
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
        private async Task<Dictionary<ExternalFileType, List<string>>> PrepareDownloadUrls()
        {
            // Only pull the download URLs if we haven't already
            if (_downloadUrls != null) { return _downloadUrls; }

            Log.Debug("Preparing URLs to download files from . . .");
            IEnumerable<DownloadSet> downloadSets;
            try
            {
                downloadSets = await LoadDownloadSetsFromWeb();
                downloadSets = downloadSets.Concat(LoadDownloadSetsFromResources());
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to download download URLs, pulling from resources instead . . .");
                downloadSets = LoadDownloadSetsFromResources();
            }

            var downloadUrls = new Dictionary<ExternalFileType, List<string>>();

            var qpVersion = VersionUtil.QuestPatcherVersion;
            // filter the compatible sets  
            downloadSets = downloadSets.Where(set => set.SupportedVersions.IsSatisfied(qpVersion));
            // Download sets are in order, highest priority comes first
            foreach (var downloadSet in downloadSets)
            {
                foreach (var (type, specificValue) in downloadSet.Downloads)
                {
                    if (downloadUrls.ContainsKey(type))
                    {
                        downloadUrls[type].Add(specificValue.Value);
                    }
                    else
                    {
                        downloadUrls[type] = new List<string> { specificValue.Value };
                    }
                }
            }

            if (downloadUrls.Count == 0)
            {
                throw new Exception($"Unable to find download URLs suitable for this QuestPatcher version ({qpVersion})");
            }

            _downloadUrls = downloadUrls.Aggregate(new Dictionary<ExternalFileType, List<string>>(), (dictionary, pair) =>
            {
                dictionary[pair.Key] = pair.Value.Distinct().ToList(); // dedupe the list 
                return dictionary;
            });

            return _downloadUrls;
        }

        /// <summary>
        /// Loads the available download sets from a DLL resource
        /// </summary>
        /// <returns>The list of download sets</returns>
        /// <exception cref="NullReferenceException">If the resource is missing</exception>
        private List<DownloadSet> LoadDownloadSetsFromResources()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("QuestPatcher.Core.Resources.file-downloads.json");
            if (stream == null)
            {
                throw new NullReferenceException("Could not find file-downloads.json in resources");
            }

            var result = JsonSerializer.Deserialize<List<DownloadSet>?>(stream, SerializerOptions);
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
            Log.Debug("Getting download URLs from {DownloadsUrl} . . .", DownloadsUrl);
            using var jsonStream = await _httpClient.GetStreamAsync(DownloadsUrl);

            var result = await JsonSerializer.DeserializeAsync<List<DownloadSet>>(jsonStream, SerializerOptions);
            if (result == null)
            {
                throw new NullReferenceException("No download sets found in web pulled file");
            }

            return result;
        }

        private async Task DownloadFileWithMirrors(ExternalFileType fileType, FileInfo fileInfo, List<string> downloadUrls, string saveLocation)
        {
            if (downloadUrls.Count == 0)
            {
                throw new ArgumentException("Download Url is empty");
            }
            Log.Information("Downloading {Name}", fileInfo.Name);
            Log.Verbose("Urls: {Urls}", downloadUrls);
            foreach (string url in downloadUrls)
            {
                if (await TryDownloadFile(fileType, fileInfo, url, saveLocation))
                {
                    return;
                }
            }
            Log.Error("Failed to download {Name} with all mirrors", fileInfo.Name);
            throw new FileDownloadFailedException($"Failed to download {fileInfo.Name} with all mirrors");
        }

        private async Task<bool> TryDownloadFile(ExternalFileType fileType, FileInfo fileInfo, string downloadUrl, string saveLocation)
        {
            bool succeeded = false;
            try
            {
                Log.Debug("Download URL: {DownloadUrl}", downloadUrl);
                DownloadingFileName = fileInfo.Name;

                if (fileInfo.ExtractionFolder != null)
                {
                    // Download to a MemoryStream first, as we cannot seek on the stream from an HTTP request
                    using var stream = new MemoryStream();
                    await DownloadToStreamWithProgressAsync(downloadUrl, stream);
                    stream.Position = 0;

                    // There is no way to asynchronously ExtractToDirectory (or ExtractContents with TAR archives), so we use Task.Run to avoid blocking
                    Log.Information("Extracting . . .");
                    IsExtracting = true;
                    await Task.Run(() =>
                    {
                        string extractFolder = Path.Combine(_specialFolders.ToolsFolder, fileInfo.ExtractionFolder);

                        if (downloadUrl.EndsWith(".tar.gz"))
                        {
                            GZipStream zipStream = new(stream, CompressionMode.Decompress);

                            var archive = TarArchive.CreateInputTarArchive(zipStream, Encoding.UTF8);
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
                    using var fileStream = File.Open(saveLocation, FileMode.Create);
                    await DownloadToStreamWithProgressAsync(downloadUrl, fileStream);
                }

                // chmod to make the downloaded executable actually usable if on mac or linux
                if (_isUnix && fileInfo.IsExecutable)
                {
                    await MakeExecutable(saveLocation);
                }

                // Write that the file has been fully downloaded
                // This is used instead of just checking that it exists to avoid exiting part way through causing a corrupted file
                _fullyDownloaded.Add(fileType);
                await SaveFullyDownloaded();
                succeeded = true;
                Log.Information("Downloaded {Name}", fileInfo.Name);
            }
            catch (HttpRequestException ex)
            {
                Log.Warning(ex, "Failed to download {Name} from {Source}", fileInfo.Name, downloadUrl);
            }
            finally
            {
                DownloadingFileName = null;
                IsExtracting = false;
            }
            return succeeded;
        }

        /// <summary>
        /// Downloads the file from the given URL, and copies the data into the given stream, while updating the download progress.
        /// </summary>
        /// <param name="url">The url to download from</param>
        /// <param name="copyTo">The stream to copy the data to</param>
        /// <returns>The content headers returned with the file in the HTTP response.</returns>
        private async Task<HttpContentHeaders> DownloadToStreamWithProgressAsync(string url, Stream copyTo)
        {
            const int BufferSize = 4096;

            using var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            using var respStream = await resp.Content.ReadAsStreamAsync();

            // Attempt to find the content length from the headers
            var contentHeaders = resp.Content.Headers;
            int contentLength = contentHeaders.Contains("Content-Length") ? int.Parse(contentHeaders.GetValues("Content-Length").First()) : -1;
            DownloadProgress = 0.0;
            DownloadProgressKnown = contentLength != -1;

            // Copy the response to the given stream
            try
            {
                byte[] buffer = new byte[BufferSize];
                int bytesRead;
                int totalBytesRead = 0;
                while ((bytesRead = await respStream.ReadAsync(buffer)) != 0)
                {
                    await copyTo.WriteAsync(buffer, 0, bytesRead);

                    // Update progress if we know the content length
                    if (contentLength != -1)
                    {
                        totalBytesRead += bytesRead;
                        DownloadProgress = totalBytesRead * 100.0 / contentLength;
                    }
                }

                return contentHeaders;
            }
            finally
            {
                DownloadProgressKnown = false;
                DownloadProgress = 0.0;
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
        /// <param name="clearExisting">Whether to delete the file if it already existed.</param>
        /// <returns>The location of the file</returns>
        /// <exception cref="FileDownloadFailedException">If downloading the file failed with every known URL</exception>
        public async Task<string> GetFileLocation(ExternalFileType fileType, bool clearExisting = false)
        {
            var fileInfo = _fileTypes[fileType];

            // Store that the existing file has been cleared.
            if (clearExisting)
            {
                if (_fullyDownloaded.Remove(fileType))
                {
                    await SaveFullyDownloaded();
                }
            }

            // The save location is relative to the extract folder if requires extraction, otherwise it's just relative to the tools folder
            string saveLocation;
            if (fileInfo.ExtractionFolder == null)
            {
                saveLocation = Path.Combine(_specialFolders.ToolsFolder, fileInfo.SaveName.Value);
                if (clearExisting && File.Exists(saveLocation))
                {
                    File.Delete(saveLocation);
                }
            }
            else
            {
                string extractLocation = Path.Combine(_specialFolders.ToolsFolder, fileInfo.ExtractionFolder);
                if (clearExisting && Directory.Exists(extractLocation))
                {
                    Directory.Delete(extractLocation, true);
                }
                saveLocation = Path.Combine(extractLocation, fileInfo.SaveName.Value);
            }

            if (!_fullyDownloaded.Contains(fileType) || !File.Exists(saveLocation))
            {
                await DownloadFileWithMirrors(fileType, fileInfo, (await PrepareDownloadUrls())[fileType], saveLocation);
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
        /// <exception cref="FileDownloadFailedException">If downloading the file failed</exception>
        /// <returns>The content headers returned with the file in the HTTP response.</returns>
        public async Task<HttpContentHeaders> DownloadUri(string url, string saveName, string? overrideFileName = null)
        {
            try
            {
                DownloadingFileName = overrideFileName ?? Path.GetFileName(saveName);

                using var fileStream = File.Open(saveName, FileMode.Create);
                return await DownloadToStreamWithProgressAsync(url, fileStream);
            }
            catch (HttpRequestException ex)
            {
                throw new FileDownloadFailedException($"Failed to download {DownloadingFileName}", ex);
            }
            finally
            {
                DownloadingFileName = null;
            }
        }

        /// <summary>
        /// Clears all downloaded files.
        /// Used in quick fix in case the files are corrupted
        /// </summary>
        public async Task ClearCache()
        {
            Log.Information("Clearing downloaded file cache . . .");
            await Task.Run(() =>
            {
                Log.Debug("Deleting {FolderPath} . . .", _specialFolders.ToolsFolder);
                Directory.Delete(_specialFolders.ToolsFolder, true); // Also deletes the saved downloaded files file
                _fullyDownloaded.Clear();
            });
            Log.Information("Recreating tools folder . . .");
            Directory.CreateDirectory(_specialFolders.ToolsFolder);
        }
    }
}
