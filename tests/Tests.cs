using System; using System.Collections.Generic; using System.Globalization; using System.IO; using System.Linq; using System.Text;
using WaktuSolat;

static class T
{
    static int fails;
    static void Ok(bool c, string m) { Console.WriteLine((c ? "PASS " : "FAIL ") + m); if (!c) fails++; }
    static bool Throws(Action a) { try { a(); return false; } catch (FormatException) { return true; } catch (InvalidCastException) { return true; } catch (KeyNotFoundException) { return true; } }

    static readonly string[] Ms = { "Jan","Feb","Mac","Apr","Mei","Jun","Jul","Ogo","Sep","Okt","Nov","Dis" };
    // e-Solat style entry; month abbreviation in Malay like the real API
    static string Day(DateTime d, string fajr = "05:51:00") =>
        $"{{\"hijri\":\"1448-05-13\",\"date\":\"{d.Day:00}-{Ms[d.Month - 1]}-{d.Year}\",\"day\":\"{d.DayOfWeek}\",\"imsak\":\"05:41:00\",\"fajr\":\"{fajr}\",\"syuruk\":\"06:59:00\",\"dhuha\":\"07:27:00\",\"dhuhr\":\"12:59:00\",\"asr\":\"16:14:00\",\"maghrib\":\"18:57:00\",\"isha\":\"20:08:00\"}}";
    static string Year(int y) { var days = new List<string>(); for (var d = new DateTime(y, 1, 1); d.Year == y; d = d.AddDays(1)) days.Add(Day(d)); return "{\"prayerTime\":[" + string.Join(",", days) + "],\"status\":\"OK!\",\"zone\":\"WLY01\"}"; }

    static int Main()
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("ms-MY");

        // --- dates: English + Malay month names
        bool allMonths = true;
        string[] En = { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };
        for (int m = 1; m <= 12; m++)
            allMonths &= Util.ParseDate($"15-{Ms[m - 1]}-2026") == new DateTime(2026, m, 15) && Util.ParseDate($"15-{En[m - 1]}-2026") == new DateTime(2026, m, 15);
        Ok(allMonths, "all 12 months parse in Malay and English (Mac, Mei, Ogo, Okt, Dis...)");
        Ok(Util.ParseDate("2026-11-04") == new DateTime(2026, 11, 4) && Util.ParseDate("31-DIS-2026") == new DateTime(2026, 12, 31), "ISO and upper-case dates");
        Ok(Throws(() => Util.ParseDate("31-Foo-2026")) && Throws(() => Util.ParseDate("1-1-1900")), "bad dates rejected");

        // --- parsing
        var p = Sources.ParseJakim("{\"prayerTime\":[" + Day(new DateTime(2026, 11, 4)) + "]}");
        Ok(p.Count == 1 && p[0].Date == new DateTime(2026, 11, 4) && p[0].Zohor == new TimeSpan(12, 59, 0) && p[0].Hijri == "1448-05-13", "parse JAKIM e-Solat day");
        Ok(Sources.ParseJakim(Year(2026)).Count == 365, "full year with Malay months parses (365 days)");
        var mixed = Sources.ParseJakim("{\"prayerTime\":[" + Day(new DateTime(2026, 11, 4)) + "," + Day(new DateTime(2026, 11, 5), "25:00:00") + "]}");
        Ok(mixed.Count == 1, "one bad day is skipped, good days kept");
        Ok(Util.HijriText("1448-05-13") == "13 Jamadilawal 1448H", "hijri text");

        // --- hostile / broken input
        Ok(Throws(() => Json.Parse(new string('[', 100000) + new string(']', 100000))), "deep nesting rejected (no stack overflow)");
        Ok(Throws(() => Json.Parse(new string(' ', 2_000_001))), "oversized JSON rejected");
        Ok(Throws(() => Sources.ParseJakim("{\"prayerTime\":[" + Day(new DateTime(2026, 11, 4), "25:99:00") + "]}")), "only-invalid data rejected");
        Ok(Throws(() => Sources.ParseJakim("{\"prayerTime\":[" + Day(new DateTime(2026, 11, 4), "14:00:00") + "]}")), "out-of-order times rejected");
        Ok(Throws(() => Sources.ParseJakim("<html>blocked by proxy</html>")), "HTML/proxy page rejected");
        Ok(Throws(() => Sources.ParseJakim("{\"prayerTime\":[{\"date\":\"x\"}]}")), "missing fields rejected");
        Ok(Throws(() => Sources.ParseJakim("{\"prayerTime\":[" + Day(new DateTime(2026, 11, 4)).Replace("1448-05-13", "<script>") + "]}")), "bad hijri rejected");

