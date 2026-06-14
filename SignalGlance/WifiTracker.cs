using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;

namespace SignalGlance
{
    public class WifiUsageData
    {
        public Dictionary<string, long> Daily { get; set; } = new Dictionary<string, long>();
        public Dictionary<string, long> Monthly { get; set; } = new Dictionary<string, long>();
    }

    public class WifiTracker
    {
        public bool IsSpeedTesting { get; set; } = false;

        public WifiTracker()
        {
            // Database is no longer stored locally on disk. We query Windows OS database natively.
        }

        public List<string> GetKnownNetworks()
        {
            try
            {
                var profiles = NetworkInformation.GetConnectionProfiles();
                if (profiles == null) return new List<string>();

                return profiles
                    .Where(p => p.IsWlanConnectionProfile && !string.IsNullOrEmpty(p.ProfileName))
                    .Select(p => p.ProfileName)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting known networks: {ex.Message}");
                return new List<string>();
            }
        }

        public string? GetCurrentSSID()
        {
            try
            {
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile != null && profile.IsWlanConnectionProfile)
                {
                    return profile.ProfileName;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting current SSID from UWP: {ex.Message}");
            }

            // Fallback to netsh if UWP fails
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
                Debug.WriteLine($"Error showing wlan interfaces via netsh: {ex.Message}");
            }
            return null;
        }

        public async Task<WifiUsageData> GetUsageAsync(string ssid)
        {
            var data = new WifiUsageData();
            if (string.IsNullOrEmpty(ssid)) return data;

            try
            {
                var profiles = NetworkInformation.GetConnectionProfiles();
                var profile = profiles.FirstOrDefault(p => p.IsWlanConnectionProfile && p.ProfileName == ssid);
                if (profile == null) return data;

                var states = new NetworkUsageStates();
                var today = DateTimeOffset.Now.Date;

                // Query last 30 days individually in parallel
                var dailyTasks = Enumerable.Range(0, 30).Select(async i =>
                {
                    var dayStart = today.AddDays(-i);
                    var dayEnd = dayStart.AddDays(1);
                    try
                    {
                        var usages = await profile.GetNetworkUsageAsync(dayStart, dayEnd, DataUsageGranularity.Total, states);
                        long total = 0;
                        if (usages != null)
                        {
                            foreach (var u in usages)
                            {
                                total += (long)(u.BytesReceived + u.BytesSent);
                            }
                        }
                        return new { DateStr = dayStart.ToString("yyyy-MM-dd"), Bytes = total };
                    }
                    catch
                    {
                        return new { DateStr = dayStart.ToString("yyyy-MM-dd"), Bytes = 0L };
                    }
                }).ToArray();

                var dailyResults = await Task.WhenAll(dailyTasks);
                foreach (var res in dailyResults)
                {
                    if (res.Bytes > 0)
                    {
                        data.Daily[res.DateStr] = res.Bytes;
                    }
                }

                // Query last 12 months individually in parallel
                var monthlyTasks = Enumerable.Range(0, 12).Select(async i =>
                {
                    var firstOfThisMonth = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero);
                    var monthStart = firstOfThisMonth.AddMonths(-i);
                    var monthEnd = monthStart.AddMonths(1);
                    try
                    {
                        var usages = await profile.GetNetworkUsageAsync(monthStart, monthEnd, DataUsageGranularity.Total, states);
                        long total = 0;
                        if (usages != null)
                        {
                            foreach (var u in usages)
                            {
                                total += (long)(u.BytesReceived + u.BytesSent);
                            }
                        }
                        return new { MonthStr = monthStart.ToString("yyyy-MM"), Bytes = total };
                    }
                    catch
                    {
                        return new { MonthStr = monthStart.ToString("yyyy-MM"), Bytes = 0L };
                    }
                }).ToArray();

                var monthlyResults = await Task.WhenAll(monthlyTasks);
                foreach (var res in monthlyResults)
                {
                    if (res.Bytes > 0)
                    {
                        data.Monthly[res.MonthStr] = res.Bytes;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error querying UWP connectivity network usage: {ex.Message}");
            }

            return data;
        }
    }
}
