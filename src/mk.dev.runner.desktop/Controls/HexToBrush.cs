using System.Globalization;
using System.Windows.Data;

namespace mk.dev.runner.desktop.Controls
{
    public sealed class HexToBrush : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return value is string s ? Palette.Brush(s) : Palette.Brush(Palette.Faint);
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }
}
