using System;
using System.Diagnostics;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Represents an AXML attribute.
    /// </summary>
    public class AxmlAttribute
    {
        /// <summary>
        /// Name of the attribute
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Uri of the namespace of this attribute
        /// <code>null</code> if it isn't in a namespace.
        /// </summary>
        public Uri? Namespace { get; set; }

        /// <summary>
        /// Resource ID of this attribute's value.
        /// This may refer to a resource inside the APK or is just a constant in cases like debuggable and legacyStorageSupport attributes.
        /// <code>null</code> if parsed resource ID index was <code>-1</code>
        /// </summary>
        public int? ResourceId { get; set; }

        /// <summary>
        /// The value of the attribute.
        /// May be <see cref="string" /> or <see cref="WrappedValue" />.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public object Value
        {
            get => _value;
            set
            {
                if (value is string)
                {
                    _valueType = AttributeType.String;
                }
                else if (value is WrappedValue)
                {
                    _valueType = null;
                }
                else if (value is bool)
                {
                    _valueType = AttributeType.Boolean;
                }
                else if (value is int)
                {
                    _valueType = AttributeType.FirstInt;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Cannot set value of axml attribute to type of {value.GetType().Name}: must be {nameof(WrappedValue)} or string");
                }

                _value = value;
            }
        }

        private object _value;
        private AttributeType? _valueType;

        internal AxmlAttribute(string name, Uri? ns, int? resourceId, object value, AttributeType valueType)
        {
            Name = name;
            Namespace = ns;
            ResourceId = resourceId;
            Value = value;
            _valueType = valueType;
            Debug.Assert(_value != null);
        }

        /// <summary>
        /// Creates a new attribute.
        /// </summary>
        /// <param name="name">The name of the attribute, without the namespace prefix</param>
        /// <param name="ns">The URI of the namespace this attribute is in, if any</param>
        /// <param name="resourceId">The resource ID of this attribute. This must be checked beforehand on the R class in an Android project, or by looking at existing resource IDs in a parsed manifest</param>
        /// <param name="value">The value of the attribute, supported types are <see cref="string"/>, <see cref="int"/>, <see cref="bool"/> and <see cref="WrappedValue"/></param>
        public AxmlAttribute(string name, Uri? ns, int? resourceId, object value)
        {
            Name = name;
            Namespace = ns;
            ResourceId = resourceId;
            Value = value;
            Debug.Assert(_value != null);
        }

        internal void PreparePooling(SavingContext ctx)
        {
            if(ResourceId != null)
            {
                ctx.ResourceMap.Add(Name, (int) ResourceId);
            }
            else
            {
                ctx.StringPool.Add(Name);
            }
            
            if(Namespace != null)
            {
                ctx.StringPool.Add(Namespace.ToString());
            }

            if (Value is WrappedValue wrappedValue)
            {
                if (wrappedValue.RawValue != null)
                {
                    ctx.StringPool.Add(wrappedValue.RawValue);
                }
            }    
            else if(Value is string asString)
            {
                ctx.StringPool.Add(asString);
            }
        }

        internal void Save(SavingContext ctx)
        {
            ctx.Writer.Write(Namespace == null ? -1 : ctx.StringPool.GetIndex(Namespace.ToString()));

            if(ResourceId != null)
            {
                int resourceIdIdx = ctx.ResourceMap.GetIndex(Name, (int) ResourceId);
                ctx.Writer.Write(resourceIdIdx);
            }
            else
            {
                ctx.Writer.Write(ctx.StringPool.GetIndex(Name));
            }
            
            int rawStringIndex = -1;
            int type = _valueType == null ? -1 : ((int)_valueType << 24) | 0x000008;
            int rawValue;
            if (Value is WrappedValue wrappedValue)
            {
                if (wrappedValue.RawValue != null)
                {
                    rawStringIndex = ctx.StringPool.GetIndex(wrappedValue.RawValue);
                }
                rawValue = wrappedValue.ReferenceId;
            }
            else if (Value is bool asBool)
            {
                rawValue = asBool ? -1 : 0;
            }
            else if (Value is string asString)
            {
                rawValue = ctx.StringPool.GetIndex(asString);
                rawStringIndex = rawValue;
            }
            else
            {
                rawValue = (int)Value;
            }

            ctx.Writer.Write(rawStringIndex);
            ctx.Writer.Write(type);
            ctx.Writer.Write(rawValue);
        }
    }
}
