using System.IO;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Class with read/write methods convenient for AXML handling
    /// </summary>
    internal static class BinaryStreamExtensions
    {
        internal static ResourceType ReadResourceType(this BinaryReader reader)
        {
            int t = reader.ReadInt32();
            return (ResourceType) (t & 0xFFFF);
        }

        internal static void WriteChunkHeader(this BinaryWriter writer, ResourceType typeEnum, int length = 0)
        {
            length += 8; // Length should include type and itself (two integers, so 8 extra bytes)

            int typePrefix = typeEnum switch
            {
                ResourceType.Xml => 0x0008,
                ResourceType.StringPool => 0x001C,
                ResourceType.XmlResourceMap => 0x0008,
                _ => 0x0010
            };
            writer.Write((int) typeEnum | typePrefix << 16);
            writer.Write(length);
        }
    }
}