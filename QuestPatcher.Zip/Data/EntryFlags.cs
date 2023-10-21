namespace QuestPatcher.Zip.Data
{
    [Flags]
    internal enum EntryFlags : short
    {
        // TODO: Implement some more of these flags if we add support for more compression algorithms
        // Note: we probably won't, as APKs don't support them.
        UsesDataDescriptor = 1 << 3,
        UsesUtf8 = 1 << 11
    }
}
