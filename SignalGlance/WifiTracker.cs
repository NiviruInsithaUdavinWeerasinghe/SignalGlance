using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SignalGlance
{
    public class WifiUsageData
    {
        public Dictionary<string, long> Daily { get; set; } = new Dictionary<string, long>();
        public Dictionary<string, long> Monthly { get; set; } = new Dictionary<string, long>();
    }

    public class WifiTracker
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private Dictionary<string, WifiUsageData> _usageDb = new Dictionary<string, WifiUsageData>();

        public WifiTracker()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SignalGlance");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "wifi_usage.json");
            LoadDatabase();
        }

        private void LoadDatabase()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        string json = File.ReadAllText(_filePath);
                        var db = JsonSerializer.Deserialize<Dictionary<string, WifiUsageData>>(json);
                        if (db != null)
                        {
                            _usageDb = db;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load wifi usage: {ex.Message}");
                }
            }
        }

        public void SaveDatabase()
        {
            lock (_lock)
            {
                try
                {
                    string json = JsonSerializer.Serialize(_usageDb, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to save wifi usage: {ex.Message}");
                }
            }
        }

        public List<string> GetKnownNetworks()
        {
            var ssids = new List<string>();
            lock (_lock)
            {
                foreach (var ssid in _usageDb.Keys)
                {
                    ssids.Add(ssid);
                }
            }

            var current = GetCurrentSSID();
            if (!string.IsNullOrEmpty(current) && !ssids.Contains(current))
            {
                ssids.Add(current);
            }

            return ssids.OrderBy(s => s).ToList();
        }

        public string? GetCurrentSSID()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = trimmed.Split(':');
                                if (parts.Length > 1)
                                {
                                    return parts[1].Trim();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing wlan interfaces: {ex.Message}");
            }
            return null;
        }

        public void RecordUsage(string ssid, long bytesRead, long bytesSent)
        {
            if (string.IsNullOrEmpty(ssid)) return;

            lock (_lock)
            {
                if (!_usageDb.ContainsKey(ssid))
                {
                    _usageDb[ssid] = new WifiUsageData();
                }

                var data = _usageDb[ssid];
                string today = DateTime.Today.ToString("yyyy-MM-dd");
                string thisMonth = DateTime.Today.ToString("yyyy-MM");

                long totalBytes = bytesRead + bytesSent;

                if (data.Daily.ContainsKey(today))
                {
                    data.Daily[today] += totalBytes;
                }
                else
                {
                    data.Daily[today] = totalBytes;
                }

                if (data.Monthly.ContainsKey(thisMonth))
                {
                    data.Monthly[thisMonth] += totalBytes;
                }
                else
                {
                    data.Monthly[thisMonth] = totalBytes;
                }
            }
        }

        public WifiUsageData GetUsage(string ssid)
        {
            lock (_lock)
            {
                if (_usageDb.TryGetValue(ssid, out var data))
                {
                    return data;
                }
            }
            return new WifiUsageData();
        }
    }
}
