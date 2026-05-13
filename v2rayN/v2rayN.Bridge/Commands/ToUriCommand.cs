using System.Text.Json;
using ServiceLib.Handler.Fmt;
using ServiceLib.Models;
using v2rayN.Bridge.Commands;

public class ToUriCommand : ICommandHandler
{
    public string CommandName => "to-uri";
    public string HelpText => "to-uri <json-item> - Convert a ProfileItem JSON to share URI";

    public void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("ERROR: Missing JSON argument");
            Console.WriteLine($"Usage: {HelpText}");
            return;
        }

        string jsonItem = args[0];

        try
        {
            var options = new JsonSerializerOptions();
            var item = JsonSerializer.Deserialize<ProfileItem>(jsonItem, options);

            if (item != null)
            {
                var uri = FmtHandler.GetShareUri(item);
                Console.WriteLine(uri ?? "Failed to generate URI");
            }
            else { Console.WriteLine("ERROR: Invalid ProfileItem JSON"); }
        }
        catch (Exception ex) { Console.WriteLine($"ERROR: {ex.Message}"); }
    }
}