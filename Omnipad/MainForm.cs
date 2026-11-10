using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Omnipad {
    // Omnific brand palette: blue leads, gold marks the one thing that matters.
    static class Theme {
        public static readonly Color Bg = ColorTranslator.FromHtml("#0E1014");       // --dark
        public static readonly Color Surface = ColorTranslator.FromHtml("#1A1D23");  // --dark-2
        public static readonly Color Surface2 = ColorTranslator.FromHtml("#242832");
        public static readonly Color Border = ColorTranslator.FromHtml("#2E333D");
        public static readonly Color Text = ColorTranslator.FromHtml("#E9EBEE");     // --on-dark
        public static readonly Color Muted = ColorTranslator.FromHtml("#B7BDC6");    // --on-dark-muted
        public static readonly Color Accent = ColorTranslator.FromHtml("#2D50B9");   // --blue
        public static readonly Color AccentHover = ColorTranslator.FromHtml("#3A5FCC");
        public static readonly Color Gold = ColorTranslator.FromHtml("#EAC42A");     // --gold
        public static readonly Color Good = ColorTranslator.FromHtml("#3CC27F");     // --success, lifted for dark
        public static readonly Color Warn = ColorTranslator.FromHtml("#EAC42A");
        public static readonly Color Bad = ColorTranslator.FromHtml("#FF6B6B");
        public static readonly Color NewRow = ColorTranslator.FromHtml("#1C3A2C");

        static string Head => FontInstalled("Montserrat") ? "Montserrat" : "Segoe UI";
        static bool FontInstalled(string name) {
            using (var f = new Font(name, 10f)) return f.Name == name;
        }

        public static readonly Font Body = new Font("Segoe UI", 9.75f);
        public static readonly Font Small = new Font("Segoe UI", 8.75f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 10f);
        public static readonly Font Title = new Font(Head, 15f, FontStyle.Bold);
        public static readonly Font CardTitle = new Font(Head, 11.5f, FontStyle.Bold);
        public static readonly Font Mono = new Font("Consolas", 9.5f);

        // Windows' built-in icon font (Segoe Fluent Icons on 11, Segoe MDL2 Assets on 10).
        public static readonly string IconFontName = FontInstalled("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        public const string GlyphGame = "\uE7FC", GlyphUsb = "\uE88E", GlyphSettings = "\uE713", GlyphLog = "\uE8A5", GlyphBluetooth = "\uE702", GlyphCable = "\uE88E";

        public static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF r, float radius) {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Small rounded "chip" with text, drawn directly onto a surface.
        public static int Chip(Graphics g, string text, int x, int y, Color fg, Color bg) {
            Size sz = TextRenderer.MeasureText(g, text, Small, Size.Empty, TextFormatFlags.NoPadding);
            var r = new Rectangle(x, y, sz.Width + 16, 22);
            using (var path = Rounded(r, 11)) using (var b = new SolidBrush(bg)) g.FillPath(b, path);
            TextRenderer.DrawText(g, text, Small, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return r.Right + 6;
        }

        public static Button Button(string text, EventHandler onClick, bool primary = false) {
            var b = new Button {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Surface2,
                ForeColor = primary ? Color.White : Text,
                Font = Body,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 5, 12, 5),
                Margin = new Padding(0, 0, 8, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Surface2;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Border;
            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static Label Label(string text, Font font = null, Color? color = null) {
            return new Label { Text = text, Font = font ?? Body, ForeColor = color ?? Text, AutoSize = true, BackColor = Color.Transparent };
        }

        public static CheckBox Check(string text, bool value) {
            return new CheckBox { Text = text, Checked = value, Font = Body, ForeColor = Text, AutoSize = true, Margin = new Padding(0, 4, 0, 4), Cursor = Cursors.Hand };
        }
    }

    // A small coloured pill used in the header for driver status.
    class Pill : Label {
        public Color Dot = Theme.Muted;
        public Pill() {
            AutoSize = false; Height = 26; Font = Theme.Small; ForeColor = Theme.Text; BackColor = Theme.Surface2;
            TextAlign = ContentAlignment.MiddleLeft; Padding = new Padding(22, 0, 10, 0); Margin = new Padding(6, 0, 0, 0);
        }
        public void Set(string text, Color dot) {
            Text = text; Dot = dot;
            Width = TextRenderer.MeasureText(text, Font).Width + 34;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Dot)) e.Graphics.FillEllipse(b, 9, Height / 2 - 4, 8, 8);
        }
    }

    // One card per connected controller on the Controllers page. Everything except the buttons is painted.
    class ControllerCard : Panel {
        public readonly Joycon Jc;
        readonly Button joinBtn;
        readonly FlowLayoutPanel buttons;
        string title = "", status = "";
        Color statusColor = Theme.Muted;

        public ControllerCard(Joycon jc, MainForm form) {
            Jc = jc;
            DoubleBuffered = true;
            Size = new Size(380, 158);
            BackColor = Theme.Bg;
            Margin = new Padding(0, 0, 16, 16);

            buttons = new FlowLayoutPanel { Location = new Point(116, 112), AutoSize = true, BackColor = Theme.Surface, WrapContents = false };
            buttons.Controls.Add(Theme.Button("Test buttons", (s, e) => form.OpenTester(Jc), true));
            buttons.Controls.Add(Theme.Button("Locate", (s, e) => Jc.SetRumble(20.0f, 400.0f, 1.0f, 300)));
            if (!jc.isUSB) buttons.Controls.Add(Theme.Button("Turn off", (s, e) => Jc.PowerOff()));
            Cursor = Cursors.Hand;
            Click += (s, e) => form.OpenTester(Jc);
            if (!jc.isPro) {
                joinBtn = Theme.Button("Pair", (s, e) => form.JoinOrSplit(Jc));
                buttons.Controls.Add(joinBtn);
            }
            Controls.Add(buttons);
            UpdateInfo();
        }

        public void UpdateInfo() {
            title = Jc.isPro ? "Pro Controller" : Jc.isLeft ? "Left Joy-Con" : "Right Joy-Con";
            if (Jc.other != null) title += " + partner";

            switch (Jc.state) {
                case Joycon.state_.IMU_DATA_OK:
                    var pad = Jc.xin ?? Jc.other?.xin;
                    status = pad != null ? "Active as Xbox controller" + (pad.LastSlot >= 0 ? " · Player " + (pad.LastSlot + 1) : "") + "  (\"" + DeviceNames.XboxName + "\")"
                           : Jc.other != null ? "Active as Xbox controller" : "Connected (Xbox output off)";
                    statusColor = Theme.Good; break;
                case Joycon.state_.DROPPED:
                    status = "Connection lost"; statusColor = Theme.Bad; break;
                default:
                    status = "Connecting…"; statusColor = Theme.Warn; break;
            }
            if (joinBtn != null) joinBtn.Text = Jc.other == null ? "Pair with other Joy-Con" : "Split";
            FitWidth();
            Invalidate();
        }

        // The card grows to fit its buttons (they used to be cut off, e.g. "Turn off").
        void FitWidth() {
            int w = Math.Max(380, buttons.Left + buttons.GetPreferredSize(Size.Empty).Width + 10);
            w = Math.Max(w, 132 + TextRenderer.MeasureText(status, Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width + 18);
            if (Width != w) Width = w;
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Rounded(r, 14)) {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }

            ControllerArt.DrawTile(g, new Rectangle(18, 18, 84, 84), ControllerArt.KindOf(Jc));

            TextRenderer.DrawText(g, title, Theme.CardTitle, new Point(114, 16), Theme.Text, TextFormatFlags.NoPadding);

            int x = 116;
            x = Theme.Chip(g, Jc.isUSB ? "USB cable" : "Bluetooth", x, 46, Theme.Text, Theme.Surface2);
            if (Jc.state == Joycon.state_.IMU_DATA_OK) {
                bool low = MainForm.BatteryLow(Jc);
                Color c = low ? Theme.Bad : Theme.Text;
                string text = MainForm.BatteryText(Jc);
                Size sz = TextRenderer.MeasureText(g, text, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
                var r2 = new Rectangle(x, 46, sz.Width + 44, 22);
                using (var path = Theme.Rounded(r2, 11)) using (var b = new SolidBrush(Theme.Surface2)) g.FillPath(b, path);
                ControllerArt.Battery(g, x + 10, 52, Jc.batteryLevel, Jc.charging, c);
                TextRenderer.DrawText(g, text, Theme.Small, new Rectangle(x + 34, 46, sz.Width + 4, 22), c, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            using (var b = new SolidBrush(statusColor)) g.FillEllipse(b, 117, 82, 9, 9);
            TextRenderer.DrawText(g, status, Theme.Body, new Point(132, 78), Theme.Text, TextFormatFlags.NoPadding);
        }
    }

    // Shown when no controller is connected.
    class EmptyState : Control {
        public EmptyState() { DoubleBuffered = true; Size = new Size(620, 170); BackColor = Theme.Bg; }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            ControllerArt.DrawTile(g, new Rectangle(0, 10, 140, 140), ControllerArt.Kind.Pro, 22);
            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
            TextRenderer.DrawText(g, "No controllers yet", Theme.CardTitle, new Point(166, 26), Theme.Text, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Plug a Pro Controller in with a USB cable, or connect it over Bluetooth: hold the small sync button on top of the controller, then pair it in Windows Bluetooth settings.",
                Theme.Body, new Rectangle(166, 56, Width - 170, 80), Theme.Muted, f);
            TextRenderer.DrawText(g, "Joy-Cons work too - pair both, then join them into one controller.", Theme.Small, new Rectangle(166, 116, Width - 170, 30), Theme.Muted, f);
        }
    }

    // Sidebar item: icon, label, optional badge, gold marker when selected.
    class NavItem : Control {
        public string Glyph, Badge;
        public bool Selected;
        bool hover;

        public NavItem(string text, string glyph) {
            Text = text; Glyph = glyph;
            Size = new Size(176, 40);
            Margin = new Padding(0, 0, 0, 4);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            BackColor = Theme.Surface;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (Selected || hover)
                using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 8))
                using (var b = new SolidBrush(Selected ? Theme.Surface2 : Color.FromArgb(30, 34, 42))) g.FillPath(b, path);
            if (Selected)
                using (var b = new SolidBrush(Theme.Gold)) g.FillRectangle(b, 0, 10, 3, Height - 20);

            Color fg = Selected ? Color.White : Theme.Muted;
            using (var f = new Font(Theme.IconFontName, 12f))
                TextRenderer.DrawText(g, Glyph, f, new Rectangle(12, 0, 24, Height), Selected ? Theme.Gold : fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Selected ? Theme.Bold : Theme.Body, new Rectangle(44, 0, Width - 44, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (!string.IsNullOrEmpty(Badge)) {
                Size sz = TextRenderer.MeasureText(g, Badge, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
                var br = new Rectangle(Width - sz.Width - 24, (Height - 20) / 2, sz.Width + 14, 20);
                using (var path = Theme.Rounded(br, 10)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
                TextRenderer.DrawText(g, Badge, Theme.Small, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // Logo + "Omnipad" wordmark ("pad" in Omnific gold) + tagline.
    class Brand : Control {
        static Image logo;
        public Brand() {
            DoubleBuffered = true; Size = new Size(460, 64); BackColor = Theme.Surface;
            if (logo == null)
                try {
                    using (var st = typeof(Brand).Assembly.GetManifestResourceStream("Omnipad.logo.png")) logo = Image.FromStream(st);
                } catch { }
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            if (logo != null) g.DrawImage(logo, 0, 12, 40, 40);
            const TextFormatFlags f = TextFormatFlags.NoPadding;
            Size omni = TextRenderer.MeasureText(g, "Omni", Theme.Title, Size.Empty, f);
            TextRenderer.DrawText(g, "Omni", Theme.Title, new Point(52, 9), Theme.Text, f);
            TextRenderer.DrawText(g, "pad", Theme.Title, new Point(52 + omni.Width, 9), Theme.Gold, f);
            TextRenderer.DrawText(g, "by Omnific  ·  Switch controllers as Xbox controllers", Theme.Small, new Point(53, 38), Theme.Muted, f);
        }
    }

    public partial class MainForm : Form {
        readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        readonly Dictionary<string, NavItem> navButtons = new Dictionary<string, NavItem>();
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(24, 18, 24, 18) };

        // Controllers page
        FlowLayoutPanel cards;
        Control emptyState;
        FlowLayoutPanel hiddenWarning;
        Label hiddenDetail;
        Button hiddenFixBtn;

        // Devices page
        readonly DeviceMonitor monitor = new DeviceMonitor();
        DataGridView grid;
        TextBox search;
        CheckBox controllersOnly;
        RichTextBox activity;

        // Log page
        RichTextBox console;

        // Header
        Pill vigemPill, hidhidePill, updatePill;

        NotifyIcon notifyIcon;
        Icon appIcon;
        bool exiting, trayHintShown;
        readonly HashSet<Joycon> lowBatteryWarned = new HashSet<Joycon>();
        readonly HashSet<string> announcedKeys = new HashSet<string>();
        readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        CheckBox hidHideCheck, autostartCheck;
        Label hidHideNote;
        bool syncing;

        public MainForm() {
            Config.Init();
            Text = "Omnipad";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1000, 660);
            MinimumSize = new Size(820, 520);
            try {
                using (var st = typeof(MainForm).Assembly.GetManifestResourceStream("Omnipad.app.ico")) appIcon = new Icon(st);
                Icon = appIcon;
            } catch { }
            if (Program.StartMinimized) { Opacity = 0; ShowInTaskbar = false; } // start hidden: no flash of the window

            Controls.Add(content);
            Controls.Add(BuildNav());
            Controls.Add(BuildHeader());

            AddPage("Controllers", BuildControllersPage());
            AddPage("USB Devices", BuildDevicesPage());
            AddPage("Buttons", BuildButtonsPage());
            AddPage("Settings", BuildSettingsPage());
            AddPage("Log", BuildLogPage());
            ShowPage("Controllers");

            // Omnipad lives in the tray: the icon is always there, closing the window only hides it.
            Icon trayIcon = Icon;
            try { using (var st = typeof(MainForm).Assembly.GetManifestResourceStream("Omnipad.app.ico")) trayIcon = new Icon(st, SystemInformation.SmallIconSize); } catch { }
            notifyIcon = new NotifyIcon { Icon = trayIcon, Text = "Omnipad", Visible = true };
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body, Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()) };
            menu.Opening += (s, e) => BuildTrayMenu(menu);
            BuildTrayMenu(menu);
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };

            monitor.Added += d => OnUi(() => DeviceEvent(d, true));
            monitor.Removed += d => OnUi(() => DeviceEvent(d, false));
            monitor.Refreshed += () => OnUi(FillGrid);
            HidHide.Changed += () => OnUi(UpdateHeader);
            Updater.Found += () => OnUi(() => {
                updatePill.Set("Update to v" + Updater.Latest + "  →", Theme.Gold);
                updatePill.Visible = true;
                notifyIcon.ShowBalloonTip(5000, "Omnipad " + Updater.Latest + " is ready",
                    Updater.AutoInstall ? "It installs itself when your controllers aren't in use. Click to update now." : "Click to update now - it only takes a few seconds.", ToolTipIcon.Info);
                if (updateNowBtn != null) { updateNowBtn.Visible = true; updateNowBtn.Text = "Update to v" + Updater.Latest + " now"; }
            });
            Updater.Status += text => OnUi(() => { updatePill.Set(text, Theme.Gold); updatePill.Visible = true; });

            int ticks = 0;
            uiTimer.Tick += (s, e) => {
                RefreshCards();
                if (++ticks % 10 == 0) FillGrid(); // FillGrid here only fades the "new" highlight
                if (ticks % 30 == 0) {
                    // Automatic update only while no controller has been used for 10 minutes, so it never cuts out mid-game.
                    bool inUse = (Program.mgr?.j?.ToList() ?? new List<Joycon>()).Any(j => j.state == Joycon.state_.IMU_DATA_OK && (DateTime.Now - j.lastActivity).TotalMinutes < 10);
                    Updater.MaybeAutoInstall(inUse);
                }
            };
        }

        // ---------- layout ----------

        Control BuildHeader() {
            var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Theme.Surface, Padding = new Padding(20, 0, 18, 0) };
            var brand = new Brand { Location = new Point(20, 1) };
            var pills = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 20, 0, 0) };
            vigemPill = new Pill(); hidhidePill = new Pill();
            updatePill = new Pill { Visible = false, Cursor = Cursors.Hand };
            updatePill.Click += (s, e) => OpenUpdate();
            pills.Controls.Add(updatePill);
            hidhidePill.Cursor = Cursors.Hand;
            hidhidePill.Click += (s, e) => { if (HidHide.Installed && !Program.IsAdmin) Program.RestartAsAdmin(); else ShowPage("Settings"); };
            pills.Controls.Add(vigemPill); pills.Controls.Add(hidhidePill);
            header.Controls.AddRange(new Control[] { brand, pills });
            header.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
            return header;
        }

        Control BuildNav() {
            var nav = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 200, BackColor = Theme.Surface, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12, 18, 12, 12), WrapContents = false };
            var items = new[] { ("Controllers", Theme.GlyphGame), ("Buttons", "\uE765"), ("USB Devices", Theme.GlyphUsb), ("Settings", Theme.GlyphSettings), ("Log", Theme.GlyphLog) };
            foreach (var it in items) {
                var b = new NavItem(it.Item1, it.Item2);
                string n = it.Item1;
                b.Click += (s, e) => ShowPage(n);
                navButtons[n] = b;
                nav.Controls.Add(b);
            }
            nav.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, nav.Width - 1, 0, nav.Width - 1, nav.Height); };
            return nav;
        }

        void AddPage(string name, Panel page) {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            pages[name] = page;
            content.Controls.Add(page);
        }

        void ShowPage(string name) {
            foreach (var kv in pages) kv.Value.Visible = kv.Key == name;
            foreach (var kv in navButtons) {
                kv.Value.Selected = kv.Key == name;
                kv.Value.Invalidate();
            }
            if (name == "USB Devices") { monitor.RefreshAsync(); FillGrid(); }
        }

        static Panel PageHeader(Panel page, string title, string subtitle) {
            var head = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Color.Transparent };
            var t = Theme.Label(title, Theme.Title); t.Location = new Point(0, 0);
            var s = Theme.Label(subtitle, Theme.Body, Theme.Muted); s.Location = new Point(2, 32);
            head.Controls.Add(t); head.Controls.Add(s);
            page.Controls.Add(head);
            return head;
        }

        Panel BuildControllersPage() {
            var page = new Panel { BackColor = Theme.Bg };
            cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(0, 6, 0, 0) };
            emptyState = new EmptyState { Margin = new Padding(0, 10, 0, 0) };
            cards.Controls.Add(emptyState);

            hiddenWarning = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, BackColor = ColorTranslator.FromHtml("#3A3214"), Padding = new Padding(14), Margin = new Padding(0, 20, 0, 0), Visible = false };
            hiddenWarning.Controls.Add(Theme.Label("Windows sees a Nintendo controller, but Omnipad can't open it.", Theme.Bold, Theme.Warn));
            hiddenDetail = Theme.Label("", Theme.Body);
            hiddenWarning.Controls.Add(hiddenDetail);
            hiddenFixBtn = Theme.Button("", (s, e) => FixHidden(), true);
            hiddenFixBtn.Margin = new Padding(0, 10, 0, 0);
            hiddenWarning.Controls.Add(hiddenFixBtn);
            cards.Controls.Add(hiddenWarning);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, BackColor = Theme.Bg, Padding = new Padding(0, 6, 0, 0) };
            bar.Controls.Add(Theme.Button("Scan for controllers now", (s, e) => ScanSoon(), true));
            bar.Controls.Add(Theme.Button("Open USB Devices", (s, e) => ShowPage("USB Devices")));

            page.Controls.Add(cards);
            page.Controls.Add(bar);
            PageHeader(page, "Controllers", "Each physical controller shows up once, whether it's on USB or Bluetooth.");
            return page;
        }

        Panel BuildDevicesPage() {
            var page = new Panel { BackColor = Theme.Bg };

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Bg, WrapContents = false };
            search = new TextBox { Width = 220, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body, Margin = new Padding(0, 3, 10, 0) };
            var searchHint = Theme.Label("Search:", Theme.Body, Theme.Muted); searchHint.Margin = new Padding(0, 6, 6, 0);
            search.TextChanged += (s, e) => FillGrid();
            controllersOnly = Theme.Check("Controllers only", false);
            controllersOnly.Margin = new Padding(0, 5, 12, 0);
            controllersOnly.CheckedChanged += (s, e) => FillGrid();
            toolbar.Controls.AddRange(new Control[] {
                searchHint, search, controllersOnly,
                Theme.Button("Refresh", (s, e) => monitor.RefreshAsync()),
                Theme.Button("Un-hide all Nintendo devices", (s, e) => FixHidden()),
            });

            grid = new DataGridView {
                Dock = DockStyle.Fill, BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None, GridColor = Theme.Border,
                RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 32,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            grid.RowTemplate.Height = 28;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Surface2, ForeColor = Theme.Muted, Font = Theme.Small, SelectionBackColor = Theme.Surface2, Padding = new Padding(6, 0, 0, 0) };
            grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body, SelectionBackColor = Theme.Accent, SelectionForeColor = Color.White, Padding = new Padding(6, 0, 0, 0) };
            foreach (var c in new[] { ("When", 70), ("Name", 230), ("Connection", 85), ("VID:PID", 85), ("Type", 90), ("Omnipad", 80), ("Device ID", 300) }) {
                var col = new DataGridViewTextBoxColumn { HeaderText = c.Item1, FillWeight = c.Item2, SortMode = DataGridViewColumnSortMode.NotSortable };
                grid.Columns.Add(col);
            }
            var rowMenu = new ContextMenuStrip();
            rowMenu.Items.Add("Use as controller", null, (s, e) => UseSelectedAsController());
            rowMenu.Items.Add("Copy device ID", null, (s, e) => { var id = SelectedDeviceId(); if (id != null) Clipboard.SetText(id); });
            rowMenu.Items.Add("Hide from Steam/games (HidHide)", null, (s, e) => HideSelected(true));
            rowMenu.Items.Add("Un-hide (HidHide)", null, (s, e) => HideSelected(false));
            grid.ContextMenuStrip = rowMenu;
            grid.CellMouseDown += (s, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) grid.Rows[e.RowIndex].Selected = true; };

            var activityHead = Theme.Label("Plug-in / unplug activity", Theme.Bold); activityHead.Dock = DockStyle.Top; activityHead.Padding = new Padding(0, 10, 0, 6);
            activity = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Mono };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 170, BackColor = Theme.Bg };
            bottom.Controls.Add(activity); bottom.Controls.Add(activityHead);

            page.Controls.Add(grid);
            page.Controls.Add(bottom);
            page.Controls.Add(toolbar);
            PageHeader(page, "USB Devices", "Everything on USB, Bluetooth and HID. New devices are highlighted green for a minute. Right-click a row for tools.");
            return page;
        }

        // Every button on the controller, each mappable to another Xbox button, nothing, or an action.
        Panel BuildButtonsPage() {
            var page = new Panel { BackColor = Theme.Bg };
            var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg };
            body.Controls.Add(Theme.Label(Playnite.Installed ? "Playnite found - pick \"Playnite: open Fullscreen mode\" for any button (Home is a good one)."
                : "Playnite not found. Its actions start working once it's installed (or open, if it's a portable copy).", Theme.Small, Playnite.Installed ? Theme.Good : Theme.Muted));
            body.Controls.Add(Theme.Label("\"Default\" is the normal layout (and follows the Nintendo / Xbox labels switch in Settings). Changes apply straight away. Pro Controller and Joy-Con pairs.", Theme.Small, Theme.Muted));
            var rows = new List<Action>();
            foreach (var pad in Mapping.Pads) {
                var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
                var l = Theme.Label(pad.label); l.AutoSize = false; l.Width = 150; l.Height = 26; l.TextAlign = ContentAlignment.MiddleLeft;
                var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface2, ForeColor = Theme.Text, Font = Theme.Body, Width = 270, MaxDropDownItems = 20 };
                foreach (var t in Mapping.Targets) box.Items.Add(t.text);
                var arg = new TextBox { Width = 220, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body, Margin = new Padding(8, 1, 0, 0) };
                var browse = Theme.Button("Browse…", null); browse.Margin = new Padding(6, 0, 0, 0);
                string id = pad.id;
                void Load() {
                    box.SelectedIndex = Math.Max(0, Array.FindIndex(Mapping.Targets, t => t.value == Mapping.Get(id)));
                    arg.Text = Mapping.Arg(id);
                    string v = Mapping.Get(id);
                    arg.Visible = v == "keys" || v == "run"; browse.Visible = v == "run";
                }
                Load(); rows.Add(Load);
                box.SelectedIndexChanged += (s, e) => {
                    string v = Mapping.Targets[box.SelectedIndex].value;
                    if (v == Mapping.Get(id)) return;
                    Mapping.Set(id, v, arg.Text.Trim());
                    arg.Visible = v == "keys" || v == "run"; browse.Visible = v == "run";
                    if (v == "keys" && arg.Text.Length == 0) arg.Text = "Win+P";
                };
                arg.Leave += (s, e) => Mapping.Set(id, Mapping.Get(id), arg.Text.Trim());
                browse.Click += (s, e) => {
                    using (var d = new OpenFileDialog { Title = "Pick what to run", Filter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|All files|*.*" })
                        if (d.ShowDialog(this) == DialogResult.OK) { arg.Text = d.FileName; Mapping.Set(id, Mapping.Get(id), d.FileName); }
                };
                row.Controls.Add(l); row.Controls.Add(box); row.Controls.Add(arg); row.Controls.Add(browse);
                body.Controls.Add(row);
            }
            body.Controls.Add(Theme.Label("Keys: e.g. Win+P, Ctrl+Alt+T, Space, Esc - held down while you hold the button.  Home on Default = Xbox Guide plus the \"Hold Home\" action in Settings; Capture on Default = the Capture action and hold shortcut in Settings.", Theme.Small, Theme.Muted));
            var reset = Theme.Button("Reset all to default", (s, e) => {
                foreach (var pad in Mapping.Pads) Mapping.Set(pad.id, "default", "");
                foreach (var r in rows) r();
            });
            reset.Margin = new Padding(0, 10, 0, 0);
            body.Controls.Add(reset);
            page.Controls.Add(body);
            PageHeader(page, "Buttons", "Map every button: to another Xbox button, to nothing, or to an action like opening Playnite.");
            return page;
        }

        Panel BuildSettingsPage() {
            var page = new Panel { BackColor = Theme.Bg };
            var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg };

            var app = ConfigurationManager.AppSettings;
            bool B(string k, bool d) { bool v; return bool.TryParse(app[k], out v) ? v : d; }

            var hidHide = hidHideCheck = Theme.Check("Hide the real controller from Steam and games (HidHide) - applies now", false);
            hidHide.CheckedChanged += (s, e) => {
                if (syncing) return;
                SaveSetting("UseHidHide", hidHide.Checked.ToString().ToLower());
                bool on = hidHide.Checked;
                ThreadPool.QueueUserWorkItem(_ => HidHide.SetEnabled(on));
            };

            var scan = Theme.Check("Automatically look for new controllers", Config.Value("ProgressiveScan"));
            scan.CheckedChanged += (s, e) => Config.Save("ProgressiveScan", scan.Checked);


            var rumble = Theme.Check("Rumble  *", B("EnableRumble", true));
            rumble.CheckedChanged += (s, e) => SaveSetting("EnableRumble", rumble.Checked.ToString().ToLower());

            var graceRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, Margin = new Padding(0, 8, 0, 4), WrapContents = false };
            var graceLbl = Theme.Label("Keep the Xbox controller alive after a disconnect for (seconds):  *"); graceLbl.Margin = new Padding(0, 5, 8, 0);
            int grace; int.TryParse(app["ReconnectGraceSeconds"], out grace);
            var graceNum = new NumericUpDown { Minimum = 0, Maximum = 600, Value = grace > 0 ? grace : 30, Width = 70, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            graceNum.ValueChanged += (s, e) => SaveSetting("ReconnectGraceSeconds", ((int)graceNum.Value).ToString());
            graceRow.Controls.Add(graceLbl); graceRow.Controls.Add(graceNum);

            body.Controls.Add(SectionLabel("Steam"));
            body.Controls.Add(hidHide);
            hidHideNote = Theme.Label("", Theme.Small, Theme.Muted);
            body.Controls.Add(hidHideNote);
            var autostart = autostartCheck = Theme.Check("Start Omnipad when I sign in to Windows (runs in the tray)", Autostart.Enabled);
            autostart.CheckedChanged += (s, e) => {
                if (syncing) return;
                if (autostart.Checked) AppendTextBox(Autostart.Enable() + "\r\n"); else Autostart.Disable();
            };
            var minimized = Theme.Check("Start hidden in the tray even when I open it myself", B("StartMinimized", false));
            minimized.CheckedChanged += (s, e) => SaveSetting("StartMinimized", minimized.Checked.ToString().ToLower());

            body.Controls.Add(SectionLabel("Startup"));
            body.Controls.Add(autostart);
            body.Controls.Add(minimized);
            body.Controls.Add(SectionLabel("Buttons"));
            layoutChoice = (ComboBox)((FlowLayoutPanel)ChoiceRow("Button layout:", "SwapButtons", Prefs.SwapButtons ? "true" : "false",
                new[] { ("false", "Nintendo positions (bottom button = Xbox A)"), ("true", "Xbox labels (press A, the game gets A)") },
                v => Prefs.SwapButtons = v == "true")).Controls[1];
            body.Controls.Add(layoutChoice.Parent);
            body.Controls.Add(Theme.Label("Applies straight away. Also in the tray menu, to flip back and forth.", Theme.Small, Theme.Muted));
            body.Controls.Add(ChoiceRow("Capture button:", "CaptureAction", Shortcuts.CaptureAction,
                new[] { ("steam", "Steam screenshot (F12, in games)"), ("windows", "Windows screenshot (Game Bar)"), ("clip", "Save last 30 seconds (Game Bar)"), ("none", "Do nothing") },
                v => Shortcuts.CaptureAction = v));
            body.Controls.Add(ChoiceRow("Hold Home:", "HomeHoldAction", Shortcuts.HomeHoldAction,
                new[] { ("playnite", "Open Playnite Fullscreen mode"), ("playnite-desktop", "Open Playnite library"), ("omnipad", "Open Omnipad"), ("bigpicture", "Open Steam Big Picture"), ("none", "Do nothing") },
                v => Shortcuts.HomeHoldAction = v));
            body.Controls.Add(Theme.Label("A quick tap of Home is still the Xbox Guide button.", Theme.Small, Theme.Muted));
            body.Controls.Add(SectionLabel("Hold-to-run shortcuts"));
            body.Controls.Add(ChoiceRow("Hold for:", "HoldSeconds", Shortcuts.HoldSeconds.ToString(),
                new[] { ("2", "2 seconds"), ("3", "3 seconds"), ("5", "5 seconds"), ("8", "8 seconds") },
                v => Shortcuts.HoldSeconds = int.Parse(v)));
            body.Controls.Add(HoldRow("Hold Capture:", "CaptureHold"));
            body.Controls.Add(HoldRow("Hold Minus (−):", "MinusHold"));
            body.Controls.Add(Theme.Label("The controller buzzes when the shortcut runs. A quick tap of Capture still does the Capture action above; Minus still works as Xbox Back.", Theme.Small, Theme.Muted));
            body.Controls.Add(SectionLabel("Battery & lights"));
            body.Controls.Add(ChoiceRow("Auto-sleep:", "AutoSleepMinutes", Prefs.AutoSleepMinutes.ToString(),
                new[] { ("5", "After 5 minutes without input"), ("10", "After 10 minutes"), ("15", "After 15 minutes"), ("30", "After 30 minutes"), ("0", "Never") },
                v => Prefs.AutoSleepMinutes = int.Parse(v)));
            body.Controls.Add(Theme.Label("Bluetooth only - a controller on the cable charges, so it stays on. Press any button to wake it.", Theme.Small, Theme.Muted));
            body.Controls.Add(ChoiceRow("Home button light:", "HomeLight", Prefs.HomeLight,
                new[] { ("bright", "Glow bright"), ("dim", "Glow dim"), ("off", "Off") },
                v => { Prefs.HomeLight = v; foreach (var jc in Program.mgr?.j?.ToList() ?? new List<Joycon>()) jc.ApplyHomeLight(); }));
            var playerLights = Theme.Check("Player lights show the Xbox controller number (1-4)", Prefs.PlayerLights);
            playerLights.CheckedChanged += (s, e) => { Prefs.PlayerLights = playerLights.Checked; SaveSetting("PlayerLights", playerLights.Checked.ToString().ToLower()); };
            body.Controls.Add(playerLights);

            body.Controls.Add(SectionLabel("Gyro aiming"));
            body.Controls.Add(ChoiceRow("Aim with motion:", "GyroMode", Prefs.GyroMode,
                new[] { ("off", "Off"), ("zl", "While holding ZL (aim down sights)"), ("zr", "While holding ZR"), ("toggle", "Toggle: click both sticks"), ("always", "Always on") },
                v => Prefs.GyroMode = v));
            body.Controls.Add(ChoiceRow("Sensitivity:", "GyroSensitivity", Prefs.GyroSensitivity.ToString(),
                new[] { ("6", "Low"), ("12", "Medium"), ("20", "High"), ("32", "Very high") },
                v => Prefs.GyroSensitivity = int.Parse(v)));
            var invert = Theme.Check("Invert up/down", Prefs.GyroInvertY);
            invert.CheckedChanged += (s, e) => { Prefs.GyroInvertY = invert.Checked; SaveSetting("GyroInvertY", invert.Checked.ToString().ToLower()); };
            body.Controls.Add(invert);
            body.Controls.Add(Theme.Label("Moves the mouse, so it works in games that accept mouse and controller at the same time. Pro Controller, or the right Joy-Con of a pair.", Theme.Small, Theme.Muted));

            body.Controls.Add(SectionLabel("Name in emulators and game controller lists"));
            var nameRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
            var nameLbl = Theme.Label("Show it as:"); nameLbl.AutoSize = false; nameLbl.Width = 130; nameLbl.Height = 26; nameLbl.TextAlign = ContentAlignment.MiddleLeft;
            var nameBox = new TextBox { Width = 280, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body, Text = DeviceNames.XboxName };
            var nameSave = Theme.Button("Save name", (s, e) => {
                string err = DeviceNames.SetXboxName(nameBox.Text.Trim());
                AppendTextBox((err == null ? "Controller name set to \"" + DeviceNames.XboxName + "\". Restart the emulator to see it." : "Couldn't set the name: " + err) + "\r\n");
            });
            nameSave.Margin = new Padding(8, 0, 0, 0);
            nameRow.Controls.Add(nameLbl); nameRow.Controls.Add(nameBox); nameRow.Controls.Add(nameSave);
            body.Controls.Add(nameRow);
            body.Controls.Add(Theme.Label("Renames Omnipad's Xbox controller in Windows' Game Controllers panel and in emulators that list DirectInput devices.\n" +
                "Emulators that use XInput show \"XInput / Xbox Controller\" plus a number: pick the number shown as \"Player N\" on the controller's card (and its lights).\n" +
                "Seeing the Nintendo controller listed as well? Turn on \"Hide the real controller\" above so only the Xbox one is left.\n" +
                "For motion controls in emulators (Cemu, Yuzu-style, Dolphin...), use their DSU / Cemuhook option: server 127.0.0.1, port 26760.", Theme.Small, Theme.Muted));

            body.Controls.Add(SectionLabel("Controllers"));
            body.Controls.AddRange(new Control[] { scan, rumble, graceRow });
            body.Controls.Add(Theme.Label("*  Takes effect the next time Omnipad starts.", Theme.Small, Theme.Muted));

            body.Controls.Add(SectionLabel("Updates"));
            var autoUpd = Theme.Check("Install updates automatically (only when no controller has been used for 10 minutes)", Updater.AutoInstall);
            autoUpd.CheckedChanged += (s, e) => { Updater.AutoInstall = autoUpd.Checked; SaveSetting("AutoUpdate", autoUpd.Checked.ToString().ToLower()); };
            body.Controls.Add(autoUpd);
            updateNowBtn = Theme.Button(Updater.UpdateAvailable ? "Update to v" + Updater.Latest + " now" : "Update now", (s, e) => Updater.Install(), true);
            updateNowBtn.Visible = Updater.UpdateAvailable; updateNowBtn.Margin = new Padding(0, 6, 8, 4);
            var checkBtn = Theme.Button("Check for updates", (s, e) => Updater.CheckAndInstallNow());
            checkBtn.Margin = new Padding(0, 6, 0, 4);
            var updRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, WrapContents = false, Margin = new Padding(0) };
            updRow.Controls.Add(updateNowBtn); updRow.Controls.Add(checkBtn);
            body.Controls.Add(updRow);
            body.Controls.Add(Theme.Label("Checks GitHub and, if there's a newer version, installs it and reopens Omnipad - nothing to download by hand.", Theme.Small, Theme.Muted));

            body.Controls.Add(SectionLabel("About"));
            body.Controls.Add(Theme.Label("Omnipad v" + Updater.Current.ToString(3) + " by Omnific - Switch controllers as Xbox controllers, over USB or Bluetooth.", Theme.Body));
            body.Controls.Add(Theme.Label("Built on BetterJoy by Davidobot (MIT license) - github.com/Davidobot/BetterJoy", Theme.Small, Theme.Muted));
            body.Controls.Add(Theme.Label("Uses ViGEmBus and HidHide by Nefarius.", Theme.Small, Theme.Muted));

            page.Controls.Add(body);
            PageHeader(page, "Settings", "Saved automatically.");
            return page;
        }

        Button updateNowBtn;
        ComboBox layoutChoice;

        // Same as the HidHide switch in Settings.
        public void SetHiding(bool on) {
            SaveSetting("UseHidHide", on.ToString().ToLower());
            ThreadPool.QueueUserWorkItem(_ => HidHide.SetEnabled(on));
        }

        public void SetButtonLayout(bool xboxLabels) {
            Prefs.SwapButtons = xboxLabels;
            SaveSetting("SwapButtons", xboxLabels ? "true" : "false");
            AppendTextBox("Button layout: " + (xboxLabels ? "Xbox labels (A is A)" : "Nintendo positions") + "\r\n");
            OnUi(() => { if (layoutChoice != null) layoutChoice.SelectedIndex = xboxLabels ? 1 : 0; });
        }

        // A hold shortcut: what to do + (for "run" and "keys") what to run / which keys.
        Control HoldRow(string label, string key) {
            var options = new[] {
                ("none", "Do nothing"), ("toggle-display", "Switch between PC screen and TV"), ("tv", "TV / second screen only"),
                ("pc", "PC screen only"), ("extend", "Extend to both screens"), ("playnite-fullscreen", "Playnite: open Fullscreen mode"),
                ("playnite-desktop", "Playnite: open library"), ("close-game", "Close the current game (Alt+F4)"), ("bigpicture", "Open Steam Big Picture"),
                ("omnipad", "Open Omnipad"), ("run", "Run a program, shortcut or website…"), ("keys", "Press keys…") };
            var wrap = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            var targetRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, WrapContents = false, Margin = new Padding(130, 0, 0, 6) };
            var target = new TextBox { Width = 280, BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body, Text = Shortcuts.Target(key) };
            var browse = Theme.Button("Browse…", null); browse.Margin = new Padding(8, 0, 0, 0);
            var hint = Theme.Label("", Theme.Small, Theme.Muted); hint.Margin = new Padding(8, 5, 0, 0);
            void Refresh() {
                string a = Shortcuts.Action(key);
                targetRow.Visible = a == "run" || a == "keys";
                browse.Visible = a == "run";
                hint.Text = a == "keys" ? "e.g. Win+P, Ctrl+Alt+T, Alt+Enter" : "a file, shortcut (.lnk), program or https:// link";
            }
            var row = ChoiceRow(label, key + "Action", Shortcuts.Action(key), options, v => { Shortcuts.SetAction(key, v); Refresh(); });
            target.Leave += (s, e) => { Shortcuts.SetTarget(key, target.Text.Trim()); SaveSetting(key + "Target", target.Text.Trim()); };
            browse.Click += (s, e) => {
                using (var d = new OpenFileDialog { Title = "Pick what to run", Filter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|All files|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK) { target.Text = d.FileName; Shortcuts.SetTarget(key, d.FileName); SaveSetting(key + "Target", d.FileName); }
            };
            targetRow.Controls.Add(target); targetRow.Controls.Add(browse); targetRow.Controls.Add(hint);
            wrap.Controls.Add(row); wrap.Controls.Add(targetRow);
            Refresh();
            return wrap;
        }

        // Label + dark drop-down that saves its choice straight away.
        Control ChoiceRow(string label, string key, string current, (string value, string text)[] options, Action<string> apply) {
            var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
            var l = Theme.Label(label); l.AutoSize = false; l.Width = 130; l.Height = 26; l.TextAlign = ContentAlignment.MiddleLeft;
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface2, ForeColor = Theme.Text, Font = Theme.Body, Width = 280 };
            foreach (var o in options) box.Items.Add(o.text);
            box.SelectedIndex = Math.Max(0, Array.FindIndex(options, o => o.value == current));
            box.SelectedIndexChanged += (s, e) => {
                string v = options[box.SelectedIndex].value;
                apply(v);
                SaveSetting(key, v);
            };
            row.Controls.Add(l); row.Controls.Add(box);
            return row;
        }

        static Label SectionLabel(string text) {
            var l = Theme.Label(text, Theme.Bold, Theme.Gold);
            l.Margin = new Padding(0, 14, 0, 6);
            return l;
        }

        Panel BuildLogPage() {
            var page = new Panel { BackColor = Theme.Bg };
            console = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Mono };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, BackColor = Theme.Bg, Padding = new Padding(0, 8, 0, 0) };
            bar.Controls.Add(Theme.Button("Copy all", (s, e) => { if (console.TextLength > 0) Clipboard.SetText(console.Text); }));
            bar.Controls.Add(Theme.Button("Open log file", (s, e) => { try { Process.Start("notepad.exe", "\"" + LogPath + "\""); } catch { } }));
            bar.Controls.Add(Theme.Button("Clear", (s, e) => console.Clear()));
            page.Controls.Add(console);
            page.Controls.Add(bar);
            PageHeader(page, "Log", "Also saved to Omnipad.log next to Omnipad.exe.");
            return page;
        }

        // ---------- lifecycle ----------

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void DarkTitleBar(IntPtr handle) {
            try {
                int on = 1;
                DwmSetWindowAttribute(handle, 20, ref on, 4);
                int caption = ColorTranslator.ToWin32(Theme.Surface);
                DwmSetWindowAttribute(handle, 35, ref caption, 4);
            } catch { }
        }

        TesterForm tester;
        public void OpenTester(Joycon jc) {
            if (tester != null && !tester.IsDisposed) tester.Close();
            tester = new TesterForm(jc);
            tester.Show(this);
        }

        // Called from a controller (hold Home): bring Omnipad to the front.
        public void ShowFromController() {
            OnUi(RestoreFromTray);
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            try {
                int on = 1;
                DwmSetWindowAttribute(Handle, 20, ref on, 4);                 // dark title bar (Windows 10 20H1+ / 11)
                int caption = ColorTranslator.ToWin32(Theme.Surface);
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);            // title bar colour matches the header (Windows 11)
            } catch { }
        }

        protected override void OnLoad(EventArgs e) {
            base.OnLoad(e);
            Program.Start();
            UpdateHeader();
            monitor.Start();
            uiTimer.Start();
            notifyIcon.BalloonTipClicked += (s, e2) => { if (Updater.UpdateAvailable) OpenUpdate(); };
            Updater.CheckInBackground();
            if (Program.JustUpdated)
                notifyIcon.ShowBalloonTip(4000, "Omnipad updated", "You're on version " + Updater.Current.ToString(3) + ".", ToolTipIcon.Info);

            // First run: start with Windows by default (it's a background tray app).
            if (ConfigurationManager.AppSettings["AutostartSetUp"] != "true") {
                SaveSetting("AutostartSetUp", "true");
                AppendTextBox(Autostart.Enable() + "\r\n");
                SyncAutostartCheck();
            } else {
                Autostart.Repair();
            }
        }

        protected override void OnShown(EventArgs e) {
            base.OnShown(e);
            if (Program.StartMinimized) {
                Hide();
                Opacity = 1;
            }
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && notifyIcon != null)
                HideToTray();
        }

        void HideToTray() {
            Hide();
            ShowInTaskbar = false;
            if (!trayHintShown) {
                trayHintShown = true;
                notifyIcon.ShowBalloonTip(3000, "Omnipad is still running", "Your controllers keep working. Click the tray icon to open Omnipad, right-click to quit.", ToolTipIcon.None);
            }
        }

        void RestoreFromTray() {
            Opacity = 1;
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void OpenUpdate() { Updater.Install(); }

        // The installer is about to replace the files: close for real (it starts Omnipad again afterwards).
        public void ExitForUpdate() { OnUi(() => { exiting = true; Close(); }); }

        void ExitApp() {
            exiting = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e) {
            // The X button hides to the tray; only "Quit Omnipad" in the tray (or Windows shutting down) really exits.
            if (!exiting && e.CloseReason == CloseReason.UserClosing) {
                e.Cancel = true;
                HideToTray();
                return;
            }
            uiTimer.Stop();
            monitor.Stop();
            try { Program.Stop(); } catch { }
            if (notifyIcon != null) notifyIcon.Visible = false;
            base.OnFormClosing(e);
            Environment.Exit(0);
        }

        class DarkMenuColors : ProfessionalColorTable {
            public override Color MenuItemSelected => Theme.Surface2;
            public override Color MenuItemBorder => Theme.Surface2;
            public override Color MenuBorder => Theme.Border;
            public override Color ToolStripDropDownBackground => Theme.Surface;
            public override Color ImageMarginGradientBegin => Theme.Surface;
            public override Color ImageMarginGradientMiddle => Theme.Surface;
            public override Color ImageMarginGradientEnd => Theme.Surface;
            public override Color CheckBackground => Theme.Accent;
            public override Color CheckSelectedBackground => Theme.Accent;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Border;
        }

        // "73%" from the voltage reading; the controller's own 5-step level until the first reading arrives.
        public static string BatteryText(Joycon jc) {
            string t = jc.batteryPercent >= 0 ? jc.batteryPercent + "%"
                : jc.batteryLevel >= 8 ? "Full" : jc.batteryLevel >= 6 ? "Medium" : jc.batteryLevel >= 4 ? "Low" : jc.batteryLevel >= 2 ? "Critical" : "Empty";
            return t + (jc.charging ? " · charging" : "");
        }

        public static bool BatteryLow(Joycon jc) {
            if (jc.charging) return false;
            return jc.batteryPercent >= 0 ? jc.batteryPercent <= 15 : jc.batteryLevel <= 2;
        }

        static string Describe(Joycon jc) {
            string name = jc.isPro ? "Pro Controller" : jc.isLeft ? "Left Joy-Con" : "Right Joy-Con";
            return name + " · " + (jc.isUSB ? "USB" : "Bluetooth") + " · " + BatteryText(jc);
        }

        void BuildTrayMenu(ContextMenuStrip menu) {
            menu.Items.Clear();
            var active = (Program.mgr?.j?.ToList() ?? new List<Joycon>()).Where(x => x.state == Joycon.state_.IMU_DATA_OK).ToList();
            if (active.Count == 0)
                menu.Items.Add(new ToolStripMenuItem("No controllers connected") { Enabled = false });
            foreach (var jc in active)
                menu.Items.Add(new ToolStripMenuItem(Describe(jc)) { Enabled = false });
            var wireless = active.Where(x => !x.isUSB).ToList();
            if (wireless.Count > 0)
                menu.Items.Add(new ToolStripMenuItem(wireless.Count == 1 ? "Turn off controller" : "Turn off all wireless controllers", null,
                    (s, e) => { foreach (var jc in wireless) jc.PowerOff(); }) { ForeColor = Theme.Text });
            menu.Items.Add(new ToolStripSeparator());
            if (Updater.UpdateAvailable)
                menu.Items.Add(new ToolStripMenuItem("Update to v" + Updater.Latest + " now", null, (s, e) => OpenUpdate()) { ForeColor = Theme.Gold });
            else menu.Items.Add(new ToolStripMenuItem("Check for updates", null, (s, e) => Updater.CheckAndInstallNow()) { ForeColor = Theme.Text });
            if (HidHide.Installed && HidHide.Allowed && Program.IsAdmin) {
                var hide = new ToolStripMenuItem("Hide the Nintendo controller (for emulators)") { Checked = HidHide.Enabled, ForeColor = Theme.Text };
                hide.Click += (s, e) => SetHiding(!HidHide.Enabled);
                menu.Items.Add(hide);
            }
            var layout = new ToolStripMenuItem(Prefs.SwapButtons ? "Buttons: Xbox labels (A is A)" : "Buttons: Nintendo positions") { ForeColor = Theme.Text, ToolTipText = "Click to switch" };
            layout.Click += (s, e) => SetButtonLayout(!Prefs.SwapButtons);
            menu.Items.Add(layout);
            menu.Items.Add(new ToolStripMenuItem("Open Omnipad", null, (s, e) => RestoreFromTray()) { Font = Theme.Bold, ForeColor = Theme.Text });
            var auto = new ToolStripMenuItem("Start with Windows") { Checked = Autostart.Enabled, ForeColor = Theme.Text };
            auto.Click += (s, e) => { if (Autostart.Enabled) Autostart.Disable(); else AppendTextBox(Autostart.Enable() + "\r\n"); SyncAutostartCheck(); };
            menu.Items.Add(auto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quit Omnipad", null, (s, e) => ExitApp()) { ForeColor = Theme.Text });
        }

        // Tray tooltip, plus a notification when a controller is ready (while the window is hidden) or its battery runs low.
        void UpdateTray(List<Joycon> all) {
            var active = all.Where(x => x.state == Joycon.state_.IMU_DATA_OK).ToList();
            string tip = active.Count == 0 ? "Omnipad - no controllers" : "Omnipad - " + Describe(active[0]) + (active.Count > 1 ? " +" + (active.Count - 1) : "");
            notifyIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

            foreach (var jc in active) {
                // Announce once per physical controller (a USB/Bluetooth switch is not a new controller).
                string key = jc.mac ?? jc.path;
                if (announcedKeys.Add(key) && !Visible)
                    notifyIcon.ShowBalloonTip(2500, "Controller ready", Describe(jc), ToolTipIcon.None);
                if (BatteryLow(jc) && lowBatteryWarned.Add(jc))
                    notifyIcon.ShowBalloonTip(5000, "Controller battery low", Describe(jc) + ". Plug it in to keep playing.", ToolTipIcon.Warning);
            }
            // Forget controllers that are gone, so reconnecting later announces again.
            var live = new HashSet<string>(all.Select(x => x.mac ?? x.path));
            announcedKeys.RemoveWhere(k => !live.Contains(k));
        }

        // Windows broadcasts WM_DEVICECHANGE whenever something is plugged in or removed: react straight away.
        protected override void WndProc(ref Message m) {
            base.WndProc(ref m);
            if (m.Msg == 0x0219) { // WM_DEVICECHANGE
                monitor.RefreshAsync();
                ScanSoon();
            }
        }

        void FixHidden() {
            if (HidGuardian.Installed) {
                string msg;
                bool ok = HidGuardian.ClearBlockList(out msg);
                AppendTextBox(msg + "\r\n");
                if (!ok && !Program.IsAdmin) { Program.RestartAsAdmin(); return; }
                ScanSoon();
                if (!HidHide.Installed) return;
            }
            if (HidHide.ClientOpen) {
                AppendTextBox("Close the HidHide Configuration Client window first - while it's open, HidHide ignores every other program.\r\n");
                return;
            }
            if (HidHide.Installed && !Program.IsAdmin) {
                AppendTextBox("Restarting as administrator so HidHide can be changed...\r\n");
                Program.RestartAsAdmin();
                return;
            }
            var ids = monitor.Snapshot().Where(d => d.IsNintendo && d.Id.StartsWith("HID", StringComparison.OrdinalIgnoreCase)).Select(d => d.Id).ToList();
            AppendTextBox("Turning HidHide hiding off and un-hiding " + ids.Count + " Nintendo device(s)...\r\n");
            ThreadPool.QueueUserWorkItem(_ => {
                if (!HidHide.Installed) AppendTextBox("HidHide is not installed, so it isn't what's hiding the controller.\r\n");
                HidHide.RestoreEverything(ids);
                SaveSetting("UseHidHide", "false");
                HidHide.LogState();
                AppendTextBox("Done. If the controller still doesn't appear, unplug and replug it. HidHide's state is in Omnipad.log.\r\n");
                Thread.Sleep(500);
                Program.mgr?.CheckForNewControllers();
            });
        }

        void ScanSoon() {
            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(300); Program.mgr?.CheckForNewControllers(); });
        }

        void OnUi(Action a) {
            if (IsDisposed) return;
            try {
                if (InvokeRequired) BeginInvoke(a); else a();
            } catch (InvalidOperationException) { }
        }

        void UpdateHeader() {
            syncing = true;
            hidHideCheck.Checked = HidHide.Enabled;
            hidHideCheck.Enabled = HidHide.Installed && HidHide.Allowed && Program.IsAdmin;
            syncing = false;
            hidHideNote.Text = !HidHide.Installed
                ? "HidHide is not installed. Get it from github.com/nefarius/HidHide/releases, restart, then run Omnipad as administrator once."
                : !Program.IsAdmin ? "Changing HidHide needs administrator rights: click the HidHide badge at the top to restart as administrator."
                : !HidHide.Allowed ? "HidHide setup failed - see the Log page."
                : "Without this, Steam and emulators see two controllers: the real one and Omnipad's Xbox one. RetroArch and other emulators that list every controller " +
                  "often grab the real Nintendo one, which only Omnipad can read, so buttons do nothing. Turn this on, then unplug and replug once.";

            if (Program.emClient != null) vigemPill.Set("Xbox driver OK", Theme.Good);
            else vigemPill.Set("Xbox driver missing", Theme.Bad);

            if (!HidHide.Installed) hidhidePill.Set("HidHide not installed", Theme.Muted);
            else if (!Program.IsAdmin) hidhidePill.Set("HidHide: click to restart as admin", Theme.Warn);
            else if (!HidHide.Allowed) hidhidePill.Set("HidHide: setup failed (see Log)", Theme.Bad);
            else if (HidHide.Enabled) hidhidePill.Set("HidHide on", Theme.Good);
            else hidhidePill.Set("HidHide off", Theme.Muted);
        }

        // ---------- controllers ----------

        public void ControllersChanged() {
            OnUi(RefreshCards);
        }

        void SyncAutostartCheck() {
            syncing = true;
            autostartCheck.Checked = Autostart.Enabled;
            syncing = false;
        }

        void SetBadge(string page, string badge) {
            var item = navButtons[page];
            if (item.Badge == badge) return;
            item.Badge = badge;
            item.Invalidate();
        }

        void RefreshCards() {
            var all = Program.mgr?.j?.ToList() ?? new List<Joycon>();
            // A connection that is still starting is hidden while another Pro Controller is already working:
            // that's almost always the same controller switching between Bluetooth and USB.
            var list = all.Where(x => x.state == Joycon.state_.IMU_DATA_OK || x.state == Joycon.state_.DROPPED ||
                                      !(x.isPro && all.Any(o => o != x && o.isPro && o.state == Joycon.state_.IMU_DATA_OK))).ToList();
            var shown = list;
            var existing = cards.Controls.OfType<ControllerCard>().ToList();

            if (!existing.Select(c => c.Jc).SequenceEqual(list)) {
                cards.SuspendLayout();
                foreach (var c in existing) { cards.Controls.Remove(c); c.Dispose(); }
                foreach (var jc in list) cards.Controls.Add(new ControllerCard(jc, this));
                emptyState.Visible = list.Count == 0;
                cards.ResumeLayout();
            } else {
                foreach (var c in existing) c.UpdateInfo();
            }
            bool windowsSeesOne = all.Count == 0 && monitor.Snapshot().Any(d => d.IsNintendo && d.Id.StartsWith("HID", StringComparison.OrdinalIgnoreCase));
            hiddenWarning.Visible = list.Count == 0 && windowsSeesOne;
            if (hiddenWarning.Visible) {
                bool denied = Program.mgr?.AccessDenied == true;
                hiddenDetail.Text = HidGuardian.Installed && denied
                    ? "HidGuardian (installed by old BetterJoy) is blocking it. Omnipad has asked to be let through;\nclick below to clear HidGuardian's block list, then unplug and replug the controller."
                    : HidHide.ClientOpen
                    ? "The HidHide Configuration Client window is open, which blocks Omnipad from changing HidHide. Close it, then click Fix it."
                    : denied
                    ? "Windows answered \"Access is denied\". That is HidHide blocking it, or another app (an old BetterJoy?) holding it.\n" +
                      (Program.IsAdmin ? "Click below to switch HidHide's hiding off, then unplug and replug." : "Omnipad needs administrator rights to switch HidHide off.")
                    : "It may be hidden by HidHide, or held by another app.";
                hiddenFixBtn.Text = HidGuardian.Installed ? "Fix it: clear HidGuardian's block list"
                    : Program.IsAdmin || !HidHide.Installed ? "Fix it: turn HidHide hiding off and un-hide controllers" : "Restart Omnipad as administrator";
            }
            SetBadge("Controllers", shown.Count > 0 ? shown.Count.ToString() : null);
            UpdateTray(all);
        }

        public void JoinOrSplit(Joycon v) {
            if (v.isPro) return;
            var all = Program.mgr.j;

            if (v.other == null) {
                Joycon partner = all.FirstOrDefault(x => x != v && !x.isPro && x.other == null && x.isLeft != v.isLeft);
                if (partner == null) {
                    AppendTextBox("Connect the other Joy-Con first, then pair them.\r\n");
                    return;
                }
                Joycon left = v.isLeft ? v : partner, right = v.isLeft ? partner : v;
                left.other = right; right.other = left;
                byte led = (byte)(0x1 << Math.Min(Math.Min(left.PadId, right.PadId), 3));
                left.LED = right.LED = led;
                if (right.xin != null) { right.xin.Dispose(); right.xin = null; }
                if (left.xin == null) { left.CreateOwnPad(); left.xin?.Connect(); }
                AppendTextBox("Joy-Cons paired into one controller.\r\n");
            } else {
                Joycon o = v.other;
                foreach (Joycon jc in new[] { v, o }) {
                    jc.other = null;
                    if (jc.xin == null) { jc.CreateOwnPad(); jc.xin?.Connect(); }
                }
                AppendTextBox("Joy-Cons split into two controllers.\r\n");
            }
            RefreshCards();
        }

        // ---------- devices ----------

        void DeviceEvent(PnpDevice d, bool added) {
            // Only log the top-level USB/Bluetooth device plus HID game controllers, to keep the list readable.
            if (d.Bus == "HID" && !d.IsGameController) return;
            string line = string.Format("{0:HH:mm:ss}  {1} {2}  [{3}{4}]\n", DateTime.Now, added ? "+ plugged in " : "- removed    ", d.Name, d.Bus, d.Vid != null ? " " + d.VidPid : "");
            activity.SelectionStart = activity.TextLength;
            activity.SelectionColor = added ? Theme.Good : Theme.Bad;
            activity.AppendText(line);
            activity.ScrollToCaret();
            Log((added ? "Device added: " : "Device removed: ") + d.Name + " " + d.Id);
        }

        string SelectedDeviceId() {
            return grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Tag as string : null;
        }

        // Opens the picked device as a controller, even if automatic detection missed it.
        void UseSelectedAsController() {
            string id = SelectedDeviceId();
            if (id == null || Program.mgr == null) return;
            var all = monitor.Snapshot();
            var row = all.FirstOrDefault(d => d.Id == id);
            if (!id.StartsWith("HID", StringComparison.OrdinalIgnoreCase)) {
                // A USB/Bluetooth parent was picked: use its HID game-controller child with the same VID:PID.
                var child = row == null ? null : all.FirstOrDefault(d => d.Bus != "USB" && d.Id.StartsWith("HID", StringComparison.OrdinalIgnoreCase) && d.VidPid == row.VidPid && d.Vid != null);
                if (child == null) {
                    AppendTextBox("That row isn't a HID device and no matching HID entry was found. Pick the \"HID-compliant game controller\" row.\r\n");
                    return;
                }
                id = child.Id;
            }
            if (row != null && row.Vid != null && row.Vid != "057E")
                AppendTextBox("Note: this isn't a Nintendo device, so it probably won't speak the Switch controller protocol.\r\n");
            Program.mgr.AddManual(HidEnum.PathFromInstanceId(id));
            ShowPage("Log");
        }

        void HideSelected(bool hide) {
            string id = SelectedDeviceId();
            if (id == null) return;
            if (!HidHide.Installed) { AppendTextBox("HidHide is not installed.\r\n"); return; }
            if (!id.StartsWith("HID", StringComparison.OrdinalIgnoreCase)) {
                AppendTextBox("HidHide works on HID devices - pick the HID row (Type = HIDClass) for that device.\r\n");
                ShowPage("Log");
                return;
            }
            ThreadPool.QueueUserWorkItem(_ => {
                bool ok = hide ? HidHide.HideId(id) : HidHide.UnhideId(id);
                AppendTextBox((hide ? "Hide " : "Un-hide ") + id + (ok ? ": done" : ": failed (run as administrator?)") + "\r\n");
            });
        }

        void FillGrid() {
            if (grid == null || !pages["USB Devices"].Visible) return;
            string q = search.Text.Trim();
            var inUse = new HashSet<string>((Program.mgr?.j?.ToList() ?? new List<Joycon>()).Select(j => HidHide.InstanceIdFromPath(j.path) ?? ""), StringComparer.OrdinalIgnoreCase);

            var rows = monitor.Snapshot()
                .Where(d => !controllersOnly.Checked || d.IsGameController)
                .Where(d => q.Length == 0 || (d.Name + " " + d.Id + " " + d.VidPid).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(d => d.FirstSeen).ThenByDescending(d => d.IsNintendo).ThenBy(d => d.Name)
                .ToList();

            string selected = SelectedDeviceId();
            int scroll = grid.FirstDisplayedScrollingRowIndex;
            grid.SuspendLayout();
            grid.Rows.Clear();
            foreach (var d in rows) {
                string when = d.FirstSeen == DateTime.MinValue ? "" : d.FirstSeen.ToString("HH:mm:ss");
                int i = grid.Rows.Add(when, d.Name, d.Bus, d.VidPid, d.Class, inUse.Contains(d.Id) ? "In use" : "", d.Id);
                var row = grid.Rows[i];
                row.Tag = d.Id;
                if (d.IsNew) row.DefaultCellStyle.BackColor = Theme.NewRow;
                if (d.IsNintendo) row.DefaultCellStyle.ForeColor = Theme.Gold;
                if (d.Status != "OK" && d.Status.Length > 0) row.DefaultCellStyle.ForeColor = Theme.Muted;
                if (d.Id == selected) row.Selected = true;
            }
            if (selected == null) grid.ClearSelection();
            if (scroll >= 0 && scroll < grid.RowCount) grid.FirstDisplayedScrollingRowIndex = scroll;
            grid.ResumeLayout();

            int fresh = rows.Count(d => d.IsNew);
            SetBadge("USB Devices", fresh > 0 ? fresh + " new" : null);
        }

        // ---------- logging ----------

        public void AppendTextBox(string value) {
            Log(value);
            OnUi(() => {
                if (console == null) return;
                console.AppendText(DateTime.Now.ToString("HH:mm:ss  ") + value.TrimEnd() + "\n");
                console.ScrollToCaret();
            });
        }

        // Everything shown in the log also goes to Omnipad.log next to the exe, so problems can be sent over.
        static readonly string LogPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Omnipad.log");
        static readonly object logLock = new object();
        public static void Log(string value) {
            try {
                lock (logLock)
                    File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff ") + value.TrimEnd() + "\r\n");
            } catch { }
        }

        public static void SaveSetting(string key, string value) {
            try {
                var cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                if (cfg.AppSettings.Settings[key] == null) cfg.AppSettings.Settings.Add(key, value);
                else cfg.AppSettings.Settings[key].Value = value;
                cfg.Save(ConfigurationSaveMode.Modified);
            } catch (Exception e) {
                Log("Could not save setting " + key + ": " + e.Message);
            }
        }
    }
}
