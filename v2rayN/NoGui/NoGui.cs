using ServiceLib.Handler.Builder;
using ServiceLib.Handler.SysProxy;
using ServiceLib.Models.Configs;
using ServiceLib.Services;
namespace NoGui;

internal class API {
    public static Config Config =>
    // ease of access + re-emphasize the singularity of these 
    // controversial unit config being passed everywhere
    AppManager.Instance.Config;
    public static Task<ProfileItem?> Profile => ConfigHandler.GetDefaultServer(Config);
    public Func<ServerSpeedItem, Task> TrafficUpdater { get; set; } = async (a) => { };
    public Func<SpeedTestResult, Task> SpeedUpdater { get; set; } = async (a) => { };
    public Func<bool, string, Task> CoreUpdater { get; set; } = async (a, b) => { };
    private static async Task<bool> InitAppManager(bool stats = true) {
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
    private async Task InitStatistics() {
        Console.WriteLine("Enabling Statistics...");
        // 4. Init StatisticsManager
        // Spawns StatisticsXrayService  (polls http://127.0.0.1:{StatePort}/debug/vars)
        // and StatisticsSingboxService  (WebSocket ws://127.0.0.1:{StatePort2}/traffic)
        // both on 1-second background loops
        await StatisticsManager.Instance.Init(Config, TrafficUpdater);
    }
    private async Task InitCoreManager() {
        // 3. Init CoreManager
        // Sets up chmod on non-Windows; stores the update callback.
        await CoreManager.Instance.Init(Config, CoreUpdater);
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
    private async Task Initialize(bool enableStats) {
        await InitAppManager(enableStats);
        await InitCoreManager();
        var allResult = await BuildContext();
        // 9. Write config.json → stop old core → start new core process
        await CoreManager.Instance.LoadCore(
            allResult.MainResult.Context,
            allResult.PreSocksResult?.Context);
    }
    public async Task Start(bool enableStats = true) {
        try {
            await Initialize(enableStats);
            await Task.Delay(3000); // Core starting up...
            if (enableStats) { await InitStatistics(); }
        }
        catch { await Stop(); throw; }
    }
    public static async Task Stop() {
        // 11.
        await AppManager.Instance.AppExitAsync(true);
    }
    public static async Task ActivateProfile(string? profileId) {
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
        await SysProxyHandler.UpdateSysProxy(Config, false);
    }
    public async Task<SpeedtestService> RunTest(ESpeedActionType action, string? subid) {
        var profiles = await AppManager.Instance.ProfileItems(subid ?? string.Empty);
        return await RunTest(action, profiles);
    }
    public async Task<SpeedtestService> RunTest(ESpeedActionType action, List<string> profileIds) {
        var profiles = SQLiteHelper.Instance.TableAsync<ProfileItem>().Where(p => profileIds.Contains(p.IndexId));
        return await RunTest(action, await profiles.ToListAsync());
    }
    public async Task<SpeedtestService> RunTest(ESpeedActionType action, List<ProfileItem>? profiles = null) {
        // 4.5. Run tests (e.g. tcping) on given profiles (null -> all profiles)
        var sts = new SpeedtestService(Config, SpeedUpdater);
        profiles ??= await SQLiteHelper.Instance.TableAsync<ProfileItem>().ToListAsync();
        sts.RunLoop(action, profiles);
        return sts;
    }
    public static async Task AddSubFromUrl(string url) { await ConfigHandler.AddSubItem(Config, url); }
    public static async Task UpdateSubById(string id) {
        await SubscriptionHandler.UpdateProcess(Config, id, false, async (a, b) => { });
    }
    public static async Task UpdateAllSubs() {
        foreach (var item in await SQLiteHelper.Instance.TableAsync<SubItem>().ToListAsync()) {
            await UpdateSubById(item.Id);
        }
    }
    public static async Task<int> DeduplicateServers(string? subid) {
        var (a, b) = await ConfigHandler.DedupServerList(Config, subid ?? string.Empty);
        return a - b;
    }
    public static async Task<int> AddServersFromText(string content) {
        return await ConfigHandler.AddBatchServers(Config, content, string.Empty, false);
    }
    public static async Task RemoveServer(string? indexId) {
        var profile = await AppManager.Instance.GetProfileItem(indexId);
        if (profile is not null) {
            await ConfigHandler.RemoveServers(Config, [profile]);
        }
    }
    public static async Task<int> AddServersFromFile(string filename) {
        var filedata = File.ReadAllText(filename);
        return await AddServersFromText(filedata);
    }
}