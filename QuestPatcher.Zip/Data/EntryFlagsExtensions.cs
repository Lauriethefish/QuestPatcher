using System.Text;

namespace QuestPatcher.Zip.Data
{
    internal static class EntryFlagsExtensions
    {
        /// <summary>
        /// Gets the string encoding for a ZIP record with the given flags.
        /// </summary>
        /// <param name="entryFlags">The general purpose bit flags of a ZIP record</param>
        /// <returns>The encoding used from strings in the record</returns>
        public static Encoding GetStringEncoding(this EntryFlags entryFlags)
        {
            if(entryFlags.HasFlag(EntryFlags.UsesUtf8))
            {
                return Encoding.UTF8;
            }   else {
                // TODO: Apparently not supported on .NET core. Using UTF8 as a temporary workaround.
                /*return Encoding.GetEncoding(437); // IBM Code Page 437*/

                return Encoding.UTF8;
            }
        }
    }
}
