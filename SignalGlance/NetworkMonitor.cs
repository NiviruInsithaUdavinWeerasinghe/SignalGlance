using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace SignalGlance
{
    public class NetworkMonitor
    {
        private readonly System.Threading.Timer _timer;
        private readonly WifiTracker _wifiTracker;
        private string? _cachedSSID = null;
        private int _ssidCheckCounter = 0;
        private long _lastBytesReceived;
        private long _lastBytesSent;
        private DateTime _lastTime;
        private ConnectionState _currentState = ConnectionState.NoSignal;
        private bool _isFirstRun = true;
        private bool _isChecking = false;

        public event Action<ConnectionState> ConnectionStateChanged;
        public event Action<double, double, double> StatsUpdated;

        public ConnectionState CurrentState => _currentState;
        public WifiTracker WifiTracker => _wifiTracker;

        public NetworkMonitor()
        {
            _wifiTracker = new WifiTracker();
            _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            _lastTime = DateTime.Now;
            ResetCounters();
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
            _timer.Change(0, 1000); // Poll every 1 second
        }

        public void Stop()
        {
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void NetworkChange_NetworkAvailabilityChanged(object? sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
        {
            // Trigger instant evaluation on network status changes
            _timer.Change(0, 1000);
        }

        private void ResetCounters()
        {
            try
            {
                GetNetworkBytes(out long rx, out long tx);
                _lastBytesReceived = rx;
                _lastBytesSent = tx;
                _lastTime = DateTime.Now;
            }
            catch
            {
                _lastBytesReceived = 0;
                _lastBytesSent = 0;
            }
        }

        private void GetNetworkBytes(out long rx, out long tx)
        {
            rx = 0;
            tx = 0;
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    !ni.Description.Contains("Virtual") &&
                    !ni.Description.Contains("Pseudo"))
                {
                    var stats = ni.GetIPStatistics();
                    rx += stats.BytesReceived;
                    tx += stats.BytesSent;
                }
            }
        }

        private double _smoothedPing = -1;

        private async void OnTick(object state)
        {
            if (_isChecking) return;
            _isChecking = true;

            try
            {
                // 1. Calculate speeds
                DateTime now = DateTime.Now;
                double dt = (now - _lastTime).TotalSeconds;
                if (dt <= 0) dt = 1.0;

                GetNetworkBytes(out long rx, out long tx);

                double downloadSpeedMbps = 0;
                double uploadSpeedMbps = 0;

                // Query SSID once every 5 seconds to minimize CPU overhead of running netsh
                _ssidCheckCounter++;
                if (_ssidCheckCounter >= 5 || _cachedSSID == null)
                {
                    _ssidCheckCounter = 0;
                    string? currentSSID = _wifiTracker.GetCurrentSSID();
                    if (currentSSID != _cachedSSID)
                    {
                        _cachedSSID = currentSSID;
                        ResetCounters();
                        // Get network bytes again post-reset to have accurate baseline for this tick
                        GetNetworkBytes(out rx, out tx);
                    }
                }

                if (!_isFirstRun)
                {
                    long rxDiff = rx - _lastBytesReceived;
                    long txDiff = tx - _lastBytesSent;

                    if (rxDiff >= 0 && txDiff >= 0)
                    {
                        if (_lastBytesReceived == 0 || _lastBytesSent == 0)
                        {
                            ResetCounters();
                            rxDiff = 0;
                            txDiff = 0;
                        }

                        downloadSpeedMbps = (rxDiff * 8.0) / (1024.0 * 1024.0 * dt);
                        uploadSpeedMbps = (txDiff * 8.0) / (1024.0 * 1024.0 * dt);


                    }
                    else
                    {
                        ResetCounters();
                    }
                }
                else
                {
                    _isFirstRun = false;
                }

                _lastBytesReceived = rx;
                _lastBytesSent = tx;
                _lastTime = now;

                // 2. Check Ping and Connection State
                double pingMs = await MeasurePingAsync();
                
                if (pingMs >= 0)
                {
                    if (_smoothedPing < 0)
                    {
                        _smoothedPing = pingMs;
                    }
                    else
                    {
                        // Apply Exponential Moving Average (EMA) with alpha = 0.25 to smooth out transient spikes
                        _smoothedPing = (0.25 * pingMs) + (0.75 * _smoothedPing);
                    }
                }
                else
                {
                    _smoothedPing = -1;
                }

                ConnectionState newState;

                if (_smoothedPing < 0)
                {
                    newState = ConnectionState.NoSignal;
                }
                else if (_smoothedPing > 200)
                {
                    newState = ConnectionState.WeakSignal;
                }
                else
                {
                    newState = ConnectionState.Connected;
                }

                if (newState != _currentState)
                {
                    _currentState = newState;
                    ConnectionStateChanged?.Invoke(newState);
                }

                StatsUpdated?.Invoke(_smoothedPing < 0 ? 0 : _smoothedPing, downloadSpeedMbps, uploadSpeedMbps);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NetworkMonitor Error: {ex.Message}");
            }
            finally
            {
                _isChecking = false;
            }
        }

        private async Task<double> MeasurePingAsync()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return -1;
            }

            var targets = new (string Host, int Port)[]
            {
                ("1.1.1.1", 443),
                ("8.8.8.8", 53),
                ("www.cloudflare.com", 443)
            };

            var tasks = targets.Select(async target =>
            {
                try
                {
                    using (var client = new System.Net.Sockets.TcpClient())
                    {
                        var watch = Stopwatch.StartNew();
                        var connectTask = client.ConnectAsync(target.Host, target.Port);
                        var delayTask = Task.Delay(1000);
                        
                        var completedTask = await Task.WhenAny(connectTask, delayTask);
                        if (completedTask == connectTask)
                        {
                            await connectTask;
                            watch.Stop();
                            return (double)watch.ElapsedMilliseconds;
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
                return -1.0;
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            var validResults = results.Where(r => r >= 0).ToList();
            if (validResults.Count > 0)
            {
                return validResults.Average();
            }

            return -1;
        }
    }
}
