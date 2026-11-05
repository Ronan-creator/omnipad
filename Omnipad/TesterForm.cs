using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using B = Omnipad.Joycon.Button;

namespace Omnipad {
    // Live view of one controller: every button lights up when pressed, sticks show their position,
    // plus a drift check (does the stick return to the centre on its own?) and a rumble test.
    public class TesterForm : Form {
        readonly Joycon jc;
        readonly Timer timer = new Timer { Interval = 16 };
        readonly List<(string label, Func<bool> pressed)> buttons = new List<(string, Func<bool>)>();
        readonly List<(string name, Func<float[]> read)> sticks = new List<(string, Func<float[]>)>();
        readonly float[] restAvg = new float[2]; // smoothed off-centre distance while the stick is (nearly) released
        readonly float[] maxSeen = new float[2];

        public TesterForm(Joycon jc) {
            this.jc = jc;
            Text = "Omnipad - Input tester";
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body;
            Size = new Size(820, 520); MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            try { using (var st = typeof(TesterForm).Assembly.GetManifestResourceStream("Omnipad.app.ico")) Icon = new Icon(st); } catch { }

            BuildLayout();

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Bg, Padding = new Padding(22, 10, 22, 10), FlowDirection = FlowDirection.RightToLeft };
            bar.Controls.Add(Theme.Button("Close", (s, e) => Close()));
            bar.Controls.Add(Theme.Button("Rumble test", (s, e) => { jc.SetRumble(160f, 320f, 0.9f, 400); jc.other?.SetRumble(160f, 320f, 0.9f, 400); }, true));
            bar.Controls.Add(Theme.Button("Reset drift check", (s, e) => { restAvg[0] = restAvg[1] = 0; maxSeen[0] = maxSeen[1] = 0; }));
            Controls.Add(bar);

