﻿using System.IO;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Class for saving AXML files.
    /// </summary>
    public static class AxmlSaver
    {
        /// <summary>
        /// Saves the given root element to the given stream as AXML.
        /// </summary>
        /// <param name="stream">Stream to save to</param>
        /// <param name="rootElement">Root element of the document</param>
        public static void SaveDocument(Stream stream, AxmlElement rootElement)
        {
            var mainOutput = new BinaryWriter(stream);

            // Write the main elements chunk of the file to a MemoryStream first
            var ctx = new SavingContext();
            rootElement.PreparePooling(ctx);
            string[] stringPool = ctx.StringPool.PrepareForSavePhase(ctx.ResourceMap);

            rootElement.Save(ctx);
            var mainChunkStream = (MemoryStream) ctx.Writer.BaseStream;


            int[] resourcePool = ctx.ResourceMap.Save();

            int stringPoolLength = StringPoolSerializer.CalculatePoolLength(stringPool);
            int stringPoolPadding = (4 - stringPoolLength % 4) % 4;
            stringPoolLength += stringPoolPadding; // Add padding to four bytes

            int resourcePoolLength = resourcePool.Length * 4; // Each pool item is an integer

            // The length of the main xml tag is that of the whole file, so also including the string pool, resource pool, and main chunk. (+ extra 8 + 8 = 16 bytes for string pool and resource pool header)
            mainOutput.WriteChunkHeader(ResourceType.Xml, stringPoolLength + resourcePoolLength + (int) mainChunkStream.Position + 16);

            mainOutput.WriteChunkHeader(ResourceType.StringPool, stringPoolLength);
            StringPoolSerializer.SaveStringPool(stringPool, mainOutput);
            for (int i = 0; i < stringPoolPadding; i++)
            {
                mainOutput.Write((byte) 0);
            }

            mainOutput.WriteChunkHeader(ResourceType.XmlResourceMap, resourcePoolLength);
            foreach (int resource in resourcePool)
            {
                mainOutput.Write(resource);
            }

            // Save the main chunk of the file
            mainChunkStream.Position = 0;
            mainChunkStream.CopyTo(stream);
        }
    }
}
