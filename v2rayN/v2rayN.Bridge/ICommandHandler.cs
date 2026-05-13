namespace v2rayN.Bridge.Commands;

public interface ICommandHandler
{
    string CommandName { get; }
    string HelpText { get; }
    void Execute(string[] args);
}