        // --- offline estimate
        var z = Zones.Find("WLY01");
        var est = PrayerCalc.Calculate(new DateTime(2026, 11, 4), z.Lat, z.Lng);
        Console.WriteLine($"     estimate WLY01: Subuh {est.Subuh} Syuruk {est.Syuruk} Zohor {est.Zohor} Asar {est.Asar} Maghrib {est.Maghrib} Isyak {est.Isyak}");
        bool near(TimeSpan a, TimeSpan b) => Math.Abs((a - b).TotalMinutes) <= 4;
        Ok(near(est.Zohor, new TimeSpan(12, 59, 0)) && near(est.Maghrib, new TimeSpan(18, 57, 0)) && near(est.Isyak, new TimeSpan(20, 8, 0)), "estimate: Zohor/Maghrib/Isyak within 4 min");
        Ok(Zones.All.All(zz => PrayerCalc.Calculate(new DateTime(2026, 6, 21), zz.Lat, zz.Lng).IsValid()), "estimate valid for every zone");
        Ok(Zones.All.Select(x => x.Code).Distinct().Count() == Zones.All.Length, $"{Zones.All.Length} unique zones");
        Ok(Zones.Find("../../evil").Code == "WLY01", "unknown zone code falls back to WLY01");

        // --- data store: fetch, cache, reload, offline, tampered cache
        var dir = Path.Combine(Path.GetTempPath(), "ws-test-" + Guid.NewGuid());
        Clock.NowProvider = () => new DateTime(2026, 11, 4, 10, 0, 0);
        string requested = null;
        var store = new DataStore(dir, "WLY01") { HttpGet = u => { requested = u; return Year(2026); } };
        store.Fetch(store.Zone, new DateTime(2026, 11, 4));
        Console.WriteLine("     status: " + store.Status);
        Ok(requested.StartsWith("https://www.e-solat.gov.my/") && requested.EndsWith("zone=WLY01"), "requests official JAKIM over HTTPS");
        Ok(store.HasOfficial(new DateTime(2026, 11, 4)) && !store.Get(new DateTime(2026, 11, 4)).Estimated && !store.LastFetchFailed, "official data used after fetch");
        var store2 = new DataStore(dir, "WLY01") { HttpGet = u => throw new Exception("offline") };
        Ok(store2.HasOfficial(new DateTime(2026, 12, 31)), "cache works offline after restart");
        Ok(store2.Get(new DateTime(2027, 1, 1)).Estimated, "falls back to estimate when no data");
        File.AppendAllLines(Path.Combine(dir, "cache_WLY01.txt"), new[] { "garbage", "2026-11-05|x|99:00|a|b|c|d|e|f", "2027-02-01||05:00|05:10|07:00|06:00|16:00|19:00|20:00" });
        var store3 = new DataStore(dir, "WLY01");
        Ok(store3.HasOfficial(new DateTime(2026, 11, 4)) && !store3.HasOfficial(new DateTime(2027, 2, 1)), "tampered cache lines ignored, good lines kept");

        // --- timeline
        var day = new DateTime(2026, 11, 4);
        var s1 = Timeline.Build(store, day.AddHours(7.5));
        Ok(s1.Current == null && s1.Next.Name == "Zohor", "07:30 → no current waktu, next Zohor");
        var s2 = Timeline.Build(store, day.AddHours(6));
        Ok(s2.Current?.Name == "Subuh" && s2.Next.Name == "Syuruk", "06:00 → Subuh, ends at Syuruk");
        var s3 = Timeline.Build(store, day.AddHours(2));
        Ok(s3.Current?.Name == "Isyak" && s3.Current.At.Date == day.AddDays(-1) && s3.Next.Name == "Subuh" && s3.Progress > 0 && s3.Progress < 1, "02:00 → still yesterday's Isyak, next Subuh");
        Ok(Timeline.Build(store, new DateTime(2026, 11, 6, 11, 0, 0)).Next.Name == "Jumaat", "Friday shows Jumaat");

