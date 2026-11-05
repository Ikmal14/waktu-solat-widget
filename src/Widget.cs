// Desktop widget (resizable; vertical or horizontal layout) and the reminder popup.
// Everything is drawn in a fixed "design" coordinate space and scaled, so it stays crisp at any size/DPI.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WaktuSolat
{
    abstract class BorderlessForm : Form
    {
        protected readonly float K;   // DPI scale

        protected BorderlessForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            using (var g = CreateGraphics()) K = g.DpiX / 96f;
        }

        protected int S(float v) => (int)Math.Round(v * K);

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: no taskbar / Alt-Tab entry
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { } // Win11 rounded corners
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("user32.dll")] protected static extern bool ReleaseCapture();
        [DllImport("user32.dll")] protected static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }

    sealed class Widget : BorderlessForm
    {
        public event Action HideRequested;
        public event Action<Entry> ToggleDone;
        public event Action<Rectangle> BoundsSaved;
        public event Action LocateRequested;
        public event Action UpdateClicked;
        public bool Locating;          // shows "Mengesan lokasi…" while a location check runs
        public string UpdateText;      // e.g. "Versi 5.1 tersedia" (null = none)

        // Design sizes (before scaling) — 4px grid
        public static readonly SizeF VerticalSize = new SizeF(232, 304), HorizontalSize = new SizeF(616, 112);
        const float Pad = 16;

        readonly ContextMenuStrip _menu;
        readonly Font _caption = Type.Caption(), _body = Type.Body(), _strong = Type.BodyStrong(),
                      _large = Type.Body(18), _largeStrong = Type.BodyStrong(18), _display = Type.Display(40), _displayH = Type.Display(38),
                      _icon = Type.Icon(14), _checkGlyph = Type.Icon(10);
        readonly StringFormat _right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        readonly StringFormat _left = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        readonly StringFormat _center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        SolidBrush _fg, _dim, _acc, _warn, _track, _plate, _hoverB, _onAcc;

        /// Re-create brushes after a theme/accent change.
        public void ApplyTheme()
        {
            foreach (var b in new[] { _fg, _dim, _acc, _warn, _track, _plate, _hoverB, _onAcc }) b?.Dispose();
            _fg = new SolidBrush(Theme.Fg); _dim = new SolidBrush(Theme.Dim); _acc = new SolidBrush(Theme.Accent);
            _warn = new SolidBrush(Theme.Warn); _track = new SolidBrush(Theme.Track); _plate = new SolidBrush(Theme.Plate);
            _hoverB = new SolidBrush(Theme.Hover); _onAcc = new SolidBrush(Theme.OnAccent);
            BackColor = Theme.Bg;
            _digitCell.Clear();
            ForceRepaint();
        }

        Snapshot _snap; Zone _zone; DateTime _now; bool _estimated; Func<Entry, bool> _isDone = _ => false;
        string _lastSecond;
        float _scale = 1, _ox, _oy;
        RectangleF _hideRect, _locRect, _statusRect;
        bool _mouseIn; Entry _hoverEntry; object _hoverBtn;
        readonly List<KeyValuePair<RectangleF, Entry>> _hits = new List<KeyValuePair<RectangleF, Entry>>();

        public bool Horizontal => ClientSize.Width > ClientSize.Height * 2.2f;

        public Widget(ContextMenuStrip menu)
        {
            _menu = menu;
            ApplyTheme();
            MinimumSize = new Size(S(176), S(64));
            MaximumSize = new Size(S(1400), S(900));
            ClientSize = Scaled(VerticalSize);
        }

        public Size Scaled(SizeF design) => new Size(S(design.Width), S(design.Height));

        public void Render(Snapshot snap, Zone zone, DateTime now, bool estimated, Func<Entry, bool> isDone)
        {
            _snap = snap; _zone = zone; _now = now; _estimated = estimated; _isDone = isDone;
            // Repaint at most once per displayed second.
            string sec = now.ToString("HHmmss") + zone.Code + estimated;
            if (sec != _lastSecond) { _lastSecond = sec; Invalidate(); }
        }

        public void ForceRepaint() { _lastSecond = null; Invalidate(); }

        // ------------------------------------------------------------ painting
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_snap == null) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var design = Horizontal ? HorizontalSize : VerticalSize;
            _scale = Math.Min(ClientSize.Width / design.Width, ClientSize.Height / design.Height);
            _ox = (ClientSize.Width - design.Width * _scale) / 2;
            _oy = (ClientSize.Height - design.Height * _scale) / 2;
            g.TranslateTransform(_ox, _oy);
            g.ScaleTransform(_scale, _scale);
            _hits.Clear();
            _statusRect = RectangleF.Empty;

            if (Horizontal) PaintHorizontal(g, design); else PaintVertical(g, design);
        }

        string Countdown()
        {
            if (_snap.Next == null) return "--:--:--";
            var left = _snap.Next.At - _now;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            return $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
        }

        /// The one line of extra context — shown only when it tells you something the rest doesn't.
        bool Status(out string text, out Brush brush, out bool isUpdate)
        {
            isUpdate = false; brush = _dim;
            if (_estimated) { text = "≈ Anggaran, bukan waktu rasmi JAKIM"; brush = _warn; return true; }
            if (_snap.Current == null && _snap.Next != null)
            {
                var d = _snap.Day;
                switch (_snap.Phase)
                {
                    case Gap.Tahrim: text = $"Subuh tamat · Dhuha bermula {d.DhuhaStart:HH:mm}"; return true;
                    case Gap.Dhuha: text = $"Waktu Dhuha · hingga ~{d.DhuhaEnd:HH:mm}"; return true;
                    case Gap.BeforeZohor: text = $"Menjelang {_snap.Next.Name} (istiwa')"; return true;
                }
            }
            if (UpdateText != null) { text = "↑ " + UpdateText; brush = _acc; isUpdate = true; return true; }
            text = null; return false;
        }

        void PaintVertical(Graphics g, SizeF d)
        {
            float w = d.Width - 2 * Pad;
            Header(g, new RectangleF(Pad, 16, w - 64, 20));
            HeaderButtons(g, new RectangleF(d.Width - 12 - 28, 12, 28, 28), new RectangleF(d.Width - 12 - 60, 12, 28, 28));

            if (_snap.Next != null)
                g.DrawString($"{_snap.Next.Name}  {_snap.Next.At:HH:mm}", _strong, _acc, new RectangleF(Pad, 40, w, 20), _left);
            DrawTabular(g, Countdown(), _display, _fg, Pad - 2, 60, 52);

            if (Status(out var st, out var sb, out bool upd))
            {
                var r = new RectangleF(Pad, 112, w, 16);
                g.DrawString(st, _caption, sb, r, _left);
                if (upd) _statusRect = r;
            }

            Progress(g, Pad, 136, w);

            float y = 148;
            foreach (var en in _snap.Today.Where(x => x.Fardhu))
            {
                var row = new RectangleF(Pad - 8, y, w + 16, 28);
                RowState(en, out bool isCur, out bool past, out bool done, out bool clickable);
                using (var path = Theme.Rounded(row, 4))
                {
                    if (isCur) g.FillPath(_plate, path);
                    else if (clickable && _hoverEntry != null && _hoverEntry.Key == en.Key) g.FillPath(_hoverB, path);
                }
                if (clickable) Tick(g, Pad + 9, y + 14, 9, done);
                var font = isCur ? _strong : _body;
                var brush = past ? _dim : _fg;
                g.DrawString(en.Name, font, brush, new RectangleF(Pad + 28, y, w - 80, 28), _left);
                g.DrawString(en.At.ToString("HH:mm"), font, brush, new RectangleF(Pad, y, w, 28), _right);
                if (clickable) _hits.Add(new KeyValuePair<RectangleF, Entry>(row, en));
                y += 28;
            }
        }

        void PaintHorizontal(Graphics g, SizeF d)
        {
            const float x0 = 188, btnCol = 44;
            Header(g, new RectangleF(Pad, 12, x0 - Pad - 8, 16));
            if (_snap.Next != null)
                g.DrawString($"{_snap.Next.Name}  {_snap.Next.At:HH:mm}", _strong, _acc, new RectangleF(Pad, 30, x0 - Pad - 8, 20), _left);
            DrawTabular(g, Countdown(), _displayH, _fg, Pad - 2, 50, 46);

            HeaderButtons(g, new RectangleF(d.Width - 40, 12, 28, 28), new RectangleF(d.Width - 40, 44, 28, 28));

            float right = d.Width - btnCol, colW = (right - x0) / 5;
            int i = 0;
            foreach (var en in _snap.Today.Where(x => x.Fardhu))
            {
                float x = x0 + i++ * colW;
                var cell = new RectangleF(x + 2, 12, colW - 4, 64);
                RowState(en, out bool isCur, out bool past, out bool done, out bool clickable);
                using (var path = Theme.Rounded(cell, 4))
                {
                    if (isCur) g.FillPath(_plate, path);
                    else if (clickable && _hoverEntry != null && _hoverEntry.Key == en.Key) g.FillPath(_hoverB, path);
                }
                // tick (when it can be ticked) sits left of a shared text edge for name + time
                if (clickable) Tick(g, x + 14, 30, 6, done);
                g.DrawString(en.Name, _caption, past ? _dim : _fg, new RectangleF(x + 24, 20, colW - 26, 20), _left);
                g.DrawString(en.At.ToString("HH:mm"), isCur ? _largeStrong : _large, past ? _dim : _fg, new RectangleF(x + 23, 42, colW - 24, 28), _left);
                if (clickable) _hits.Add(new KeyValuePair<RectangleF, Entry>(cell, en));
            }
            if (Status(out var st, out var sb, out bool upd))
            {
                var r = new RectangleF(x0 + 8, 78, right - x0 - 8, 16);
                g.DrawString(st, _caption, sb, r, _left);
                if (upd) _statusRect = r;
            }
            Progress(g, Pad, d.Height - 12, d.Width - 2 * Pad);
        }

        void Header(Graphics g, RectangleF r) =>
            g.DrawString(Locating ? "Mengesan lokasi…" : _zone == null ? "" : $"{_zone.ShortName} · {_zone.Code}", _caption, _dim, r, _left);

        /// Location + hide buttons: shown on hover (and while locating), 28px targets.
        void HeaderButtons(Graphics g, RectangleF hide, RectangleF loc)
        {
            _hideRect = hide; _locRect = loc;
            if (!_mouseIn && !Locating) return;
            HeaderButton(g, hide, "hide");
            HeaderButton(g, loc, "loc");
        }

        void HeaderButton(Graphics g, RectangleF r, string which)
        {
            if (Equals(_hoverBtn, which)) using (var p = Theme.Rounded(r, 4)) g.FillPath(_hoverB, p);
            var brush = which == "loc" && Locating ? _acc : _dim;
            if (_icon != null) { g.DrawString(which == "hide" ? Type.GlyphMinimize : Type.GlyphLocation, _icon, brush, r, _center); return; }
            using (var p = new Pen(brush, 1.2f))   // fallback when no icon font (shouldn't happen on Windows 10/11)
            {
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                if (which == "hide") g.DrawLine(p, cx - 5, cy, cx + 5, cy);
                else { g.DrawEllipse(p, cx - 5, cy - 5, 10, 10); g.DrawLine(p, cx, cy - 8, cx, cy + 8); g.DrawLine(p, cx - 8, cy, cx + 8, cy); }
            }
        }

        // Countdown with fixed-width digit cells so the numbers never jiggle as they change.
        readonly Dictionary<Font, SizeF> _digitCell = new Dictionary<Font, SizeF>();
        void DrawTabular(Graphics g, string text, Font f, Brush b, float x, float y, float h)
        {
            var tf = StringFormat.GenericTypographic;
            if (!_digitCell.TryGetValue(f, out var cell))
            {
                float max = 0;
                for (char c = '0'; c <= '9'; c++) max = Math.Max(max, g.MeasureString(c.ToString(), f, 1000, tf).Width);
                _digitCell[f] = cell = new SizeF(max, g.MeasureString(":", f, 1000, tf).Width);
            }
            float top = y + (h - f.GetHeight(g)) / 2;
            foreach (char c in text)
            {
                float cw = char.IsDigit(c) || c == '-' ? cell.Width : cell.Height + 2;
                float tw = g.MeasureString(c.ToString(), f, 1000, tf).Width;
                g.DrawString(c.ToString(), f, b, x + (cw - tw) / 2, top, tf);
                x += cw;
            }
        }

        void Progress(Graphics g, float x, float y, float w)
        {
            using (var p = Theme.Rounded(new RectangleF(x, y, w, 3), 1.5f)) g.FillPath(_track, p);
            float fw = w * (float)_snap.Progress;
            if (fw >= 3) using (var p = Theme.Rounded(new RectangleF(x, y, fw, 3), 1.5f)) g.FillPath(_estimated ? _warn : _acc, p);
        }

        void RowState(Entry en, out bool isCur, out bool past, out bool done, out bool clickable)
        {
            isCur = _snap.Current != null && en.Key == _snap.Current.Key;
            past = en.At <= _now && !isCur;
            done = _isDone(en);
            clickable = en.At <= _now;          // only a waktu that has started can be ticked
        }

        void Tick(Graphics g, float cx, float cy, float r, bool done)
        {
            if (done)
            {
                g.FillEllipse(_acc, cx - r, cy - r, 2 * r, 2 * r);
                if (_checkGlyph != null && r >= 8) g.DrawString(Type.GlyphCheck, _checkGlyph, _onAcc, new RectangleF(cx - r, cy - r, 2 * r, 2 * r), _center);
                else
                    using (var p = new Pen(Theme.OnAccent, r / 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(p, new[] { new PointF(cx - r * .45f, cy), new PointF(cx - r * .12f, cy + r * .35f), new PointF(cx + r * .48f, cy - r * .35f) });
            }
            else using (var p = new Pen(Theme.Stroke, 1f)) g.DrawEllipse(p, cx - r + .5f, cy - r + .5f, 2 * r - 1, 2 * r - 1);
        }

        // ------------------------------------------------------------ input
        PointF ToDesign(Point p) => new PointF((p.X - _ox) / _scale, (p.Y - _oy) / _scale);

        Entry HitEntry(Point p)
        {
            var d = ToDesign(p);
            foreach (var h in _hits) if (h.Key.Contains(d)) return h.Value;
            return null;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _mouseIn = true; ForceRepaint(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _mouseIn = false; _hoverEntry = null; _hoverBtn = null; ForceRepaint(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var d = ToDesign(e.Location);
            var entry = HitEntry(e.Location);
            object btn = _hideRect.Contains(d) ? "hide" : _locRect.Contains(d) ? "loc" : null;
            bool link = _statusRect.Contains(d);
            if (!_mouseIn) { _mouseIn = true; ForceRepaint(); }
            if (entry?.Key != _hoverEntry?.Key || !Equals(btn, _hoverBtn)) { _hoverEntry = entry; _hoverBtn = btn; ForceRepaint(); }
            Cursor = entry != null || btn != null || link ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { _menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            var d = ToDesign(e.Location);
            if (_hideRect.Contains(d)) { HideRequested?.Invoke(); return; }
            if (_locRect.Contains(d)) { if (!Locating) LocateRequested?.Invoke(); return; }
            if (_statusRect.Contains(d)) { UpdateClicked?.Invoke(); return; }
            var hit = HitEntry(e.Location);
            if (hit != null) { ToggleDone?.Invoke(hit); ForceRepaint(); return; }
            ReleaseCapture();
            SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero);  // drag to move
        }

        protected override void OnResizeEnd(EventArgs e)   // fires after a move or resize
        {
            base.OnResizeEnd(e);
            BoundsSaved?.Invoke(Bounds);
        }

        // Resize by dragging any edge or corner of the borderless window.
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x84 /*WM_NCHITTEST*/ || (int)m.Result != 1 /*HTCLIENT*/) return;
            long lp = m.LParam.ToInt64();
            var p = PointToClient(new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF))));
            int grip = S(6);
            bool l = p.X < grip, r = p.X >= ClientSize.Width - grip, t = p.Y < grip, b = p.Y >= ClientSize.Height - grip;
            int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 1;
            m.Result = (IntPtr)hit;
        }

        bool _exiting;
        public void ForceClose() { _exiting = true; Close(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 hides instead of quitting; use the menu's "Keluar" to exit.
            if (!_exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideRequested?.Invoke(); return; }
            base.OnFormClosing(e);
        }
    }

    /// Popup in the Windows notification anatomy: attribution row, title, body, up to two actions.
    sealed class Toast : BorderlessForm
    {
        static Toast _current;
        readonly string _title, _body;
        readonly Action _onDone;
        readonly DateTime _expires;          // Malaysia time; closes itself when its moment has passed
        readonly DateTime _autoCloseAt;      // DateTime.MaxValue = stays until acted on (reminders)
        bool _sticky, _closing;
        static readonly Font _caption = Type.Caption(), _strong = Type.BodyStrong(), _body14 = Type.Body(), _icon = Type.Icon(12);
        static readonly StringFormat _left = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        static readonly StringFormat _center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        readonly Timer _timer = new Timer { Interval = 15 };
        readonly System.Diagnostics.Stopwatch _anim = new System.Diagnostics.Stopwatch();
        RectangleF _doneBtn, _laterBtn, _closeBtn;
        object _hover;
        int _restX;

        /// onDone = null → info toast without actions. autoCloseSeconds = 0 → stays until acted on or expired.
        /// sticky = an info toast other info toasts must not replace. Returns false if not shown.
        public static bool Show(string title, string body, Action onDone, int autoCloseSeconds, DateTime expires, bool sticky = false)
        {
            if (onDone == null && !sticky && _current != null && (_current._onDone != null || _current._sticky)) return false;
            _current?.Close();
            _current = new Toast(title, body, onDone, autoCloseSeconds, expires) { _sticky = sticky };
            _current.Show();
            return true;
        }

        public static void CloseCurrent() => _current?.Close();

        /// Re-colour an open toast after a theme change.
        public static void Restyle() { if (_current != null) { _current.BackColor = Theme.Bg; _current.Invalidate(); } }

        Toast(string title, string body, Action onDone, int autoCloseSeconds, DateTime expires)
        {
            _title = title; _body = body; _onDone = onDone; _expires = expires;
            _autoCloseAt = autoCloseSeconds > 0 ? DateTime.UtcNow.AddSeconds(autoCloseSeconds) : DateTime.MaxValue;
            ClientSize = new Size(S(360), S(onDone != null ? 140 : 96));
            var wa = Screen.PrimaryScreen.WorkingArea;
            _restX = wa.Right - Width - S(12);
            Location = new Point(_restX, wa.Bottom - Height - S(12));
            _timer.Tick += (s, e) => Step();
        }

        protected override bool ShowWithoutActivation => true;   // never steals focus from your work

        // ---- motion: in 250 ms decelerate (slide 16px + fade), out 167 ms accelerate; none if Windows animations are off
        static bool Animations() { try { return !SystemParametersInfo(0x1042 /*SPI_GETCLIENTAREAANIMATION*/, 0, out bool on, 0) || on; } catch { return true; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Animations()) { Opacity = 0; Left = _restX + S(16); _anim.Restart(); }
            _timer.Start();
        }

        void Step()
        {
            double t = _anim.IsRunning ? _anim.Elapsed.TotalMilliseconds : double.MaxValue;
            if (_closing)
            {
                double k = Math.Min(1, t / 167);                 // ease-in (accelerate)
                Opacity = 1 - k * k * k;
                Left = _restX + (int)(S(16) * k * k * k);
                if (k >= 1 || !_anim.IsRunning) Close();
                return;
            }
            if (t < 250)
            {
                double k = t / 250, ease = 1 - Math.Pow(1 - k, 3);   // ease-out (decelerate)
                Opacity = ease;
                Left = _restX + (int)(S(16) * (1 - ease));
            }
            else if (Opacity < 1) { Opacity = 1; Left = _restX; }
            else if (_timer.Interval != 1000) _timer.Interval = 1000;  // idle: check expiry once a second

            if (DateTime.UtcNow >= _autoCloseAt || Clock.Now >= _expires) Dismiss();
        }

        void Dismiss()
        {
            if (_closing) return;
            _closing = true;
            if (!Animations()) { Close(); return; }
            _timer.Interval = 15;
            _anim.Restart();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(K, K);                       // draw in design px
            const float pad = 16, w = 360 - 2 * pad;

            using (var fg = new SolidBrush(Theme.Fg)) using (var dim = new SolidBrush(Theme.Dim)) using (var hov = new SolidBrush(Theme.Hover))
            {
                // attribution row: icon · app name · close
                using (var bmp = Theme.Crescent(32, Theme.Accent)) g.DrawImage(bmp, pad, 16, 16, 16);
                g.DrawString("Waktu Solat", _caption, dim, new RectangleF(pad + 24, 16, 200, 16), _left);
                _closeBtn = new RectangleF(360 - 12 - 28, 10, 28, 28);
                if (Equals(_hover, "close")) using (var p = Theme.Rounded(_closeBtn, 4)) g.FillPath(hov, p);
                if (_icon != null) g.DrawString(Type.GlyphClose, _icon, dim, _closeBtn, _center);
                else using (var p = new Pen(dim, 1.2f)) { var c = _closeBtn; g.DrawLine(p, c.X + 10, c.Y + 10, c.Right - 10, c.Bottom - 10); g.DrawLine(p, c.Right - 10, c.Y + 10, c.X + 10, c.Bottom - 10); }

                g.DrawString(_title, _strong, fg, new RectangleF(pad, 40, w, 20), _left);
                g.DrawString(_body, _body14, dim, new RectangleF(pad, 60, w, 20), _left);
                if (_onDone == null) return;

                float bw = (w - 8) / 2;
                _doneBtn = new RectangleF(pad, 96, bw, 32);
                _laterBtn = new RectangleF(pad + bw + 8, 96, bw, 32);
                using (var p = Theme.Rounded(_doneBtn, 4))
                using (var b = new SolidBrush(Equals(_hover, "done") ? ControlPaint.Light(Theme.Accent, 0.1f) : Theme.Accent)) g.FillPath(b, p);
                using (var t = new SolidBrush(Theme.OnAccent)) g.DrawString("Dah solat", _body14, t, _doneBtn, _center);
                using (var p = Theme.Rounded(_laterBtn, 4))
                {
                    using (var b = new SolidBrush(Equals(_hover, "later") ? Theme.Hover : Theme.ButtonFill)) g.FillPath(b, p);
                    using (var pen = new Pen(Theme.ButtonStroke, 1f)) g.DrawPath(pen, p);
                }
                g.DrawString("Nanti", _body14, fg, _laterBtn, _center);
            }
        }

        PointF D(Point p) => new PointF(p.X / K, p.Y / K);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var d = D(e.Location);
            object h = _closeBtn.Contains(d) ? "close" : _onDone != null && _doneBtn.Contains(d) ? "done" : _onDone != null && _laterBtn.Contains(d) ? "later" : null;
            if (!Equals(h, _hover)) { _hover = h; Invalidate(); }
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hover != null) { _hover = null; Invalidate(); } }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var d = D(e.Location);
            if (_onDone != null && _doneBtn.Contains(d)) _onDone();
            // reminders close only via a button or ✕; info toasts close on any click
            if (_onDone == null || _doneBtn.Contains(d) || _laterBtn.Contains(d) || _closeBtn.Contains(d)) Dismiss();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Dispose();
            if (_current == this) _current = null;
            base.OnFormClosed(e);
        }

        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);
    }
}
