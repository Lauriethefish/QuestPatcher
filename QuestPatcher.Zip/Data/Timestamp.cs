namespace QuestPatcher.Zip.Data
{
    /// <summary>
    /// A ZIP timestamp, equivalent to MS-DOS format
    /// </summary>
    internal struct Timestamp
    {
        public ushort TimeShort { get; set; }

        public ushort DateShort { get; set; }

        public DateTime? DateTime { 
            get  {
                if(TimeShort == 0 && DateShort == 0)
                {
                    return null;
                }

                var second = (TimeShort & 0b11111) << 1;
                var minute = (TimeShort >> 5) & 0b111111;
                var hour = (TimeShort >> 11) & 0b11111;

                var day = DateShort & 0b11111;
                var month = (DateShort >> 5) & 0b1111;
                var year = (DateShort >> 9) + 1980;

                var dateTime = new DateTime(year, month, day);


                dateTime.AddHours(hour);
                dateTime.AddMinutes(minute);
                dateTime.AddSeconds(second);

                return dateTime;
            }
            set {
                if (value == null)
                {
                    TimeShort = 0;
                    DateShort = 0;
                    return;
                }

                var time = 0;
                var dateTime = (DateTime) value;

                time |= dateTime.Hour;
                time <<= 6;
                time |= dateTime.Minute;
                time <<= 5;
                time |= (dateTime.Second >> 1);

                var date = 0;
                var correctedYear = dateTime.Year - 1980;

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

        public static Timestamp Read(BinaryReader reader)
        {
            return new Timestamp()
            {
                TimeShort = reader.ReadUInt16(),
                DateShort = reader.ReadUInt16(),
            };
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(TimeShort);
            writer.Write(DateShort);
        }
    }
}