        // --- reminders: simulate 30 hours minute by minute; reminder = 30 min before CURRENT waktu ends
        var done = new DoneStore(dir);
        var rem = new Reminders(); var alerts = new List<string>();
        for (var t = day; t < day.AddDays(1).AddHours(6); t = t.AddMinutes(1))
        {
            var snap = Timeline.Build(store, t);
            var e = rem.Check(snap, t, 30, done.IsDone);
            if (e != null) alerts.Add($"{t:HH:mm} {e.Name} (tamat {snap.Next.At:HH:mm})");
        }
        alerts.ForEach(a => Console.WriteLine("     " + a));
        Ok(alerts.SequenceEqual(new[] { "05:21 Isyak (tamat 05:51)", "06:29 Subuh (tamat 06:59)", "15:44 Zohor (tamat 16:14)",
                                        "18:27 Asar (tamat 18:57)", "19:38 Maghrib (tamat 20:08)", "05:21 Isyak (tamat 05:51)" }),
           "reminders fire 30 min before each waktu ends, once each");

        // ticks
        var asar = Timeline.Build(store, day.AddHours(17)).Current;
        done.Toggle(asar);
        var rem2 = new Reminders();
        Ok(rem2.Check(Timeline.Build(store, day.AddHours(18.5)), day.AddHours(18.5), 30, done.IsDone) == null, "ticked Asar → no reminder");
        Ok(new DoneStore(dir).IsDone(asar), "tick survives restart");
        var jumaat = Timeline.Build(store, new DateTime(2026, 11, 6, 14, 0, 0)).Current;
        Ok(jumaat.Name == "Jumaat" && jumaat.Key == "2026-11-06|Zohor", "Jumaat uses the Zohor slot key");
        done.Toggle(asar);
        Ok(!new DoneStore(dir).IsDone(asar), "untick persists");
        File.AppendAllLines(Path.Combine(dir, "done.txt"), new[] { "2020-01-01|Asar", new string('x', 5000), "junk" });
        Ok(!new DoneStore(dir).IsDone(new Entry("Asar", new DateTime(2020, 1, 1, 16, 0, 0), true)), "old/garbage tick lines dropped");
        var rem3 = new Reminders();
        Ok(rem3.Check(Timeline.Build(store, new DateTime(2026, 11, 4, 16, 13, 30)), new DateTime(2026, 11, 4, 16, 13, 30), 30, _ => false) == null, "no reminder in the final minute");
        Ok(new Reminders().Check(Timeline.Build(store, day.AddHours(15.9)), day.AddHours(15.9), 0, _ => false) == null, "reminder off = none");

        // --- review fixes
        var night = Timeline.Build(store, day.AddHours(1));
        Ok(night.Current?.Name == "Isyak" && night.Today.Any(e => e.Key == night.Current.Key), "after midnight, last night's Isyak is in the list (can be ticked)");
        var r4 = new Reminders(); var t4 = new DateTime(2026, 11, 4, 18, 30, 0); var sn4 = Timeline.Build(store, t4);
        bool ticked = true;
        Ok(r4.Check(sn4, t4, 30, _ => ticked) == null, "ticked → no reminder");
        ticked = false;
        Ok(r4.Check(sn4, t4.AddMinutes(1), 30, _ => ticked)?.Name == "Asar", "untick inside window → reminder comes back");
        Ok(DataStore.Allowed(new Uri("https://www.e-solat.gov.my/x")) && DataStore.Allowed(new Uri("https://e-solat.gov.my/"))
           && !DataStore.Allowed(new Uri("http://www.e-solat.gov.my/")) && !DataStore.Allowed(new Uri("https://evil-e-solat.gov.my.attacker.com/"))
           && !DataStore.Allowed(new Uri("https://e-solat.gov.my.evil.com/")), "only HTTPS e-solat.gov.my allowed (redirects too)");

