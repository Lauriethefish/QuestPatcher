using System.IO;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Used to store information about the state of the document while saving AXML.
    /// </summary>
    internal class SavingContext
    {
        public StringPool StringPool { get; } = new StringPool();

        public ResourceMap ResourceMap { get; } = new ResourceMap();

        /// <summary>
        /// The writer for the main section of the document, which is written to memory.
        /// The main section is written to a <see cref="MemoryStream"/>> because the string/resource pool size determines the length at the beginning of the document.
        /// Therefore it is impossible to know what we need to write in the pools without first writing the latter part of the document.
        ///
        /// This could be also done by an initial pass that adds all the strings to the pool, but this would add a lot of extra code, even though the memory usage would be more efficient.
        /// </summary>
        public BinaryWriter Writer { get; } = new BinaryWriter(new MemoryStream());
    }
}
