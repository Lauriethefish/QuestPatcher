using System.Text;

namespace QuestPatcher.Zip.Data
{
    internal static class EntryFlagsExtensions
    {
        private static readonly Encoding _codePage437; // IBM code page 437

        static EntryFlagsExtensions()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _codePage437 = Encoding.GetEncoding(437);
            }
            catch
            {
                // Fallback to ASCII if loading code page 437 fails
                // This sometimes happens on Xamarin targets if the user has not added the correct internationalisation assemblies.
                _codePage437 = Encoding.ASCII;
            }
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