        // --- "masuk waktu" notices
        var sn = new StartNotices(); var starts = new List<string>();
        for (var t = day; t < day.AddDays(1); t = t.AddMinutes(1)) { var e = sn.Check(Timeline.Build(store, t), t); if (e != null) starts.Add($"{t:HH:mm} {e.Name}"); }
        starts.ForEach(x => Console.WriteLine("     " + x));
        Ok(starts.SequenceEqual(new[] { "05:51 Subuh", "12:59 Zohor", "16:14 Asar", "18:57 Maghrib", "20:08 Isyak" }), "masuk waktu: once at each of the 5 waktu");
        Ok(new StartNotices().Check(Timeline.Build(store, day + new TimeSpan(16, 17, 0)), day + new TimeSpan(16, 17, 0))?.Name == "Asar", "laptop wakes 3 min into Asar → still notified");
        Ok(new StartNotices().Check(Timeline.Build(store, day + new TimeSpan(16, 25, 0)), day + new TimeSpan(16, 25, 0)) == null, "11 min late → no stale 'masuk waktu'");
        Ok(new StartNotices().Check(Timeline.Build(store, new DateTime(2026, 11, 6, 12, 59, 30)), new DateTime(2026, 11, 6, 12, 59, 30))?.Name == "Jumaat", "Friday: 'Masuk waktu Jumaat'");

        // --- Dhuha
        var pd4 = store.Get(day);
        Ok(pd4.DhuhaStart == day + new TimeSpan(7, 24, 0) && pd4.DhuhaEnd == day + new TimeSpan(12, 49, 0), "Dhuha = Syuruk+25 (07:24) until Zohor-10 (12:49)");
        Ok(Timeline.Build(store, day.AddHours(7.2)).Phase == Gap.Tahrim && Timeline.Build(store, day.AddHours(9)).Phase == Gap.Dhuha
           && Timeline.Build(store, day + new TimeSpan(12, 55, 0)).Phase == Gap.BeforeZohor && Timeline.Build(store, day.AddHours(14)).Phase == Gap.None,
           "phase: tahrim 07:12, Dhuha 09:00, before Zohor 12:55, none during Zohor");
        var dr = new DhuhaReminders(); var dev = new List<string>();
        for (var t = day; t < day.AddDays(1); t = t.AddMinutes(1))
        { var ev = dr.Check(store.Get(t.Date), t, 30); if (ev != DhuhaEvent.None) dev.Add($"{t:HH:mm} {ev}"); }
        dev.ForEach(x => Console.WriteLine("     " + x));
        Ok(dev.SequenceEqual(new[] { "07:24 Started", "12:19 Ending" }), "Dhuha notices: start 07:24, 'ending' 30 min before 12:49, once each");
        var dr2 = new DhuhaReminders(); var dev2 = new List<string>();
        for (var t = day; t < day.AddDays(1); t = t.AddMinutes(1)) { var ev = dr2.Check(store.Get(t.Date), t, 0); if (ev != DhuhaEvent.None) dev2.Add(ev.ToString()); }
        Ok(dev2.SequenceEqual(new[] { "Started" }), "reminders off → only the start notice");
        Ok(new DhuhaReminders().Check(pd4, day + new TimeSpan(7, 40, 0), 30) == DhuhaEvent.None, "laptop wakes at 07:40 → no stale start notice");
        var dz = new DhuhaReminders(); var t24 = day + new TimeSpan(7, 25, 0);
        Ok(dz.Check(pd4, t24, 30, "WLY01") == DhuhaEvent.Started && dz.Check(pd4, t24, 30, "WLY01") == DhuhaEvent.None
           && dz.Check(pd4, t24, 30, "SBH07") == DhuhaEvent.Started, "Dhuha notice once per zone per day (travelling)");
        Ok(new DhuhaReminders().Check(pd4, day + new TimeSpan(12, 48, 30), 30) == DhuhaEvent.None, "no 'ending' notice in the final minute");

