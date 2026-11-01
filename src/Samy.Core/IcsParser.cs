using System.Globalization;
using System.Text.RegularExpressions;

namespace Samy.Core;

public sealed record IcsEvent(string Summary, DateTime Start);

/// <summary>
/// Minimal, safe iCalendar (.ics) reader for reminders. Handles line folding, UTC/local/all-day starts,
/// DAILY/WEEKLY/MONTHLY/YEARLY rules with INTERVAL, UNTIL and COUNT, and skips cancelled events.
/// Not supported (documented limits): TZID conversion, EXDATE/RDATE, BYDAY and other BY* rules.
/// </summary>
public static class IcsParser
{
    private const int MaxIterations = 400;

    public static IReadOnlyList<IcsEvent> Parse(string ics, DateTime from, int daysAhead = 90)
    {
        var results = new List<IcsEvent>();
        if (string.IsNullOrWhiteSpace(ics)) return results;

        string text = Regex.Replace(ics, "\r?\n[ \t]", "");       // unfold long lines
        text = Regex.Replace(text, "\r\n?|\n", "\n");
        DateTime windowEnd = from.AddDays(daysAhead);

        foreach (Match match in Regex.Matches(text, "BEGIN:VEVENT\\n(.*?)END:VEVENT", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string block = match.Groups[1].Value;
            if (string.Equals(ReadValue(block, "STATUS"), "CANCELLED", StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryParseDate(ReadValue(block, "DTSTART") ?? "", out DateTime start)) continue;

            string summary = Unescape(ReadValue(block, "SUMMARY") ?? "Calendar event");
            string rule = Regex.Match(block, "(?m)^RRULE:(.+)$", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            string freq = (ReadRule(rule, "FREQ") ?? "").ToUpperInvariant();
            int interval = int.TryParse(ReadRule(rule, "INTERVAL"), out int i) ? Math.Clamp(i, 1, 365) : 1;
            long maxCount = long.TryParse(ReadRule(rule, "COUNT"), out long c) && c > 0 ? c : long.MaxValue;

            DateTime until = windowEnd;
            string? untilRaw = ReadRule(rule, "UNTIL");
            if (untilRaw is not null && TryParseDate(untilRaw, out DateTime ruleUntil) && ruleUntil < until) until = ruleUntil;

            bool repeats = freq is "DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY";
            if (!repeats)
            {
                if (start >= from.AddMinutes(-1) && start <= windowEnd) results.Add(new IcsEvent(summary, start));
                continue;
            }

            DateTime occurrence = start;
            long produced = 0; // occurrences generated so far, including ones in the past (COUNT counts from the start)

            // Jump close to "now" for day/week rules so an old series doesn't exhaust the loop guard.
            long stepDays = freq == "DAILY" ? interval : freq == "WEEKLY" ? 7L * interval : 0;
            if (stepDays > 0 && occurrence.Date < from.Date)
            {
                long skip = (long)Math.Floor((from.Date - occurrence.Date).TotalDays / stepDays);
                if (skip > 0)
                {
                    produced += skip;
                    occurrence = occurrence.AddDays(skip * stepDays);
                }
            }

            int guard = 0;
            while (occurrence <= until && produced < maxCount && guard++ < MaxIterations)
            {
                if (occurrence >= from.AddMinutes(-1)) results.Add(new IcsEvent(summary, occurrence));
                produced++;
                occurrence = freq switch
                {
                    "DAILY" => occurrence.AddDays(interval),
                    "WEEKLY" => occurrence.AddDays(7 * interval),
                    "MONTHLY" => occurrence.AddMonths(interval),
                    _ => occurrence.AddYears(interval),
                };
            }
        }

        results.Sort((a, b) => a.Start.CompareTo(b.Start));
        return results;
    }

    private static string? ReadValue(string block, string name)
    {
        var m = Regex.Match(block, "(?m)^" + Regex.Escape(name) + "(?:;[^:\\n]*)?:(.*)$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? ReadRule(string rule, string name)
    {
        var m = Regex.Match(rule, "(?:^|;)" + Regex.Escape(name) + "=([^;]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string Unescape(string value) =>
        value.Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");

    /// <summary>Parses 20261105T100000Z (UTC, converted to local), 20261105T100000 (local) and 20261105 (all-day, midnight local).</summary>
    public static bool TryParseDate(string value, out DateTime date)
    {
        date = default;
        value = value.Trim();
        string[] utcFormats = ["yyyyMMdd'T'HHmmss'Z'"];
        string[] localFormats = ["yyyyMMdd'T'HHmmss", "yyyyMMdd"];
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateTime.TryParseExact(value.ToUpperInvariant(), utcFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)) return false;
            date = utc.ToLocalTime();
            return true;
        }
        return DateTime.TryParseExact(value, localFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
