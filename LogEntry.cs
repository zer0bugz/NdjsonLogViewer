namespace NdjsonLogViewer;

public sealed class LogEntry
{
    public int Index { get; set; }
    public string TimeRaw { get; init; } = string.Empty;
    public string TimeDisplay { get; init; } = string.Empty;
    public DateTime? Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Stacktrace { get; init; }
    public string ResourceId { get; init; } = string.Empty;
    public string ShortResource { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string Haystack { get; init; } = string.Empty;
    public string RawJson { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;

    public string MessageOneLine =>
        Message.Length == 0
            ? string.Empty
            : Message.Replace('\r', ' ').Replace('\n', ' ');
}
