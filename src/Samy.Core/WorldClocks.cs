namespace Samy.Core;

/// <summary>A user-configurable list of time zones for the world-clock card.</summary>
public sealed class WorldClockList
{
    public const int MaxClocks = 8;
    private readonly List<string> _ids = new();

    public static IReadOnlyList<string> DefaultIds { get; } = new[] { "Asia/Kolkata", "Europe/London", "America/New_York" };

    public WorldClockList(IEnumerable<string>? ids = null)
    {
        foreach (var id in ids ?? DefaultIds) TryAdd(id, out _);
        if (_ids.Count == 0) foreach (var id in DefaultIds) TryAdd(id, out _);
    }

    public IReadOnlyList<string> Ids => _ids;

    /// <summary>Resolves an IANA ("Asia/Tokyo") or Windows ("Tokyo Standard Time") zone id. Returns null if unknown.</summary>
    public static TimeZoneInfo? Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id.Trim()); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    public bool TryAdd(string? id, out string message)
    {
        string trimmed = (id ?? "").Trim();
        var zone = Resolve(trimmed);
        if (zone is null) { message = "Unknown time zone. Try a name like Asia/Tokyo or America/Los_Angeles."; return false; }
        if (_ids.Any(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase))) { message = "That clock is already added."; return false; }
        if (_ids.Count >= MaxClocks) { message = $"You can keep up to {MaxClocks} clocks."; return false; }
        _ids.Add(trimmed);
        message = "Added.";
        return true;
    }

    public bool Remove(string id) =>
        _ids.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>"America/New_York" -> "New York". Non-IANA ids are returned unchanged.</summary>
    public static string Label(string id)
    {
        int slash = id.LastIndexOf('/');
        return (slash >= 0 ? id[(slash + 1)..] : id).Replace('_', ' ');
    }

    public IReadOnlyList<string> Format(DateTimeOffset nowUtc)
    {
        var lines = new List<string>();
        foreach (var id in _ids)
        {
            var zone = Resolve(id);
            lines.Add(zone is null ? $"{Label(id)}  unavailable" : $"{Label(id)}  {TimeZoneInfo.ConvertTime(nowUtc, zone):ddd HH:mm}");
        }
        return lines;
    }
}
