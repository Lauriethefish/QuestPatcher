using System.IO;
using System.Text;

namespace QuestPatcher.Axml
{
    /// <summary>
    /// Class for loading and saving the AXML string pool format.
    /// Does not currently support styles.
    /// </summary>
    internal static class StringPool
    {
        /// <summary>
        /// Set if the string pool in the manifest is UTF-8 instead of UTF-16 (the default)
        /// </summary>
        private const int Utf8Flag = 0x00000100;
        
        /// <summary>
        /// Loads the string pool from the binary data.
        /// Does not current load files.
        /// </summary>
        /// <param name="input">The reader to read the string pool from</param>
        /// <returns>The pool as a string array</returns>
        internal static string[] LoadStringPool(BinaryReader input)
        {
            int beginChunkOffset = (int) input.BaseStream.Position - 8;
            int numStrings = input.ReadInt32();
            input.ReadInt32(); // Style offset count, styles are currently unimplemented
            
            int flags = input.ReadInt32();
            bool useUtf8 = (flags & Utf8Flag) != 0;
            
            int stringsOffset = input.ReadInt32();
            input.ReadInt32(); // Style offset count, styles are currently unimplemented

            int[] stringBeginOffsets = new int[numStrings];
            for (int i = 0; i < numStrings; i++)
            {
                stringBeginOffsets[i] = input.ReadInt32();
            }

            string[] result = new string[numStrings];
            
            int stringsBeginning = beginChunkOffset + stringsOffset;
            for(int i = 0; i < numStrings; i++)
            {
                input.BaseStream.Position = stringsBeginning + stringBeginOffsets[i];

                string currentStr;
                if (useUtf8)
                {
                    ReadUtf8Length(input); // Ignored, we just need to get past this varint
                    int statedLength = ReadUtf8Length(input);

                    MemoryStream stringStream = new MemoryStream();
                    BinaryWriter stringWriter = new BinaryWriter(stringStream);
                    stringWriter.Write(input.ReadBytes(statedLength));
                    while(true)
                    {
                        byte b = input.ReadByte();
                        if (b == 0)
                        {
                            break;
                        }
                        
                        stringWriter.Write(b);
                    }

                    currentStr =  Encoding.UTF8.GetString(stringStream.ToArray());
                }
                else
                {
                    int length = ReadUtf16Length(input);
                    // Read string as UTF16_LE
                    byte[] test = input.ReadBytes(length * 2);
                    currentStr = Encoding.Unicode.GetString(test);
                }

                result[i] = currentStr;
            }

            return result;
        }

        /// <summary>
        /// Calculates the length that the given string pool will take up when written (not including the resource type and length prefix)
        /// </summary>
        /// <param name="pool">The pool to calculate</param>
        /// <returns>The length in bytes</returns>
        internal static int CalculatePoolLength(string[] pool)
        {
            int stringsLength = 0;
            foreach (string str in pool)
            {
                stringsLength += CalculatePooledStringLength(str);
            }

            return 20 + pool.Length * 4 + stringsLength;
        }

        /// <summary>
        /// Saves the given strings as a string pool.
        /// Assumes that the <see cref="ResourceType"/> and length prefix have already been sent
        /// </summary>
        /// <param name="pool"></param>
        /// <param name="writer"></param>
        internal static void SaveStringPool(string[] pool, BinaryWriter writer)
        {
            writer.Write(pool.Length);
            writer.Write(0); // Style count, but we do not implement styles
            writer.Write(0); // We do not use UTF-8 at the moment, UTF-16 is always used instead. If we were using UTF-8, this would be the Utf8Flag.
            
            // This value is the offset from the beginning of the chunk to the strings
            // The initial 7 integers are resource type, chunk length, number of strings (pool length), style count (unused), UTF-8 flag (never set to non-zero), this value and 0 (the last I am unsure about).
            // Then we have 1 integer per item in the pool, as the offset needs to be stored.
            writer.Write(7 * 4 + pool.Length * 4);
            writer.Write(0);
            
            int currentStringPos = 0;
            foreach (string str in pool)
            {
                writer.Write(currentStringPos);
                currentStringPos += CalculatePooledStringLength(str);
            }
            
            foreach (string str in pool)
            {
                WriteUtf16Length(writer, str);
                writer.Write(Encoding.Unicode.GetBytes(str));
                writer.Write((byte) 0);
                writer.Write((byte) 0);
            }
        }

        /// <summary>
        /// Reads the UTF-8 length format in axml, which is 1-2 bytes depending on size.
        /// </summary>
        /// <param name="input">Stream to read from</param>
        /// <returns>Length of the proceeding UTF-8 string</returns>
        private static int ReadUtf8Length(BinaryReader input)
        {
            int length = input.ReadByte();
            if ((length & 0x80) != 0) // Next byte required
            {
                // Move the previous byte to the left, and add the second length byte to the end
                length = ((length & 0x7F) << 8) | input.ReadByte();
            }

            return length;
        }

        /// <summary>
        /// Reads the UTF-16 length format in axml, which is 2-4 bytes depending on size.
        /// </summary>
        /// <param name="input">Stream to read from</param>
        /// <returns>Length of the proceeding UTF-16 string</returns>
        private static int ReadUtf16Length(BinaryReader input)
        {
            int length = input.ReadInt16();
            
            if (length > 0x7FFF)  { // Second two bytes required
                // Move the previous bytes to the left, and add the second two bytes to the end
                length = ((length & 0x7FFF) << 8) | input.ReadUInt16(); 
            }
            return length;
        }

        /// <summary>
        /// Writes the axml UTF-16 length of the given string. (2-4 bytes depending on size)
        /// </summary>
        /// <param name="output">Writer to write to</param>
        /// <param name="str">String to write the length of</param>
        private static void WriteUtf16Length(BinaryWriter output, string str)
        {
            if (str.Length > 0x7FFF) {
                int x = (str.Length >> 16) | 0x8000;
                output.Write((byte) x);
                output.Write((byte) (x >> 8));
            }
            output.Write((byte) str.Length);
            output.Write((byte) (str.Length >> 8));
        }

        private static int CalculatePooledStringLength(string str)
        {
            return (str.Length > 0x7FFF ? 4 : 2) + 2 + Encoding.Unicode.GetByteCount(str);
        }
    }
}