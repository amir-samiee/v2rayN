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
        await StatisticsManager.Instance.Init(Config, updateFunc ?? (async (a) => { }));
    }
    private static async Task InitCoreManager(Func<bool, string, Task>? updateFunc = null) {
        // 3. Init CoreManager
        // Sets up chmod on non-Windows; stores the update callback.
        await CoreManager.Instance.Init(Config, updateFunc ?? (async (a, b) => { }));
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
    public static async Task Start(bool enableStats = true, Func<ServerSpeedItem, Task>? statUpdater = null, Func<bool, string, Task>? coreLogUpdater = null) {
        try {
            await InitApp(enableStats);
            await InitCoreManager(coreLogUpdater);
            await InitFinal();
            await Task.Delay(3000); // Core starting up...
            if (enableStats) { await InitStatistics(statUpdater); }
        }
        catch { await Stop(); throw; }
    }
    public static async Task Stop() {
        // 11.
        await AppManager.Instance.AppExitAsync(true);
    }
}
public class APIManager {
    public static Config Config
    // not returning AppManager.Instance.Config to prevent any potential bug-leading 
    // conflicts; though they ultimately reference the same object either way
    => Subprogram.Config;
    public static async Task Start(bool enableStats = true, Func<ServerSpeedItem, Task>? statUpdater = null, Func<bool, string, Task>? coreLogUpdater = null) { await Subprogram.Start(enableStats, statUpdater, coreLogUpdater); }
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
    public static async Task<SpeedtestService> RunTest(ESpeedActionType action, string? subid) {
        var profiles = await AppManager.Instance.ProfileItems(subid ?? string.Empty);
        return await RunTest(action, profiles);
    }
    public static async Task<SpeedtestService> RunTest(ESpeedActionType action, List<string> profileIds) {
        var profiles = SQLiteHelper.Instance.TableAsync<ProfileItem>().Where(p => profileIds.Contains(p.IndexId));
        return await RunTest(action, await profiles.ToListAsync());
    }
    public static async Task<SpeedtestService> RunTest(ESpeedActionType action, List<ProfileItem>? profiles = null, Func<SpeedTestResult, Task>? updateFunc = null) {
        // 4.5. Run tests (e.g. tcping) on given profiles (null -> all profiles)
        var sts = new SpeedtestService(Config, updateFunc ?? (async (a) => { }));
        profiles ??= await SQLiteHelper.Instance.TableAsync<ProfileItem>().ToListAsync();
        sts.RunLoop(action, profiles);
        return sts;
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
        var filedata = File.ReadAllText(filename);
        return await AddServersFromText(filedata);
    }
}