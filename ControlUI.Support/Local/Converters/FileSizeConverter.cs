using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace ControlUI.Support.Local.Converters
{
    public class FileSizeConverter : MarkupExtension, IValueConverter
    {
        private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!long.TryParse(value?.ToString(), out long bytes) || bytes == 0)
                return string.Empty;

            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < Units.Length - 1)
            {
                size /= 1024;
                ++unit;
            }
            return string.Format(culture, "{0:#,##0.#}{1}", size, Units[unit]);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}
