// Entry point, tray icon, menu, settings, location and the 1-second tick.
using System;
using System.Device.Location;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WaktuSolat
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool updated = args.Contains("--updated");
            int mi = Array.IndexOf(args, "--moved-from");
            string movedFrom = mi >= 0 && mi + 1 < args.Length ? args[mi + 1] : null;
            bool wait = updated || movedFrom != null;   // the previous copy may still be closing
            using (var mutex = new Mutex(false, @"Local\WaktuSolat-SingleInstance"))
            {
                bool owned;
                // After a self-update the old process may still be closing: wait for it instead of quitting.
                try { owned = mutex.WaitOne(wait ? 20000 : 0); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) return;   // already running (see tray)
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += (s, e) => Log(e.Exception);
                    AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
                    SelfUpdate.CleanupOldVersion();
                    if (movedFrom != null) AppHome.RemoveOldCopy(movedFrom);
                    Application.Run(new TrayApp(updated, movedFrom != null));
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        public static readonly string DataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WaktuSolat");

        public static void Log(Exception ex)
        {
            try
            {
                var f = Path.Combine(DataDir, "error.log");
                if (File.Exists(f) && new FileInfo(f).Length > 256 * 1024) File.Delete(f);   // keep it small
                File.AppendAllText(f, $"{DateTime.Now:s} {ex}\r\n");
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- settings (%APPDATA%\WaktuSolat\settings.ini)
    sealed class Settings
    {
        public static readonly int[] RemindChoices = { 0, 10, 15, 20, 30, 45, 60 };
        public string Zone = "WLY01";
        public bool ZoneAuto = true;
        public int Remind = 30;
        public int X = int.MinValue, Y = int.MinValue, W, H;
        public bool Dhuha = true;           // Dhuha start/end notices
        public bool StartNotice = true;     // popup when a waktu begins
        public bool Sound = true;           // Windows notification sounds
        public bool UpdateCheck = true;     // weekly GitHub release check
        public DateTime LastUpdateUtc = DateTime.MinValue;
        public bool LastUpdateOk;
        public string UpdateVersion, UpdateUrl;   // newer release found (validated)
        public int ThemeMode;               // 0 follow Windows, 1 dark, 2 light
        public int Accent;                  // index into Theme.Accents
        public bool AutostartInit;          // autostart was set up once (v5.1.1+); older settings get it enabled once

        static string FilePath => Path.Combine(Program.DataDir, "settings.ini");

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) return s;
            try
            {
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int.TryParse(v, out int n);
                    switch (k)
                    {
                        case "zone": s.Zone = Zones.Find(v).Code; break;   // only known zone codes accepted
                        case "zoneAuto": s.ZoneAuto = v != "0"; break;
                        case "remind": if (RemindChoices.Contains(n)) s.Remind = n; break;
                        case "x": s.X = n; break;
                        case "y": s.Y = n; break;
                        case "w": s.W = Math.Max(0, Math.Min(n, 4000)); break;
                        case "h": s.H = Math.Max(0, Math.Min(n, 4000)); break;
                        case "autostartInit": s.AutostartInit = v == "1"; break;
                        case "dhuha": s.Dhuha = v != "0"; break;
                        case "startNotice": s.StartNotice = v != "0"; break;
                        case "sound": s.Sound = v != "0"; break;
                        case "updateCheck": s.UpdateCheck = v != "0"; break;
                        case "lastUpdate": if (long.TryParse(v, out long tk) && tk > 0 && tk < DateTime.MaxValue.Ticks) s.LastUpdateUtc = new DateTime(tk, DateTimeKind.Utc); break;
                        case "lastUpdateOk": s.LastUpdateOk = v == "1"; break;
                        case "updateVersion": try { s.UpdateVersion = Updates.ParseVersion(v).ToString(3); } catch { } break;
                        case "updateUrl": if (Updates.SafePage(v)) s.UpdateUrl = v; break;
                        case "theme": if (n >= 0 && n <= 2) s.ThemeMode = n; break;
                        case "accent": if (n >= 0 && n < Theme.Accents.Length) s.Accent = n; break;
                    }
                }
            }
            catch { /* unreadable: defaults */ }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                File.WriteAllLines(FilePath, new[]
                {
                    "zone=" + Zone, "zoneAuto=" + (ZoneAuto ? 1 : 0), "remind=" + Remind,
                    "x=" + X, "y=" + Y, "w=" + W, "h=" + H, "autostartInit=" + (AutostartInit ? 1 : 0),
                    "dhuha=" + (Dhuha ? 1 : 0), "theme=" + ThemeMode, "accent=" + Accent,
                    "startNotice=" + (StartNotice ? 1 : 0), "sound=" + (Sound ? 1 : 0),
                    "updateCheck=" + (UpdateCheck ? 1 : 0), "lastUpdate=" + LastUpdateUtc.Ticks, "lastUpdateOk=" + (LastUpdateOk ? 1 : 0),
                    "updateVersion=" + UpdateVersion, "updateUrl=" + UpdateUrl
                });
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- start with Windows (per-user, no admin)
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string Name = "WaktuSolat";
        static string Command(string exe) => "\"" + exe + "\"";

        public enum State { Off, On, DisabledByWindows }

        public static State Current
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) if (k?.GetValue(Name) == null) return State.Off;
                return DisabledInWindows() ? State.DisabledByWindows : State.On;
            }
        }

        // Task Manager / Settings → Startup apps keep their on/off switch here (first byte odd = turned off).
        static bool DisabledInWindows()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey)) return k?.GetValue(Name) is byte[] b && b.Length > 0 && (b[0] & 1) == 1; }
            catch { return false; }
        }

        /// User choice "on": register this exe, clear Windows' off-switch for this entry, and unblock the file
        /// so SmartScreen can't stop it at sign-in (before the network is up).
        public static void Enable(string exe = null)
        {
            exe = exe ?? Application.ExecutablePath;
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(Name, Command(exe));
            try { using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey, true)) k?.DeleteValue(Name, false); } catch { }
            Unblock(exe);
        }

        public static void Disable()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue(Name, false);
        }

        /// Every start: keep the registered path current (exe moved or replaced) and the file unblocked.
        /// Never overrides an off-switch the user set in Windows.
        public static void Sync()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k?.GetValue(Name) == null) return;
                    var want = Command(Application.ExecutablePath);
                    if (!string.Equals(k.GetValue(Name) as string, want, StringComparison.OrdinalIgnoreCase)) k.SetValue(Name, want);
                }
                Unblock(Application.ExecutablePath);
            }
            catch { }
        }

        /// Same as Properties → "Unblock": removes the "downloaded from the internet" mark from this app's own exe only.
        public static void Unblock(string exe) { try { DeleteFile(exe + ":Zone.Identifier"); } catch { } }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string path);
    }

    // ---------------------------------------------------------------- app folder (avoid OneDrive / Downloads at sign-in)
    static class AppHome
    {
        public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WaktuSolat");
        public static string Exe => Path.Combine(Dir, "WaktuSolat.exe");

        static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        /// A folder from which Windows may fail to start the app at sign-in, or null if the location is fine.
        public static string RiskyLocation()
        {
            try
            {
                var p = Application.ExecutablePath;
                if (Same(p, Exe)) return null;
                foreach (var v in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
                {
                    var root = Environment.GetEnvironmentVariable(v);
                    if (!string.IsNullOrEmpty(root) && p.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return "OneDrive";
                }
                uint a = GetFileAttributes(p);   // cloud placeholder (online-only / recall on open)
                if (a != uint.MaxValue && (a & (0x400000u | 0x40000u | 0x1000u)) != 0) return "OneDrive";
                if (p.IndexOf(@"\Downloads\", StringComparison.OrdinalIgnoreCase) >= 0) return "Downloads";
                if (p.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return "Temp";
            }
            catch { }
            return null;
        }

        /// Copy this exe to %LOCALAPPDATA%\Programs\WaktuSolat, start it there with autostart pointing to it.
        /// The new copy removes the old one once this process has closed.
        public static void MoveHere()
        {
            var src = Application.ExecutablePath;
            Directory.CreateDirectory(Dir);
            var tmp = Exe + ".new";
            File.Copy(src, tmp, true);
            if (File.Exists(Exe)) File.Delete(Exe);
            File.Move(tmp, Exe);
            Autostart.Enable(Exe);
            Process.Start(new ProcessStartInfo(Exe) { Arguments = "--moved-from \"" + src + "\"", UseShellExecute = false, WorkingDirectory = Dir });
        }

        /// Delete the copy we moved from — only if it really is a Waktu Solat exe and not this one.
        public static void RemoveOldCopy(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Same(path, Application.ExecutablePath)) return;
                if (!File.Exists(path) || FileVersionInfo.GetVersionInfo(path).ProductName != "Waktu Solat") return;
            }
            catch { return; }
            new Thread(() =>
            {
                for (int i = 0; i < 30 && File.Exists(path); i++)
                {
                    try { File.Delete(path); } catch { }
                    Thread.Sleep(1000);
                }
            }) { IsBackground = true, Name = "RemoveOldCopy" }.Start();
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint GetFileAttributes(string path);
    }

    // ---------------------------------------------------------------- location (Windows Location; nothing leaves the PC)
    static class Locator
    {
        public const double MaxErrorMeters = 2000;

        public sealed class Result { public string Zone; public string Message; public bool SettingsHint; }

        public static Result Detect()
        {
            try
            {
                using (var w = new GeoCoordinateWatcher(GeoPositionAccuracy.High))
                {
                    bool started = w.TryStart(true, TimeSpan.FromSeconds(10));
                    if (w.Permission == GeoPositionPermission.Denied || !started)
                        return new Result { Message = "Lokasi dimatikan dalam Windows", SettingsHint = true };

                    // Wait (max 30 s) for a fresh fix that is accurate enough; Windows may first hand back an old cached one.
                    var startedAt = DateTimeOffset.Now.AddSeconds(-5);
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    var pos = w.Position;
                    while ((pos.Location.IsUnknown || pos.Location.HorizontalAccuracy > MaxErrorMeters || pos.Timestamp < startedAt)
                           && DateTime.UtcNow < deadline)
                    {
                        Thread.Sleep(500);
                        pos = w.Position;
                    }
                    w.Stop();
                    var c = pos.Location;
                    if (!c.IsUnknown && pos.Timestamp < DateTimeOffset.Now.AddMinutes(-10))
                        return new Result { Message = "Lokasi terakhir sudah lama, zon tidak ditukar", SettingsHint = true };

                    if (c.IsUnknown) return new Result { Message = "Lokasi tidak dapat dikesan", SettingsHint = true };
                    if (c.HorizontalAccuracy > MaxErrorMeters)
                        return new Result { Message = $"Lokasi kurang tepat (±{c.HorizontalAccuracy / 1000:0.#} km), zon tidak ditukar" };

                    using (var s = typeof(Locator).Assembly.GetManifestResourceStream("zones.dat.gz"))
                    {
                        var zone = ZoneLocator.Locate(s, c.Latitude, c.Longitude);   // coordinates used in memory only
                        return zone == null
                            ? new Result { Message = "Lokasi di luar zon JAKIM, zon tidak ditukar" }
                            : new Result { Zone = zone, Message = "OK" };
                    }
                }
            }
            catch (Exception ex) { Program.Log(ex); return new Result { Message = "Ralat lokasi: " + ex.Message }; }
        }
    }

    // ---------------------------------------------------------------- tray application
    sealed class TrayApp : ApplicationContext
    {
        readonly Settings _settings = Settings.Load();
        readonly DataStore _store;
        readonly DoneStore _done;
        readonly Reminders _reminders = new Reminders();
        readonly DhuhaReminders _dhuha = new DhuhaReminders();
        readonly StartNotices _starts = new StartNotices();
        readonly Notifier _notify = new Notifier();
        static readonly Version Current = typeof(TrayApp).Assembly.GetName().Version;
        readonly DateTime _startedUtc = DateTime.UtcNow;
        bool _updateBusy;
        Font _bold;   // cached: menu is rebuilt on every open
        readonly NotifyIcon _tray;
        readonly Widget _widget;
        readonly ContextMenuStrip _menu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        DateTime _lastAutoLocate = DateTime.MinValue;
        string _locStatus = "";
        bool _locSettingsHint;

        readonly bool _justUpdated, _justMoved;
        bool _installing;
        DateTime _locationTipAt = DateTime.UtcNow.AddSeconds(20);   // one tip per run if the exe sits in OneDrive/Downloads

        public TrayApp(bool justUpdated = false, bool justMoved = false)
        {
            _justUpdated = justUpdated; _justMoved = justMoved;
            Theme.Apply((Theme.Mode)_settings.ThemeMode, _settings.Accent);
            _store = new DataStore(Program.DataDir, _settings.Zone);
            _done = new DoneStore(Program.DataDir);

            if (!_settings.AutostartInit) { try { Autostart.Enable(); } catch { } _settings.AutostartInit = true; _settings.Save(); }
            else Autostart.Sync();

            _widget = new Widget(_menu);
            _widget.HideRequested += () => SetVisible(false);
            _widget.ToggleDone += e => { _done.Toggle(e); if (_done.IsDone(e)) Toast.CloseFor(e.Key); Tick(); };
            _widget.LocateRequested += () => Locate(manual: true);
            _widget.UpdateClicked += OpenUpdate;
            _widget.BoundsSaved += b => { _settings.X = b.X; _settings.Y = b.Y; _settings.W = b.Width; _settings.H = b.Height; _settings.Save(); };
            PlaceWidget();

            _tray = new NotifyIcon { Icon = Theme.MakeIcon(SystemInformation.SmallIconSize.Width), ContextMenuStrip = _menu, Visible = true, Text = "Waktu Solat" };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) SetVisible(!_widget.Visible); };
            _menu.Opening += (s, e) => BuildMenu();

            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.UserPreferenceChanged += OnUserPreference;

            _timer.Tick += (s, e) => Tick();
            _timer.Start();
            Tick();
            _widget.Show();   // always visible after start/sign-in; "–" hides it until the next start
            if (_justUpdated)
            {
                _settings.UpdateVersion = null; _settings.UpdateUrl = null; _settings.Save();
                _notify.Info($"Dikemas kini ke versi {Current.ToString(3)}", "Versi lama telah dibuang.");
            }
            if (_justMoved) _notify.Info("Dipindahkan ke folder aplikasi", "Waktu Solat akan mula bersama Windows dari sini.");
        }

        void OnPower(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            _lastAutoLocate = DateTime.MinValue;   // laptop may have moved (home <-> office)
        }

        // Windows switched light/dark while running: follow it if the theme is "Ikut Windows".
        void OnUserPreference(object sender, UserPreferenceChangedEventArgs e)
        {
            // (tray icon follows the taskbar theme, so refresh for any mode)
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle)
                try { _widget.BeginInvoke((Action)ApplyTheme); } catch { }
        }

        void ApplyTheme()
        {
            Theme.Apply((Theme.Mode)_settings.ThemeMode, _settings.Accent);
            _widget.ApplyTheme();
            Toast.Restyle();
            var old = _tray.Icon;
            _tray.Icon = Theme.MakeIcon(SystemInformation.SmallIconSize.Width);
            old?.Dispose();
        }

        void PlaceWidget()
        {
            if (_settings.W > 0 && _settings.H > 0) _widget.Size = new Size(_settings.W, _settings.H);
            var wa = Screen.PrimaryScreen.WorkingArea;
            var p = new Point(_settings.X, _settings.Y);
            // Fall back to bottom-right if no saved position or it's off every screen (e.g. monitor unplugged).
            if (!Screen.AllScreens.Any(sc => sc.WorkingArea.Contains(p.X + 20, p.Y + 20)))
                p = new Point(wa.Right - _widget.Width - 16, wa.Bottom - _widget.Height - 16);
            _widget.Location = p;
        }

        void SetVisible(bool v)
        {
            if (v) _widget.Show(); else _widget.Hide();
        }

        void SetLayout(bool horizontal)
        {
            var b = _widget.Bounds;
            _widget.Size = _widget.Scaled(horizontal ? Widget.HorizontalSize : Widget.VerticalSize);
            // keep the bottom-right corner anchored and on screen
            var wa = Screen.FromRectangle(b).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(b.Right - _widget.Width, wa.Right - _widget.Width));
            int y = Math.Max(wa.Top, Math.Min(b.Bottom - _widget.Height, wa.Bottom - _widget.Height));
            _widget.Location = new Point(x, y);
            var nb = _widget.Bounds;
            _settings.X = nb.X; _settings.Y = nb.Y; _settings.W = nb.Width; _settings.H = nb.Height; _settings.Save();
            _widget.ForceRepaint();
        }

        void Tick()
        {
            var now = Clock.Now;
            var snap = Timeline.Build(_store, now);
            bool estimated = snap.Day.Estimated
                || (snap.Next != null && !_store.HasOfficial(snap.Next.At))
                || (snap.Current != null && !_store.HasOfficial(snap.Current.At));
            _widget.Render(snap, _store.Zone, now, estimated, _done.IsDone);

            if (snap.Next != null)
            {
                var left = Util.Duration(snap.Next.At - now);
                string tip = snap.Current != null && !_done.IsDone(snap.Current)
                    ? $"{snap.Current.Name} · tinggal {left} (belum solat)"
                    : $"{snap.Next.Name} {snap.Next.At:HH:mm} · lagi {left}";
                if (estimated) tip = "≈ " + tip;
                _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            }

            _notify.SoundOn = _settings.Sound;
            _notify.Tick(now);   // shows anything held back during a presentation/fullscreen app
            string est = estimated ? " · anggaran" : "";

            var started = _starts.Check(snap, now);
            if (started != null && _settings.StartNotice && !_done.IsDone(started))
            {
                var e = started; var end = snap.Next.At; string zone = _store.Zone.ShortName;
                _notify.Show(e.Key, () => $"Masuk waktu {e.Name}", () => $"{e.At:HH:mm} · {zone} · tamat {end:HH:mm}{est}",
                             () => MarkDone(e), () => !_done.IsDone(e), 0, false, Sounds.Default,
                             e.At.AddMinutes(20), e.At.AddMinutes(20) < end ? e.At.AddMinutes(20) : end);
            }

            var due = _reminders.Check(snap, now, _settings.Remind, _done.IsDone);
            if (due != null)
            {
                var e = due; var next = snap.Next; string zone = _store.Zone.ShortName;
                _notify.Show(e.Key, () => $"Waktu {e.Name} tinggal {Util.Duration(next.At - Clock.Now)}",   // text built when shown
                             () => $"Tamat {next.At:HH:mm} ({next.Name}) · {zone}{est}",
                             () => MarkDone(e), () => !_done.IsDone(e), 0, false, Sounds.Reminder, next.At, next.At);
            }

            if (_settings.Dhuha)
            {
                var d = snap.Day; string tag = estimated ? " · anggaran" : "";
                string zohor = (d.Date.DayOfWeek == DayOfWeek.Friday ? "Jumaat " : "Zohor ") + (d.Date + d.Zohor).ToString("HH:mm");
                switch (_dhuha.Check(d, now, _settings.Remind, _store.Zone.Code))
                {
                    case DhuhaEvent.Started:
                        _notify.Show(null, () => "Waktu Dhuha bermula", () => $"Hingga ~{d.DhuhaEnd:HH:mm} ({zohor}){tag}", null, null, 25, true, Sounds.Default, d.DhuhaStart.AddMinutes(30), d.DhuhaEnd);
                        break;
                    case DhuhaEvent.Ending:
                        _notify.Show(null, () => $"Dhuha tamat dalam {Util.Duration(d.DhuhaEnd - Clock.Now)}", () => $"Solat sebelum ~{d.DhuhaEnd:HH:mm} · {zohor}{tag}", null, null, 0, true, Sounds.Default, d.DhuhaEnd, d.DhuhaEnd);
                        break;
                }
            }

            if (_locationTipAt != DateTime.MaxValue && DateTime.UtcNow >= _locationTipAt)
            {
                _locationTipAt = DateTime.MaxValue;
                var risky = AppHome.RiskyLocation();
                if (risky != null)
                    _notify.Info($"Waktu Solat ada dalam {risky}", "Ia mungkin tidak mula bersama Windows. Klik kanan widget → Pindah ke folder aplikasi.");
            }

            ShowUpdateState();
            if (_settings.UpdateCheck && !_updateBusy && (DateTime.UtcNow - _startedUtc).TotalSeconds > 90
                && Updates.Due(_settings.LastUpdateUtc, _settings.LastUpdateOk, DateTime.UtcNow))
                CheckUpdate();

            if (_settings.ZoneAuto && !_widget.Locating && (DateTime.UtcNow - _lastAutoLocate).TotalMinutes >= 60)
                Locate(manual: false);
        }

        // ------------------------------------------------------------ updates
        bool UpdateAvailable => _settings.UpdateUrl != null && _settings.UpdateVersion != null
                                && Updates.IsNewer(new Version(_settings.UpdateVersion), Current);

        void ShowUpdateState() => _widget.UpdateText =
            _installing ? "Memuat turun kemas kini…" : UpdateAvailable ? $"Versi {_settings.UpdateVersion} tersedia · klik untuk kemas kini" : null;

        void CheckUpdate()
        {
            _updateBusy = true;
            new Thread(() =>
            {
                Updates.Release rel = null; bool ok;
                try { rel = Updates.Check(); ok = true; } catch { ok = false; }
                try
                {
                    _widget.BeginInvoke((Action)(() =>
                    {
                        _updateBusy = false;
                        _settings.LastUpdateUtc = DateTime.UtcNow; _settings.LastUpdateOk = ok;
                        if (ok)
                        {
                            bool newer = rel != null && Updates.IsNewer(rel.Version, Current);
                            _settings.UpdateVersion = newer ? rel.Version.ToString(3) : null;
                            _settings.UpdateUrl = newer ? rel.Url : null;
                        }
                        _settings.Save();
                        ShowUpdateState(); _widget.ForceRepaint();
                    }));
                }
                catch { }
            }) { IsBackground = true, Name = "UpdateCheck" }.Start();
        }

        /// Click on "Versi X tersedia": download + verify + install, then restart. Falls back to the release page.
        void OpenUpdate()
        {
            if (!UpdateAvailable || _installing) return;
            _installing = true;
            _widget.UpdateText = "Memuat turun kemas kini…"; _widget.ForceRepaint();
            new Thread(() =>
            {
                Updates.Release rel = null; byte[] exe = null; string error = null;
                try
                {
                    rel = Updates.Check();                                   // fresh info, not days-old settings
                    if (rel == null || !Updates.IsNewer(rel.Version, Current)) error = "Tiada versi baharu";
                    else if (rel.AssetUrl != null) exe = Updates.Download(rel);   // size + SHA-256 verified
                }
                catch (Exception ex) { error = ex.Message; }
                try { _widget.BeginInvoke((Action)(() => FinishUpdate(rel, exe, error))); } catch { }
            }) { IsBackground = true, Name = "Update" }.Start();
        }

        void FinishUpdate(Updates.Release rel, byte[] exe, string error)
        {
            _installing = false;
            if (error == null && exe != null)
            {
                try { SelfUpdate.Install(exe, rel.Version); ExitThread(); return; }   // new version starts, this one exits
                catch (Exception ex) { error = ex.Message; }
            }
            ShowUpdateState(); _widget.ForceRepaint();
            if (error == "Tiada versi baharu") { _settings.UpdateVersion = null; _settings.UpdateUrl = null; _settings.Save(); ShowUpdateState(); return; }
            // Couldn't install automatically (e.g. folder not writable): open the release page instead.
            var page = rel?.Url ?? _settings.UpdateUrl;
            if (error != null) _notify.Info("Kemas kini automatik gagal", Short(error) + " · buka halaman muat turun");
            if (Updates.SafePage(page)) try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); } catch { }
        }

        void CheckUpdateNow()
        {
            _settings.LastUpdateUtc = DateTime.MinValue; _settings.LastUpdateOk = false;
            if (!_updateBusy) CheckUpdate();
        }

        void MoveHome()
        {
            try { AppHome.MoveHere(); ExitThread(); }   // the copy in the app folder starts and removes this one
            catch (Exception ex) { MessageBox.Show("Tidak dapat memindahkan: " + ex.Message, "Waktu Solat"); }
        }

        void MarkDone(Entry e)
        {
            if (!_done.IsDone(e)) _done.Toggle(e);
            Toast.CloseFor(e.Key);
            _widget.ForceRepaint();
        }

        // ------------------------------------------------------------ location
        void Locate(bool manual)
        {
            if (_widget.Locating) return;
            _lastAutoLocate = DateTime.UtcNow;
            _widget.Locating = true; _widget.ForceRepaint();
            var t = new Thread(() =>
            {
                var r = Locator.Detect();
                try { _widget.BeginInvoke((Action)(() => OnLocated(r, manual))); } catch { /* app closing */ }
            }) { IsBackground = true, Name = "Locate" };
            t.Start();
        }

        void OnLocated(Locator.Result r, bool manual)
        {
            _widget.Locating = false;
            if (!manual && !_settings.ZoneAuto) { _widget.ForceRepaint(); return; }   // user picked a zone meanwhile
            _locSettingsHint = r.SettingsHint;
            if (r.Zone == null)
            {
                _locStatus = r.Message;
                if (manual) _notify.Info("Lokasi tidak dapat digunakan", r.Message + (r.SettingsHint ? " · lihat menu" : ""));
                _widget.ForceRepaint();
                return;
            }
            var z = Zones.Find(r.Zone);
            _locStatus = $"{z.Code} dikesan {Clock.Now:HH:mm}";
            if (manual) _settings.ZoneAuto = true;   // pressing 📍 means "follow my location"
            if (z.Code != _store.Zone.Code)
            {
                ChangeZone(z.Code, auto: true);
                _notify.Info($"Zon ditukar: {z.Code}", z.Name);
            }
            else if (manual) _notify.Info($"Zon kekal: {z.Code}", z.Name);
            _settings.Save();
            _widget.ForceRepaint();
        }

        void ChangeZone(string code, bool auto)
        {
            _settings.Zone = code;
            _settings.ZoneAuto = auto;
            _settings.Save();
            _store.SetZone(code);
            Tick();
        }

        // ------------------------------------------------------------ menu
        void BuildMenu()
        {
            var old = _menu.Items.Cast<ToolStripItem>().ToList();
            _menu.Items.Clear();
            foreach (var it in old) it.Dispose();   // free submenu handles from the previous build
            if (UpdateAvailable)
            {
                _menu.Items.Add(new ToolStripMenuItem($"↑ Kemas kini ke versi {_settings.UpdateVersion}", null, (s, e) => OpenUpdate())
                    { Font = _bold ?? (_bold = new Font(_menu.Font, FontStyle.Bold)) });
                _menu.Items.Add(new ToolStripSeparator());
            }
            _menu.Items.Add(new ToolStripMenuItem(_widget.Visible ? "Sembunyi widget" : "Tunjuk widget", null, (s, e) => SetVisible(!_widget.Visible)));
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add(new ToolStripMenuItem("Kesan lokasi sekarang", null, (s, e) => Locate(manual: true)) { Enabled = !_widget.Locating });

            var zone = new ToolStripMenuItem($"Zon: {_store.Zone.Code}" + (_settings.ZoneAuto ? " (auto)" : ""));
            zone.DropDownItems.Add(new ToolStripMenuItem("Auto, ikut lokasi", null, (s, e) => { _settings.ZoneAuto = true; _settings.Save(); Locate(manual: true); })
                { Checked = _settings.ZoneAuto });
            zone.DropDownItems.Add(new ToolStripSeparator());
            foreach (var state in Zones.All.GroupBy(z => z.State))
            {
                var st = new ToolStripMenuItem(state.Key) { Checked = state.Key == _store.Zone.State };
                foreach (var z in state)
                {
                    var code = z.Code;
                    st.DropDownItems.Add(new ToolStripMenuItem($"{z.Code}  {z.Name}", null, (s, e) => ChangeZone(code, auto: false))
                        { Checked = code == _store.Zone.Code });
                }
                zone.DropDownItems.Add(st);
            }
            _menu.Items.Add(zone);

            var remind = new ToolStripMenuItem("Peringatan");
            foreach (int m in Settings.RemindChoices)
            {
                int mm = m;
                remind.DropDownItems.Add(new ToolStripMenuItem(m == 0 ? "Tutup" : $"{m} minit sebelum waktu tamat", null,
                    (s, e) => { _settings.Remind = mm; _settings.Save(); }) { Checked = _settings.Remind == m });
            }
            remind.DropDownItems.Add(new ToolStripSeparator());
            remind.DropDownItems.Add(new ToolStripMenuItem("Notifikasi masuk waktu", null,
                (s, e) => { _settings.StartNotice = !_settings.StartNotice; _settings.Save(); }) { Checked = _settings.StartNotice });
            remind.DropDownItems.Add(new ToolStripMenuItem(_settings.Remind > 0 ? "Dhuha (mula & hampir tamat)" : "Dhuha (mula sahaja)", null,
                (s, e) => { _settings.Dhuha = !_settings.Dhuha; _settings.Save(); }) { Checked = _settings.Dhuha });
            remind.DropDownItems.Add(new ToolStripSeparator());
            remind.DropDownItems.Add(new ToolStripMenuItem("Bunyi (bunyi notifikasi Windows)", null,
                (s, e) => { _settings.Sound = !_settings.Sound; _settings.Save(); if (_settings.Sound) Sounds.Play(Sounds.Default); }) { Checked = _settings.Sound });
            _menu.Items.Add(remind);

            var theme = new ToolStripMenuItem("Tema");
            string[] modes = { "Ikut Windows", "Gelap", "Terang" };
            for (int i = 0; i < modes.Length; i++)
            {
                int ii = i;
                theme.DropDownItems.Add(new ToolStripMenuItem(modes[i], null, (s, e) => { _settings.ThemeMode = ii; _settings.Save(); ApplyTheme(); })
                    { Checked = _settings.ThemeMode == i });
            }
            theme.DropDownItems.Add(new ToolStripSeparator());
            for (int i = 0; i < Theme.Accents.Length; i++)
            {
                int ii = i;
                var a = Theme.Accents[i];
                theme.DropDownItems.Add(new ToolStripMenuItem(a.Name, Swatch(Theme.IsLight ? a.Light : a.Dark),
                    (s, e) => { _settings.Accent = ii; _settings.Save(); ApplyTheme(); }) { Checked = _settings.Accent == i });
            }
            _menu.Items.Add(theme);

            var layout = new ToolStripMenuItem("Susun atur");
            layout.DropDownItems.Add(new ToolStripMenuItem("Menegak", null, (s, e) => SetLayout(false)) { Checked = !_widget.Horizontal });
            layout.DropDownItems.Add(new ToolStripMenuItem("Mendatar", null, (s, e) => SetLayout(true)) { Checked = _widget.Horizontal });
            _menu.Items.Add(layout);

            var autoState = Autostart.State.Off; try { autoState = Autostart.Current; } catch { }
            _menu.Items.Add(new ToolStripMenuItem(autoState == Autostart.State.DisabledByWindows ? "Mula bersama Windows (dimatikan dalam Windows · klik untuk hidupkan)" : "Mula bersama Windows", null, (s, e) =>
            {
                try { if (autoState == Autostart.State.On) Autostart.Disable(); else Autostart.Enable(); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Waktu Solat"); }
            }) { Checked = autoState == Autostart.State.On });
            var risky = AppHome.RiskyLocation();
            if (risky != null)
                _menu.Items.Add(new ToolStripMenuItem($"⚠ Fail dalam {risky}: pindah ke folder aplikasi (supaya mula bersama Windows)", null, (s, e) => MoveHome()));

            // Status (read-only) so problems are visible, not silent.
            _menu.Items.Add(new ToolStripSeparator());
            bool est = _store.Get(Clock.Now.Date).Estimated;
            _menu.Items.Add(Info(est ? "Data: ≈ anggaran (" + Short(_store.LastFetchFailed ? _store.Status : "belum dimuat turun") + ")"
                                     : "Data: JAKIM e-Solat ✓"));
            if (_locStatus.Length > 0) _menu.Items.Add(Info("Lokasi: " + Short(_locStatus)));
            _menu.Items.Add(new ToolStripMenuItem("Muat turun semula data JAKIM", null, (s, e) => _store.Refresh()));
            var upd = new ToolStripMenuItem($"Kemas kini (v{Current.ToString(3)})");
            upd.DropDownItems.Add(new ToolStripMenuItem("Semak sekarang", null, (s, e) => CheckUpdateNow()) { Enabled = !_updateBusy && !_installing });
            upd.DropDownItems.Add(new ToolStripMenuItem("Semak seminggu sekali", null,
                (s, e) => { _settings.UpdateCheck = !_settings.UpdateCheck; _settings.Save(); }) { Checked = _settings.UpdateCheck });
            _menu.Items.Add(upd);
            if (_locSettingsHint)
                _menu.Items.Add(new ToolStripMenuItem("Buka tetapan Lokasi Windows", null,
                    (s, e) => { try { Process.Start("ms-settings:privacy-location"); } catch { } }));

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Keluar", null, (s, e) => ExitThread()));
        }

        static readonly System.Collections.Generic.Dictionary<int, Bitmap> _swatches = new System.Collections.Generic.Dictionary<int, Bitmap>();

        static Bitmap Swatch(Color c)   // cached: menu items are rebuilt on every open
        {
            if (_swatches.TryGetValue(c.ToArgb(), out var b)) return b;
            b = new Bitmap(12, 12);
            using (var g = Graphics.FromImage(b)) { g.SmoothingMode = SmoothingMode.AntiAlias; using (var br = new SolidBrush(c)) g.FillEllipse(br, 1, 1, 10, 10); }
            return _swatches[c.ToArgb()] = b;
        }

        static ToolStripMenuItem Info(string text) => new ToolStripMenuItem(text) { Enabled = false };
        static string Short(string s) => s.Length > 70 ? s.Substring(0, 70) + "…" : s;

        protected override void ExitThreadCore()
        {
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.UserPreferenceChanged -= OnUserPreference;
            _timer.Stop();
            Toast.CloseCurrent();
            _tray.Visible = false;
            _tray.Dispose();
            _widget.ForceClose();   // real close; doesn't save "hidden" state
            base.ExitThreadCore();
        }
    }

    // ---------------------------------------------------------------- Windows sounds (user's own sound scheme)
    static class Sounds
    {
        // Built-in Windows sound events (Control Panel → Sound → Sounds): "Notification" and "Calendar Reminder".
        public const string Default = "Notification.Default", Reminder = "Notification.Reminder";

        [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool PlaySound(string name, IntPtr module, uint flags);
        const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_ALIAS = 0x10000, SND_SYSTEM = 0x200000;

        public static void Play(string evt)
        {
            try
            {
                // Plays whatever the user chose for this event; silent if their scheme has none ("No sounds").
                if (PlaySound(evt, IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NODEFAULT | SND_SYSTEM)) return;
                if (EventExists(evt)) return;                 // event exists but set to (None): respect silence
                System.Media.SystemSounds.Asterisk.Play();     // very old systems without the event
            }
            catch { }
        }

        static bool EventExists(string evt)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\" + evt)) return k != null; }
            catch { return false; }
        }
    }

    // ---------------------------------------------------------------- notifications (held back during presentations)
    sealed class Notifier
    {
        sealed class Item
        {
            public string Key, Sound; public Func<string> Title, Body; public Action OnDone; public Func<bool> Valid;
            public int AutoClose; public bool Sticky; public DateTime ShowBy, Expires;
        }

        Item _pending, _shown;
        public bool SoundOn = true;

        /// key: the waktu this popup is about (closed when ticked). title/body are built when actually shown.
        /// valid: re-checked before showing (e.g. not ticked meanwhile). autoClose: seconds (0 = until acted on / expired).
        /// showBy: if held back (presentation, fullscreen, locked), drop after this. expires: popup closes itself then.
        public void Show(string key, Func<string> title, Func<string> body, Action onDone, Func<bool> valid,
                         int autoClose, bool sticky, string sound, DateTime showBy, DateTime expires)
        {
            var it = new Item { Key = key, Title = title, Body = body, OnDone = onDone, Valid = valid, AutoClose = autoClose,
                                Sticky = sticky, Sound = sound, ShowBy = showBy, Expires = expires };
            if (UserBusy()) { Hold(it); return; }
            Present(it);
        }

        /// Low-priority, silent info (zone changed, location result); dropped when the user is busy.
        public void Info(string title, string body)
        {
            if (!UserBusy()) Present(new Item { Title = () => title, Body = () => body, AutoClose = 8, Expires = DateTime.MaxValue });
        }

        public void Tick(DateTime now)
        {
            if (UserBusy())
            {
                // A presentation started while a popup is up: take it down and bring it back afterwards.
                if (_shown != null && Toast.IsOpen(_shown.Key) && _shown.OnDone != null) { Toast.CloseCurrent(); Hold(_shown); _shown = null; }
                return;
            }
            if (_pending == null) return;
            var p = _pending; _pending = null;
            if (now < p.ShowBy && (p.Valid == null || p.Valid())) Present(p);
        }

        void Hold(Item it)
        {
            if (_pending == null || _pending.OnDone == null || it.OnDone != null) _pending = it;   // reminders win
        }

        void Present(Item it)
        {
            if (!Toast.Show(it.Title(), it.Body(), it.OnDone, it.AutoClose, it.Expires, it.Sticky, it.Key)) return;
            _shown = it;
            if (SoundOn && it.Sound != null) Sounds.Play(it.Sound);
        }

        // Locked (1), fullscreen app (2), Direct3D fullscreen (3) or presentation mode / slideshow (4).
        static bool UserBusy()
        {
            try { return SHQueryUserNotificationState(out int s) == 0 && s >= 1 && s <= 4; }
            catch { return false; }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);
    }

    // ---------------------------------------------------------------- self-update (replace exe, restart, remove old)
    static class SelfUpdate
    {
        static string Exe => Application.ExecutablePath;
        static string Old => Exe + ".old";
        static string New => Exe + ".new";

        /// bytes are already size- and SHA-256-verified against GitHub. Swap files and start the new version.
        public static void Install(byte[] bytes, Version expected)
        {
            TryDelete(New); TryDelete(Old);
            File.WriteAllBytes(New, bytes);
            try
            {
                // Must be our app, at exactly the version announced.
                var name = System.Reflection.AssemblyName.GetAssemblyName(New);
                var info = FileVersionInfo.GetVersionInfo(New);
                if (name.Name != "WaktuSolat" || info.ProductName != "Waktu Solat"
                    || name.Version.Major != expected.Major || name.Version.Minor != expected.Minor || name.Version.Build != expected.Build)
                    throw new InvalidDataException("Fail kemas kini bukan Waktu Solat " + expected.ToString(3));
            }
            catch { TryDelete(New); throw; }

            File.Move(Exe, Old);                      // a running exe can be renamed, not overwritten
            try
            {
                File.Move(New, Exe);
                Process.Start(new ProcessStartInfo(Exe, "--updated") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Exe) });
            }
            catch
            {
                TryDelete(Exe); if (File.Exists(Old)) File.Move(Old, Exe);   // roll back
                throw;
            }
        }

        /// Called at every start: removes the previous version left by an update (retries while it is still closing).
        public static void CleanupOldVersion()
        {
            if (!File.Exists(Old) && !File.Exists(New)) return;
            new Thread(() =>
            {
                for (int i = 0; i < 30 && (File.Exists(Old) || File.Exists(New)); i++)
                {
                    TryDelete(Old); TryDelete(New);
                    Thread.Sleep(1000);
                }
            }) { IsBackground = true, Name = "Cleanup" }.Start();
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    }
}
