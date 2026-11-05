// Core logic: zones, data sources, cache, offline calculation, timeline & reminders.
// No WinForms dependency so it can be unit-tested on any platform.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace WaktuSolat
{
    public static class Clock
    {
        // Malaysia time, independent of the laptop's timezone setting.
        public static Func<DateTime> NowProvider = () => DateTime.UtcNow.AddHours(8);
        public static DateTime Now => NowProvider();
    }

    // ------------------------------------------------------------------ zones
    public sealed class Zone
    {
        public string Code, State, Name;
        public double Lat, Lng;
        public Zone(string code, string state, string name, double lat, double lng)
        { Code = code; State = state; Name = name; Lat = lat; Lng = lng; }
        public string ShortName => Name.Split(',')[0].Trim();
    }

    public static class Zones
    {
        public static readonly Zone[] All =
        {
            new Zone("JHR01","Johor","Pulau Aur, Pulau Pemanggil",2.45,104.52),
            new Zone("JHR02","Johor","Johor Bahru, Kota Tinggi, Mersing, Kulai",1.49,103.74),
            new Zone("JHR03","Johor","Kluang, Pontian",2.03,103.32),
            new Zone("JHR04","Johor","Batu Pahat, Muar, Segamat, Gemas, Tangkak",1.85,102.93),
            new Zone("KDH01","Kedah","Kota Setar, Kubang Pasu, Pokok Sena",6.12,100.37),
            new Zone("KDH02","Kedah","Kuala Muda, Yan, Pendang",5.65,100.49),
            new Zone("KDH03","Kedah","Padang Terap, Sik",5.81,100.73),
            new Zone("KDH04","Kedah","Baling",5.68,100.92),
            new Zone("KDH05","Kedah","Bandar Baharu, Kulim",5.37,100.56),
            new Zone("KDH06","Kedah","Langkawi",6.33,99.84),
            new Zone("KDH07","Kedah","Puncak Gunung Jerai",5.79,100.43),
            new Zone("KTN01","Kelantan","Kota Bharu, Bachok, Machang, Pasir Mas, Pasir Puteh, Tanah Merah, Tumpat, Kuala Krai",6.13,102.24),
            new Zone("KTN02","Kelantan","Gua Musang, Jeli, Lojing",4.88,101.97),
            new Zone("MLK01","Melaka","Seluruh Negeri Melaka",2.19,102.25),
            new Zone("NGS01","Negeri Sembilan","Tampin, Jempol",2.47,102.23),
            new Zone("NGS02","Negeri Sembilan","Jelebu, Kuala Pilah, Rembau",2.74,102.25),
            new Zone("NGS03","Negeri Sembilan","Seremban, Port Dickson",2.73,101.94),
            new Zone("PHG01","Pahang","Pulau Tioman",2.79,104.17),
            new Zone("PHG02","Pahang","Kuantan, Pekan, Muadzam Shah",3.81,103.33),
            new Zone("PHG03","Pahang","Temerloh, Jerantut, Maran, Bera, Chenor, Jengka",3.45,102.42),
            new Zone("PHG04","Pahang","Bentong, Lipis, Raub",3.79,101.86),
            new Zone("PHG05","Pahang","Genting Sempah, Janda Baik, Bukit Tinggi",3.35,101.82),
            new Zone("PHG06","Pahang","Cameron Highlands, Genting Highlands, Bukit Fraser",4.47,101.38),
            new Zone("PHG07","Pahang","Rompin, Endau, Pontian",2.80,103.49),
            new Zone("PLS01","Perlis","Kangar, Padang Besar, Arau",6.44,100.20),
            new Zone("PNG01","Pulau Pinang","Seluruh Negeri Pulau Pinang",5.41,100.33),
            new Zone("PRK01","Perak","Tapah, Slim River, Tanjung Malim",4.20,101.26),
            new Zone("PRK02","Perak","Ipoh, Kuala Kangsar, Sg. Siput, Batu Gajah, Kampar",4.60,101.08),
            new Zone("PRK03","Perak","Gerik, Lenggong, Pengkalan Hulu",5.43,101.13),
            new Zone("PRK04","Perak","Temengor, Belum",5.50,101.35),
            new Zone("PRK05","Perak","Teluk Intan, Kg Gajah, Bagan Datuk, Seri Iskandar, Lumut, Sitiawan",4.02,101.02),
            new Zone("PRK06","Perak","Taiping, Selama, Bagan Serai, Parit Buntar",4.85,100.74),
            new Zone("PRK07","Perak","Bukit Larut",4.86,100.79),
            new Zone("SBH01","Sabah","Sandakan, Bukit Garam, Semawang, Temanggong, Tambisan",5.84,118.12),
            new Zone("SBH02","Sabah","Beluran, Telupid, Pinangah, Terusan, Kuamut",5.89,117.56),
            new Zone("SBH03","Sabah","Lahad Datu, Silabukan, Kunak, Sahabat, Semporna, Tungku",5.03,118.33),
            new Zone("SBH04","Sabah","Tawau, Balong, Merotai, Kalabakan",4.24,117.89),
            new Zone("SBH05","Sabah","Kudat, Kota Marudu, Pitas, Pulau Banggi",6.88,116.84),
            new Zone("SBH06","Sabah","Gunung Kinabalu",6.08,116.56),
            new Zone("SBH07","Sabah","Kota Kinabalu, Ranau, Kota Belud, Tuaran, Penampang, Papar, Putatan",5.98,116.07),
            new Zone("SBH08","Sabah","Keningau, Pensiangan, Tambunan, Nabawan",5.34,116.16),
            new Zone("SBH09","Sabah","Beaufort, Kuala Penyu, Sipitang, Tenom, Long Pasia, Membakut, Weston",5.35,115.75),
            new Zone("SGR01","Selangor","Shah Alam, Gombak, Petaling, Sepang, Hulu Langat, Hulu Selangor",3.07,101.52),
            new Zone("SGR02","Selangor","Kuala Selangor, Sabak Bernam",3.34,101.25),
            new Zone("SGR03","Selangor","Klang, Kuala Langat",3.04,101.45),
            new Zone("SWK01","Sarawak","Limbang, Lawas, Sundar, Trusan",4.75,115.01),
            new Zone("SWK02","Sarawak","Miri, Niah, Bekenu, Sibuti, Marudi",4.40,113.99),
            new Zone("SWK03","Sarawak","Bintulu, Pandan, Belaga, Suai, Tatau, Sebauh",3.17,113.04),
            new Zone("SWK04","Sarawak","Sibu, Mukah, Dalat, Song, Igan, Oya, Balingian, Kanowit, Kapit",2.29,111.83),
            new Zone("SWK05","Sarawak","Sarikei, Matu, Julau, Rajang, Daro, Bintangor, Belawai",2.13,111.52),
            new Zone("SWK06","Sarawak","Sri Aman, Lubok Antu, Roban, Debak, Kabong, Lingga, Engkelili, Betong, Saratok",1.24,111.46),
            new Zone("SWK07","Sarawak","Serian, Simunjan, Samarahan, Sebuyau, Meludam",1.17,110.57),
            new Zone("SWK08","Sarawak","Kuching, Bau, Lundu, Sematan",1.55,110.36),
            new Zone("SWK09","Sarawak","Zon Khas (Kampung Patarikan)",1.70,109.85),
            new Zone("TRG01","Terengganu","Kuala Terengganu, Marang, Kuala Nerus",5.33,103.14),
            new Zone("TRG02","Terengganu","Besut, Setiu",5.83,102.56),
            new Zone("TRG03","Terengganu","Hulu Terengganu",5.07,103.01),
            new Zone("TRG04","Terengganu","Dungun, Kemaman",4.50,103.42),
            new Zone("WLY01","Wilayah Persekutuan","Kuala Lumpur, Putrajaya",3.14,101.69),
            new Zone("WLY02","Wilayah Persekutuan","Labuan",5.28,115.24),
        };

        public static Zone Find(string code) =>
            All.FirstOrDefault(z => string.Equals(z.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Find("WLY01");
    }

    // ------------------------------------------------------------------ data model
    public sealed class PrayerDay
    {
        public DateTime Date;
        public string Hijri = "";          // yyyy-MM-dd (JAKIM), empty if unknown
        public bool Estimated;             // true = computed offline
        public TimeSpan Imsak, Subuh, Syuruk, Zohor, Asar, Maghrib, Isyak;

        // Dhuha (derived from the official Syuruk/Zohor):
        //  start: "Awal waktu Dhuha: 25 minit selepas waktu Syuruk" (Pejabat Mufti WP timetables 2024-2026, citing JAKIM Almanak Falak Syarie)
        //  end:   Dhuha ends at istiwa' just before Zohor; no official minute figure was found, so we stop 10 min before Zohor as a precaution.
        public const int DhuhaAfterSyurukMin = 25, DhuhaEndBeforeZohorMin = 10;
        public DateTime DhuhaStart => Date + Syuruk + TimeSpan.FromMinutes(DhuhaAfterSyurukMin);
        public DateTime DhuhaEnd => Date + Zohor - TimeSpan.FromMinutes(DhuhaEndBeforeZohorMin);

        public IEnumerable<Entry> Entries()
        {
            bool friday = Date.DayOfWeek == DayOfWeek.Friday;
            yield return new Entry("Imsak", Date + Imsak, false);
            yield return new Entry("Subuh", Date + Subuh, true);
            yield return new Entry("Syuruk", Date + Syuruk, false);
            yield return new Entry(friday ? "Jumaat" : "Zohor", Date + Zohor, true);
            yield return new Entry("Asar", Date + Asar, true);
            yield return new Entry("Maghrib", Date + Maghrib, true);
            yield return new Entry("Isyak", Date + Isyak, true);
        }

        public string Serialize() => string.Join("|", new[]
        {
            Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Hijri,
            T(Imsak), T(Subuh), T(Syuruk), T(Zohor), T(Asar), T(Maghrib), T(Isyak)
        });

        public static PrayerDay Deserialize(string line)
        {
            var p = line.Split('|');
            if (p.Length != 9) return null;
            var d = new PrayerDay
            {
                Date = DateTime.ParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Hijri = p[1],
                Imsak = Util.ParseTime(p[2]), Subuh = Util.ParseTime(p[3]), Syuruk = Util.ParseTime(p[4]),
                Zohor = Util.ParseTime(p[5]), Asar = Util.ParseTime(p[6]), Maghrib = Util.ParseTime(p[7]),
                Isyak = Util.ParseTime(p[8]),
            };
            return d.IsValid() ? d : null;
        }

        static string T(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

        /// Rejects corrupted/unexpected data: times must be in order and Hijri must look like yyyy-MM-dd.
        public bool IsValid()
        {
            var t = new[] { Imsak, Subuh, Syuruk, Zohor, Asar, Maghrib, Isyak };
            for (int i = 1; i < t.Length; i++) if (t[i] <= t[i - 1]) return false;
            return Hijri.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(Hijri, @"^\d{4}-\d{2}-\d{2}$");
        }
    }

    public sealed class Entry
    {
        public string Name; public DateTime At; public bool Fardhu;
        public Entry(string name, DateTime at, bool fardhu) { Name = name; At = at; Fardhu = fardhu; }
        /// Stable id of this waktu on this day, e.g. "2026-11-06|Zohor" (Jumaat counts as Zohor slot).
        public string Key => At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + (Name == "Jumaat" ? "Zohor" : Name);
        public override string ToString() => $"{Name} {At:yyyy-MM-dd HH:mm}";
    }

    public static class Util
    {
        public static TimeSpan ParseTime(string s)
        {
            var p = (s ?? "").Trim().Split(':');
            if (p.Length < 2 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h)
                || !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m) || h > 23 || m > 59)
                throw new FormatException("Bad time: " + s);
            return new TimeSpan(h, m, 0);
        }

        static readonly string[] MonthsEn = { "jan","feb","mar","apr","may","jun","jul","aug","sep","oct","nov","dec" };
        static readonly string[] MonthsMs = { "jan","feb","mac","apr","mei","jun","jul","ogo","sep","okt","nov","dis" };

        /// e-Solat dates: "04-Nov-2026", "31-Dis-2026" (Malay month names), or "2026-11-04".
        public static DateTime ParseDate(string s)
        {
            var p = (s ?? "").Trim().Split('-');
            if (p.Length != 3) throw new FormatException("Bad date: " + s);
            int y, m, d;
            if (p[0].Length == 4) { y = Int(p[0]); m = Int(p[1]); d = Int(p[2]); }
            else
            {
                d = Int(p[0]); y = Int(p[2]);
                string mon = p[1].ToLowerInvariant();
                if (mon.Length > 3) mon = mon.Substring(0, 3);
                m = Array.IndexOf(MonthsEn, mon) + 1;
                if (m == 0) m = Array.IndexOf(MonthsMs, mon) + 1;
                if (m == 0) m = Int(p[1]);
            }
            if (y < 2000 || y > 2100) throw new FormatException("Bad year: " + s);
            return new DateTime(y, m, d);
        }

        static int Int(string s) =>
            int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : throw new FormatException("Bad number: " + s);

        static readonly string[] HijriMonths =
            { "Muharam","Safar","Rabiulawal","Rabiulakhir","Jamadilawal","Jamadilakhir",
              "Rejab","Syaaban","Ramadan","Syawal","Zulkaedah","Zulhijah" };
        static readonly string[] Days = { "Ahad","Isnin","Selasa","Rabu","Khamis","Jumaat","Sabtu" };
        static readonly string[] Months = { "Jan","Feb","Mac","Apr","Mei","Jun","Jul","Ogo","Sep","Okt","Nov","Dis" };

        public static string HijriText(string hijri)
        {
            var p = (hijri ?? "").Split('-');
            if (p.Length != 3 || !int.TryParse(p[1], out int m) || m < 1 || m > 12) return "";
            return $"{int.Parse(p[2])} {HijriMonths[m - 1]} {p[0]}H";
        }

        public static string DateText(DateTime d) => $"{Days[(int)d.DayOfWeek]}, {d.Day} {Months[d.Month - 1]} {d.Year}";

        public static string Duration(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return "kurang 1 min";
            int h = (int)t.TotalHours, m = (int)Math.Ceiling(t.TotalMinutes - h * 60);
            if (m == 60) { h++; m = 0; }
            return h > 0 ? $"{h}j {m}m" : $"{m} min";
        }
    }

    // ------------------------------------------------------------------ tiny JSON reader
    public static class Json
    {
        const int MaxDepth = 16, MaxLength = 2_000_000;

        public static object Parse(string s)
        {
            if (s == null || s.Length > MaxLength) throw new FormatException("JSON too large");
            try { int i = 0; return Val(s, ref i, 0); }
            catch (Exception ex) when (!(ex is FormatException)) { throw new FormatException("JSON tidak sah", ex); }   // truncated/odd input
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Val(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON too deep");
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                i++; var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                Ws(s, ref i); if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); var k = Str(s, ref i); Ws(s, ref i);
                    if (s[i++] != ':') throw new FormatException("Expected :");
                    d[k] = Val(s, ref i, depth + 1); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("Expected , or }");
                }
            }
            if (c == '[')
            {
                i++; var a = new List<object>();
                Ws(s, ref i); if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Val(s, ref i, depth + 1)); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw new FormatException("Expected , or ]");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("Bad JSON value at " + i);
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected string");
            i++; var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++; return sb.ToString();
        }

        public static string Str(object o) =>
            o is string s ? s : o is double d ? d.ToString(CultureInfo.InvariantCulture) : o?.ToString() ?? "";
    }

    // ------------------------------------------------------------------ parsers for online sources
    public static class Sources
    {
        /// e-solat.gov.my (official JAKIM): {"prayerTime":[{"hijri","date":"04-Nov-2026","imsak":"05:41:00","fajr",...}]}
        public static List<PrayerDay> ParseJakim(string json)
        {
            var root = (Dictionary<string, object>)Json.Parse(json);
            var list = new List<PrayerDay>();
            string firstError = null;
            foreach (var o in (List<object>)root["prayerTime"])
            {
              try
              {
                var it = (Dictionary<string, object>)o;
                var date = Util.ParseDate(Json.Str(it["date"]));
                var d = new PrayerDay
                {
                    Date = date.Date,
                    Hijri = it.TryGetValue("hijri", out var h) ? Json.Str(h) : "",
                    Subuh = Util.ParseTime(Json.Str(it["fajr"])),
                    Syuruk = Util.ParseTime(Json.Str(it["syuruk"])),
                    Zohor = Util.ParseTime(Json.Str(it["dhuhr"])),
                    Asar = Util.ParseTime(Json.Str(it["asr"])),
                    Maghrib = Util.ParseTime(Json.Str(it["maghrib"])),
                    Isyak = Util.ParseTime(Json.Str(it["isha"])),
                };
                d.Imsak = it.TryGetValue("imsak", out var im) ? Util.ParseTime(Json.Str(im)) : d.Subuh - TimeSpan.FromMinutes(10);
                if (!d.IsValid()) throw new FormatException("Data waktu tidak sah");
                list.Add(d);
              }
              catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is KeyNotFoundException
                                         || ex is ArgumentException || ex is NullReferenceException || ex is OverflowException)
              {
                  firstError = firstError ?? ex.Message;   // skip this day only; it falls back to an estimate
              }
            }
            if (list.Count == 0) throw new FormatException("Tiada data sah" + (firstError != null ? ": " + firstError : ""));
            return list;
        }
    }

    // ------------------------------------------------------------------ offline calculation (JAKIM parameters)
    public static class PrayerCalc
    {
        // Fajr 20°, Isha 18°, Asr shadow factor 1 (Syafie), + ihtiyati, MYT (UTC+8).
        public static PrayerDay Calculate(DateTime date, double lat, double lng)
        {
            double jd = JulianDate(date.Year, date.Month, date.Day) - lng / (15 * 24.0);
            double fajr = 5, sunrise = 6, dhuhr = 12, asr = 13, sunset = 18, isha = 18;
            for (int iter = 0; iter < 2; iter++)
            {
                fajr = AngleTime(jd, lat, 20, fajr / 24, true);
                sunrise = AngleTime(jd, lat, 0.833, sunrise / 24, true);
                dhuhr = MidDay(jd, dhuhr / 24);
                asr = AsrTime(jd, lat, 1, asr / 24);
                sunset = AngleTime(jd, lat, 0.833, sunset / 24, false);
                isha = AngleTime(jd, lat, 18, isha / 24, false);
            }
            double adj = 8 - lng / 15;
            var d = new PrayerDay
            {
                Date = date.Date, Estimated = true,
                Subuh = Up(fajr + adj, 1), Syuruk = Down(sunrise + adj, 1), Zohor = Up(dhuhr + adj, 1),
                Asar = Up(asr + adj, 1), Maghrib = Up(sunset + adj, 1), Isyak = Up(isha + adj, 1),
            };
            d.Imsak = d.Subuh - TimeSpan.FromMinutes(10);
            return d;
        }

        // Round to the safe side: start times up, Syuruk (end of Subuh) down.
        static TimeSpan Up(double hours, int plusMin) => TimeSpan.FromMinutes(Math.Ceiling(hours * 60) + plusMin);
        static TimeSpan Down(double hours, int minusMin) => TimeSpan.FromMinutes(Math.Floor(hours * 60) - minusMin);

        static double D2R(double d) => d * Math.PI / 180;
        static double R2D(double r) => r * 180 / Math.PI;
        static double Fix(double a, double b) { a -= b * Math.Floor(a / b); return a < 0 ? a + b : a; }

        static double JulianDate(int y, int m, int d)
        {
            if (m <= 2) { y--; m += 12; }
            double A = Math.Floor(y / 100.0), B = 2 - A + Math.Floor(A / 4);
            return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + B - 1524.5;
        }

        static void SunPosition(double jd, out double decl, out double eqt)
        {
            double D = jd - 2451545.0;
            double g = Fix(357.529 + 0.98560028 * D, 360);
            double q = Fix(280.459 + 0.98564736 * D, 360);
            double L = Fix(q + 1.915 * Math.Sin(D2R(g)) + 0.020 * Math.Sin(D2R(2 * g)), 360);
            double e = 23.439 - 0.00000036 * D;
            double RA = Fix(R2D(Math.Atan2(Math.Cos(D2R(e)) * Math.Sin(D2R(L)), Math.Cos(D2R(L)))) / 15, 24);
            eqt = q / 15 - RA;
            decl = R2D(Math.Asin(Math.Sin(D2R(e)) * Math.Sin(D2R(L))));
        }

        static double MidDay(double jd, double t)
        {
            SunPosition(jd + t, out _, out double eqt);
            return Fix(12 - eqt, 24);
        }

        static double AngleTime(double jd, double lat, double angle, double t, bool beforeNoon)
        {
            SunPosition(jd + t, out double decl, out _);
            double noon = MidDay(jd, t);
            double x = (-Math.Sin(D2R(angle)) - Math.Sin(D2R(decl)) * Math.Sin(D2R(lat))) /
                       (Math.Cos(D2R(decl)) * Math.Cos(D2R(lat)));
            double T = R2D(Math.Acos(Math.Max(-1, Math.Min(1, x)))) / 15;
            return noon + (beforeNoon ? -T : T);
        }

        static double AsrTime(double jd, double lat, double factor, double t)
        {
            SunPosition(jd + t, out double decl, out _);
            double angle = -R2D(Math.Atan(1 / (factor + Math.Tan(D2R(Math.Abs(lat - decl))))));
            return AngleTime(jd, lat, angle, t, false);
        }
    }

    // ------------------------------------------------------------------ data store (cache + online fetch + fallback)
    public sealed class DataStore
    {
        public Func<string, string> HttpGet = DefaultHttpGet;
        public Zone Zone { get; private set; }
        public string Status { get; private set; } = "";
        public bool LastFetchFailed { get; private set; }

        readonly string _dir;
        readonly object _lock = new object();
        readonly Dictionary<DateTime, PrayerDay> _days = new Dictionary<DateTime, PrayerDay>();
        readonly Dictionary<DateTime, PrayerDay> _estimates = new Dictionary<DateTime, PrayerDay>();
        readonly Dictionary<string, DateTime> _lastAttempt = new Dictionary<string, DateTime>();
        bool _lastOk;   // last download succeeded -> retry missing dates rarely
        int _busy;

        public DataStore(string dir, string zone)
        {
            _dir = dir;
            try { Directory.CreateDirectory(dir); } catch { /* e.g. offline redirected AppData; runs without cache */ }
            SetZone(zone);
        }

        string CachePath => Path.Combine(_dir, $"cache_{Zone.Code}.txt");

        public void SetZone(string code)
        {
            lock (_lock)
            {
                Zone = Zones.Find(code);
                _days.Clear(); _estimates.Clear(); _lastAttempt.Clear();
                try
                {
                    if (File.Exists(CachePath))
                        foreach (var line in File.ReadAllLines(CachePath))
                        {
                            try { var d = PrayerDay.Deserialize(line); if (d != null) _days[d.Date] = d; }
                            catch { /* skip bad line */ }
                        }
                }
                catch { /* corrupt cache: ignore, refetch */ }
                Status = _days.ContainsKey(Clock.Now.Date) ? "JAKIM e-Solat (tersimpan)" : "";
                LastFetchFailed = false;
            }
        }

        /// Always returns a day: official data if available, otherwise an offline estimate
        /// (and a background fetch is started).
        public PrayerDay Get(DateTime date)
        {
            date = date.Date;
            PrayerDay d;
            lock (_lock)
            {
                if (_days.TryGetValue(date, out d)) return d;
                if (!_estimates.TryGetValue(date, out d))
                    _estimates[date] = d = PrayerCalc.Calculate(date, Zone.Lat, Zone.Lng);
            }
            RequestFetch(date, force: false);
            return d;
        }

        public bool HasOfficial(DateTime date) { lock (_lock) return _days.ContainsKey(date.Date); }

        public void Refresh() => RequestFetch(Clock.Now.Date, force: true);

        void RequestFetch(DateTime date, bool force)
        {
            string key = $"{Zone.Code}-{date:yyyy-MM}";
            lock (_lock)
            {
                if (!force && _lastAttempt.TryGetValue(key, out var last) && (DateTime.UtcNow - last).TotalMinutes < (_lastOk ? 360 : 15)) return;
                if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;   // one fetch at a time; retried next tick
                _lastAttempt[key] = DateTime.UtcNow;
            }
            var zone = Zone;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Fetch(zone, date); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }

        /// Synchronous fetch; public for tests.
        public void Fetch(Zone zone, DateTime date)
        {
            var errors = new List<string>();
            List<PrayerDay> got = null; string source = "";

            try
            {
                // Whole year in one request, so it works offline afterwards.
                string url = $"https://www.e-solat.gov.my/index.php?r=esolatApi/takwimsolat&period=year&zone={zone.Code}";
                got = Sources.ParseJakim(HttpGet(url));
                source = "JAKIM e-Solat";   // keep the year even if `date` (e.g. last year's 31 Dec) isn't in it
            }
            catch (Exception ex) { errors.Add("e-solat: " + ex.Message); }

            lock (_lock)
            {
                if (zone != Zone) return; // zone changed meanwhile
                if (got != null && got.Count > 0)
                {
                    foreach (var d in got) { _days[d.Date] = d; _estimates.Remove(d.Date); }
                    Status = source; LastFetchFailed = false; _lastOk = true;
                    try
                    {
                        File.WriteAllLines(CachePath, _days.Values.OrderBy(d => d.Date)
                            .Where(d => d.Date >= Clock.Now.Date.AddDays(-7)).Select(d => d.Serialize()));
                    }
                    catch { }
                }
                else { Status = string.Join("; ", errors); LastFetchFailed = true; _lastOk = false; }
            }
        }

        const string Host = "e-solat.gov.my";

        public static bool Allowed(Uri u) =>
            u.Scheme == Uri.UriSchemeHttps && (u.Host == Host || u.Host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase));

        // TLS version is left to Windows policy (TLS 1.2/1.3); certificate validation is never overridden.
        static string DefaultHttpGet(string url) =>
            Net.Get(url, Allowed, "application/json", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) WaktuSolat");
    }

    /// The only HTTP client in the app: HTTPS GET to an allow-listed host, manual redirects (re-checked),
    /// size and time limits. TLS version follows Windows policy; certificate validation is never overridden.
    public static class Net
    {
        public static string Get(string url, Func<Uri, bool> allowed, string accept, string userAgent) =>
            Encoding.UTF8.GetString(GetBytes(url, allowed, accept, userAgent, 2_000_000));

        public static byte[] GetBytes(string url, Func<Uri, bool> allowed, string accept, string userAgent, int maxBytes)
        {
            var uri = new Uri(url);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            for (int hop = 0; hop < 4; hop++)
            {
                if (!allowed(uri)) throw new WebException("Ditolak: alamat tidak dibenarkan");
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.AllowAutoRedirect = false;          // redirects checked manually below
                req.Timeout = 20000;
                req.ReadWriteTimeout = 20000;
                req.UserAgent = userAgent;
                req.Accept = accept;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                var proxy = WebRequest.GetSystemWebProxy();          // corporate proxy from Windows settings
                proxy.Credentials = CredentialCache.DefaultCredentials;
                req.Proxy = proxy;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code >= 300 && code < 400)
                    {
                        var loc = resp.Headers[HttpResponseHeader.Location];
                        if (string.IsNullOrEmpty(loc)) throw new WebException("Redirect tanpa lokasi");
                        uri = new Uri(uri, loc);
                        continue;
                    }
                    if (resp.StatusCode != HttpStatusCode.OK) throw new WebException("HTTP " + code);
                    using (var st = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[81920];
                        int r;
                        while ((r = st.Read(buf, 0, buf.Length)) > 0)
                        {
                            ms.Write(buf, 0, r);
                            if (ms.Length > maxBytes) throw new WebException("Respons terlalu besar");
                            if (DateTime.UtcNow > deadline) throw new WebException("Masa tamat");
                        }
                        return ms.ToArray();
                    }
                }
            }
            throw new WebException("Terlalu banyak redirect");
        }
    }

    // ------------------------------------------------------------------ timeline, reminders, ticks
    public sealed class Snapshot
    {
        public PrayerDay Day;
        public List<Entry> Today;   // all entries of today
        public Entry Current;       // fardhu waktu we're in now (null between Syuruk and Zohor)
        public Entry Next;          // next boundary: a fardhu or Syuruk (= when Current ends)
        public double Progress;     // 0..1 from previous boundary to Next
        public Gap Phase;           // what period we're in when Current == null (Syuruk..Zohor)
    }

    public enum Gap { None, Tahrim, Dhuha, BeforeZohor }

    public static class Timeline
    {
        public static Snapshot Build(DataStore store, DateTime now)
        {
            var today = store.Get(now.Date);
            var all = store.Get(now.Date.AddDays(-1)).Entries()
                .Concat(today.Entries())
                .Concat(store.Get(now.Date.AddDays(1)).Entries());
            return Build(all, today, now);
        }

        public static Snapshot Build(IEnumerable<Entry> entries, PrayerDay today, DateTime now)
        {
            var b = entries.Where(e => e.Fardhu || e.Name == "Syuruk").OrderBy(e => e.At).ToList();
            var s = new Snapshot { Day = today, Today = today.Entries().ToList() };
            int i = b.FindIndex(e => e.At > now);
            if (i < 0) return s;
            s.Next = b[i];
            if (i > 0)
            {
                var prev = b[i - 1];
                s.Current = prev.Fardhu ? prev : null;   // after Syuruk: no waktu until Zohor
                // 00:00–Subuh: we're still in yesterday's Isyak, so show (and allow ticking) yesterday's list.
                if (s.Current != null && s.Current.At.Date < today.Date)
                    s.Today = b.Concat(entries.Where(e => !e.Fardhu && e.Name != "Syuruk"))
                               .Where(e => e.At.Date == s.Current.At.Date).OrderBy(e => e.At).ToList();
                if (s.Current == null)
                    s.Phase = now < today.DhuhaStart ? Gap.Tahrim : now < today.DhuhaEnd ? Gap.Dhuha : Gap.BeforeZohor;
                double total = (s.Next.At - prev.At).TotalSeconds;
                s.Progress = total > 0 ? Math.Min(1, Math.Max(0, (now - prev.At).TotalSeconds / total)) : 0;
            }
            return s;
        }
    }

    public sealed class Reminders
    {
        readonly HashSet<string> _fired = new HashSet<string>();

        /// The current waktu if its reminder window (N minutes before it ends) was just entered and it isn't ticked.
        public Entry Check(Snapshot s, DateTime now, int remindMinutes, Func<Entry, bool> isDone)
        {
            if (remindMinutes <= 0 || s.Current == null || s.Next == null) return null;
            var left = s.Next.At - now;
            if (left > TimeSpan.FromMinutes(remindMinutes) || left <= TimeSpan.Zero) return null;
            if (isDone(s.Current)) return null;            // ticked: no reminder (unticking later re-enables it)
            if (!_fired.Add(s.Current.Key)) return null;
            return left.TotalSeconds >= 60 ? s.Current : null;   // pointless in the final minute (e.g. laptop just woke)
        }
    }

    /// "Masuk waktu": the current fardhu waktu if it began less than 5 minutes ago (once per waktu).
    public sealed class StartNotices
    {
        readonly HashSet<string> _fired = new HashSet<string>();

        public Entry Check(Snapshot s, DateTime now)
        {
            var c = s.Current;
            if (c == null || now < c.At || now >= c.At.AddMinutes(5)) return null;
            return _fired.Add(c.Key) ? c : null;
        }
    }

    public enum DhuhaEvent { None, Started, Ending }

    /// Small Dhuha notices: when it starts, and N minutes before it ends. Each fires at most once per day.
    public sealed class DhuhaReminders
    {
        readonly HashSet<string> _fired = new HashSet<string>();

        public DhuhaEvent Check(PrayerDay day, DateTime now, int remindMinutes, string zone = "")
        {
            string d = zone + "|" + day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);   // per zone: travelling
            if (now >= day.DhuhaStart && now < day.DhuhaStart.AddMinutes(5) && _fired.Add(d + "|start"))
                return DhuhaEvent.Started;
            if (remindMinutes > 0)
            {
                var left = day.DhuhaEnd - now;
                if (left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(remindMinutes) && now >= day.DhuhaStart && _fired.Add(d + "|end"))
                    return left.TotalSeconds >= 60 ? DhuhaEvent.Ending : DhuhaEvent.None;
            }
            return DhuhaEvent.None;
        }
    }

    /// "Dah solat" ticks, kept for a few days in %APPDATA%\WaktuSolat\done.txt.
    public sealed class DoneStore
    {
        readonly string _path;
        readonly HashSet<string> _keys = new HashSet<string>();

        public DoneStore(string dir)
        {
            _path = Path.Combine(dir, "done.txt");
            try
            {
                var min = Clock.Now.Date.AddDays(-3);
                foreach (var l in File.ReadAllLines(_path))   // format: yyyy-MM-dd|Name ; old/invalid lines dropped
                    if (l.Length < 32 && DateTime.TryParseExact(l.Split('|')[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var d) && d >= min)
                        _keys.Add(l);
            }
            catch { }
        }

        public bool IsDone(Entry e) => e != null && _keys.Contains(e.Key);

        public void Toggle(Entry e)
        {
            if (e == null) return;
            if (!_keys.Remove(e.Key)) _keys.Add(e.Key);
            try { File.WriteAllLines(_path, _keys); } catch { }
        }
    }

    // ------------------------------------------------------------------ offline location -> zone (no network)
    /// JAKIM zones by district boundary. Data: mptwaktusolat/malaysia.geojson (district polygons with JAKIM codes),
    /// simplified (~200 m) and with Perak corrected to JAKIM's zone list. Loaded only while detecting, then released.
    public static class ZoneLocator
    {
        sealed class Region { public string Code; public List<double[]> Rings = new List<double[]>(); public double MinX = 999, MinY = 999, MaxX = -999, MaxY = -999; }

        public static string Locate(Stream gz, double lat, double lng)
        {
            var regions = Load(gz);
            foreach (var r in regions)
                if (lng >= r.MinX && lng <= r.MaxX && lat >= r.MinY && lat <= r.MaxY && Inside(r, lng, lat)) return r.Code;
            // Coastline was simplified: accept the nearest zone only if within ~450 m.
            string best = null; double bestD = 0.004;
            foreach (var r in regions)
            {
                if (lng < r.MinX - bestD || lng > r.MaxX + bestD || lat < r.MinY - bestD || lat > r.MaxY + bestD) continue;
                foreach (var ring in r.Rings)
                    for (int i = 0, j = ring.Length - 2; i < ring.Length; j = i, i += 2)
                    {
                        double d = SegDist(lng, lat, ring[j], ring[j + 1], ring[i], ring[i + 1]);
                        if (d < bestD) { bestD = d; best = r.Code; }
                    }
            }
            return best;
        }

        static List<Region> Load(Stream gz)
        {
            var map = new Dictionary<string, Region>();
            using (var z = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionMode.Decompress))
            using (var sr = new StreamReader(z, Encoding.ASCII))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    int bar = line.IndexOf('|');
                    if (bar != 5) continue;
                    string code = line.Substring(0, 5);
                    if (!map.TryGetValue(code, out var r)) map[code] = r = new Region { Code = code };
                    var pts = line.Substring(6).Split(';');
                    var ring = new double[pts.Length * 2];
                    for (int k = 0; k < pts.Length; k++)
                    {
                        int c = pts[k].IndexOf(',');
                        double x = double.Parse(pts[k].Substring(0, c), CultureInfo.InvariantCulture);
                        double y = double.Parse(pts[k].Substring(c + 1), CultureInfo.InvariantCulture);
                        ring[2 * k] = x; ring[2 * k + 1] = y;
                        if (x < r.MinX) r.MinX = x; if (x > r.MaxX) r.MaxX = x;
                        if (y < r.MinY) r.MinY = y; if (y > r.MaxY) r.MaxY = y;
                    }
                    r.Rings.Add(ring);
                }
            }
            return map.Values.ToList();
        }

        // Even-odd rule over all rings of a zone (handles holes and multi-part districts).
        static bool Inside(Region r, double x, double y)
        {
            bool c = false;
            foreach (var p in r.Rings)
                for (int i = 0, j = p.Length - 2; i < p.Length; j = i, i += 2)
                    if ((p[i + 1] > y) != (p[j + 1] > y) && x < (p[j] - p[i]) * (y - p[i + 1]) / (p[j + 1] - p[i + 1]) + p[i]) c = !c;
            return c;
        }

        static double SegDist(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l = dx * dx + dy * dy;
            double t = l > 0 ? Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / l)) : 0;
            double cx = ax + t * dx - px, cy = ay + t * dy - py;
            return Math.Sqrt(cx * cx + cy * cy);
        }
    }

    // ------------------------------------------------------------------ update check (weekly, GitHub Releases)
    /// Reads only the public "latest release" of this repo. It never downloads or runs anything:
    /// the user is shown a link to the release page and decides.
    public static class Updates
    {
        public const string Owner = "Ikmal14", Repo = "waktu-solat-widget";
        public static readonly string ApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        public static readonly string PagePrefix = $"https://github.com/{Owner}/{Repo}/releases/";

        public const string AssetName = "WaktuSolat.exe";
        public const int MaxAssetBytes = 5_000_000;

        /// AssetUrl/Sha256/Size are null/0 when the release has no valid exe (then: update by hand from Url).
        public sealed class Release { public Version Version; public string Tag, Url, AssetUrl, Sha256; public long Size; }

        /// Downloads only from this repo's release files (github.com → GitHub's release storage), over HTTPS.
        public static bool AllowedDownload(Uri u) =>
            u.Scheme == Uri.UriSchemeHttps
            && ((u.Host == "github.com" && u.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.Ordinal))
                || u.Host == "release-assets.githubusercontent.com" || u.Host == "objects.githubusercontent.com");

        public static bool AllowedApi(Uri u) =>
            u.Scheme == Uri.UriSchemeHttps && u.Host == "api.github.com"
            && u.AbsolutePath.StartsWith($"/repos/{Owner}/{Repo}/", StringComparison.OrdinalIgnoreCase);

        /// Only a release page of this repo may ever be opened.
        public static bool SafePage(string url) =>
            url != null && url.Length < 200 && url.StartsWith(PagePrefix, StringComparison.Ordinal) && !url.Contains("..")
            && System.Text.RegularExpressions.Regex.IsMatch(url.Substring(PagePrefix.Length), @"^[A-Za-z0-9._/\-]*\z");

        public static Version ParseVersion(string tag)
        {
            var m = System.Text.RegularExpressions.Regex.Match(tag ?? "", @"^v?([0-9]{1,4})\.([0-9]{1,4})(?:\.([0-9]{1,4}))?\z");
            if (!m.Success) throw new FormatException("Bad version: " + tag);
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        /// GitHub "latest release" JSON → release, or null for drafts/pre-releases.
        public static Release Parse(string json)
        {
            var root = Json.Parse(json) as Dictionary<string, object> ?? throw new FormatException("Bad release JSON");
            if (root.TryGetValue("draft", out var dr) && dr is bool d && d) return null;
            if (root.TryGetValue("prerelease", out var pr) && pr is bool p && p) return null;
            var url = root.TryGetValue("html_url", out var u) ? Json.Str(u) : null;
            if (!SafePage(url)) throw new FormatException("Unexpected release URL");
            string tag = root.TryGetValue("tag_name", out var t) ? Json.Str(t) : null;
            var rel = new Release { Version = ParseVersion(tag), Tag = tag, Url = url };

            // The exe: exact name, exact URL for this tag, GitHub's SHA-256 digest, sane size. Anything else → manual update.
            if (root.TryGetValue("assets", out var assets) && assets is List<object> list)
                foreach (var o in list)
                {
                    if (!(o is Dictionary<string, object> a) || !a.TryGetValue("name", out var n) || Json.Str(n) != AssetName) continue;
                    string dl = a.TryGetValue("browser_download_url", out var b) ? Json.Str(b) : null;
                    string dg = a.TryGetValue("digest", out var g) ? Json.Str(g) : null;
                    long size = a.TryGetValue("size", out var z) && z is double zd ? (long)zd : 0;
                    if (dl == $"https://github.com/{Owner}/{Repo}/releases/download/{tag}/{AssetName}"
                        && dg != null && System.Text.RegularExpressions.Regex.IsMatch(dg, "^sha256:[0-9a-f]{64}\\z")
                        && size > 10_000 && size <= MaxAssetBytes)
                    { rel.AssetUrl = dl; rel.Sha256 = dg.Substring(7); rel.Size = size; }
                    break;
                }
            return rel;
        }

        public static bool IsNewer(Version latest, Version current) =>
            latest != null && latest > new Version(current.Major, current.Minor, Math.Max(0, current.Build));

        /// Weekly after a successful check; a failed check (offline, blocked) is retried the next day.
        public static bool Due(DateTime lastUtc, bool lastOk, DateTime nowUtc) =>
            nowUtc - lastUtc >= TimeSpan.FromDays(lastOk ? 7 : 1) || nowUtc < lastUtc;

        /// Download the release exe and check it byte-for-byte against GitHub's published size and SHA-256.
        public static byte[] Download(Release r, Func<string, byte[]> get = null)
        {
            if (r?.AssetUrl == null) throw new InvalidOperationException("Tiada fail exe dalam keluaran ini");
            var bytes = (get ?? (url => Net.GetBytes(url, AllowedDownload, "application/octet-stream", "WaktuSolat-Widget", MaxAssetBytes)))(r.AssetUrl);
            Verify(bytes, r);
            return bytes;
        }

        public static void Verify(byte[] bytes, Release r)
        {
            if (bytes == null || bytes.LongLength != r.Size) throw new InvalidDataException("Saiz fail tidak sepadan");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hex = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (hex != r.Sha256) throw new InvalidDataException("SHA-256 tidak sepadan, fail ditolak");
            }
            if (bytes.Length < 2 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z') throw new InvalidDataException("Bukan fail program Windows");
        }

        /// null = no (stable) release published yet — GitHub answers 404 then, which is not a failure.
        public static Release Check()
        {
            try { return Parse(Net.Get(ApiUrl, AllowedApi, "application/vnd.github+json", "WaktuSolat-Widget")); }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound) { return null; }
        }
    }
}
