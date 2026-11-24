using System.Globalization;
using System.Windows.Data;

namespace mk.dev.runner.desktop.Controls
{
    public sealed class CardColumns : IValueConverter
    {
        private const double MinCardWidth = 340;

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            return value is double width ? Math.Max(1, (int)(width / MinCardWidth)) : 1;
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }
}
