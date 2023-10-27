namespace QuestPatcher.Zip
{
    /// <summary>
    /// Implementation of the Crc32 algorithm, for use in the ZIP saver.
    /// </summary>
    public class Crc32
    {
        /// <summary>
        /// The polynomial used for the ZIP CRC
        /// </summary>
        private static uint ZipCrcPolynomial = 0xEDB88320;

        private static uint[] Lookup = new uint[256];

        static Crc32()
        {
            for (uint b = 0; b < 256; b++)
            {
                uint current = b;

                for (int i = 0; i < 8; i++)
                {
                    if ((current & 1) == 1)
                    {
                        current = (current >> 1) ^ ZipCrcPolynomial;
                    }
                    else
                    {
                        current >>= 1;
                    }
                }

                Lookup[b] = current;
            }
        }

        /// <summary>
        /// The value of the Crc32 for the data that has been hashed thus far
        /// </summary>
        public uint Current => _crc ^ 0xFFFFFFFF;
        private uint _crc = 0xFFFFFFFF;

        /// <summary>
        /// Updates the current Crc32 with a series of bytes.
        /// </summary>
        /// <param name="data">Array containing the data</param>
        /// <param name="offset">Offset within the array to start reading from</param>
        /// <param name="length">Number of bytes of data to read</param>
        public void Update(byte[] data, int offset, int length)
        {
            for (int addr = offset; addr < offset + length; addr++)
            {
                _crc ^= data[addr];
                _crc = (_crc >> 8) ^ Lookup[_crc & 255];
            }
        }
    }
}
