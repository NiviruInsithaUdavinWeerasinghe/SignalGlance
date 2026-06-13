using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace SignalGlance
{
    public partial class MainWindow : Window
    {
        private bool _isPinned = false;
        private bool _isSpeedTesting = false;
        private CancellationTokenSource? _speedTestCts;
        private double _currentMaxSpeed = 100.0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private System.Windows.Threading.DispatcherTimer? _taskbarCheckTimer;
        private bool _wasTaskbarHidden = false;

        public MainWindow()
        {
            InitializeComponent();
            this.Deactivated += MainWindow_Deactivated;
            this.Loaded += MainWindow_Loaded;
            StartTaskbarCheckTimer();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            }
            catch { }
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (!_isSpeedTesting && !_isPinned)
            {
                this.Hide();
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = !_isPinned;
            
            var pinButton = (System.Windows.Controls.Button)sender;
            var path = pinButton.Template.FindName("PinPath", pinButton) as System.Windows.Shapes.Path;
            if (path != null)
            {
                path.Fill = _isPinned 
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88FFFFFF"));
                
                path.RenderTransform = _isPinned 
                    ? new RotateTransform(45) 
                    : Transform.Identity;
            }

            CloseButton.IsEnabled = !_isPinned;
        }

        public void UpdateStats(double ping, double downloadMbps, double uploadMbps, ConnectionState state)
        {
            Dispatcher.Invoke(() =>
            {
                if (_isSpeedTesting) return;

                PingVal.Text = ping > 0 ? $"{ping:0}" : "--";
                DownVal.Text = $"{downloadMbps:0.0}";
                UpVal.Text = $"{uploadMbps:0.0}";

                switch (state)
                {
                    case ConnectionState.Connected:
                        HeaderStatusIcon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
                        StatusLabel.Text = "Connected to Internet";
                        break;
                    case ConnectionState.WeakSignal:
                        HeaderStatusIcon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffc107"));
                        StatusLabel.Text = "Unstable Connection";
                        break;
                    case ConnectionState.NoSignal:
                        HeaderStatusIcon.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ff1744"));
                        StatusLabel.Text = "No Signal / Offline";
                        break;
                }
            });
        }

        public void ToggleShow()
        {
            if (this.IsVisible)
            {
                this.Hide();
            }
            else
            {
                this.Show();
                AlignToTray();
                this.Activate();
            }
        }

        public void ShowAndActivate()
        {
            this.Show();
            AlignToTray();
            this.Activate();
        }

        private void AlignToTray()
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

                double workingAreaRight = screen.WorkingArea.Right / scaleX;
                double spacingX = 16;
                double spacingY = 16;

                this.Left = workingAreaRight - this.Width - spacingX;
                this.Top = targetBottom - this.Height - spacingY;
                _wasTaskbarHidden = isHidden;
            }
            catch
            {
                var workArea = SystemParameters.WorkArea;
                this.Left = workArea.Right - this.Width - 16;
                this.Top = workArea.Bottom - this.Height - 16;
            }
        }

        public WifiTracker? WifiTracker { get; set; }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPinned) return;
            CancelSpeedTest();
            this.Hide();
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (MonitorGrid == null || SpeedTestGrid == null || WifiGrid == null) return;

            if (TabMonitor.IsChecked == true)
            {
                MonitorGrid.Visibility = Visibility.Visible;
                SpeedTestGrid.Visibility = Visibility.Collapsed;
                WifiGrid.Visibility = Visibility.Collapsed;
            }
            else if (TabSpeedTest.IsChecked == true)
            {
                MonitorGrid.Visibility = Visibility.Collapsed;
                SpeedTestGrid.Visibility = Visibility.Visible;
                WifiGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                MonitorGrid.Visibility = Visibility.Collapsed;
                SpeedTestGrid.Visibility = Visibility.Collapsed;
                WifiGrid.Visibility = Visibility.Visible;
                LoadWifiNetworksList();
            }
        }

        private void LoadWifiNetworksList()
        {
            if (WifiTracker == null) return;
            
            var selectedItem = WifiNetworksList.SelectedItem as WifiNetworkItem;
            var selectedSsid = selectedItem?.SSID;
            
            var knownNetworks = WifiTracker.GetKnownNetworks();
            var activeSsid = WifiTracker.GetCurrentSSID();
            
            var items = new List<WifiNetworkItem>();
            
            // Add active one first (pinned at top)
            if (!string.IsNullOrEmpty(activeSsid) && knownNetworks.Contains(activeSsid))
            {
                items.Add(new WifiNetworkItem { SSID = activeSsid, IsActive = true });
            }
            
            // Add remaining networks
            foreach (var ssid in knownNetworks)
            {
                if (ssid != activeSsid)
                {
                    items.Add(new WifiNetworkItem { SSID = ssid, IsActive = false });
                }
            }
            
            WifiNetworksList.ItemsSource = items;

            // Restore selection
            if (!string.IsNullOrEmpty(selectedSsid))
            {
                var match = items.FirstOrDefault(i => i.SSID == selectedSsid);
                if (match != null)
                {
                    WifiNetworksList.SelectedItem = match;
                }
            }
            else if (items.Count > 0)
            {
                WifiNetworksList.SelectedIndex = 0;
            }
        }

        private void WifiNetworksList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateWifiUsageDetails();
        }

        private void Filter_Checked(object sender, RoutedEventArgs e)
        {
            UpdateWifiUsageDetails();
        }

        private void UpdateWifiUsageDetails()
        {
            if (WifiTracker == null || WifiNetworksList == null || WifiEmptyStateText == null || WifiStatsPanel == null) return;
            var selectedItem = WifiNetworksList.SelectedItem as WifiNetworkItem;
            string? selectedSsid = selectedItem?.SSID;

            if (string.IsNullOrEmpty(selectedSsid))
            {
                WifiEmptyStateText.Visibility = Visibility.Visible;
                WifiStatsPanel.Visibility = Visibility.Collapsed;
                SelectedWifiNameText.Text = "Select a network";
                return;
            }

            WifiEmptyStateText.Visibility = Visibility.Collapsed;
            WifiStatsPanel.Visibility = Visibility.Visible;
            SelectedWifiNameText.Text = selectedSsid;

            var usage = WifiTracker.GetUsage(selectedSsid);
            bool isDaily = FilterDaily.IsChecked == true;

            var historyItems = new List<KeyValuePair<string, string>>();
            long totalBytes = 0;

            if (isDaily)
            {
                var sortedDaily = usage.Daily.OrderByDescending(d => d.Key).ToList();
                foreach (var day in sortedDaily)
                {
                    historyItems.Add(new KeyValuePair<string, string>(day.Key, FormatBytes(day.Value)));
                    totalBytes += day.Value;
                }
            }
            else
            {
                var sortedMonthly = usage.Monthly.OrderByDescending(m => m.Key).ToList();
                foreach (var month in sortedMonthly)
                {
                    historyItems.Add(new KeyValuePair<string, string>(month.Key, FormatBytes(month.Value)));
                    totalBytes += month.Value;
                }
            }

            double formattedTotal;
            string unit;
            if (totalBytes >= 1024L * 1024 * 1024)
            {
                formattedTotal = totalBytes / (1024.0 * 1024 * 1024);
                unit = " GB";
            }
            else
            {
                formattedTotal = totalBytes / (1024.0 * 1024);
                unit = " MB";
            }

            WifiTotalUsageText.Text = $"{formattedTotal:0.00}";
            WifiTotalUsageUnit.Text = unit;
            WifiHistoryList.ItemsSource = historyItems;
        }

        private string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
            {
                return $"{bytes / (1024.0 * 1024 * 1024):0.00} GB";
            }
            return $"{bytes / (1024.0 * 1024):0.00} MB";
        }

        private void StartSpeedTestButton_Click(object sender, RoutedEventArgs e)
        {
            RunSpeedTest();
        }

        private void RestartSpeedTestButton_Click(object sender, RoutedEventArgs e)
        {
            RunSpeedTest();
        }

        private void CancelSpeedTest()
        {
            if (_speedTestCts != null)
            {
                _speedTestCts.Cancel();
                _speedTestCts.Dispose();
                _speedTestCts = null;
            }
            _isSpeedTesting = false;
        }

        private void UpdateNeedle(double speed)
        {
            Dispatcher.Invoke(() =>
            {
                if (speed > _currentMaxSpeed)
                {
                    _currentMaxSpeed = Math.Ceiling(speed / 100.0) * 100.0;
                }

                double targetAngle = -90.0 + (speed / _currentMaxSpeed) * 180.0;
                if (targetAngle > 90.0) targetAngle = 90.0;
                if (targetAngle < -90.0) targetAngle = -90.0;

                var anim = new DoubleAnimation(targetAngle, TimeSpan.FromMilliseconds(200));
                NeedleRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            });
        }

        private async Task TransitionPhaseAsync(string phase, string unit, string initialValue, string statusText, CancellationToken token)
        {
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150));
            ActivePhaseText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            ActiveSpeedText.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            ActiveUnitText.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            // Reset needle to 0 position (-90 degrees) during phase transitions
            var needleReset = new DoubleAnimation(-90.0, TimeSpan.FromMilliseconds(150));
            NeedleRotate.BeginAnimation(RotateTransform.AngleProperty, needleReset);

            await Task.Delay(150, token);

            ActivePhaseText.Text = phase;
            ActiveUnitText.Text = unit;
            ActiveSpeedText.Text = initialValue;
            SpeedTestStatusText.Text = statusText;

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
            ActivePhaseText.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ActiveSpeedText.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ActiveUnitText.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            await Task.Delay(200, token);
        }

        private async void RunSpeedTest()
        {
            if (_isSpeedTesting) return;

            _isSpeedTesting = true;
            _speedTestCts = new CancellationTokenSource();
            _currentMaxSpeed = 100.0; // Reset max speed limit
            var token = _speedTestCts.Token;

            SpeedTestReadyState.BeginAnimation(UIElement.OpacityProperty, null);
            SpeedTestResultState.BeginAnimation(UIElement.OpacityProperty, null);
            SpeedTestActiveState.BeginAnimation(UIElement.OpacityProperty, null);

            SpeedTestReadyState.Visibility = Visibility.Collapsed;
            SpeedTestResultState.Visibility = Visibility.Collapsed;
            SpeedTestActiveState.Opacity = 1.0;
            SpeedTestActiveState.Visibility = Visibility.Visible;

            double finalPing = 0;
            double finalDownload = 0;
            double finalUpload = 0;

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                
                // Add headers to avoid 403 Forbidden checks from Cloudflare's WAF
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Origin", "https://speed.cloudflare.com");
                client.DefaultRequestHeaders.Add("Referer", "https://speed.cloudflare.com/");

                // --- 1. PING PHASE ---
                await TransitionPhaseAsync("PING", "ms", "0", "Measuring latency...", token);

                double totalPing = 0;
                int pingSamples = 4;
                for (int i = 0; i < pingSamples; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    using (var response = await client.GetAsync("https://speed.cloudflare.com/cdn-cgi/trace", HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        response.EnsureSuccessStatusCode();
                    }
                    watch.Stop();
                    totalPing += watch.ElapsedMilliseconds;
                    
                    double currentAvg = totalPing / (i + 1);
                    ActiveSpeedText.Text = $"{currentAvg:0}";
                    UpdateNeedle(currentAvg); // Let the needle jump with latency

                    await Task.Delay(150, token);
                }
                finalPing = totalPing / pingSamples;

                // --- 2. DOWNLOAD PHASE ---
                token.ThrowIfCancellationRequested();
                await TransitionPhaseAsync("DOWNLOAD", "Mbps", "0.0", "Testing download throughput...", token);

                var downloadUrl = "https://speed.cloudflare.com/__down?bytes=104857600"; // 100MB
                var dlWatch = System.Diagnostics.Stopwatch.StartNew();
                long totalBytesRead = 0;

                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync(token))
                    {
                        byte[] buffer = new byte[32768];
                        int bytesRead;

                        while (dlWatch.Elapsed.TotalSeconds < 5.0 && 
                               (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                        {
                            totalBytesRead += bytesRead;
                            double seconds = dlWatch.Elapsed.TotalSeconds;
                            if (seconds > 0)
                            {
                                double mbps = (totalBytesRead * 8.0 / 1000000.0) / seconds;
                                ActiveSpeedText.Text = $"{mbps:0.0}";
                                UpdateNeedle(mbps); // Smooth needle updates
                            }
                        }
                    }
                }
                dlWatch.Stop();
                finalDownload = (totalBytesRead * 8.0 / 1000000.0) / dlWatch.Elapsed.TotalSeconds;
                UpdateNeedle(finalDownload);

                // --- 3. UPLOAD PHASE ---
                token.ThrowIfCancellationRequested();
                await TransitionPhaseAsync("UPLOAD", "Mbps", "0.0", "Testing upload throughput...", token);

                byte[] uploadChunk = new byte[2097152]; // 2MB chunk
                new Random().NextBytes(uploadChunk);

                var ulWatch = System.Diagnostics.Stopwatch.StartNew();
                long totalBytesUploaded = 0;

                while (ulWatch.Elapsed.TotalSeconds < 5.0)
                {
                    token.ThrowIfCancellationRequested();
                    var content = new ByteArrayContent(uploadChunk);
                    
                    using (var response = await client.PostAsync("https://speed.cloudflare.com/__up", content, token))
                    {
                        response.EnsureSuccessStatusCode();
                    }

                    totalBytesUploaded += uploadChunk.Length;
                    double seconds = ulWatch.Elapsed.TotalSeconds;
                    if (seconds > 0)
                    {
                        double mbps = (totalBytesUploaded * 8.0 / 1000000.0) / seconds;
                        ActiveSpeedText.Text = $"{mbps:0.0}";
                        UpdateNeedle(mbps); // Smooth needle updates
                    }
                }
                ulWatch.Stop();
                finalUpload = (totalBytesUploaded * 8.0 / 1000000.0) / ulWatch.Elapsed.TotalSeconds;
                UpdateNeedle(finalUpload);
                await Task.Delay(400, token); // Let the user see the final needle position

                // Smooth fade out of the active test gauge
                var fadeOutActive = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200));
                SpeedTestActiveState.BeginAnimation(UIElement.OpacityProperty, fadeOutActive);
                await Task.Delay(200, token);

                SpeedTestActiveState.Visibility = Visibility.Collapsed;
                SpeedTestActiveState.BeginAnimation(UIElement.OpacityProperty, null); // Clear animation

                // --- SHOW RESULTS ---
                ResultPingVal.Text = $"{finalPing:0}";
                ResultDownVal.Text = $"{finalDownload:0.0}";
                ResultUpVal.Text = $"{finalUpload:0.0}";

                SpeedTestResultState.Opacity = 0.0;
                SpeedTestResultState.Visibility = Visibility.Visible;

                // Smooth fade in of the results panel
                var fadeInResult = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250));
                SpeedTestResultState.BeginAnimation(UIElement.OpacityProperty, fadeInResult);
                await Task.Delay(250, token);
                SpeedTestResultState.BeginAnimation(UIElement.OpacityProperty, null); // Clear animation
                SpeedTestResultState.Opacity = 1.0;
            }
            catch (OperationCanceledException)
            {
                SpeedTestActiveState.BeginAnimation(UIElement.OpacityProperty, null);
                SpeedTestResultState.BeginAnimation(UIElement.OpacityProperty, null);
                SpeedTestActiveState.Visibility = Visibility.Collapsed;
                SpeedTestResultState.Visibility = Visibility.Collapsed;
                SpeedTestReadyState.Visibility = Visibility.Visible;
                SpeedTestReadyState.Opacity = 1.0;
            }
            catch (Exception ex)
            {
                SpeedTestActiveState.BeginAnimation(UIElement.OpacityProperty, null);
                SpeedTestResultState.BeginAnimation(UIElement.OpacityProperty, null);
                SpeedTestActiveState.Visibility = Visibility.Collapsed;
                SpeedTestResultState.Visibility = Visibility.Collapsed;
                SpeedTestReadyState.Visibility = Visibility.Visible;
                SpeedTestReadyState.Opacity = 1.0;
                MessageBox.Show($"Speed test failed: {ex.Message}", "Speed Test Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _isSpeedTesting = false;
                _speedTestCts = null;
            }
        }

        private void ListBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var listBox = sender as DependencyObject;
            if (listBox == null) return;
            var scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(listBox);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta / 4.0));
                e.Handled = true;
            }
        }

        private T? FindVisualChild<T>(DependencyObject? depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null)
                    {
                        if (child is T)
                        {
                            return (T)child;
                        }

                        T? childItem = FindVisualChild<T>(child);
                        if (childItem != null) return childItem;
                    }
                }
            }
            return null;
        }

        private void StartTaskbarCheckTimer()
        {
            _taskbarCheckTimer = new System.Windows.Threading.DispatcherTimer();
            _taskbarCheckTimer.Interval = TimeSpan.FromMilliseconds(250);
            _taskbarCheckTimer.Tick += (s, e) =>
            {
                if (this.IsVisible)
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

                    if (isHidden != _wasTaskbarHidden)
                    {
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
    }

    public class WifiNetworkItem
    {
        public string SSID { get; set; } = "";
        public bool IsActive { get; set; }
    }
}