namespace QuestPatcher.Axml
{
    /// <summary>
    /// Represents an AXML chunk type.
    /// </summary>
    internal enum ResourceType
    {
        StringPool = 0x0001,
        Table = 0x0002,
        PackageTable = 0x0200,
        TypeSpecTable = 0x0202,
        TypeTable = 0x0201,
        
        Xml = 0x0003,
        XmlResourceMap = 0x0180,
        XmlEndNamespace = 0x0101,
        XmlEndElement = 0x0103,
        XmlStartNamespace = 0x0100,
        XmlStartElement = 0x0102,
        XmlCdata = 0x0104
    }
}