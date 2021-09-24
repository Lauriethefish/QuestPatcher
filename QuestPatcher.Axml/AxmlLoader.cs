using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Class for loading AXML files.
    /// </summary>
    public static class AxmlLoader
    {
        private struct QueuedNamespace
        {
            public string? Prefix { get; }
            public Uri Uri { get; }

            public QueuedNamespace(string? prefix, Uri uri)
            {
                Prefix = prefix;
                Uri = uri;
            }
        }
        
        /// <summary>
        /// Loads an AXML document from the given stream.
        /// The stream must be seekable.
        /// </summary>
        /// <param name="stream">The stream to load from, must be seekable</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">If the given stream is not seekable</exception>
        /// <exception cref="AxmlParseException">Any errors in the AXML format of the file</exception>
        public static AxmlElement LoadDocument(Stream stream)
        {
            if (!stream.CanSeek)
            {
                throw new ArgumentException("Cannot read axml from non-seekable stream");
            }

            BinaryReader input = new BinaryReader(stream);
            if (input.ReadResourceType() != ResourceType.Xml)
            {
                throw new AxmlParseException("Initial tag was not xml");
            }

            int fileSize = input.ReadInt32();

            string[]? stringPool = null;
            int[]? resourceMap = null;

            Stack<AxmlElement> elementStack = new Stack<AxmlElement>();
            List<QueuedNamespace> queuedNamespaces = new List<QueuedNamespace>();
            AxmlElement? rootElement = null;
            
            int preChunkPosition = 8; // Already gone past two ints for initial XML tag and file size
            while(preChunkPosition < fileSize)
            {
                ResourceType chunkType = input.ReadResourceType();
                int chunkLength = input.ReadInt32();

                if (stringPool == null && chunkType != ResourceType.StringPool)
                {
                    throw new AxmlParseException("String pool must be located after Xml tag");
                }


                int currentLineNumber;
                switch (chunkType)
                {
                    // The string pool must come before any elements, a check for this is above
                    case ResourceType.StringPool:
                        stringPool = StringPoolSerializer.LoadStringPool(input);
                        break;
                    case ResourceType.XmlResourceMap:
                        // Divide by 4 because the resource map is made up of integers, subtract 2 for the resource type and length values
                        int resourceCount = chunkLength / 4 - 2;
                        resourceMap = new int[resourceCount];
                        for (int i = 0; i < resourceCount; i++)
                        {
                            resourceMap[i] = input.ReadInt32();
                        }

                        break;
                    case ResourceType.XmlStartNamespace:
                        input.ReadInt32(); // Line number, currently unused
                        if (input.ReadUInt32() != 0xFFFFFFFF)
                        {
                            throw new AxmlParseException("Expected 0xFFFFFFFF");
                        }

                        Debug.Assert(stringPool != null);
                        int prefixId = input.ReadInt32();
                        string? prefix = prefixId == -1 ? null : stringPool[prefixId];

                        string uriString = stringPool[input.ReadInt32()];
                        Uri uri = ParseNamespaceUri(uriString);
                        
                        queuedNamespaces.Add(new QueuedNamespace(prefix, uri));
                        break;
                    case ResourceType.XmlEndNamespace:
                        break;
                    case ResourceType.XmlStartElement:
                        currentLineNumber = input.ReadInt32();
                        if (input.ReadUInt32() != 0xFFFFFFFF)
                        {
                            throw new AxmlParseException("Expected 0xFFFFFFFF");
                        }

                        Debug.Assert(stringPool != null);
                        int namespaceId = input.ReadInt32(); // -1 means no namespace prefix, so default namespace
                        string elementName = stringPool[input.ReadInt32()];
                        if (input.ReadUInt32() != 0x00140014)
                        {
                            throw new AxmlParseException("Expected 0x00140014");
                        }
                        
                        AxmlElement childElement = new AxmlElement(elementName, namespaceId == -1 ? null : ParseNamespaceUri(stringPool[namespaceId]), currentLineNumber);

                        int numAttributes = input.ReadInt16();
                        int idAttributeIndex = input.ReadInt16() - 1;
                        int classAttributeIndex = input.ReadInt16() - 1;
                        int styleAttributeIndex = input.ReadInt16() - 1;
                        for (int i = 0; i < numAttributes; i++)
                        {
                            int attrNamespaceId = input.ReadInt32();
                            Uri? attrNamespace = attrNamespaceId == -1 ? null : ParseNamespaceUri(stringPool[attrNamespaceId]);

                            int attrNameAndResourceIdIndex = input.ReadInt32();

                            string attrName = stringPool[attrNameAndResourceIdIndex];
                            int? attrResourceId = null;
                            
                            if (resourceMap == null)
                            {
                                throw new AxmlParseException(
                                    $"Attempted to access resource ID with index {attrNameAndResourceIdIndex} when the resource pool chunk had not yet been received");
                            }
                            if (attrNameAndResourceIdIndex >= 0 && attrNameAndResourceIdIndex < resourceMap.Length)
                            {
                                attrResourceId = resourceMap[attrNameAndResourceIdIndex];
                            }

                            int attrRawStringIndex = input.ReadInt32();
                            AttributeType attrType = (AttributeType) (input.ReadInt32() >> 24); // The first byte contains the actual type, so we shift this to the right
                            int attrRawValue = input.ReadInt32();

                            object value;
                            if (i == idAttributeIndex)
                            {
                                value = new WrappedValue(WrappedValueType.Id, stringPool[attrRawStringIndex], attrRawValue);
                            }
                            else if(i == classAttributeIndex)
                            {
                                value = new WrappedValue(WrappedValueType.Class, stringPool[attrRawStringIndex], attrRawValue);
                            }   else if (i == styleAttributeIndex)
                            {
                                value = new WrappedValue(WrappedValueType.Style, stringPool[attrRawStringIndex], attrRawValue);
                            }   else if (attrType == AttributeType.Reference)
                            {
                                value = new WrappedValue(WrappedValueType.Reference, null, attrRawValue);
                            }
                            else if(attrType == AttributeType.String)
                            {
                                value = stringPool[attrRawValue];
                            }   else if (attrType == AttributeType.Boolean)
                            {
                                value = attrRawValue != 0;
                            }
                            else
                            {
                                value = attrRawValue;
                            }

                            childElement.Attributes.Add(new AxmlAttribute(attrName, attrNamespace, attrResourceId, value, attrType));
                        }

                        // Add the namespaces of any parent StartNamespace resources to this element
                        foreach (QueuedNamespace ns in queuedNamespaces)
                        {
                            if (ns.Prefix == null) // No prefix means that this is a default namespace
                            {
                                childElement.DeclaredDefaultNamespace = ns.Uri;
                            }
                            else
                            {
                                childElement.DeclaredNamespaces[ns.Prefix] = ns.Uri;
                            }
                        }
                        queuedNamespaces.Clear();

                        // Add the child element to the current bottom-most element in the stack.
                        if (elementStack.Count == 0)
                        {
                            if (rootElement != null)
                            {
                                throw new AxmlParseException("Document contained multiple root elements");
                            }
                            rootElement = childElement;
                        }
                        else
                        {
                            elementStack.Peek().Children.Add(childElement);
                        }
                        // Set this element as the bottom-most element
                        elementStack.Push(childElement);
                        
                        
                        break;
                    case ResourceType.XmlEndElement:
                        int lineNumber = input.ReadInt32();
                        elementStack.Pop().ClosingTextLineNumber = lineNumber; // Current bottom-most element is now the next element up
                        break;
                    case ResourceType.XmlCdata:
                        input.ReadInt32(); // Line number, currently unused
                        if (input.ReadUInt32() != 0xFFFFFFFF)
                        {
                            throw new AxmlParseException("Expected 0xFFFFFFFF");
                        }

                        input.ReadInt32(); // TODO: ID of "text" value. Currently unused
                        
                        // TODO: Unused bytes. (figure out what they are)
                        input.ReadInt32();
                        input.ReadInt32();

                        break;
                    default:
                        throw new AxmlParseException("Unknown chunk type: " + chunkType);
                }

                input.BaseStream.Position = preChunkPosition + chunkLength;
                preChunkPosition = (int) input.BaseStream.Position;
            }

            if (rootElement == null)
            {
                throw new AxmlParseException("Document did not contain a root element");
            }

            return rootElement;
        }

        /// <summary>
        /// Parses a namespace URI, wrapping any parse failures in <see cref="AxmlParseException"/>
        /// </summary>
        /// <param name="uriString">The string of the URI to parse</param>
        /// <returns>The URI that represents the given string</returns>
        /// <exception cref="AxmlParseException">If any parsing errors occur. See inner exception for details</exception>
        private static Uri ParseNamespaceUri(string uriString)
        {
            try
            {
                return new Uri(uriString);
            }
            catch (UriFormatException ex)
            {
                throw new AxmlParseException(
                    $"Failed to parse URI {uriString} of namespace", ex);
            }
        }
    }
}
