using ServiceLib.Handler.Fmt;
using ServiceLib.Resx;
using NoGui;

internal class Updaters {
    internal class SpeedWrapper(API? api = null) {
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
                if (delay < 0) {
                    // await Api.RemoveServer(result.IndexId);
                }
                else if (delay < LeastDelay) {
                    Misc.ConLog($"switching to a faster server (delay: {LeastDelay} > {delay})...", "config", true);
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
                Misc.ConLog("test operation completion timed out", "tests");
                sts.ExitLoop();
            }
        }
    }

    public static Func<ServerSpeedItem, Task> TrafficUpdater() {
        var maxFormat = "max ⇅ {0} KB";
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
        return async update => Misc.ConLog(await FormatUpdate(update), "stats", true);
    }
    public static Func<bool, string, Task> CoreUpdater() {
        return (_, msg) => {
            Misc.ConLog(msg.TrimEnd(), "core");
            return Task.CompletedTask;
        };
    }
}