        // --- update check
        string Rel(string tag, string url, bool draft = false, bool pre = false) =>
            $"{{\"tag_name\":\"{tag}\",\"html_url\":\"{url}\",\"draft\":{(draft ? "true" : "false")},\"prerelease\":{(pre ? "true" : "false")},\"assets\":[]}}";
        const string page = "https://github.com/Ikmal14/waktu-solat-widget/releases/tag/v5.1.0";
        var rel = Updates.Parse(Rel("v5.1.0", page));
        Ok(rel.Version == new Version(5, 1, 0) && rel.Url == page, "parse latest release (tag + page)");
        Ok(Updates.Parse(Rel("v9.0.0", page, draft: true)) == null && Updates.Parse(Rel("v9.0.0", page, pre: true)) == null, "drafts / pre-releases ignored");
        Ok(Throws(() => Updates.Parse(Rel("v5.1.0", "https://evil.example.com/x"))) && Throws(() => Updates.Parse(Rel("v5.1.0", "https://github.com/someoneelse/repo/releases/x")))
           && Throws(() => Updates.Parse(Rel("v5.1.0", "https://github.com/Ikmal14/waktu-solat-widget/releases/../../evil")))
           && Throws(() => Updates.Parse(Rel("v5.1.0", "https://github.com/Ikmal14/waktu-solat-widget/releases/x\\\" & calc"))), "only this repo's release pages can be opened");
        Ok(Throws(() => Updates.Parse(Rel("latest", page))) && Throws(() => Updates.Parse("<html>proxy</html>")), "bad tag / non-JSON rejected");
        Ok(Throws(() => Updates.ParseVersion("v5.1.0\n")) && Throws(() => Updates.ParseVersion("v\u0665.1.0")) && !Updates.SafePage(page + "\n")
           && Throws(() => Updates.ParseVersion("v5.1.0-beta")), "trailing newline / Unicode digits / pre-release tags rejected");
        Ok(Updates.IsNewer(new Version(5, 1, 0), new Version(5, 0, 0, 0)) && !Updates.IsNewer(new Version(5, 0, 0), new Version(5, 0, 0, 0)) && !Updates.IsNewer(new Version(4, 9, 9), new Version(5, 0, 0, 0)), "version comparison");
        var now0 = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
        Ok(Updates.Due(DateTime.MinValue, false, now0) && !Updates.Due(now0.AddDays(-6), true, now0) && Updates.Due(now0.AddDays(-7), true, now0)
           && !Updates.Due(now0.AddHours(-20), false, now0) && Updates.Due(now0.AddDays(-1), false, now0) && Updates.Due(now0.AddDays(3), true, now0), "weekly after success, daily retry after failure, clock-change safe");
        Ok(Updates.AllowedApi(new Uri(Updates.ApiUrl)) && !Updates.AllowedApi(new Uri("https://api.github.com/repos/other/repo/releases/latest"))
           && !Updates.AllowedApi(new Uri("http://api.github.com/repos/Ikmal14/waktu-solat-widget/releases/latest")) && !Updates.AllowedApi(new Uri("https://evil.com/repos/Ikmal14/waktu-solat-widget/")), "update check only talks to this repo on api.github.com over HTTPS");

