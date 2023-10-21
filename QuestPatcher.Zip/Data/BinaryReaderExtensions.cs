namespace QuestPatcher.Zip.Data
{
    internal static class BinaryReaderExtensions
    {
        /// <summary>
        /// Reads a string from a ZIP record.
        /// </summary>
        /// <param name="length">Length of the string, in bytes</param>
        /// <param name="flags">The general purpose flags from whichever record this string is being read.
        /// These are necessary to check if the string must be read in UTF-8 format</param>
        /// <returns></returns>
        public static string ReadZipString(this BinaryReader reader, int length, EntryFlags flags)
        {
            byte[] bytes = reader.ReadBytes(length);

            return flags.GetStringEncoding().GetString(bytes);
        }
    }
}
