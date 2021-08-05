namespace QuestPatcher.Axml
{
    /// <summary>
    /// The type of the value that a wrapped value holds.
    /// </summary>
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
        /// Raw underlying string value that represents this value, <code>null</code> if a reference value.
        /// </summary>
        public string? RawValue { get; }
        
        /// <summary>
        /// ID of the value this wrapped value refers to. Not quite sure what this points to as of now
        /// </summary>
        public int ReferenceId { get; }

        /// <summary>
        /// Creates a new wrapped AXML value
        /// </summary>
        /// <param name="type">Type of the underlying value</param>
        /// <param name="rawValue">Raw string value</param>
        /// <param name="referenceId">ID of the value this wrapped value refers to</param>
        public WrappedValue(WrappedValueType type, string? rawValue, int referenceId)
        {
            Type = type;
            RawValue = rawValue;
            ReferenceId = referenceId;
        }
    }
}