            timer.Tick += (s, e) => Invalidate();
            timer.Start();
        }

        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            MainForm.DarkTitleBar(Handle);
        }

        protected override void OnFormClosed(FormClosedEventArgs e) {
            timer.Stop();
            base.OnFormClosed(e);
        }

        void BuildLayout() {
            Joycon left = jc.isPro ? jc : jc.isLeft ? jc : jc.other;
            Joycon right = jc.isPro ? jc : !jc.isLeft ? jc : jc.other;

            if (jc.isPro) {
                Add("ZL", jc, B.SHOULDER_2); Add("L", jc, B.SHOULDER_1); Add("R", jc, B.SHOULDER2_1); Add("ZR", jc, B.SHOULDER2_2);
                Add("−", jc, B.MINUS); Add("Capture", jc, B.CAPTURE); Add("Home", jc, B.HOME); Add("+", jc, B.PLUS);
                Add("Up", jc, B.DPAD_UP); Add("Down", jc, B.DPAD_DOWN); Add("Left", jc, B.DPAD_LEFT); Add("Right", jc, B.DPAD_RIGHT);
                Add("Y", jc, B.Y); Add("X", jc, B.X); Add("B", jc, B.B); Add("A", jc, B.A);
                Add("L stick", jc, B.STICK); Add("R stick", jc, B.STICK2);
                sticks.Add(("Left stick", jc.GetStick)); sticks.Add(("Right stick", jc.GetStick2));
                return;
            }

            // Joy-Cons: a right Joy-Con reports its A/B/X/Y in the D-pad slots.
            if (left != null) {
                Add("ZL", left, B.SHOULDER_2); Add("L", left, B.SHOULDER_1); Add("−", left, B.MINUS); Add("Capture", left, B.CAPTURE);
                Add("Up", left, B.DPAD_UP); Add("Down", left, B.DPAD_DOWN); Add("Left", left, B.DPAD_LEFT); Add("Right", left, B.DPAD_RIGHT);
                Add("L stick", left, B.STICK); Add("SL (left)", left, B.SL); Add("SR (left)", left, B.SR);
                Joycon l = left; sticks.Add(("Left stick", () => l.GetStick()));
            }
            if (right != null) {
                Add("R", right, B.SHOULDER_1); Add("ZR", right, B.SHOULDER_2); Add("+", right, B.PLUS); Add("Home", right, B.HOME);
                Add("X", right, B.DPAD_UP); Add("B", right, B.DPAD_DOWN); Add("Y", right, B.DPAD_LEFT); Add("A", right, B.DPAD_RIGHT);
                Add("R stick", right, B.STICK); Add("SL (right)", right, B.SL); Add("SR (right)", right, B.SR);
                Joycon r = right;
                // In a pair the right stick is read through the left Joy-Con's second stick.
                sticks.Add(("Right stick", () => jc.other != null && left != null ? left.GetStick2() : r.GetStick()));
            }
        }

        void Add(string label, Joycon c, B b) => buttons.Add((label, () => c.GetButton(b)));

        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            ControllerArt.DrawTile(g, new Rectangle(24, 20, 60, 60), ControllerArt.KindOf(jc), 12);
            string name = jc.isPro ? "Pro Controller" : jc.other != null ? "Joy-Con pair" : jc.isLeft ? "Left Joy-Con" : "Right Joy-Con";
            TextRenderer.DrawText(g, name, Theme.CardTitle, new Point(98, 24), Theme.Text, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, (jc.isUSB ? "USB cable" : "Bluetooth") + "  ·  Battery " + MainForm.BatteryText(jc) + "  ·  Press anything - it lights up below.",
                Theme.Small, new Point(99, 54), Theme.Muted, TextFormatFlags.NoPadding);

            // Sticks
            int sx = 24, sy = 104, size = 170;
            for (int i = 0; i < sticks.Count && i < 2; i++) {
                DrawStick(g, new Rectangle(sx + i * (size + 26), sy, size, size), sticks[i].name, sticks[i].read(), i);
            }

            // Buttons
            int bx = sx + 2 * (size + 26) + 14, by = 104, col = 0, w = 84, h = 34;
            foreach (var b in buttons) {
                bool on = SafePressed(b.pressed);
                var r = new Rectangle(bx + col * (w + 8), by, w, h);
                using (var path = Theme.Rounded(r, 9)) using (var br = new SolidBrush(on ? Theme.Gold : Theme.Surface2)) g.FillPath(br, path);
                TextRenderer.DrawText(g, b.label, on ? Theme.Bold : Theme.Small, r, on ? Theme.Bg : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (++col == 4) { col = 0; by += h + 8; }
            }
        }

        static bool SafePressed(Func<bool> f) {
            try { return f(); } catch { return false; }
        }

        void DrawStick(Graphics g, Rectangle r, string name, float[] v, int idx) {
            float x = v != null && v.Length > 0 ? Clamp(v[0]) : 0, y = v != null && v.Length > 1 ? Clamp(v[1]) : 0;
            float mag = (float)Math.Sqrt(x * x + y * y);

            // Drift check: average how far off-centre the stick sits when it's nearly released.
            if (mag < 0.35f) restAvg[idx] = restAvg[idx] * 0.97f + mag * 0.03f;
            maxSeen[idx] = Math.Max(maxSeen[idx], mag);

            using (var path = Theme.Rounded(r, 16)) using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
            var c = new Rectangle(r.X + 20, r.Y + 20, r.Width - 40, r.Width - 40);
            using (var p = new Pen(Theme.Border, 2)) { g.DrawEllipse(p, c); g.DrawLine(p, c.Left, c.Top + c.Height / 2, c.Right, c.Top + c.Height / 2); g.DrawLine(p, c.Left + c.Width / 2, c.Top, c.Left + c.Width / 2, c.Bottom); }
            float cx = c.X + c.Width / 2f, cy = c.Y + c.Height / 2f, rad = c.Width / 2f;
            float px = cx + x * rad, py = cy - y * rad;
            using (var p = new Pen(Color.FromArgb(120, Theme.Gold), 2)) g.DrawLine(p, cx, cy, px, py);
            using (var b = new SolidBrush(Theme.Gold)) g.FillEllipse(b, px - 8, py - 8, 16, 16);

            TextRenderer.DrawText(g, name, Theme.Bold, new Point(r.X, r.Bottom + 10), Theme.Text, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, string.Format("x {0:+0.00;-0.00}   y {1:+0.00;-0.00}", x, y), Theme.Mono, new Point(r.X, r.Bottom + 34), Theme.Muted, TextFormatFlags.NoPadding);

            string drift; Color dc;
            if (maxSeen[idx] < 0.5f) { drift = "Move it around once, then let go"; dc = Theme.Muted; }
            else if (restAvg[idx] < 0.06f) { drift = "Centres well - no drift"; dc = Theme.Good; }
            else if (restAvg[idx] < 0.15f) { drift = string.Format("Slight drift ({0:0}%)", restAvg[idx] * 100); dc = Theme.Warn; }
            else { drift = string.Format("Drifting ({0:0}%) - add a deadzone in Steam", restAvg[idx] * 100); dc = Theme.Bad; }
            TextRenderer.DrawText(g, drift, Theme.Small, new Rectangle(r.X, r.Bottom + 58, r.Width + 20, 40), dc, TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        }

        static float Clamp(float f) => float.IsNaN(f) || float.IsInfinity(f) ? 0 : Math.Max(-1f, Math.Min(1f, f));
    }
}
