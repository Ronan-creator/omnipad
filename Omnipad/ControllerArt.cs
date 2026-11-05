using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Omnipad {
    // Vector drawings of the controllers in the Omnific palette: a dark Pro Controller with gold sticks and a
    // deep-blue D-pad, a gold left Joy-Con and a deep-blue right Joy-Con. Designed on a 100x100 tile.
    public static class ControllerArt {
        static readonly Color Gold = ColorTranslator.FromHtml("#EAC42A");
        static readonly Color Deep = ColorTranslator.FromHtml("#0022FF");
        static readonly Color White = ColorTranslator.FromHtml("#F1F1F1");
        static readonly Color Body = ColorTranslator.FromHtml("#171A21");
        static readonly Color Rim = ColorTranslator.FromHtml("#464E5F");
        static readonly Color Stick = ColorTranslator.FromHtml("#0C0E12");
        static readonly Color StickTop = ColorTranslator.FromHtml("#1E222A");
        static readonly Color TileA = ColorTranslator.FromHtml("#2D50B9");
        static readonly Color TileB = ColorTranslator.FromHtml("#0A143E");

        public enum Kind { Pro, LeftJoyCon, RightJoyCon, JoyConPair }

        public static Kind KindOf(Joycon jc) {
            if (jc.isPro) return Kind.Pro;
            if (jc.other != null) return Kind.JoyConPair;
            return jc.isLeft ? Kind.LeftJoyCon : Kind.RightJoyCon;
        }

        // Blue gradient tile with the controller drawn on it.
        public static void DrawTile(Graphics g, Rectangle r, Kind kind, float radius = 14) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(r, radius))
            using (var b = new LinearGradientBrush(r, TileA, TileB, 45f))
                g.FillPath(b, path);
            Draw(g, r, kind);
        }

        public static void Draw(Graphics g, RectangleF r, Kind kind) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Math.Min(r.Width, r.Height) / 100f;
            float x0 = r.X + (r.Width - 100 * k) / 2, y0 = r.Y + (r.Height - 100 * k) / 2;
            switch (kind) {
                case Kind.Pro: Pro(new Pen(g, x0, y0, k, 4, 14, 0.46f)); break;
                case Kind.LeftJoyCon: JoyCon(new Pen(g, x0, y0, k, 36, 14, 0.52f), true, Gold); break;
                case Kind.RightJoyCon: JoyCon(new Pen(g, x0, y0, k, 36, 14, 0.52f), false, Deep); break;
                case Kind.JoyConPair:
                    JoyCon(new Pen(g, x0, y0, k, 20, 14, 0.52f), true, Gold);
                    JoyCon(new Pen(g, x0, y0, k, 52, 14, 0.52f), false, Deep);
                    break;
            }
        }

        // Maps design coordinates to the screen.
        struct Pen {
            readonly Graphics g; readonly float x0, y0, k, ox, oy, s;
            public Pen(Graphics g, float x0, float y0, float k, float ox, float oy, float s) { this.g = g; this.x0 = x0; this.y0 = y0; this.k = k; this.ox = ox; this.oy = oy; this.s = s; }
            public PointF P(float x, float y) => new PointF(x0 + (ox + x * s) * k, y0 + (oy + y * s) * k);
            float L(float v) => v * s * k;

            public void Circle(float x, float y, float r, Color c) {
                var p = P(x - r, y - r);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, p.X, p.Y, L(r * 2), L(r * 2));
            }
            public void Rect(float xa, float ya, float xb, float yb, float r, Color fill, Color? outline = null, float w = 0) {
                var a = P(xa, ya);
                var rect = new RectangleF(a.X, a.Y, L(xb - xa), L(yb - ya));
                using (var path = Theme.Rounded(rect, Math.Max(0.5f, Math.Min(L(r), Math.Min(rect.Width, rect.Height) / 2)))) {
                    if (fill.A > 0) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    if (outline.HasValue) using (var p = new System.Drawing.Pen(outline.Value, Math.Max(1f, L(w)))) g.DrawPath(p, path);
                }
            }
            public void Poly(PointF[] pts, Color fill, Color? outline = null) {
                using (var b = new SolidBrush(fill)) g.FillPolygon(b, pts);
                if (outline.HasValue) using (var p = new System.Drawing.Pen(outline.Value, Math.Max(1f, L(1.2f)))) g.DrawPolygon(p, pts);
            }
        }

        static void Pro(Pen d) {
            // grips hang below each end of the body
            foreach (int sgn in new[] { -1, 1 }) {
                Func<float, float> X = v => 100 + sgn * (100 - v);
                d.Poly(new[] { d.P(X(16), 56), d.P(X(78), 70), d.P(X(70), 108), d.P(X(52), 126), d.P(X(30), 122), d.P(X(16), 92) }, Body, Rim);
                d.Circle(X(42), 114, 13, Body);
            }
            d.Rect(14, 14, 186, 82, 32, Body, Rim, 1.6f);
            d.Circle(58, 38, 14, Stick); d.Circle(58, 38, 10, Gold);                    // left stick
            d.Rect(70, 62, 94, 70, 2.5f, Deep); d.Rect(78, 54, 86, 78, 2.5f, Deep);     // D-pad
            d.Circle(124, 64, 14, Stick); d.Circle(124, 64, 10, Gold);                  // right stick
            foreach (var b in new[] { (148f, 26f), (148f, 50f), (136f, 38f), (160f, 38f) }) d.Circle(b.Item1, b.Item2, 6, White);
            d.Rect(80, 24, 90, 27, 1, White);                                            // minus
            d.Rect(110, 24, 120, 27, 1, White); d.Rect(113.5f, 20.5f, 116.5f, 30.5f, 1, White); // plus
            d.Rect(84, 38, 92, 46, 1.5f, Rim);                                           // capture
            d.Circle(112, 42, 5, Rim); d.Circle(112, 42, 3, Gold);                       // home
        }

        static void JoyCon(Pen d, bool left, Color color) {
            d.Rect(0, 0, 56, 140, 26, color);
            if (left) d.Rect(30, 0, 56, 140, 6, color); else d.Rect(0, 0, 26, 140, 6, color);
            float rail = left ? 52 : 0;
            d.Rect(rail, 10, rail + 4, 130, 2, Color.FromArgb(90, 0, 0, 0));
            if (left) {
                d.Circle(28, 40, 12, Stick); d.Circle(28, 40, 8.5f, StickTop);
                foreach (var b in new[] { (28f, 76f), (28f, 100f), (16f, 88f), (40f, 88f) }) d.Circle(b.Item1, b.Item2, 5.5f, Stick);
                d.Rect(36, 14, 46, 17, 1, Stick);                // minus
                d.Rect(34, 118, 42, 126, 1.5f, Stick);           // capture
            } else {
                foreach (var b in new[] { (28f, 30f), (28f, 54f), (16f, 42f), (40f, 42f) }) d.Circle(b.Item1, b.Item2, 5.5f, Stick);
                d.Circle(28, 92, 12, Stick); d.Circle(28, 92, 8.5f, StickTop);
                d.Rect(10, 14, 20, 17, 1, Stick); d.Rect(13.5f, 10.5f, 16.5f, 20.5f, 1, Stick); // plus
                d.Circle(16, 122, 5, Stick); d.Circle(16, 122, 3, White);                        // home
            }
        }

        // Small battery icon: outline plus a fill bar for level 0-8.
        public static void Battery(Graphics g, float x, float y, int level, bool charging, Color color) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new System.Drawing.Pen(color, 1.4f)) {
                using (var path = Theme.Rounded(new RectangleF(x, y, 18, 10), 2.5f)) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(color)) {
                g.FillRectangle(b, x + 18.6f, y + 3, 2, 4);
                float w = 14f * Math.Max(1, level) / 8f;
                g.FillRectangle(b, x + 2, y + 2, w, 6);
            }
            if (charging)
                using (var b = new SolidBrush(Gold))
                    g.FillPolygon(b, new[] { new PointF(x + 10, y - 2), new PointF(x + 6, y + 6), new PointF(x + 9, y + 6), new PointF(x + 8, y + 12), new PointF(x + 12.5f, y + 4), new PointF(x + 9.5f, y + 4) });
        }
    }
}
