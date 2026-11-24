using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using mk.dev.runner.core.Logs;

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
