using ServiceLib.Services;
using NoGui;

internal class Updaters {
    internal class Speed(int maxRepetitions = 40) {
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
        public async Task Update(SpeedTestResult result) {
            if (int.TryParse(result.Delay, out var delay)) {
                pending -= 1;
                HandleRepetition(delay);
                var rep = await AppManager.Instance.GetProfileItem(result.IndexId);
                Misc.ConLog($"↪ {result.Delay + " ms",9} \tid: {rep?.IndexId,19} \tremarks: {rep?.Remarks}"); // ʋʊ⇉⇒
                if (0 < delay && delay < LeastDelay) {
                    Misc.ConLog($"switching to a faster server (delay: {LeastDelay} > {delay})...", "config", true);
                    LeastDelay = delay;
                    await API.ActivateProfile(result.IndexId);
                }
            }
        }
        public async Task Test(ESpeedActionType action, List<ProfileItem>? profiles, bool removeUnqualified = false) {
            if (skip) { return; }
            profiles ??= await AppManager.Instance.ProfileItems(string.Empty);
            pending += profiles?.Count ?? throw new Exception();
            var api = new API() { SpeedUpdater = Update };
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
                foreach (var id in timedouts) { await API.RemoveServer(id); }
            }
        }
    }
    internal class Traffic() {
        private const string trafficFormat = "{0}: ↑ {1} KB/s ↓ {2} KB/s";
        public static long Measure(ServerSpeedItem update) => update.ProxyUp + update.ProxyDown;
        public static string Format(ServerSpeedItem update) {
            var proxyFmt = string.Format(trafficFormat, "proxy", update.ProxyUp, update.ProxyDown);
            var directFmt = string.Format(trafficFormat, "direct", update.DirectUp, update.DirectDown);
            return string.Join("  |  ", proxyFmt, directFmt);
        }
        public static async Task Log(ServerSpeedItem update) { Misc.ConLog(Format(update), "stats", true); }
        private static ServerSpeedItem chosen = new() { ProxyDown = 0, ProxyUp = 0 };
        private static async Task<bool> HandleSwitch(ServerSpeedItem update) {
            ServerSpeedItem[] components = { update, chosen };
            var sorted = from c in components
                         orderby c.ProxyUp, c.ProxyDown
                         select c;
            var newChosen = sorted.Last();
            var didChange = chosen.IndexId != newChosen.IndexId;
            await API.ActivateProfile(newChosen.IndexId);
            chosen = newChosen;
            return didChange;
        }
        public static async Task Main(ServerSpeedItem update) {
            await Log(update);
            if (await HandleSwitch(update)) { Misc.ConLog("switched to a higher-rated profile based on traffic", "config"); }
        }
    }
    public static Func<bool, string, Task> CoreUpdater() {
        return (_, msg) => {
            Misc.ConLog(msg.TrimEnd(), "core");
            return Task.CompletedTask;
        };
    }
}
