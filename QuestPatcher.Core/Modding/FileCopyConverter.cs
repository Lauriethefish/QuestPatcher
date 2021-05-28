using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Modding
{
    /// <summary>
    /// Used to pass in the extra parameters to a FileCopyType
    /// </summary>
    class FileCopyConverter : CustomCreationConverter<FileCopyType>
    {
        private readonly AndroidDebugBridge _debugBridge;

        public FileCopyConverter(AndroidDebugBridge debugBridge)
        {
            _debugBridge = debugBridge;
        }

        public override FileCopyType Create(Type objectType)
        {
            return new FileCopyType(_debugBridge);
        }
    }
}
