namespace QuestPatcher.Axml
{
    /// <summary>
    /// Represents the saved type of an attribute.
    /// Some of these types cannot be set manually, but they are just preserved for later saving if loaded.
    /// </summary>
    internal enum AttributeType
    {
        FirstInt = 0x10,
        Boolean = 0x12,
        Hex = 0x11,
        Reference = 0x01,
        String = 0x03
    }
}