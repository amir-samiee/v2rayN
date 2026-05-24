using ServiceLib.Common;

internal static class Misc {
    private static string? prev = null;
    public static void ConLog(object? msg, string? name = null, bool overwrite = false) {
        if (name is null || !overwrite) { msg = "\n" + msg; }
        else {
            var encaps = name.IsNullOrEmpty() ? string.Empty : $"[{name}]";
            msg = (name == prev && overwrite ? '\r' : '\n')
        + $"{encaps}  {msg}  {encaps}";
        }
        Console.Write(msg);
        prev = name;
    }
}
