using ServiceLib.Common;
using NLog;
namespace NoGui;

internal class Flow {
    public static API api = new() { CoreUpdater = Updaters.CoreUpdater(), TrafficUpdater = Updaters.TrafficUpdater() };
    public static async Task MainFlow() {
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
        // the reason to batch is that doing all of it would neither
        // seem to work, nor keep the user regularly updated
        var updater = new Updaters.SpeedWrapper(api);
        var loopBreaker = false;
        Console.CancelKeyPress += (sender, e) => {
            loopBreaker = true;
            e.Cancel = true;
            Misc.ConLog("Tests cancelled; will stop the tests after currently active batch...", "tests");
        };
        for (var i = 0; i < chunks.Count; i++) {
            if (loopBreaker) { break; }
            var chunk = chunks[i].ToList();
            Misc.ConLog($"chunk {i + 1}/{chunks.Count} (batch size: {chunk.Count}/{batchSize})");
            if (updater.skip) { updater.skip = false; continue; }
            try { await updater.Test(action, chunk, true).WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception) { Misc.ConLog("continueing due to timeout...", "tests"); updater.skip = false; continue; }
            await Task.Delay(1000); // so that at the beginning of the test, it doesn't suddenly burst
        }
    }
    public static void InitLogging() {
        Logging.Setup();
        var config = LogManager.Configuration;
        var consoleTarget = new NLog.Targets.ConsoleTarget("console") {
            Layout = "${longdate} [${level:uppercase=true}] ${message:exception=false}"
        };
        config?.AddTarget(consoleTarget);
        config?.LoggingRules.Add(new NLog.Config.LoggingRule("*", LogLevel.Debug, consoleTarget));
        LogManager.Configuration = config;
    }
}
