using System.Text.Json;
using ServiceLib.Handler.Fmt;
using v2rayN.Bridge.Commands;

public class ToJsonCommand : ICommandHandler
{
    public string CommandName => "to-json";
    public string HelpText => "to-json <share-link> - Convert a share link to JSON";

    public void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("ERROR: Missing share link argument");
            Console.WriteLine($"Usage: {HelpText}");
            return;
        }

        string shareLink = args[0];
        var item = FmtHandler.ResolveConfig(shareLink, out string msg);

        if (item != null)
        {
            var json = JsonSerializer.Serialize(
                item,
                new JsonSerializerOptions { WriteIndented = true }
            );
            Console.WriteLine(json);
        }
        else { Console.WriteLine($"ERROR: {msg}"); }
    }
}