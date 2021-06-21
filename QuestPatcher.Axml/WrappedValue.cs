namespace QuestPatcher.Axml
{
    public enum WrappedValueType
    {
        Id,
        Style,
        Class,
        Reference
    }
    
    /// <summary>
    /// Represents an AXML ID, Style or Class attribute value.
    /// These values have a reference ID and an underlying string value
    /// </summary>
    public class WrappedValue
    {
        /// <summary>
        /// Type of the underlying string value
        /// </summary>
        public WrappedValueType Type { get; }
        
        /// <summary>
        /// Raw underlying string value that represents this value.
        /// </summary>
        public string RawValue { get; }
        
        /// <summary>
        /// TODO: figure out what this is
        /// </summary>
        public int ReferenceId { get; }

        public WrappedValue(WrappedValueType type, string rawValue, int referenceId)
        {
            Type = type;
            RawValue = rawValue;
            ReferenceId = referenceId;
        }
    }
}