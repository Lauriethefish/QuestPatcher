using System.Text;

namespace QuestPatcher.Zip.Data
{
    internal static class EntryFlagsExtensions
    {
        private static readonly Encoding _codePage437; // IBM code page 437

        static EntryFlagsExtensions()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _codePage437 = Encoding.GetEncoding(437);
        }

        /// <summary>
        /// Gets the string encoding for a ZIP record with the given flags.
        /// </summary>
        /// <param name="entryFlags">The general purpose bit flags of a ZIP record</param>
        /// <returns>The encoding used from strings in the record</returns>
        public static Encoding GetStringEncoding(this EntryFlags entryFlags)
        {
            if (entryFlags.HasFlag(EntryFlags.UsesUtf8))
            {
                return Encoding.UTF8;
            }
            else
            {
                return _codePage437;
            }
        }
    }
}
