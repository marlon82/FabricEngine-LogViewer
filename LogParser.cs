using System.Text.RegularExpressions;
using System.IO;

namespace LogViewer2;

public sealed record ParseResult(List<LogEntry> Entries, string SystemName, string SystemType, string SystemVersion, string MacAddress, int Skipped);

public static class LogParser
{
    private static readonly Regex OldVoss = new(@"^(?<module>[^\[]+)\[(?<time>[^\]]+)\]\s+(?<event>0x\S+)\s+(?<alarm>\S+)\s+(?:(?:DYNAMIC\s+(?:SET|NONE)|CLEAR)\s+)?(?<vrf>\S+)\s+(?<category>\S+)\s+(?<severity>\S+)\s+(?<message>.*)$", RegexOptions.Compiled);
    private static readonly Regex OldErs = new(@"^(?<module>[^\[]+)\[(?<time>[^\]]+)\]\s+(?<category>\S+)\s+(?<severity>\S+)\s+(?<message>.*)$", RegexOptions.Compiled);
    // RFC/VOSS: sequence timestamp hostname module - event - alarm [DYNAMIC SET|CLEAR|NONE] vrf category severity message
    private static readonly Regex NewVoss = new(@"^(?<sequence>\d+)\s+(?<time>\S+)\s+(?<host>\S+)\s+(?<module>\S+)\s+-\s+(?<event>0x\S+)\s+-\s+(?<alarm>\S+)\s+(?:(?:DYNAMIC\s+(?:SET|CLEAR|NONE)|CLEAR)\s+)?(?<vrf>\S+)\s+(?<category>\S+)\s+(?<severity>\S+)\s*(?<message>.*)$", RegexOptions.Compiled);
    private static readonly Regex Trace = new(@"^(?<time>\S+)\s+(?<module>\S+\.c)\s+.*?(?<event>[^\s\[]+)\[(?<alarm>[^\]]*)\].*?\[(?<vrf>[^\]]*)\]\s*(?<category>[^: ]+)\s*:+\s*(?<severity>[^:]+):(?<message>.*)$", RegexOptions.Compiled);

    public static ParseResult ParseFiles(IEnumerable<string> files)
    {
        var list = new List<LogEntry>(); var skipped = 0;
        string systemName = "", systemType = "", systemVersion = "", mac = "";
        foreach (var file in files)
        {
            long lineNo = 0;
            foreach (var raw in File.ReadLines(file))
            {
                lineNo++; var line = raw.TrimEnd();
                Detect(line, ref systemName, ref systemType, ref systemVersion, ref mac);
                LogEntry? entry = null;
                if (line.Contains("<NP>", StringComparison.OrdinalIgnoreCase))
                {
                    if (!line.Contains("</NP>", StringComparison.OrdinalIgnoreCase))
                        entry = Make("00:00:00", "?", "?", "?", "?", "?", "?", "LOGENTRY NOT CLOSED WITH </NP>: " + line);
                    else
                    {
                        var clean = Regex.Replace(line, @"<NP>.*?</NP>", "").Trim();
                        if (clean.Contains("The previous message repeated", StringComparison.OrdinalIgnoreCase) && list.Count > 0)
                        {
                            var previous = list[^1]; var close = clean.IndexOf(']');
                            entry = Make(close >= 0 ? clean[..(close + 1)] : previous.Time, previous.Module, previous.EventId, previous.AlarmId, previous.Vrf, previous.Category, previous.Severity, close >= 0 ? clean[(close + 1)..].Trim() : clean);
                            if (string.IsNullOrWhiteSpace(entry.Date)) entry.Date = previous.Date;
                        }
                        else entry = Match(clean, NewVoss) ?? Match(clean, OldVoss) ?? Match(clean, OldErs);
                    }
                }
                else if (line.Contains(".c", StringComparison.OrdinalIgnoreCase)) entry = Match(line, Trace);
                if (entry is null) { skipped++; continue; }
                entry.Id = list.Count + 1; entry.File = file; entry.LineNumber = lineNo; list.Add(entry);
            }
        }
        return new(list, systemName, systemType, systemVersion, mac, skipped);
    }

    private static LogEntry? Match(string text, Regex regex)
    {
        var m = regex.Match(text); if (!m.Success) return null;
        string G(string n) => m.Groups[n].Success ? m.Groups[n].Value.Trim() : "";
        return Make(G("time"), G("module"), G("event"), G("alarm"), G("vrf"), G("category"), G("severity"), G("message"));
    }
    private static LogEntry Make(string t, string mod, string ev, string alarm, string vrf, string cat, string sev, string msg)
    {
        var (date, time) = SplitTimestamp(t.Trim().Trim('[', ']'));
        return new() { Date=date, Time=time, Module=mod.Trim(), EventId=ev.Trim(), AlarmId=alarm.Trim(), Vrf=vrf.Trim(), Category=cat.Trim(), Severity=sev.Trim(), Message=msg.Trim() };
    }
    private static (string Date, string Time) SplitTimestamp(string value)
    {
        var t = value.IndexOf('T');
        if (t > 0 && t < value.Length - 1) return (value[..t], value[(t + 1)..]);
        var space = value.IndexOf(' ');
        if (space > 0 && space < value.Length - 1) return (value[..space], value[(space + 1)..]);
        return ("", value);
    }
    private static void Detect(string line, ref string name, ref string type, ref string version, ref string mac)
    {
        var mm = Regex.Match(line, @"MAC ADDRESS OF CHASSIS\s+([0-9A-Fa-f:-]{12,17})"); if (mm.Success) mac = mm.Groups[1].Value;
        if (line.Contains("VSP-9000")) type="VSP9000"; else if (line.Contains("VSP-8200") || line.Contains("VSP-8400")) type="VSP8000"; else if (line.Contains("VSP-7200")) type="VSP7200"; else if (line.Contains("VSP-4400") || line.Contains("VSP-4800")) type="VSP4000";
        var mt = Regex.Match(line, @"(?:HW INFO Detected|Detected)\s+([\w-]+)\s+(?:module|chassis)");
        if (mt.Success && !IsFabricEngineModel(type)) type=mt.Groups[1].Value;
        var mv = Regex.Match(line, @"(?:System Software Release|Extreme Networks Fabric Engine Release)\s+([^<]+)"); if (mv.Success) version=mv.Groups[1].Value.Trim();
        var nh = NewVoss.Match(Regex.Replace(line, @"<NP>.*?</NP>", "").Trim());
        if (nh.Success)
        {
            var host = nh.Groups["host"].Value;
            if (IsFabricEngineModel(host)) type = host;
            else name = host;
        }
    }
    private static bool IsFabricEngineModel(string value) => !string.IsNullOrWhiteSpace(value) && value.EndsWith("-FabricEngine", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(value, @"^\d{4,5}-");
}
