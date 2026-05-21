using ServiceLib.Common;
using ServiceLib.Resx;
using NLog;
using NoGui;

internal class Flow {
    public static async Task Main() {
        Misc.ConLog("App Started");
        try {
            InitLogging();
            await APIManager.Start(true, GetCustomStatUpdater(), GetCustomCoreUpdater());
            // await APIManager.UpdateAllSubs();
            // await ConfigHandler.RemoveInvalidServerResult(APIManager.Config, string.Empty);
            // await APIManager.SetProxyMode(ESysProxyType.ForcedChange);
            // await APIManager.DeduplicateServers(null);
            // ...
            await TestCycle(ESpeedActionType.Tcping, null, batchSize: 64);
            // ...
            // await APIManager.RunTest(ESpeedActionType.Tcping, await SomeProfileIds(4));
            // await APIManager.ActivateProfile(await SomeProfileId());
            // await Misc.WaitForAWhile();
        }
        catch (Exception exc) { Misc.ConLog(exc.Message); }
        finally { await APIManager.Stop(); }
    }
    public static async Task TestCycle(ESpeedActionType action, string? subid, int batchSize = 10) {
        subid ??= string.Empty;
        // deduplicate
        // await APIManager.DeduplicateServers(subid);
        var profiles = await AppManager.Instance.ProfileItems(subid);
        var tester = new UpdateWrapper();
        // batch-test
        // reason for batching is that doing all of it would neither
        // seem to work, nor keep the user regularly updated
        var chunks = profiles?.Chunk(batchSize).ToList() ?? [];
        for (var i = 0; i < chunks.Count; i++) {
            Misc.ConLog($"chunk {i + 1}/{chunks.Count} (batch size: {batchSize})");
            var chunk = chunks[i].ToList();
            var updater = new UpdateWrapper();
            await updater.Test(action, chunk);
        }
        // remove invalid
        await ConfigHandler.RemoveInvalidServerResult(APIManager.Config, subid);
        // sort
        await ConfigHandler.SortServers(APIManager.Config, subid, "DelayVal", false);
        // activate
        var sorted = await AppManager.Instance.ProfileItems(subid);
        if (sorted?.Count > 0) {
            await APIManager.ActivateProfile(sorted.First().IndexId);
        }
    }
    private static Func<ServerSpeedItem, Task> GetCustomStatUpdater() {
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
    private class UpdateWrapper {
        public bool Done = false;
        public async Task UpdateFunc(SpeedTestResult result) {
            var terminations = new List<string?> { ResUI.SpeedtestingCompleted, ResUI.SpeedtestingStop };
            if (terminations.Contains(result.Delay)) { Done = true; }
            Misc.ConLog($"{result.IndexId}: {result.Delay}");
            await AppManager.Instance.GetProfileItemsByIndexIds([result.IndexId ?? string.Empty]);
        }
        public async Task Test(ESpeedActionType action, List<ProfileItem>? profiles) {
            // var sts = new SpeedtestService(APIManager.Config, UpdateFunc);
            profiles ??= await SQLiteHelper.Instance.TableAsync<ProfileItem>().ToListAsync();
            // sts.RunLoop(action, profiles);
            var sts = await APIManager.RunTest(action, profiles, UpdateFunc);
            var i = 0;
            while (!Done && i < 400) { await Task.Delay(1000); }
            if (i == 400) {
                Misc.ConLog("test operation completion timed out");
                sts.ExitLoop();
            }
        }
    }

    public static async Task<List<string>> SomeProfileIds(int? n = null) {
        var profiles = SQLiteHelper.Instance.TableAsync<ProfileItem>();
        if (n is not null) { profiles = profiles.Take((int)n); }
        return (await profiles.ToListAsync()).Select(p => p.IndexId).ToList();
    }
    public static async Task<string> SomeProfileId() {
        // 5. Pick an arbitrary profile
        var profile = await SomeProfileIds(1);
        return profile.First();
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
internal class Misc {
    public static async Task<ConsoleKeyInfo?> WaitForAWhile(int? delay = null) {
        // 10. Wait
        ConLog($"Active: {(await Subprogram.Profile)?.Remarks}");
        if (delay is null) {
            Console.Write("press any key to quit: ");
            var Key = Console.ReadKey(intercept: true);
            return Key;
        }
        else { await Task.Delay((int)delay); return null; }
    }
    private static string? prev = null;
    public static void ConLog(string msg, string? name = null) {
        if (name is null) { msg = "\n" + msg; }
        else {
            msg = (name == prev ? '\r' : '\n')
        + $"{'[' + name + ']':>7}  {msg}  {"[/" + name + ']'}";
        }
        Console.Write(msg);
        prev = name;
    }
}