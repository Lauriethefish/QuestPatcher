using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using QuestPatcher.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Modding
{
    public class OtherFilesManager
    {
        public List<FileCopyType> CurrentDestinations { get => _copyIndex[_config.AppId]; }

        private readonly Dictionary<string, List<FileCopyType>> _copyIndex;

        private readonly Config _config;
        private readonly AndroidDebugBridge _debugBridge;

        public OtherFilesManager(Config config, AndroidDebugBridge debugBridge)
        {
            _config = config;
            _debugBridge = debugBridge;

            // Load the file copy paths from resources
            // I put them in there to allow for easier changing, although it makes things a little messier in here
            using Stream? pathsStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuestPatcher.Core.Resources.file-copy-paths.json");
            Debug.Assert(pathsStream != null);

            using TextReader textReader = new StreamReader(pathsStream);
            using JsonReader jsonReader = new JsonTextReader(textReader);
            JsonSerializer serializer = new()
            {
                ContractResolver = new DefaultContractResolver()
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                },
            };
            // File copies require a debug bridge reference, so we use a converter to pass this in
            serializer.Converters.Add(new FileCopyConverter(_debugBridge));

            var copyIndex = serializer.Deserialize<Dictionary<string, List<FileCopyType>>>(jsonReader);
            Debug.Assert(copyIndex != null);
            _copyIndex = copyIndex;
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
    }
}
