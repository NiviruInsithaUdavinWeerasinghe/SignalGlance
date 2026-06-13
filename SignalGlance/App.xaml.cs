using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace SignalGlance
{
    public partial class App : Application
    {
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;
        private NetworkMonitor? _networkMonitor;
        private ConnectionState _lastState = ConnectionState.NoSignal;
        private bool _isFirstState = true;
        private Icon? _currentIcon;
        private static EventWaitHandle? _instanceEvent;
        private NotificationWindow? _activeNotification;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Determine if we should run as installer or the main app
            string exePath = Environment.ProcessPath ?? "";
            string localAppDataInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SignalGlance");
            string installedExe = Path.Combine(localAppDataInstallDir, "SignalGlance.exe");
            
            bool isInstalled = File.Exists(installedExe);
            bool forceApp = false;
            bool forceInstaller = false;

            foreach (var arg in e.Args)
            {
                if (arg.Equals("--app", StringComparison.OrdinalIgnoreCase)) forceApp = true;
                if (arg.Equals("--installer", StringComparison.OrdinalIgnoreCase)) forceInstaller = true;
            }

            // If it is not installed yet, we always show the installer (unless explicitly run with --app)
            bool runAsInstaller = forceInstaller || (!forceApp && !isInstalled);

            if (runAsInstaller)
            {
                // Run the Installer Wizard Window
                var installerWindow = new InstallerWindow();
                installerWindow.Show();
                return;
            }

            // --- RUN AS MONITOR APP ---
            _instanceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "SignalGlance-Instance-Event", out bool isNewInstance);
            if (!isNewInstance)
            {
                // Signal existing instance to show its window
                try
                {
                    _instanceEvent.Set();
                }
                catch { }
                Shutdown();
                return;
            }

            // Listen for activation requests from subsequent instances
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_instanceEvent.WaitOne())
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                _mainWindow?.ShowAndActivate();
                            }));
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            });

            // 1. Setup Dashboard Window
            _mainWindow = new MainWindow();

            // 2. Setup System Tray Icon
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = "SignalGlance";
            UpdateTrayIcon(ConnectionState.NoSignal);
            _notifyIcon.Visible = true;
            _notifyIcon.Click += NotifyIcon_Click;

            // Setup Context Menu for Tray
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open Dashboard", null, (s, ev) => _mainWindow?.ToggleShow());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (s, ev) => ShutdownApp());
            _notifyIcon.ContextMenuStrip = contextMenu;

            // 3. Setup Network Monitor
            _networkMonitor = new NetworkMonitor();
            _mainWindow.WifiTracker = _networkMonitor.WifiTracker;
            _networkMonitor.ConnectionStateChanged += OnConnectionStateChanged;
            _networkMonitor.StatsUpdated += OnStatsUpdated;
            _networkMonitor.Start();

            // Prevent application from shutting down when MainWindow hides
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Check if application should start headlessly (e.g., from startup key)
            bool startHeadless = false;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--startup", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--headless", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--background", StringComparison.OrdinalIgnoreCase))
                {
                    startHeadless = true;
                    break;
                }
            }

            if (!startHeadless)
            {
                // Align window nicely and display it
                _mainWindow.ShowAndActivate();
            }
        }

        private void NotifyIcon_Click(object? sender, EventArgs e)
        {
            // Only toggle on left click (right click opens context menu)
            var mouseEventArgs = e as MouseEventArgs;
            if (mouseEventArgs != null && mouseEventArgs.Button == MouseButtons.Left)
            {
                _mainWindow?.ToggleShow();
            }
        }

        private void OnConnectionStateChanged(ConnectionState newState)
        {
            UpdateTrayIcon(newState);

            // Trigger slide-in notification toast
            Dispatcher.Invoke(() =>
            {
                // Skip the first notification on startup to avoid spamming the user immediately
                if (!_isFirstState)
                {
                    if (_activeNotification != null)
                    {
                        try
                        {
                            _activeNotification.Close();
                        }
                        catch { }
                    }

                    _activeNotification = new NotificationWindow(newState);
                    _activeNotification.Show();
                }
                else
                {
                    _isFirstState = false;
                }
            });

            _lastState = newState;
        }

        private void OnStatsUpdated(double ping, double downloadMbps, double uploadMbps)
        {
            _mainWindow?.UpdateStats(ping, downloadMbps, uploadMbps, _lastState);
        }

        private void UpdateTrayIcon(ConnectionState state)
        {
            try
            {
                using (var bitmap = new Bitmap(16, 16))
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // Choose color based on connection state
                    Brush brush = state switch
                    {
                        ConnectionState.Connected => new SolidBrush(Color.FromArgb(0, 230, 118)),   // Green
                        ConnectionState.WeakSignal => new SolidBrush(Color.FromArgb(255, 193, 7)),  // Yellow
                        ConnectionState.NoSignal => new SolidBrush(Color.FromArgb(244, 67, 54)),    // Red
                        _ => Brushes.Gray
                    };

                    // Draw dot-and-arc logo matching the app icon
                    float dotRadius = 1.3f;
                    float dotX = 6.7f - dotRadius;
                    float dotY = 12.5f - dotRadius;
                    g.FillEllipse(brush, dotX, dotY, dotRadius * 2, dotRadius * 2);

                    using (var pen = new Pen(brush, 2.1f))
                    {
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        
                        float arcSize = 11.5f;
                        float arcX = 8f - arcSize / 2f;
                        float arcY = 9.3f - arcSize / 2f;
                        
                        g.DrawArc(pen, arcX, arcY, arcSize, arcSize, 205, 130);
                    }

                    var iconHandle = bitmap.GetHicon();
                    var newIcon = Icon.FromHandle(iconHandle);
                    
                    var oldIcon = _currentIcon;
                    _currentIcon = newIcon;
                    _notifyIcon!.Icon = _currentIcon;
                    
                    if (oldIcon != null)
                    {
                        oldIcon.Dispose();
                    }
                    
                    DestroyIcon(iconHandle);
                }
            }
            catch
            {
                // Fallback to system application icon if drawing fails
                _notifyIcon!.Icon = SystemIcons.Application;
            }
        }

        private void ShutdownApp()
        {
            _networkMonitor?.Stop();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            if (_instanceEvent != null)
            {
                try
                {
                    _instanceEvent.Close();
                }
                catch { }
                _instanceEvent.Dispose();
            }
            Shutdown();
        }
    }
}
