using ServiceLib.Handler.Fmt;
using ServiceLib.Common;
using ServiceLib.Resx;
using NoGui;
using NLog;

internal class Flow {
    public static API api = new() { CoreUpdater = GetCustomCoreUpdater(), TrafficUpdater = GetCustomSpeedUpdater() };
    public static async Task Main() {
        Misc.ConLog("App Started");
        try {
            InitLogging();
            await api.Start(true);
            await ConfigHandler.SortServers(AppManager.Instance.Config, "", "Delay", false);
            await ConfigHandler.RemoveInvalidServerResult(API.Config, string.Empty);
            Misc.ConLog("ordering & syncing DB...", "DB");
            var exs = SQLiteHelper.Instance.TableAsync<ProfileExItem>().OrderBy(t => t.Delay);
            Misc.ConLog((await exs.ToListAsync()).Count.ToString(), "DBStats");
            Misc.ConLog("getting profiles in order...", "DB");
            var profiles = await AppManager.Instance.GetProfileItemsByIndexIds(await AppManager.Instance.ProfileItemIndexes("") ?? []);
            Misc.ConLog(profiles.Count.ToString(), "DBStats");
            await api.SetProxyMode(ESysProxyType.Unchanged);
            await api.DeduplicateServers(null);
            await TestCycle(ESpeedActionType.Tcping, profileItems: profiles, batchSize: 64);
        }
        catch (Exception exc) { Misc.ConLog(exc.Message); Misc.ConLog(exc.StackTrace); }
        finally { await api.Stop(); }
    }
    public static async Task TestCycle(ESpeedActionType action, string? subid = null, int batchSize = 10, List<ProfileItem>? profileItems = null) {
        subid ??= string.Empty;
        // deduplicate
        var profiles = profileItems ?? await AppManager.Instance.ProfileItems(subid);
        // batch-test
        // reason for batching is that doing all of it would neither
        // seem to work, nor keep the user regularly updated
        var chunks = profiles?.Chunk(batchSize).ToList() ?? [];
        var breakFlag = false;
        Console.CancelKeyPress += (sender, e) => {
            breakFlag = true;
            e.Cancel = true;
            Misc.ConLog("Tests cancelled; will stop the tests after currently active batch...");
        };
        var updater = new SpeedUpdateWrapper(api);
        for (var i = 0; i < chunks.Count; i++) {
            updater.Done = false;
            var chunk = chunks[i].ToList();
            Misc.ConLog($"chunk {i + 1}/{chunks.Count} (batch size: {chunk.Count}/{batchSize})");
            try { await updater.Test(action, chunk).WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (TimeoutException) { }
            if (breakFlag) { break; }
        }
        // remove invalid
        await ConfigHandler.RemoveInvalidServerResult(API.Config, subid);
        // sort
        await ConfigHandler.SortServers(API.Config, subid, "DelayVal", false);
        // activate
        var sorted = await AppManager.Instance.ProfileItems(subid);
        if (sorted?.Count > 0) {
            await api.ActivateProfile(sorted.First().IndexId);
        }
    }
    private class SpeedUpdateWrapper(API? api = null) {
        public API Api = api ?? new();
        public bool Done = false;
        private float LeastDelay = float.PositiveInfinity;
        public async Task SpeedUpdater(SpeedTestResult result) {
            var terminations = new List<string?> { ResUI.SpeedtestingCompleted, ResUI.SpeedtestingStop };
            if (terminations.Contains(result.Delay)) {
                Misc.ConLog(result.Delay);
                Done = true;
                return;
            }
            Misc.ConLog($"{result.IndexId}: {result.Delay}");
            if (int.TryParse(result.Delay, out var delay)) {
                if (delay < 0) { await Api.RemoveServer(result.IndexId); }
                else if (delay < LeastDelay) {
                    Misc.ConLog($"switching to a faster server (delay: {LeastDelay} > {delay})...", "config");
                    LeastDelay = delay;
                    await Api.ActivateProfile(result.IndexId);
                }
            }
        }
        public async Task Test(ESpeedActionType action, List<ProfileItem>? profiles) {
            profiles ??= await SQLiteHelper.Instance.TableAsync<ProfileItem>().ToListAsync();
            Api.SpeedUpdater = SpeedUpdater;
            var sts = await Api.RunTest(action, profiles);
            var i = 0;
            while (!Done && i < 400) { await Task.Delay(1000); }
            if (i == 400) {
                Misc.ConLog("test operation completion timed out");
                sts.ExitLoop();
            }
        }
    }
    private static Func<ServerSpeedItem, Task> GetCustomSpeedUpdater() {
        var maxFormat = "max⇅ {0} KB";
        var trafficFormat = "{0}: ↑ {1} KB/s ↓ {2} KB/s";
        string? maxId = null;
        long maxTraffic = 0;
        long Traffic(ServerSpeedItem update) {
            return new long[] { update.ProxyUp, update.ProxyDown }.Sum();
        }
        async Task<string> FormatUpdate(ServerSpeedItem update) {
            var proxyFmt = string.Format(trafficFormat, "proxy", update.ProxyUp, update.ProxyDown);
            var directFmt = string.Format(trafficFormat, "direct", update.DirectUp, update.DirectDown);
            var traffic = Traffic(update);
            if (traffic > maxTraffic) {
                maxTraffic = traffic;
                var profile = await AppManager.Instance.GetProfileItem(update.IndexId);
                if (profile is not null && update.IndexId != maxId) {
                    await File.AppendAllTextAsync("KBs.tmp", FmtHandler.GetShareUri(profile) + "\n");
                }
                maxId = update.IndexId;
            }
            var trafficFmt = string.Format(maxFormat, maxTraffic);
            return string.Join("  |  ", proxyFmt, directFmt, trafficFmt);
        }
        return async update => Misc.ConLog(await FormatUpdate(update), "stats");
    }
    private static Func<bool, string, Task> GetCustomCoreUpdater() {
        return (_, msg) => {
            Misc.ConLog(msg.TrimEnd(), "core");
            return Task.CompletedTask;
        };
    }
    public static void InitLogging() {
        Logging.Setup();
        var config = LogManager.Configuration;
        var consoleTarget = new NLog.Targets.ConsoleTarget("console") {
            Layout = "${longdate} [${level:uppercase=true}] ${message:exception=false}"
        };
        config?.AddTarget(consoleTarget);
        config?.LoggingRules.Add(new NLog.Config.LoggingRule("*", NLog.LogLevel.Debug, consoleTarget));
        LogManager.Configuration = config;
    }
}
internal static class Misc {
    private static string? prev = null;
    public static void ConLog(string? msg, string? name = null) {
        if (name is null) { msg = "\n" + msg; }
        else {
            msg = (name == prev ? '\r' : '\n')
        + $"{'[' + name + ']':>7}  {msg}  {"[/" + name + ']'}";
        }
        Console.Write(msg);
        prev = name;
    }
}