using System;
using System.Threading.Tasks;

namespace QuestPatcher.Zip.Data
{
    /// <summary>
    /// A ZIP timestamp, equivalent to MS-DOS format
    /// </summary>
    internal struct Timestamp
    {
        public ushort TimeShort { get; set; }

        public ushort DateShort { get; set; }

        public DateTime? DateTime
        {
            get
            {
                if (TimeShort == 0 && DateShort == 0)
                {
                    return null;
                }

                int second = (TimeShort & 0b11111) << 1;
                int minute = (TimeShort >> 5) & 0b111111;
                int hour = (TimeShort >> 11) & 0b11111;

                int day = DateShort & 0b11111;
                int month = (DateShort >> 5) & 0b1111;
                int year = (DateShort >> 9) + 1980;

                var dateTime = new DateTime(year, month, day);


                dateTime.AddHours(hour);
                dateTime.AddMinutes(minute);
                dateTime.AddSeconds(second);

                return dateTime;
            }
            set
            {
                if (value == null)
                {
                    TimeShort = 0;
                    DateShort = 0;
                    return;
                }

                int time = 0;
                var dateTime = (DateTime) value;

                time |= dateTime.Hour;
                time <<= 6;
                time |= dateTime.Minute;
                time <<= 5;
                time |= (dateTime.Second >> 1);

                int date = 0;
                int correctedYear = dateTime.Year - 1980;

                if (correctedYear > 127)
                {
                    correctedYear = 0; // Could throw an exception, but I don't really want to introduce a ticking time bomb
                }

                date |= correctedYear;
                date <<= 4;
                date |= dateTime.Month;
                date <<= 5;
                date |= dateTime.Day;

                DateShort = (ushort) date;
                TimeShort = (ushort) time;
            }
        }

        public static Timestamp Read(ZipMemory reader)
        {
            return new Timestamp()
            {
                TimeShort = reader.ReadUInt16(),
                DateShort = reader.ReadUInt16(),
            };
        }

        public void Write(ZipMemory writer)
        {
            writer.Write(TimeShort);
            writer.Write(DateShort);
        }

        public static async Task<Timestamp> ReadAsync(ZipMemory reader)
        {
            return new Timestamp()
            {
                TimeShort = await reader.ReadUInt16Async(),
                DateShort = await reader.ReadUInt16Async(),
            };
        }

        public async Task WriteAsync(ZipMemory writer)
        {
            await writer.WriteAsync(TimeShort);
            await writer.WriteAsync(DateShort);
        }
    }
}
