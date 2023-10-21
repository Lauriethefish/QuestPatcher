namespace QuestPatcher.Zip.Data
{
    /// <summary>
    /// Android APKs only support the Store and Deflate compression methods.
    /// </summary>
    internal enum CompressionMethod: short
    {
        Store = 0,
        Deflate = 8,
    }
}
