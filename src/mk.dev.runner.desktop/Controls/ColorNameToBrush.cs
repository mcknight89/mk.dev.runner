using System.Globalization;
using System.Windows.Data;

namespace mk.dev.runner.desktop.Controls
{
    public sealed class ColorNameToBrush : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return Palette.Brush(Palette.ServiceHex(value as string));
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }
}
