using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using SkiaSharp;

namespace Samy;

internal static class AdvancedServices
{
    private static long _lastNetworkSent;
    private static long _lastNetworkReceived;
    private static DateTime _lastNetworkSample = DateTime.MinValue;
    public static async Task ConvertImageAsync(string source, string destination, string format, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using var input = File.OpenRead(source);
            using var bitmap = SKBitmap.Decode(input) ?? throw new InvalidDataException("Windows could not decode this image.");
            using var image = SKImage.FromBitmap(bitmap);
            if (format.Equals("PDF", StringComparison.OrdinalIgnoreCase))
            {
                using var document = SKDocument.CreatePdf(destination);
                var canvas = document.BeginPage(bitmap.Width, bitmap.Height);
                canvas.DrawImage(image, new SKRect(0, 0, bitmap.Width, bitmap.Height));
                document.EndPage();
                document.Close();
                return;
            }

            SKEncodedImageFormat encoded = format.ToUpperInvariant() switch
            {
                "JPG" or "JPEG" => SKEncodedImageFormat.Jpeg,
                "WEBP" => SKEncodedImageFormat.Webp,
                "BMP" => SKEncodedImageFormat.Bmp,
                _ => SKEncodedImageFormat.Png
            };
            using var data = image.Encode(encoded, 92) ?? throw new InvalidDataException("The selected image format is not supported.");
            using var output = File.Create(destination);
            data.SaveTo(output);
        }, cancellationToken);
    }

    public static CodingActivityReport AnalyzeGitRepository(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing local repository folder.");
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=off"); // keep non-ASCII file names readable
        start.ArgumentList.Add("log");
        start.ArgumentList.Add("--since=30.days");
        start.ArgumentList.Add("--date=short");
        start.ArgumentList.Add("--pretty=format:@@%ad");
        start.ArgumentList.Add("--name-only");
        Process process;
        try { process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started."); }
        catch (System.ComponentModel.Win32Exception)
        { throw new InvalidOperationException("Git was not found. Install Git for Windows and make sure it is on your PATH."); }
        string output, error;
        using (process)
        {
            // Drain both pipes concurrently so a full buffer can never block git, and the timeout always applies.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                throw new TimeoutException("Git activity scan exceeded 10 seconds.");
            }
            process.WaitForExit();
            output = outputTask.GetAwaiter().GetResult();
            error = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "This folder is not a readable Git repository." : error.Trim());
        }

        var days = new HashSet<string>(StringComparer.Ordinal);
        var extensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int commits = 0, files = 0;
        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                commits++;
                days.Add(line[2..].Trim());
            }
            else if (line.Length > 0 && !line.StartsWith("Merge:", StringComparison.Ordinal))
            {
                files++;
                string ext = Path.GetExtension(line);
                if (!string.IsNullOrWhiteSpace(ext)) extensions[ext] = extensions.GetValueOrDefault(ext) + 1;
            }
        }
        return new CodingActivityReport(folder, commits, days.Count, files,
            extensions.OrderByDescending(p => p.Value).Take(6).Select(p => $"{p.Key} ({p.Value})").ToArray());
    }

    public static string ProtectToken(string token)
    {
        byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes("SAMY-analytics-v1"), DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string UnprotectToken(string value)
    {
        byte[] clear = ProtectedData.Unprotect(Convert.FromBase64String(value), Encoding.UTF8.GetBytes("SAMY-analytics-v1"), DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clear);
    }

    public static async Task<ProviderProbeResult> ProbeProviderAsync(string endpoint, string token, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Use an HTTPS API URL. HTTP is allowed only for localhost.");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.Content.Headers.ContentLength is long length && length > 512 * 1024)
            throw new InvalidOperationException("Provider response is larger than 512 KB; use a summary endpoint.");
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string summary = "Response received";
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(body);
            summary = json.RootElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Object => BuildProviderSummary(json.RootElement),
                System.Text.Json.JsonValueKind.Array => $"Valid JSON array · {json.RootElement.GetArrayLength()} items",
                _ => $"Valid JSON · {json.RootElement.ValueKind}"
            };
        }
        catch (System.Text.Json.JsonException) { summary = "Response is not JSON; raw response was not displayed."; }
        return new ProviderProbeResult((int)response.StatusCode, response.IsSuccessStatusCode, summary);
    }

    private static string BuildProviderSummary(System.Text.Json.JsonElement root)
    {
        var properties = root.EnumerateObject().ToList();
        var numeric = properties.Where(p => p.Value.ValueKind is System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
            .Take(8).Select(p => $"{p.Name}={p.Value.GetRawText()}").ToList();
        return $"Valid JSON object · {properties.Count} top-level fields" +
            (numeric.Count > 0 ? " · " + string.Join(", ", numeric) : " · no top-level numeric/boolean metrics");
    }

    public static IReadOnlyList<SamyReminder> ParseCalendarFeed(string ics, string sourceUrl, DateTime from, int daysAhead = 90)
        => Samy.Core.IcsParser.Parse(ics, from, daysAhead)
            .Select(e => new SamyReminder { Text = e.Summary, DueAt = e.Start, FeedSource = sourceUrl })
            .ToList();

    public static string GetSystemExtras()
    {
        var parts = new List<string>();
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            if (drive.IsReady)
            {
                double freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
                double totalGb = drive.TotalSize / 1024d / 1024d / 1024d;
                double usedPercent = totalGb <= 0 ? 0 : 100d * (totalGb - freeGb) / totalGb;
                parts.Add($"System disk · {usedPercent:0}% used · {freeGb:0.0} GB free of {totalGb:0.0} GB");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { parts.Add("Disk · unavailable"); }
        try
        {
            long sent = 0, received = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)))
            {
                try
                {
                    var stats = nic.GetIPv4Statistics();
                    sent += stats.BytesSent;
                    received += stats.BytesReceived;
                }
                catch (NetworkInformationException) { } // adapters without IPv4 are skipped, not fatal
            }
            DateTime sampleNow = DateTime.UtcNow;
            double elapsed = _lastNetworkSample == DateTime.MinValue ? 0 : (sampleNow - _lastNetworkSample).TotalSeconds;
            double receiveRate = elapsed > 0 ? Math.Max(0, received - _lastNetworkReceived) / elapsed : 0;
            double sendRate = elapsed > 0 ? Math.Max(0, sent - _lastNetworkSent) / elapsed : 0;
            _lastNetworkReceived = received; _lastNetworkSent = sent; _lastNetworkSample = sampleNow;
            parts.Add($"Network · ↓ {receiveRate / 1024d:0} KB/s  ↑ {sendRate / 1024d:0} KB/s · totals ↓ {received / 1024d / 1024d:0} MB / ↑ {sent / 1024d / 1024d:0} MB");
        }
        catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException) { parts.Add("Network · unavailable"); }
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            float volume = device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
            bool muted = device.AudioEndpointVolume.Mute;
            parts.Add($"Audio · {volume:0}% {(muted ? "muted" : "unmuted")}");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or System.Runtime.InteropServices.ExternalException) { parts.Add("Audio · unavailable"); }
        try
        {
            if (GetSystemPowerStatus(out var power))
                parts.Add(power.BatteryFlag == 128 ? "Battery · no battery" :
                    power.BatteryLifePercent == 255 ? "Battery · status unavailable" :
                    $"Battery · {power.BatteryLifePercent}%{(power.ACLineStatus == 1 ? " · plugged in" : " · on battery")}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or EntryPointNotFoundException) { parts.Add("Battery · unavailable"); }
        return string.Join(Environment.NewLine, parts);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}

internal sealed record CodingActivityReport(string Repository, int CommitsLast30Days, int ActiveDays, int ChangedFileEntries, IReadOnlyList<string> TopFileTypes);
internal sealed record ProviderProbeResult(int StatusCode, bool Success, string Summary);
