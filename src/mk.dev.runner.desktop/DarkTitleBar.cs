using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace mk.dev.runner.desktop
{
    internal static class DarkTitleBar
    {
        private const int UseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void ForAllWindows()
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => Apply((Window)sender)));
        }

        private static void Apply(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            var on = 1;
            DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref on, sizeof(int));
        }
    }
}
