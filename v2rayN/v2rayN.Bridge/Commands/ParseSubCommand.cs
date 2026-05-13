
using System.Text.Json;
using ServiceLib.Handler.Fmt;
using ServiceLib.Models;
using v2rayN.Bridge.Commands;

public class ParseSubCommand : ICommandHandler
{
    public string CommandName => "parse-sub";
    public string HelpText => "parse-sub <config-text> - Parse subscription/config text with share links";

    public void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("ERROR: Missing config text argument");
            Console.WriteLine($"Usage: {HelpText}");
            return;
        }

        string configText = args[0];
        string msg;
        var lines = configText.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var items = new List<ProfileItem>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                var item = FmtHandler.ResolveConfig(trimmed, out msg);
                if (item != null) { items.Add(item); }
            }
        }

        var json = JsonSerializer.Serialize(
            items,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }
        );
        Console.WriteLine(json);
    }
}