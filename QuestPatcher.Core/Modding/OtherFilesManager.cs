using QuestPatcher.Core.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace QuestPatcher.Core.Modding
{
    public class OtherFilesManager : INotifyPropertyChanged
    {
        public ObservableCollection<FileCopyType> CurrentDestinations
        {
            get
            {
                // If file copy types for this app are available in the index, return those
                if (_copyIndex.TryGetValue(_config.AppId, out var copyTypes))
                {
                    return copyTypes;
                }
                // Otherwise, return a list of no types to avoid throwing exceptions/null
                return _noTypesAvailable;
            }
        }
        private readonly ObservableCollection<FileCopyType> _noTypesAvailable = new();

        private readonly Dictionary<string, ObservableCollection<FileCopyType>> _copyIndex;

        private readonly Config _config;

        public event PropertyChangedEventHandler? PropertyChanged;


        public OtherFilesManager(Config config, AndroidDebugBridge debugBridge)
        {
            _config = config;

            _config.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(_config.AppId))
                {
                    NotifyPropertyChanged(nameof(CurrentDestinations));
                }
            };

            // Load the file copy paths from resources
            // I put them in there to allow for easier changing, although it makes things a little messier in here
            using Stream? pathsStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuestPatcher.Core.Resources.file-copy-paths.json");
            Debug.Assert(pathsStream != null);

            var serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Deserialize the FileCopyInfo for each file copy
            var copyInfoIndex = JsonSerializer.Deserialize<Dictionary<string, List<FileCopyInfo>>>(pathsStream, serializerOptions);
            Debug.Assert(copyInfoIndex != null);

            // Copy those into ObservableCollections of FileCopyType, passing in the debug bridge to allow fetching the files of each type
            var copyIndex = new Dictionary<string, ObservableCollection<FileCopyType>>();
            foreach ((string key, var list) in copyInfoIndex)
            {
                copyIndex[key] = new ObservableCollection<FileCopyType>(list.Select(info => new FileCopyType(debugBridge, info)));
            }
            _copyIndex = copyIndex;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Gets the file copy destinations that can support files of the given extension
        /// </summary>
        /// <param name="extension"></param>
        /// <returns>The list of file copy destinations that work with the extension</returns>
        public List<FileCopyType> GetFileCopyTypes(string extension)
        {
            // Sanitise the extension to remove periods and make it lower case
            extension = extension.Replace(".", "").ToLower();

            return CurrentDestinations.Where(copyType => copyType.SupportedExtensions.Contains(extension)).ToList();
        }

        /// <summary>
        /// Adds the given file copy.
        /// </summary>
        /// <param name="packageId">The package ID that files of this type are intended for</param>
        /// <param name="type">The <see cref="FileCopyType"/> to add</param>
        public void RegisterFileCopy(string packageId, FileCopyType type)
        {
            if (!_copyIndex.TryGetValue(packageId, out var copyTypes))
            {
                copyTypes = new();
                _copyIndex[packageId] = copyTypes;
            }

            copyTypes.Add(type);
        }

        /// <summary>
        /// Removes the given file copy.
        /// </summary>
        /// <param name="packageId">The package ID that files of this type are intended for</param>
        /// <param name="type">The <see cref="FileCopyType"/> to remove</param>
        public void RemoveFileCopy(string packageId, FileCopyType type)
        {
            _copyIndex[packageId].Remove(type);
        }
    }
}
