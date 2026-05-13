using System.Reflection;
using v2rayN.Bridge.Commands;

if (args.Length < 1)
{
    PrintUsage();
    return;
}

string command = args[0];
string[] commandArgs = args.Length > 1 ? args[1..] : [];

// Discover all command handlers at startup
var handlers = LoadCommandHandlers();

if (command == "--help" || command == "-h" || command == "help")
{
    if (commandArgs.Length > 0)
    {
        // Show help for specific command
        PrintCommandHelp(handlers, commandArgs[0]);
    }
    else { PrintUsage(handlers); }
    return;
}

if (!handlers.TryGetValue(command, out var handler))
{
    Console.WriteLine($"Unknown command: {command}");
    PrintUsage(handlers);
    return;
}

// Execute the selected command with its arguments
try { handler.Execute(commandArgs); }
catch (Exception ex)
{
    Console.WriteLine($"ERROR: Command execution failed: {ex.Message}");
    Console.WriteLine($"Usage: {handler.HelpText}");
}

// =============================================
// Helper methods
// =============================================

static Dictionary<string, ICommandHandler> LoadCommandHandlers()
{
    return Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(t => typeof(ICommandHandler).IsAssignableFrom(t)
                    && !t.IsInterface
                    && !t.IsAbstract)
        .Select(t => (ICommandHandler)Activator.CreateInstance(t)!)
        .ToDictionary(h => h.CommandName, StringComparer.OrdinalIgnoreCase);
}

static void PrintUsage(Dictionary<string, ICommandHandler>? handlers = null)
{
    handlers ??= LoadCommandHandlers();

    Console.WriteLine("v2rayN.Bridge - Convert and parse v2rayN share links");
    Console.WriteLine();
    Console.WriteLine("Usage: v2rayN.Bridge <command> [arguments...]");
    Console.WriteLine("       v2rayN.Bridge help <command>  - Show help for specific command");
    Console.WriteLine();
    Console.WriteLine("Available commands:");

    // Print all commands with their help text
    foreach (var cmd in handlers.Values.OrderBy(c => c.CommandName)) { Console.WriteLine($"  {cmd.HelpText}"); }
}

static void PrintCommandHelp(Dictionary<string, ICommandHandler> handlers, string commandName)
{
    if (handlers.TryGetValue(commandName, out var handler))
    {
        Console.WriteLine($"Help for '{commandName}':");
        Console.WriteLine($"  {handler.HelpText}");

        // If the command supports it, you could add more detailed help
        // by adding an optional GetDetailedHelp() method to the interface
    }
    else
    {
        Console.WriteLine($"Unknown command: {commandName}");
        PrintUsage(handlers);
    }
}
