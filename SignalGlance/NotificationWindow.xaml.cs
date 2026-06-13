using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SignalGlance
{
    public partial class NotificationWindow : Window
    {
        private readonly ConnectionState _state;
        private readonly System.Threading.Timer _autoCloseTimer;
        private bool _isClosing = false;
        private System.Windows.Threading.DispatcherTimer? _taskbarCheckTimer;
        private bool _wasTaskbarHidden = false;
        private bool _isFirstPositionCheck = true;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        public NotificationWindow(ConnectionState state)
        {
            InitializeComponent();
            _state = state;
            
            // Set up visuals depending on the state
            SetupStateVisuals();
            
            // Position window at Bottom Left
            Loaded += NotificationWindow_Loaded;
            StartTaskbarCheckTimer();

            // Auto-close connected, weak, and offline notifications after 5 seconds
            _autoCloseTimer = new System.Threading.Timer(_ => 
            {
                Dispatcher.Invoke(CloseNotification);
            }, null, 5000, System.Threading.Timeout.Infinite);
        }

        private void SetupStateVisuals()
        {
            // Simple SVG geometries
            string wifiConnectedIcon = "M12 21a2 2 0 1 1-4 0 2 2 0 0 1 4 0z M2.293 8.293a1 1 0 0 1 1.414 0 13.947 13.947 0 0 1 16.586 0 1 1 0 0 1 0 1.414L18.88 11.12a1 1 0 0 1-1.393.023 9.946 9.946 0 0 0-10.975 0 1 1 0 0 1-1.393-.023L3.707 9.707a1 1 0 0 1-1.414 0z";
            string wifiWeakIcon = "M12 21a2 2 0 1 1-4 0 2 2 0 0 1 4 0z M9.757 14.757a3.002 3.002 0 0 1 4.486 0 1 1 0 0 1-.023 1.393l-1.414 1.414a1 1 0 0 1-1.414 0l-1.414-1.414a1 1 0 0 1-.221-1.393z";
            string wifiOfflineIcon = "M3 3l18 18M12 21a2 2 0 1 1-4 0 2 2 0 0 1 4 0z M16.036 12.036a7.973 7.973 0 0 0-4.036-.536c-1.542.1-2.98.59-4.223 1.394l-1.428-1.428A9.97 9.97 0 0 1 12 9.5a9.975 9.975 0 0 1 6.892 2.768l-2.856-2.232z";

            switch (_state)
            {
                case ConnectionState.Connected:
                    IconBackground.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1500E676"));
                    StatusIcon.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00E676"));
                    StatusIcon.Data = Geometry.Parse(wifiConnectedIcon);
                    TitleText.Text = "You're online now";
                    MessageText.Text = "Internet is connected.";
                    break;

                case ConnectionState.WeakSignal:
                    IconBackground.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#15FFC107"));
                    StatusIcon.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFC107"));
                    StatusIcon.Data = Geometry.Parse(wifiWeakIcon);
                    TitleText.Text = "Weak Connection";
                    MessageText.Text = "Your connection latency is high.";
                    break;

                case ConnectionState.NoSignal:
                    IconBackground.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#15FF1744"));
                    StatusIcon.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF1744"));
                    StatusIcon.Data = Geometry.Parse(wifiOfflineIcon);
                    TitleText.Text = "No signal";
                    MessageText.Text = "You're completely offline. Check connection.";
                    break;
            }
        }

        private void NotificationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            }
            catch { }

            AlignToTaskbarState();

            // Trigger slide in
            Storyboard slideIn = (Storyboard)FindResource("SlideIn");
            slideIn.Begin(this);
        }

        private void AlignToTaskbarState()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
                
                double scaleX = 1.0;
                double scaleY = 1.0;
                
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);
                bool isHidden = false;
                double taskbarHeight = 48;
                if (taskbarHandle != IntPtr.Zero && GetWindowRect(taskbarHandle, out RECT rect))
                {
                    isHidden = rect.Top >= (screen.Bounds.Height - 5);
                    taskbarHeight = (rect.Bottom - rect.Top) / scaleY;
                }

                double targetBottom = screen.Bounds.Height / scaleY;
                if (!isHidden)
                {
                    targetBottom -= taskbarHeight;
                }

                this.Left = (screen.WorkingArea.Left / scaleX) + 20;
                this.Top = targetBottom - this.Height - 16;
                _wasTaskbarHidden = isHidden;
                _isFirstPositionCheck = false;
            }
            catch
            {
                var workArea = SystemParameters.WorkArea;
                this.Left = workArea.Left + 20;
                this.Top = workArea.Bottom - this.Height - 16;
            }
        }

        private void StartTaskbarCheckTimer()
        {
            _taskbarCheckTimer = new System.Windows.Threading.DispatcherTimer();
            _taskbarCheckTimer.Interval = TimeSpan.FromMilliseconds(250);
            _taskbarCheckTimer.Tick += (s, e) =>
            {
                if (this.IsVisible && !_isClosing)
                {
                    CheckTaskbarStateAndReposition();
                }
            };
            _taskbarCheckTimer.Start();
        }

        private void CheckTaskbarStateAndReposition()
        {
            try
            {
                IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);
                if (taskbarHandle == IntPtr.Zero) return;

                RECT rect;
                if (GetWindowRect(taskbarHandle, out rect))
                {
                    var helper = new WindowInteropHelper(this);
                    var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
                    
                    double scaleY = 1.0;
                    var source = PresentationSource.FromVisual(this);
                    if (source?.CompositionTarget != null)
                    {
                        scaleY = source.CompositionTarget.TransformToDevice.M22;
                    }

                    // Check if taskbar is hidden (slid down)
                    bool isHidden = rect.Top >= (screen.Bounds.Height - 5);

                    if (isHidden != _wasTaskbarHidden || _isFirstPositionCheck)
                    {
                        _isFirstPositionCheck = false;
                        _wasTaskbarHidden = isHidden;
                        
                        double spacingY = 16;
                        double targetBottom = screen.Bounds.Height / scaleY;

                        if (!isHidden)
                        {
                            double taskbarHeight = (rect.Bottom - rect.Top) / scaleY;
                            targetBottom -= taskbarHeight;
                        }

                        this.Top = targetBottom - this.Height - spacingY;
                    }
                }
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseNotification();
        }

        private void CloseNotification()
        {
            if (_isClosing) return;
            _isClosing = true;

            _autoCloseTimer?.Dispose();
            _taskbarCheckTimer?.Stop();

            Storyboard slideOut = (Storyboard)FindResource("SlideOut");
            slideOut.Completed += (s, ev) => this.Close();
            slideOut.Begin(this);
        }
    }
}