        // --- self-update: release asset parsing + verification
        var exeBytes = new byte[20000]; exeBytes[0] = (byte)'M'; exeBytes[1] = (byte)'Z'; new Random(1).NextBytes(new Span<byte>(exeBytes, 2, exeBytes.Length - 2));
        string shaHex; using (var sha = System.Security.Cryptography.SHA256.Create()) shaHex = BitConverter.ToString(sha.ComputeHash(exeBytes)).Replace("-", "").ToLowerInvariant();
        string RelA(string tag, string dl, string digest, long size) =>
            $"{{\"tag_name\":\"{tag}\",\"html_url\":\"https://github.com/Ikmal14/waktu-solat-widget/releases/tag/{tag}\",\"draft\":false,\"prerelease\":false," +
            $"\"assets\":[{{\"name\":\"WaktuSolat.exe\",\"size\":{size},\"digest\":\"{digest}\",\"browser_download_url\":\"{dl}\"}}]}}";
        string goodDl = "https://github.com/Ikmal14/waktu-solat-widget/releases/download/v5.2.0/WaktuSolat.exe";
        var ra = Updates.Parse(RelA("v5.2.0", goodDl, "sha256:" + shaHex, exeBytes.Length));
        Ok(ra.AssetUrl == goodDl && ra.Sha256 == shaHex && ra.Size == exeBytes.Length, "release exe found with GitHub's SHA-256 digest");
        Ok(Updates.Parse(RelA("v5.2.0", "https://evil.example.com/WaktuSolat.exe", "sha256:" + shaHex, exeBytes.Length)).AssetUrl == null
           && Updates.Parse(RelA("v5.2.0", goodDl.Replace("v5.2.0", "v5.0.0"), "sha256:" + shaHex, exeBytes.Length)).AssetUrl == null
           && Updates.Parse(RelA("v5.2.0", goodDl.Replace("Ikmal14", "someone"), "sha256:" + shaHex, exeBytes.Length)).AssetUrl == null,
           "exe from another host / tag / repo is ignored (manual update instead)");
        Ok(Updates.Parse(RelA("v5.2.0", goodDl, "md5:abc", exeBytes.Length)).AssetUrl == null && Updates.Parse(RelA("v5.2.0", goodDl, "sha256:" + shaHex, 99_000_000)).AssetUrl == null,
           "missing/odd digest or huge size → no auto-install");
        Ok(Updates.Download(ra, _ => exeBytes).Length == exeBytes.Length, "download accepted when size + SHA-256 match");
        var tampered = (byte[])exeBytes.Clone(); tampered[5000] ^= 1;
        bool rejected(Func<byte[]> f) { try { f(); return false; } catch (InvalidDataException) { return true; } }
        Ok(rejected(() => Updates.Download(ra, _ => tampered)) && rejected(() => Updates.Download(ra, _ => new byte[10])), "tampered or truncated download rejected");
        Ok(Updates.AllowedDownload(new Uri(goodDl)) && Updates.AllowedDownload(new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1/x"))
           && !Updates.AllowedDownload(new Uri("http://release-assets.githubusercontent.com/x")) && !Updates.AllowedDownload(new Uri("https://github.com/someone/repo/releases/download/v1/x.exe"))
           && !Updates.AllowedDownload(new Uri("https://evil.com/Ikmal14/waktu-solat-widget/releases/download/x")), "download allow-list: this repo's files + GitHub storage, HTTPS only");

        // --- zone locator (offline, embedded polygons)
        var gz = Path.Combine(AppContext.BaseDirectory, "zones.dat.gz");
        string L(double lat, double lng) { using (var f = File.OpenRead(gz)) return ZoneLocator.Locate(f, lat, lng); }
        var cases = new (string name, double lat, double lng, string want)[]
        {
            ("KLCC", 3.1579, 101.7117, "WLY01"), ("Putrajaya", 2.9264, 101.6942, "WLY01"), ("Shah Alam", 3.0733, 101.5183, "SGR01"),
            ("Petaling Jaya", 3.1073, 101.6068, "SGR01"), ("Klang", 3.0449, 101.4455, "SGR03"), ("Kuala Selangor", 3.3400, 101.2500, "SGR02"),
            ("Ipoh", 4.5975, 101.0901, "PRK02"), ("Teluk Intan", 4.0259, 101.0213, "PRK05"), ("Taiping", 4.8500, 100.7333, "PRK06"),
            ("Parit Buntar", 5.1257, 100.4930, "PRK06"), ("Tapah", 4.1977, 101.2611, "PRK01"), ("Georgetown", 5.4141, 100.3354, "PNG01"),
            ("Alor Setar", 6.1210, 100.3678, "KDH01"), ("Langkawi", 6.3500, 99.8000, "KDH06"), ("Kota Bharu", 6.1254, 102.2381, "KTN01"),
            ("Kuala Terengganu", 5.3296, 103.1370, "TRG01"), ("Kuantan", 3.8077, 103.3260, "PHG02"), ("Seremban", 2.7258, 101.9424, "NGS03"),
            ("Melaka", 2.1896, 102.2501, "MLK01"), ("Johor Bahru", 1.4927, 103.7414, "JHR02"), ("Muar", 2.0442, 102.5689, "JHR04"),
            ("Kuching", 1.5535, 110.3592, "SWK08"), ("Miri", 4.3995, 113.9914, "SWK02"), ("Kota Kinabalu", 5.9804, 116.0735, "SBH07"),
            ("Labuan", 5.2831, 115.2308, "WLY02"), ("Singapore", 1.3521, 103.8198, null), ("Woodlands SG", 1.4360, 103.7865, null),
            ("Strait of Malacca", 3.0, 100.5, null), ("Bangkok", 13.75, 100.5, null),
        };
        var wrong = cases.Where(c => L(c.lat, c.lng) != c.want).Select(c => $"{c.name}={L(c.lat, c.lng) ?? "null"} (want {c.want ?? "null"})").ToList();
        Ok(wrong.Count == 0, $"zone detection for {cases.Length} places (incl. corrected Perak, Singapore=none)" + (wrong.Count > 0 ? ": " + string.Join(", ", wrong) : ""));
        var sw = System.Diagnostics.Stopwatch.StartNew(); L(3.1579, 101.7117);
        Console.WriteLine($"     one zone lookup (load+search): {sw.ElapsedMilliseconds} ms");

        Directory.Delete(dir, true);
        Console.WriteLine(fails == 0 ? "\nALL PASSED" : $"\n{fails} FAILED");
        return fails;
    }
}
