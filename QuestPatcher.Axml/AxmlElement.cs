using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Represents an AXML element.
    /// </summary>
    public class AxmlElement
    {
        /// <summary>
        /// Axml text line number read from the parser.
        /// TODO: Automatically set this to a sensible value upon saving?
        /// </summary>
        public int TextLineNumber { get; set; }

        /// <summary>
        /// Attributes of this element.
        /// </summary>
        public List<AxmlAttribute> Attributes { get; } = new List<AxmlAttribute>();

        /// <summary>
        /// Child elements of this element
        /// </summary>
        public List<AxmlElement> Children { get; } = new List<AxmlElement>();

        /// <summary>
        /// Any namespace declared within this element.
        /// Keys are namespace prefixes, values are URIs.
        /// </summary>
        public Dictionary<string, Uri> DeclaredNamespaces { get; } = new Dictionary<string, Uri>();

        /// <summary>
        /// The URI of a namespace declared as the default namespace for this element and all child elements
        /// </summary>
        public Uri? DeclaredDefaultNamespace { get; set; }

        /// <summary>
        /// The name of the element, not including the namespace prefix
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The URI of the namespace in the name of the element.
        /// </summary>
        public Uri? NamespaceUri { get; set; }

        /// <summary>
        /// Creates a new element.
        /// </summary>
        /// <param name="name">The name of the element, without the namespace prefix</param>
        /// <param name="namespaceUri">URI of the namespace that this element is in. Set to <code>null</code> (leave at default value) for no namespace.</param>
        /// <param name="textLineNumber">Line number of the element, can safely be left at the default value of <code>0</code> - Android will still load elements with out-of-order line numbers.</param>
        public AxmlElement(string name, Uri? namespaceUri = null, int textLineNumber = 0)
        {
            TextLineNumber = textLineNumber;
            Name = name;
            NamespaceUri = namespaceUri;
        }

        internal void PrepareResourceIndices(SavingContext ctx)
        {
            foreach (AxmlAttribute attribute in Attributes)
            {
                attribute.PrepareResourceIndices(ctx);
            }

            foreach (AxmlElement element in Children)
            {
                element.PrepareResourceIndices(ctx);
            }
        }

        internal void Save(SavingContext ctx)
        {
            // First we need to write the namespaces declared within this element
            foreach (KeyValuePair<string, Uri> pair in DeclaredNamespaces)
            {
                ctx.Writer.WriteChunkHeader(ResourceType.XmlStartNamespace, 16); // Each namespace tag is 3 integers, so 3 * 4 = 12 bytes
                ctx.Writer.Write(TextLineNumber);
                ctx.Writer.Write(0xFFFFFFFF);
                ctx.Writer.Write(ctx.StringPool.GetIndex(pair.Key));
                ctx.Writer.Write(ctx.StringPool.GetIndex(pair.Value.ToString()));
            }

            ctx.Writer.WriteChunkHeader(ResourceType.XmlStartElement, 28 + 20 * Attributes.Count); // Each attribute is 5 integers, so 5 * 4 = 20 bytes of the tag
            ctx.Writer.Write(TextLineNumber);
            ctx.Writer.Write(0xFFFFFFFF);
            ctx.Writer.Write(NamespaceUri == null ? -1 : ctx.StringPool.GetIndex(NamespaceUri.ToString()));
            ctx.Writer.Write(ctx.StringPool.GetIndex(Name));
            ctx.Writer.Write(0x00140014);

            // Find the ID, class and style attribute indices if they exist
            short idAttributeIndex = -1;
            short classAttributeIndex = -1;
            short styleAttributeIndex = -1;
            for (short i = 0; i < Attributes.Count; i++)
            {
                WrappedValue? wrappedValue = Attributes[i].Value as WrappedValue;
                if(wrappedValue == null) { continue; }

                // Make sure to prevent multiple of these attributes, as this will save incorrectly
                switch (wrappedValue.Type)
                {
                    case WrappedValueType.Id:
                        if (idAttributeIndex != -1) { throw new InvalidDataException("Cannot have multiple ID attributes on one element"); }
                        idAttributeIndex = i;
                        break;
                    case WrappedValueType.Class:
                        if (classAttributeIndex != -1) { throw new InvalidDataException("Cannot have multiple class attributes on one element"); }
                        classAttributeIndex = i;
                        break;
                    case WrappedValueType.Style:
                        if (styleAttributeIndex != -1) { throw new InvalidDataException("Cannot have multiple style attributes on one element"); }
                        styleAttributeIndex = i;
                        break;
                }
            }
            
            ctx.Writer.Write((short) Attributes.Count);
            // Stored indices are one above the actual ones
            ctx.Writer.Write((short) (idAttributeIndex + 1));
            ctx.Writer.Write((short) (classAttributeIndex + 1));
            ctx.Writer.Write((short) (styleAttributeIndex + 1));
            foreach(AxmlAttribute attribute in Attributes)
            {
                attribute.Save(ctx);
            }

            foreach (AxmlElement child in Children)
            {
                child.Save(ctx);
            }
            ctx.Writer.WriteChunkHeader(ResourceType.XmlEndElement, 16);
            ctx.Writer.Write(-1);
            ctx.Writer.Write(0xFFFFFFFF);
            ctx.Writer.Write(NamespaceUri == null ? -1 : ctx.StringPool.GetIndex(NamespaceUri.ToString()));
            ctx.Writer.Write(ctx.StringPool.GetIndex(Name));
            
            // End the namespaces stated by this element, as we have exited it
            foreach (KeyValuePair<string, Uri> pair in DeclaredNamespaces.Reverse())
            {
                ctx.Writer.WriteChunkHeader(ResourceType.XmlEndNamespace, 16); // Each namespace tag is 3 integers, so 3 * 4 = 12 bytes
                ctx.Writer.Write(TextLineNumber);
                ctx.Writer.Write(0xFFFFFFFF);
                ctx.Writer.Write(ctx.StringPool.GetIndex(pair.Key));
                ctx.Writer.Write(ctx.StringPool.GetIndex(pair.Value.ToString()));
            }
        }
    }
}