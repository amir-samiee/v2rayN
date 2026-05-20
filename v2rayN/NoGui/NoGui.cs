using ServiceLib.Handler.SysProxy;
using ServiceLib.Handler.Builder;
using ServiceLib.Handler.Fmt;
using ServiceLib.Services;
using ServiceLib.Handler;
using ServiceLib.Manager;
using ServiceLib.Common;
using ServiceLib.Helper;
using ServiceLib.Models;
using ServiceLib.Enums;
using ServiceLib.Resx;
using NLog;
namespace NoGui;

internal class Subprogram {
    public static Config Config => AppManager.Instance.Config;
    public static Task<ProfileItem?> Profile => ConfigHandler.GetDefaultServer(Config);
    private static async Task<bool> InitApp(bool stats = true) {
        // 1. Bootstrap
        var init_app_success = AppManager.Instance.InitApp();
        if (!init_app_success) { return false; }
        // 2. Enable statistics
        // Must be set BEFORE BuildAll, because CoreConfigContextBuilder reads this flag
        // to decide whether to include the stats API endpoint in the generated core config.
        Config.GuiItem.EnableStatistics = stats;
        // 1. ...
        return AppManager.Instance.InitComponents();
    }
    private static async Task InitStatistics(Func<ServerSpeedItem, Task>? updateFunc = null) {
        Console.WriteLine("Enabling Statistics...");
        // 4. Init StatisticsManager
        // Spawns StatisticsXrayService  (polls http://127.0.0.1:{StatePort}/debug/vars)
        // and StatisticsSingboxService  (WebSocket ws://127.0.0.1:{StatePort2}/traffic)
        // both on 1-second background loops.
        updateFunc ??= update => {
            Console.Write($"[stats]   proxy: ↑ {update.ProxyUp} KB/s ↓ {update.ProxyDown} KB/s");
            Console.Write($"  |  direct: ↑ {update.DirectUp} KB/s ↓ {update.DirectDown} KB/s\r");
            return Task.CompletedTask;
        };
        await StatisticsManager.Instance.Init(Config, updateFunc);
    }
    private static async Task InitCoreManager() {
        // 3. Init CoreManager
        // Sets up chmod on non-Windows; stores the update callback.
        await CoreManager.Instance.Init(Config, (_, msg) => {
            Console.WriteLine($"[core] {msg.TrimEnd()} [/core]");
            return Task.CompletedTask;
        });
    }
    private static async Task<CoreConfigContextBuilderAllResult> BuildContext() {
        // 7. Resolve the active ProfileItem
        var profileItem = await Profile ?? throw new InvalidOperationException("Could not resolve default server.");
        // 8. Build the proxy config context
        // Resolves routing, DNS, inbound/outbound, stats API endpoint, optional pre-socks.
        var allResult = await CoreConfigContextBuilder.BuildAll(Config, profileItem);
        if (!allResult.Success) { throw new InvalidOperationException("Config build failed."); }
        return allResult;
    }
    private static async Task InitFinal() {
        var allResult = await BuildContext();
        // 9. Write config.json → stop old core → start new core process
        await CoreManager.Instance.LoadCore(
            allResult.MainResult.Context,
            allResult.PreSocksResult?.Context);
        await SysProxyHandler.UpdateSysProxy(Config, false);
    }
    public static async Task Start(bool enableStats = true, Func<ServerSpeedItem, Task>? updateFunc = null) {
        try {
            await InitApp(enableStats);
            await InitCoreManager();
            await InitFinal();
            await Task.Delay(3000); // Core loading...
            if (enableStats) { await InitStatistics(updateFunc); }
        }
        catch { await Stop(); throw; }
    }
    public static async Task Stop() {
        // 11.
        await AppManager.Instance.AppExitAsync(true);
    }
}
public class ApiManager {
    protected static Config Config
    // not returning AppManager.Instance.Config to prevent any potential bug-leading 
    // conflicts; though they ultimately reference the same object either way
    => Subprogram.Config;
    public static async Task Start(bool enableStats = true, Func<ServerSpeedItem, Task>? updateFunc = null) { await Subprogram.Start(enableStats, updateFunc); }
    public static async Task Stop() { await Subprogram.Stop(); }
    public static async Task ActivateProfile(string profileId) {
        // 6. Profile activation
        // Sets config.IndexId = targetId and persists config.json to disk.
        await ConfigHandler.SetDefaultServerIndex(Config, profileId);
    }
    public static async Task SetProxyMode(ESysProxyType system_proxy_config = ESysProxyType.Unchanged, bool tun_mode = false) {
        // 8.5. Handle the system proxy use status
        // ESysProxyType.Unchanged;                 // Option 1 – don't touch system proxy
        // ESysProxyType.ForcedChange;              // Option 2 – set system proxy → 127.0.0.1:<socks-port>
        // ESysProxyType.ForcedClear;               // Option 3 – clear system proxy
        Config.TunModeItem.EnableTun = tun_mode;    // Option 4 – TUN mode (requires admin/root; set BEFORE BuildAll+LoadCore)
        if (tun_mode && system_proxy_config != ESysProxyType.Unchanged) {
            throw new ArgumentException("For clarity, you can't set both TUN mode and proxy using this method");
        }
        Config.SystemProxyItem.SysProxyType = system_proxy_config;
    }
    public static async Task RunTest(ESpeedActionType action, List<string> profileIds) {
        var profiles = SQLiteHelper.Instance.TableAsync<ProfileItem>().Where(p => profileIds.Contains(p.IndexId));
        await RunTest(action, await profiles.ToListAsync());
    }
    public static async Task RunTest(ESpeedActionType action, List<ProfileItem>? profiles = null) {
        // 4.5. Run tests (e.g. tcping) on given profiles (null -> all profiles)
        var Done = false;
        async Task TestResFunc(SpeedTestResult result) {
            var terminations = new List<string?> { ResUI.SpeedtestingCompleted, ResUI.SpeedtestingStop };
            if (terminations.Contains(result.Delay)) { Done = true; }
            Console.WriteLine($"{result.IndexId}: {result.Delay}");
        }
        var sts = new SpeedtestService(Config, TestResFunc);
        profiles ??= await SQLiteHelper.Instance.TableAsync<ProfileItem>().ToListAsync();
        sts.RunLoop(action, profiles);
        var i = 0;
        while (!Done && i < 400) {
            Console.Write($"{++i}\r");
            await Task.Delay(1000);
        }
        if (i == 400) {
            Console.WriteLine("test operation completion timed out");
            sts.ExitLoop();
        }
    }
    public static async Task AddSubFromUrl(string url) {
        await ConfigHandler.AddSubItem(Config, url);
    }
    public static async Task UpdateSubById(string id) {
        await SubscriptionHandler.UpdateProcess(
                Config, id, false, static async (a, b) => { });
    }
    public static async Task UpdateAllSubs() {
        foreach (var item in await SQLiteHelper.Instance.TableAsync<SubItem>().ToListAsync()) {
            await UpdateSubById(item.Id);
        }
    }
    public static async Task<SubItem?> GetSubByUrl(string url) {
        var subitems = SQLiteHelper.Instance.TableAsync<SubItem>().Where(s => s.Url == url);
        return await subitems.FirstOrDefaultAsync();
    }
    public static async Task<int> DeduplicateServers(string? subid) {
        var (a, b) = await ConfigHandler.DedupServerList(Config, subid ?? string.Empty);
        return a - b;
    }
    public static async Task<int> AddServersFromText(string content) {
        return await ConfigHandler.AddBatchServers(Config, content, string.Empty, false);
    }
    public static async Task<int> AddServersFromFile(string filename) {
        var content = File.ReadAllText(filename);
        return await AddServersFromText(content);
    }
}
internal class Etc {
    private static async Task Main() {
        Console.WriteLine("App Started");
        try {
            InitLogging();
            await ApiManager.Start();
            await ApiManager.SetProxyMode(ESysProxyType.ForcedChange);
            await ApiManager.RunTest(ESpeedActionType.Tcping, await SomeProfileIds(4));
            await ApiManager.ActivateProfile(await SomeProfileId());
            await WaitForAWhile();
        }
        catch (Exception exc) { Console.WriteLine(exc.Message); }
        finally { await ApiManager.Stop(); }
    }
    public static async Task<ConsoleKeyInfo?> WaitForAWhile(int? delay = null) {
        // 10. Wait
        Console.WriteLine($"Active: {(await Subprogram.Profile)?.Remarks}");
        if (delay is null) {
            Console.Write("press any key to quit: ");
            var Key = Console.ReadKey(intercept: true);
            return Key;
        }
        else { await Task.Delay((int)delay); return null; }
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
    private static void InitLogging() {
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
