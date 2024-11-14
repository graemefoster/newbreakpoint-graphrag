using System.Diagnostics;

[DebuggerDisplay("{entity}|{type} - {summary}")]
public class Document
{
    public string id { get; set; }
    public string entity { get; set; }
    public string type { get; set; }
    public string summary { get; set; }
}