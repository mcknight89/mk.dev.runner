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
    public sealed class BoolToVisibility : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool v = value switch { bool b => b, string s => !string.IsNullOrEmpty(s), null => false, _ => true };
            return v ^ Invert ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
        {
            throw new NotSupportedException();
        }
    }
}
