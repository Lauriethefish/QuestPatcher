using Newtonsoft.Json.Converters;
using System;

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
            return new(_debugBridge);
        }
    }
}
