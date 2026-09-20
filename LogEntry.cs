namespace LogViewer2;
public sealed class LogEntry
{
    public int Id { get; set; }
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public string Module { get; set; } = "";
    public string EventId { get; set; } = "";
    public string AlarmId { get; set; } = "";
    public string Vrf { get; set; } = "";
    public string Category { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public string File { get; set; } = "";
    public long LineNumber { get; set; }
}
