using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;

namespace QuestPatcher
{
    public class FileNameConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return null;
            }

            string filePath = (string) value;
            return Path.GetFileName(filePath);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
