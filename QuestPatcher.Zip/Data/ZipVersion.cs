using System.Threading.Tasks;

namespace QuestPatcher.Zip.Data
{
    internal struct ZipVersion
    {
        public byte Major { get; set; }

        public byte Minor { get; set; }

        public static ZipVersion Read(ZipMemory reader)
        {
            return new ZipVersion()
            {
                Major = reader.ReadByte(),
                Minor = reader.ReadByte()
            };
        }

        public void Write(ZipMemory writer)
        {
            writer.Write(Major);
            writer.Write(Minor);
        }

        public static async Task<ZipVersion> ReadAsync(ZipMemory reader)
        {
            return new ZipVersion()
            {
                Major = await reader.ReadByteAsync(),
                Minor = await reader.ReadByteAsync()
            };
        }

        public async Task WriteAsync(ZipMemory writer)
        {
            await writer.WriteAsync(Major);
            await writer.WriteAsync(Minor);
        }

        public override string ToString()
        {
            return $"{Major}.{Minor}";
        }
    }
}
