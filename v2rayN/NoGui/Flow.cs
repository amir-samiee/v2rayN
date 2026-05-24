using ServiceLib.Common;
using NoGui;
using NLog;
internal class Flow {
    public static API api = new() { CoreUpdater = Updaters.CoreUpdater(), TrafficUpdater = Updaters.TrafficUpdater() };
    public static async Task Main() {
        Misc.ConLog("App Started");
        try {
            InitLogging();
            await api.Start(true);
            await api.SetProxyMode(ESysProxyType.Unchanged);
            await TestCycle(ESpeedActionType.Tcping, batchSize: 64);
        }
        catch (Exception exc) { Misc.ConLog(exc.Message); Misc.ConLog(exc.StackTrace); }
        finally { await api.Stop(); }
    }
    public static async Task TestCycle(ESpeedActionType action, string? subid = null, int batchSize = 10, List<ProfileItem>? profileItems = null) {
        subid ??= string.Empty;
        var profiles = profileItems ?? await AppManager.Instance.ProfileItems(subid);
        var chunks = profiles?.Chunk(batchSize).ToList() ?? [];
        // reason for batching is that doing all of it would neither
        // seem to work, nor keep the user regularly updated
        var breakFlag = false;
        Console.CancelKeyPress += (sender, e) => {
            breakFlag = true;
            e.Cancel = true;
            Misc.ConLog("Tests cancelled; will stop the tests after currently active batch...", "tests");
        };
        var updater = new Updaters.SpeedWrapper(api);
        for (var i = 0; i < chunks.Count; i++) {
            updater.Done = false;
            var chunk = chunks[i].ToList();
            Misc.ConLog($"chunk {i + 1}/{chunks.Count} (batch size: {chunk.Count}/{batchSize})");
            try { await updater.Test(action, chunk).WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (TimeoutException) { /* ignoreing for now */ }
            if (breakFlag) { break; }
        }
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
