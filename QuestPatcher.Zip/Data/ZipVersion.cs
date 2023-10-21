namespace QuestPatcher.Zip.Data
{
    internal struct ZipVersion
    {
        public byte Major { get; set; }

        public byte Minor { get; set; }

        public static ZipVersion Read(BinaryReader reader)
        {
            return new ZipVersion()
            {
                Major = reader.ReadByte(),
                Minor = reader.ReadByte()
            };
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Major);
            writer.Write(Minor);
        }

        public override string ToString()
        {
            return $"{Major}.{Minor}";
        }
    }
}
