using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Island.Core;

/// <summary>Turns Mojang's XML console events into readable log lines; plain loader output passes through.</summary>
public sealed class GameLogFormatter
{
    private StringBuilder? current;
    public string? Push(string line)
    {
        if (current is null && !line.TrimStart().StartsWith("<log4j:Event", StringComparison.Ordinal)) return line;
        current ??= new StringBuilder(); current.AppendLine(line);
        if (!line.Contains("</log4j:Event>", StringComparison.Ordinal) && current.Length < 262144) return null;
        var fragment = current.ToString(); current = null;
        try
        {
            using var reader = XmlReader.Create(new StringReader("<root xmlns:log4j='http://jakarta.apache.org/log4j/'>" + fragment + "</root>"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var item = XElement.Load(reader).Elements().First();
            var message = item.Elements().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value ?? "";
            var exception = item.Elements().FirstOrDefault(e => e.Name.LocalName == "Throwable")?.Value;
            return $"{item.Attribute("level")?.Value ?? "INFO"} [{item.Attribute("thread")?.Value ?? "Game"}] {message}" + (string.IsNullOrWhiteSpace(exception) ? "" : "\n" + exception);
        }
        catch (XmlException) { return fragment.TrimEnd(); }
    }
    public string? Flush() { var text = current?.ToString(); current = null; return text; }
}
