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
            for(int addr = offset; addr < offset + length; addr++)
            {
                _crc ^= data[addr];

                for(int i = 0; i < 8; i++)
                {
                    if((_crc & 1) == 1)
                    {
                        _crc = (_crc >> 1) ^ ZipCrcPolynomial;
                    }
                    else
                    {
                        _crc >>= 1;
                    }
                }
            }
        }
    }
}
