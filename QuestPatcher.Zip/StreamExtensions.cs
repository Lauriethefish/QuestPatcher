namespace QuestPatcher.Zip
{
    public static class StreamExtensions
    {
        /// <summary>
        /// Copies one stream to another, while calculating the Crc32 value of the source stream.
        /// </summary>
        /// <param name="source">The stream to copy from</param>
        /// <param name="destination">The stream to copy to. If null, the Crc32 will still be calculated, but no data will be written.</param>
        /// <param name="bufferSize">The size of the copying buffer</param>
        /// <returns>The Crc32 of source, as found in a ZIP file</returns>
        public static uint CopyToCrc32(this Stream source, Stream? destination, int bufferSize = 8192)
        {
            var buffer = new byte[bufferSize];
            var crc = new Crc32();

            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                destination?.Write(buffer, 0, bytesRead);
                crc.Update(buffer, 0, bytesRead);
            }

            return crc.Current;
        }
    }
}
