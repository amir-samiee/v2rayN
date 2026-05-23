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
            Console.WriteLine(string.Join("\n", AppManager.Instance.Config.Inbound.Select(a => a.LocalPort)));
            // await API.UpdateAllSubs();
            await ConfigHandler.RemoveInvalidServerResult(API.Config, string.Empty);
            // ...
            await API.SetProxyMode(ESysProxyType.ForcedChange);
            // ...
            await API.DeduplicateServers(null);
            // ...
            await TestCycle(ESpeedActionType.Tcping, null, batchSize: 64);
            // ...
        }
        catch (Exception exc) { Misc.ConLog(exc.Message); }
        finally { await API.Stop(); }
    }
    public static async Task TestCycle(ESpeedActionType action, string? subid, int batchSize = 10) {
        subid ??= string.Empty;
        // deduplicate
        var profiles = await AppManager.Instance.ProfileItems(subid);
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
        var updater = new UpdateWrapper(api);
        for (var i = 0; i < chunks.Count; i++) {
            updater.Done = false;
            Misc.ConLog($"chunk {i + 1}/{chunks.Count} (batch size: {batchSize})");
            var chunk = chunks[i].ToList();
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
            await API.ActivateProfile(sorted.First().IndexId);
        }
    }
    private class UpdateWrapper(API? api = null) {
        public API Api = api ?? new();
        public bool Done = false;
        private int? LeastDelay = null;
        public async Task SpeedUpdater(SpeedTestResult result) {
            var terminations = new List<string?> { ResUI.SpeedtestingCompleted, ResUI.SpeedtestingStop };
            if (terminations.Contains(result.Delay)) {
                Done = true;
                Misc.ConLog(result.Delay);
                return;
            }
            Misc.ConLog($"{result.IndexId}: {result.Delay}");
            int? delay;
            try { delay = Convert.ToInt32(result.Delay); }
            catch (Exception) { return; }
            if (delay < 0) { await API.RemoveServer(result.IndexId); }
            else if (!(delay >= LeastDelay)) {
                Misc.ConLog($"switching to a faster server (delay: {LeastDelay} > {delay})...", "core");
                await API.ActivateProfile(result.IndexId);
                LeastDelay = delay;
            }
        }
        public async Task Test(ESpeedActionType action, List<ProfileItem>? profiles) {
            // var sts = new SpeedtestService(APIManager.Config, UpdateFunc);
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
        var subStat = "{0}: ↑ {1} KB/s {2} ↓ KB/s";
        string FormatUpdate(ServerSpeedItem update) {
            var proxy = string.Format(subStat, "proxy", update.ProxyUp, update.ProxyDown);
            var direct = string.Format(subStat, "direct", update.DirectUp, update.DirectDown);
            return proxy + "  |  " + direct;
        }
        return async update => Misc.ConLog(FormatUpdate(update), "stats");
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
    public static async Task<ConsoleKeyInfo?> WaitForAWhile(int? delay = null) {
        // 10. Wait
        ConLog($"Active: {(await API.Profile)?.Remarks}");
        if (delay is null) {
            Console.Write("press any key to quit: ");
            var Key = Console.ReadKey(intercept: true);
            return Key;
        }
        else { await Task.Delay((int)delay); return null; }
    }
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