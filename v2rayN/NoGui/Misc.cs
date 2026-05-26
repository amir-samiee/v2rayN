using ServiceLib.Common;

internal static class Misc {
    private static string? prev = null;
    public static void ConLog(object? msg, string? name = null, bool overwrite = false) {
        if (name.IsNullOrEmpty()) { msg = "\n" + msg; }
        else {
            var encaps = name.IsNullOrEmpty() ? string.Empty : $"[{name}]";
            msg = (name == prev && overwrite ? '\r' : '\n')
        + $"{encaps}  {msg}  {encaps}";
        }
        Console.Write(msg);
        prev = name;
    }
    public static string FilePathShortName(string url, bool noExt = true, int? onlyEnds = null) {
        var name = url.Split("/", StringSplitOptions.None).Last();
        if (noExt) {
            var i = name.LastIndexOf('.');
            if (i >= 0) { name = name[..i]; }
        }
        var side = onlyEnds ?? (name.Length << 1);
        if (name.Length >= side << 1) { name = name[..side] + name[(name.Length - side)..]; }
        return name;
    }
}
