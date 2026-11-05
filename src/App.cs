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
        static void Main()
        {
            using (var mutex = new Mutex(true, @"Local\WaktuSolat-SingleInstance", out bool first))
            {
                if (!first) return;   // already running (see tray)
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);
                Application.Run(new TrayApp());
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
        public bool Visible = true;
        public bool Dhuha = true;           // Dhuha start/end notices
        public bool StartNotice = true;     // popup when a waktu begins
        public bool Sound = true;           // Windows notification sounds
        public bool UpdateCheck = true;     // weekly GitHub release check
        public DateTime LastUpdateUtc = DateTime.MinValue;
        public bool LastUpdateOk;
        public string UpdateVersion, UpdateUrl;   // newer release found (validated)
        public int ThemeMode;               // 0 follow Windows, 1 dark, 2 light
        public int Accent;                  // index into Theme.Accents
        public bool FirstRun;

        static string FilePath => Path.Combine(Program.DataDir, "settings.ini");

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) { s.FirstRun = true; return s; }
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
                        case "visible": s.Visible = v != "0"; break;
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
                    "x=" + X, "y=" + Y, "w=" + W, "h=" + H, "visible=" + (Visible ? 1 : 0),
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
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "WaktuSolat";
        static string Command => "\"" + Application.ExecutablePath + "\"";

        public static bool Enabled
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return k?.GetValue(Name) != null; }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                    if (value) k.SetValue(Name, Command); else k.DeleteValue(Name, false);
            }
        }

        /// Keep the path correct if the exe was moved.
        public static void Sync() { try { if (Enabled) Enabled = true; } catch { } }
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

        public TrayApp()
        {
            Theme.Apply((Theme.Mode)_settings.ThemeMode, _settings.Accent);
            _store = new DataStore(Program.DataDir, _settings.Zone);
            _done = new DoneStore(Program.DataDir);

            if (_settings.FirstRun) { try { Autostart.Enabled = true; } catch { } _settings.Save(); }
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
            if (_settings.Visible) _widget.Show();
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
            _settings.Visible = v; _settings.Save();
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

        void ShowUpdateState() => _widget.UpdateText = UpdateAvailable ? $"Versi {_settings.UpdateVersion} tersedia · klik" : null;

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

        void OpenUpdate()
        {
            if (!UpdateAvailable || !Updates.SafePage(_settings.UpdateUrl)) return;
            try { Process.Start(new ProcessStartInfo(_settings.UpdateUrl) { UseShellExecute = true }); } catch { }
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
                _menu.Items.Add(new ToolStripMenuItem($"↑ Muat turun versi {_settings.UpdateVersion}", null, (s, e) => OpenUpdate())
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

            bool auto = false; try { auto = Autostart.Enabled; } catch { }
            _menu.Items.Add(new ToolStripMenuItem("Mula bersama Windows", null, (s, e) =>
            {
                try { Autostart.Enabled = !auto; } catch (Exception ex) { MessageBox.Show(ex.Message, "Waktu Solat"); }
            }) { Checked = auto });

            // Status (read-only) so problems are visible, not silent.
            _menu.Items.Add(new ToolStripSeparator());
            bool est = _store.Get(Clock.Now.Date).Estimated;
            _menu.Items.Add(Info(est ? "Data: ≈ anggaran (" + Short(_store.LastFetchFailed ? _store.Status : "belum dimuat turun") + ")"
                                     : "Data: JAKIM e-Solat ✓"));
            if (_locStatus.Length > 0) _menu.Items.Add(Info("Lokasi: " + Short(_locStatus)));
            _menu.Items.Add(new ToolStripMenuItem("Muat turun semula data JAKIM", null, (s, e) => _store.Refresh()));
            _menu.Items.Add(new ToolStripMenuItem($"Semak kemas kini seminggu sekali (v{Current.ToString(3)})", null,
                (s, e) => { _settings.UpdateCheck = !_settings.UpdateCheck; _settings.Save(); }) { Checked = _settings.UpdateCheck });
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
}
