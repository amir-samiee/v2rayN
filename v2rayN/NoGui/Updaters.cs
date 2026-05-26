using ServiceLib.Handler.Fmt;
using ServiceLib.Common;
using NoGui;
using ServiceLib.Services;

internal class Updaters {
    internal class SpeedWrapper(API? api = null, int maxRepetitions = 40) {
        public API api = api ?? new();
        public bool skip = false;
        private float LeastDelay = float.PositiveInfinity;
        public int pending = 0;
        private readonly int[] prevNRep = { -1, 0 }; // previous pendings count and its repetition's

        /// <summary> returns whether the loop must be broken </summary>
        private void HandleRepetition(int delay) {
            if (delay == prevNRep[0]) { prevNRep[1] += 1; }
            else { prevNRep[1] = 0; }
            prevNRep[0] = delay;
            if (prevNRep[1] >= maxRepetitions) { skip = true; }
            Misc.ConLog($"pending: {pending}", "tests", true);
        }
        public async Task SpeedUpdater(SpeedTestResult result) {
            if (int.TryParse(result.Delay, out var delay)) {
                pending -= 1;
                HandleRepetition(delay);
                Misc.ConLog($"{result.IndexId}: {result.Delay}");
                if (delay < LeastDelay) {
                    Misc.ConLog($"switching to a faster server (delay: {LeastDelay} > {delay})...", "config", true);
                    LeastDelay = delay;
                    await api.ActivateProfile(result.IndexId);
                }
            }
        }
        public async Task Test(ESpeedActionType action, List<ProfileItem>? profiles, bool removeUnqualified = false) {
            if (skip) { return; }
            profiles ??= await AppManager.Instance.ProfileItems(string.Empty);
            pending += profiles?.Count ?? throw new Exception();
            api.SpeedUpdater = SpeedUpdater;
            SpeedtestService? sts = null;
            sts = await api.RunTest(action, profiles);
            if (removeUnqualified) {
                var pIds = from p in profiles select p.IndexId;
                var timedouts = from px in await
                                SQLiteHelper.Instance.
                                TableAsync<ProfileExItem>().ToListAsync()
                                where pIds.Contains(px.IndexId)
                                where !(px.Delay > 0)
                                select px.IndexId;
                foreach (var id in timedouts) { await api.RemoveServer(id); }
            }
        }
    }

    public static Func<ServerSpeedItem, Task> TrafficUpdater() {
        var maxFormat = "max ⇅ {0} KB";
        var trafficFormat = "{0}: ↑ {1} KB/s ↓ {2} KB/s";
        string? maxId = null;
        long maxTraffic = 0;
        long Traffic(ServerSpeedItem update) { return new long[] { update.ProxyUp, update.ProxyDown }.Sum(); }
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
