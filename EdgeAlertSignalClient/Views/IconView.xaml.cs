using EdgeAlertSignalClient.ViewModels;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace EdgeAlertSignalClient.Views
{
    /// <summary>
    /// Interaction logic for IconView.xaml
    /// </summary>
    public partial class IconView : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private DispatcherTimer _timer;

        private readonly IconViewModel _iconView;

        public IconView(IconViewModel iconView)
        {
            InitializeComponent();
            _iconView = iconView;
            DataContext = _iconView;

            // Create a new timer that calls the BringToFront method every 1 second
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (sender, e) => BringToFront();
            _timer.Start();

            // Handle the Closed and IsVisibleChanged events
            Closed += Window_Closed;
            IsVisibleChanged += IconView_IsVisibleChanged;

            // Set the Topmost property and handle activation/deactivation events
            Topmost = true;
            Activated += (sender, e) => Topmost = true;
            Activated += IconView_Activated;
            Deactivated += (sender, e) => Topmost = true;
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void IconView_Activated(object sender, EventArgs e)
        {
            BringToFront();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Topmost = true;
        }

        // Stop the timer when the window is closed
        private void Window_Closed(object sender, EventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }
        }

        // Stop the timer when the window's visibility is collapsed
        private void IconView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                _timer?.Start();
            }
            else
            {
                _timer?.Stop();
            }
        }

        // Call this method to bring the window to the front and make it topmost again
        public void BringToFront()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                Topmost = true; // Ensure the window is always set to topmost
            }), DispatcherPriority.Normal);
        }
    }
}
