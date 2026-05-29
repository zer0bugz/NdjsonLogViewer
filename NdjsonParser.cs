using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NdjsonLogViewer;

public readonly record struct ParseProgress(int Percent, string Status);

public static class NdjsonParser
{
    private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true };
    private static readonly string[] DefaultExtensions = { ".json", ".ndjson", ".jsonl" };

    public static IReadOnlyList<string> EnumerateLogFiles(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path);
            if (DefaultExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                list.Add(path);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public static async Task<List<LogEntry>> ParseFilesAsync(
        IReadOnlyList<string> paths,
        IProgress<ParseProgress>? progress,
        CancellationToken ct = default)
    {
        var entries = new List<LogEntry>();
        if (paths.Count == 0) return entries;

        long totalBytes = 0;
        foreach (var p in paths)
        {
            try { totalBytes += new FileInfo(p).Length; }
            catch { /* skip unreadable */ }
        }
        if (totalBytes == 0) totalBytes = 1;

        long bytesDoneBefore = 0;
        int lastReported = -1;
        int fileIdx = 0;

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            fileIdx++;
            long fileSize;
            try { fileSize = new FileInfo(path).Length; }
            catch { fileSize = 0; }

            string fileLabel = paths.Count > 1
                ? $"({fileIdx}/{paths.Count}) {Path.GetFileName(path)}"
                : Path.GetFileName(path);

            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 64 * 1024, useAsync: true);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                {
                    ct.ThrowIfCancellationRequested();

                    var trimmed = line.Trim();
                    if (trimmed.Length != 0)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(trimmed);
                            entries.Add(BuildEntry(doc.RootElement, trimmed, path));
                        }
                        catch (JsonException) { }
                    }

                    if (progress != null)
                    {
                        long cumulative = bytesDoneBefore + stream.Position;
                        int pct = (int)Math.Min(99, cumulative * 100 / totalBytes);
                        if (pct != lastReported)
                        {
                            lastReported = pct;
                            progress.Report(new ParseProgress(pct, fileLabel));
                        }
                    }
                }
            }
            catch (IOException)
            {
                // skip files we can't read but keep going
            }

            bytesDoneBefore += fileSize;
        }

        progress?.Report(new ParseProgress(99, "Sorting…"));
        entries.Sort(CompareByTimestamp);
        for (int i = 0; i < entries.Count; i++)
            entries[i].Index = i + 1;

        progress?.Report(new ParseProgress(100, "Done."));
        return entries;
    }

    public static List<LogEntry> ParseText(string text, string sourceLabel = "")
    {
        var entries = new List<LogEntry>();
        foreach (var raw in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                entries.Add(BuildEntry(doc.RootElement, trimmed, sourceLabel));
            }
            catch (JsonException) { }
        }
        entries.Sort(CompareByTimestamp);
        for (int i = 0; i < entries.Count; i++) entries[i].Index = i + 1;
        return entries;
    }

    private static int CompareByTimestamp(LogEntry a, LogEntry b)
    {
        if (a.Timestamp.HasValue && b.Timestamp.HasValue)
            return a.Timestamp.Value.CompareTo(b.Timestamp.Value);
        if (a.Timestamp.HasValue) return 1;   // timestamped after untimestamped
        if (b.Timestamp.HasValue) return -1;
        return string.CompareOrdinal(a.SourceFile, b.SourceFile);
    }

    private static LogEntry BuildEntry(JsonElement obj, string rawJson, string sourceFile)
    {
        string timeRaw = GetString(obj, "time")
            ?? GetNestedString(obj, "properties", "preciseDateTime")
            ?? string.Empty;

        DateTime? ts = null;
        string timeDisplay = timeRaw;
        if (DateTime.TryParse(
                timeRaw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            ts = dt;
            timeDisplay = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        string level = GetString(obj, "level")
            ?? GetNestedString(obj, "properties", "level")
            ?? "Verbose";

        string category = GetString(obj, "category") ?? "AppServiceAppLogs";

        string source = GetNestedString(obj, "properties", "source")
            ?? GetString(obj, "operationName")
            ?? "System";

        string message = GetString(obj, "resultDescription")
            ?? GetNestedString(obj, "properties", "message")
            ?? string.Empty;

        string? stacktrace = GetNestedString(obj, "properties", "stacktrace");
        string resourceId = GetString(obj, "resourceId") ?? string.Empty;
        string instanceId = GetNestedString(obj, "properties", "webSiteInstanceId") ?? string.Empty;

        string shortResource = resourceId;
        int sitesIdx = resourceId.IndexOf("/SITES/", StringComparison.OrdinalIgnoreCase);
        if (sitesIdx >= 0)
            shortResource = resourceId[(sitesIdx + "/SITES/".Length)..];

        string pretty;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            pretty = JsonSerializer.Serialize(doc.RootElement, PrettyOptions);
        }
        catch
        {
            pretty = rawJson;
        }

        var sb = new StringBuilder(level.Length + source.Length + category.Length + message.Length + shortResource.Length + timeDisplay.Length + sourceFile.Length + 16);
        sb.Append(level).Append(' ')
          .Append(source).Append(' ')
          .Append(category).Append(' ')
          .Append(message).Append(' ')
          .Append(shortResource).Append(' ')
          .Append(timeDisplay).Append(' ')
          .Append(sourceFile);
        string haystack = sb.ToString().ToLowerInvariant();

        return new LogEntry
        {
            Index = 0,
            TimeRaw = timeRaw,
            Timestamp = ts,
            TimeDisplay = timeDisplay,
            Level = level,
            Category = category,
            Source = source,
            Message = message,
            Stacktrace = stacktrace,
            ResourceId = resourceId,
            ShortResource = shortResource,
            InstanceId = instanceId,
            Haystack = haystack,
            RawJson = pretty,
            SourceFile = sourceFile,
        };
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Number => prop.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => prop.GetRawText()
        };
    }

    private static string? GetNestedString(JsonElement obj, string a, string b)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(a, out var inner)) return null;
        return GetString(inner, b);
    }
}
