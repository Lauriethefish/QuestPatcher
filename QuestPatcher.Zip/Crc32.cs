namespace QuestPatcher.Zip
{
    internal class Crc32
    {
        /// <summary>
        /// The polynomial used for the ZIP CRC
        /// </summary>
        private static uint ZipCrcPolynomial = 0xEDB88320;

        public uint Current => _crc ^ 0xFFFFFFFF;
        private uint _crc = 0xFFFFFFFF;

        internal void Update(byte[] data, int offset, int length)
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
