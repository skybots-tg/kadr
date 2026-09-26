using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Kadr.Core;

namespace Kadr.Editor
{
    public enum Tool { None, Arrow, Rect, Ellipse, Pen, Marker, Text, Counter, Blur }

    public sealed class RenderEnv
    {
        public PixelImage Base;     // image under the annotations (for pixelate)
        public Point BaseOrigin;    // DIP position of Base's top-left in annotation space
    }

    public abstract class Annotation
    {
        public Color Color;
        public int SizeLevel;       // 0 small, 1 medium, 2 large

        public double Stroke => SizeLevel switch { 0 => 3, 1 => 5, _ => 8 };

        public abstract void Render(DrawingContext dc, RenderEnv env);
        public abstract bool HitTest(Point p, double tol);
        public abstract Rect Bounds { get; }
        public abstract void Move(Vector d);
        public virtual Point[] Handles => Array.Empty<Point>();
        public virtual void SetHandle(int i, Point p, bool shift) { }
        public virtual bool HasShadow => true;
        public virtual bool IsEmpty => false;

        public Annotation Clone()
        {
            var c = (Annotation)MemberwiseClone();
            c.DeepCopy();
            return c;
        }
        protected virtual void DeepCopy() { }

        static DropShadowEffect _shadow;
        public static Effect Shadow
        {
            get
            {
                if (_shadow == null)
                {
                    _shadow = new DropShadowEffect { BlurRadius = 7, ShadowDepth = 1.5, Direction = 270, Opacity = 0.38, Color = Colors.Black, RenderingBias = RenderingBias.Quality };
                    _shadow.Freeze();
                }
                return _shadow;
            }
        }

        protected static Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static Color Contrast(Color c)
        {
            double l = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return l > 170 ? Color.FromRgb(20, 20, 22) : Colors.White;
        }

        protected static double DistToSegment(Point p, Point a, Point b)
        {
            var ab = b - a; var ap = p - a;
            double len2 = ab.LengthSquared;
            double t = len2 < 1e-6 ? 0 : Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / len2, 0, 1);
            return (p - (a + ab * t)).Length;
        }

        public static Point SnapAngle(Point from, Point to)
        {
            var v = to - from;
            double ang = Math.Atan2(v.Y, v.X);
            double step = Math.PI / 4;
            ang = Math.Round(ang / step) * step;
            double len = v.Length;
            return from + new Vector(Math.Cos(ang) * len, Math.Sin(ang) * len);
        }
    }

    /// <summary>Tapered arrow with a crisp head; optionally curved through a control point.</summary>
    public sealed class ArrowAnnotation : Annotation
    {
        public Point Start, End;
        public Point? Control;

        public override bool IsEmpty => (End - Start).Length < 4;

        Point Eval(double t)
        {
            if (Control is not Point c) return Start + (End - Start) * t;
            double u = 1 - t;
            return new Point(u * u * Start.X + 2 * u * t * c.X + t * t * End.X, u * u * Start.Y + 2 * u * t * c.Y + t * t * End.Y);
        }

        Point Mid => Control is Point c ? Eval(0.5) : Start + (End - Start) * 0.5;

        public override Point[] Handles => new[] { Start, End, Mid };

        public override void SetHandle(int i, Point p, bool shift)
        {
            if (i == 0) Start = shift ? SnapAngle(End, p) : p;
            else if (i == 1) End = shift ? SnapAngle(Start, p) : p;
            else
            {
                // p is where the curve midpoint should pass: control = 2*mid - (start+end)/2
                var chordMid = Start + (End - Start) * 0.5;
                if ((p - chordMid).Length < 3) Control = null;
                else Control = new Point(2 * p.X - chordMid.X, 2 * p.Y - chordMid.Y);
            }
        }

        public override void Move(Vector d)
        {
            Start += d; End += d;
            if (Control is Point c) Control = c + d;
        }

        public override Rect Bounds
        {
            get
            {
                var r = new Rect(Start, End);
                if (Control is Point) for (int i = 1; i < 10; i++) r.Union(Eval(i / 10.0));
                r.Inflate(Stroke * 3, Stroke * 3);
                return r;
            }
        }

        public override bool HitTest(Point p, double tol)
        {
            double t = tol + Stroke * 1.5;
            Point prev = Start;
            int n = Control is null ? 1 : 24;
            for (int i = 1; i <= n; i++)
            {
                var q = Eval(i / (double)n);
                if (DistToSegment(p, prev, q) <= t) return true;
                prev = q;
            }
            return false;
        }

        public Geometry BuildGeometry()
        {
            double w = Stroke * 1.25 + 1.5;      // shaft width at the head
            double headLen = w * 3.1 + 6;
            double headHalf = w * 2.0 + 3;
            double total = (End - Start).Length;
            if (Control is Point) total = ApproxLength();
            headLen = Math.Min(headLen, total * 0.6);
            headHalf = Math.Min(headHalf, Math.Max(headLen * 0.75, w));

            // sample the centre line up to the head base
            int n = Control is null ? 2 : 40;
            var pts = new List<Point>();
            var tangents = new List<Vector>();
            double tHead = FindT(total - headLen * 0.85);
            for (int i = 0; i <= n; i++)
            {
                double t = tHead * i / n;
                pts.Add(Eval(t));
                tangents.Add(Tangent(t));
            }
            var endTan = Tangent(1.0);
            var tip = End;
            var baseC = tip - endTan * headLen;

            var left = new List<Point>();
            var right = new List<Point>();
            for (int i = 0; i < pts.Count; i++)
            {
                double f = pts.Count == 1 ? 1 : i / (double)(pts.Count - 1);
                double half = (w * (0.28 + 0.72 * f)) / 2;
                var nrm = new Vector(-tangents[i].Y, tangents[i].X);
                left.Add(pts[i] + nrm * half);
                right.Add(pts[i] - nrm * half);
            }
            var en = new Vector(-endTan.Y, endTan.X);

            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(left[0], true, true);
                for (int i = 1; i < left.Count; i++) c.LineTo(left[i], true, true);
                // head: shoulder in, swept back barbs, tip
                var lb = baseC + en * headHalf;
                var rb = baseC - en * headHalf;
                var lInner = baseC + endTan * (headLen * 0.12) + en * (w / 2);
                var rInner = baseC + endTan * (headLen * 0.12) - en * (w / 2);
                c.LineTo(lInner, true, true);
                c.LineTo(lb, true, true);
                c.LineTo(tip, true, true);
                c.LineTo(rb, true, true);
                c.LineTo(rInner, true, true);
                for (int i = right.Count - 1; i >= 0; i--) c.LineTo(right[i], true, true);
                // rounded tail
                c.ArcTo(left[0], new Size(w * 0.14 + 0.01, w * 0.14 + 0.01), 0, false, SweepDirection.Counterclockwise, true, true);
            }
            g.Freeze();
            return g;
        }

        Vector Tangent(double t)
        {
            Vector v;
            if (Control is Point c)
            {
                double u = 1 - t;
                v = new Vector(2 * u * (c.X - Start.X) + 2 * t * (End.X - c.X), 2 * u * (c.Y - Start.Y) + 2 * t * (End.Y - c.Y));
            }
            else v = End - Start;
            if (v.Length < 1e-6) v = new Vector(1, 0);
            v.Normalize();
            return v;
        }

        double ApproxLength()
        {
            double len = 0; Point prev = Start;
            for (int i = 1; i <= 32; i++) { var q = Eval(i / 32.0); len += (q - prev).Length; prev = q; }
            return len;
        }

        double FindT(double dist)
        {
            if (Control is null) { double L = (End - Start).Length; return L < 1e-6 ? 0 : Math.Clamp(dist / L, 0, 1); }
            double len = 0; Point prev = Start;
            for (int i = 1; i <= 64; i++)
            {
                double t = i / 64.0; var q = Eval(t);
                len += (q - prev).Length; prev = q;
                if (len >= dist) return t;
            }
            return 1;
        }

        public override void Render(DrawingContext dc, RenderEnv env)
        {
            if (IsEmpty) return;
            var geo = BuildGeometry();
            var pen = new Pen(B(Color), 1.2) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(B(Color), pen, geo);
        }
    }

    public sealed class ShapeAnnotation : Annotation
    {
        public Point A, B2;
        public bool IsEllipse;

        public Rect R => new Rect(A, B2);
        public override bool IsEmpty => R.Width < 4 && R.Height < 4;

        public override Point[] Handles
        {
            get { var r = R; return new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft }; }
        }

        public override void SetHandle(int i, Point p, bool shift)
        {
            var r = R;
            Point opp = i switch { 0 => r.BottomRight, 1 => r.BottomLeft, 2 => r.TopLeft, _ => r.TopRight };
            if (shift) p = Square(opp, p);
            A = opp; B2 = p;
        }

        public static Point Square(Point from, Point to)
        {
            var v = to - from;
            double s = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
            return new Point(from.X + Math.Sign(v.X == 0 ? 1 : v.X) * s, from.Y + Math.Sign(v.Y == 0 ? 1 : v.Y) * s);
        }

        public override void Move(Vector d) { A += d; B2 += d; }

        public override Rect Bounds { get { var r = R; r.Inflate(Stroke + 4, Stroke + 4); return r; } }

        public override bool HitTest(Point p, double tol)
        {
            var r = R;
            double t = tol + Stroke / 2 + 2;
            if (IsEllipse)
            {
                double rx = r.Width / 2, ry = r.Height / 2;
                if (rx < 1 || ry < 1) return false;
                var c = new Point(r.X + rx, r.Y + ry);
                double nx = (p.X - c.X) / rx, ny = (p.Y - c.Y) / ry;
                double d = Math.Sqrt(nx * nx + ny * ny);
                return Math.Abs(d - 1) * Math.Min(rx, ry) <= t;
            }
            var outer = r; outer.Inflate(t, t);
            var inner = r; inner.Inflate(-t, -t);
            return outer.Contains(p) && (inner.IsEmpty || !inner.Contains(p));
        }

        public override void Render(DrawingContext dc, RenderEnv env)
        {
            if (IsEmpty) return;
            var pen = new Pen(B(Color), Stroke) { LineJoin = PenLineJoin.Round };
            var r = R;
            if (IsEllipse) dc.DrawEllipse(null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
            else
            {
                double rad = Math.Min(Stroke * 1.4 + 2, Math.Min(r.Width, r.Height) / 2);
                dc.DrawRoundedRectangle(null, pen, r, rad, rad);
            }
        }
    }

    public sealed class PenAnnotation : Annotation
    {
        public List<Point> Points = new();
        public bool Marker;

        protected override void DeepCopy() { Points = new List<Point>(Points); }

        public override bool HasShadow => !Marker;
        double Width => Marker ? Stroke * 3.2 + 8 : Stroke;
        public override bool IsEmpty => Points.Count < 2;

        public override void Move(Vector d) { for (int i = 0; i < Points.Count; i++) Points[i] += d; }

        public override Rect Bounds
        {
            get
            {
                if (Points.Count == 0) return Rect.Empty;
                var r = new Rect(Points[0], Points[0]);
                foreach (var p in Points) r.Union(p);
                r.Inflate(Width, Width);
                return r;
            }
        }

        public override bool HitTest(Point p, double tol)
        {
            double t = tol + Width / 2;
            for (int i = 1; i < Points.Count; i++)
                if (DistToSegment(p, Points[i - 1], Points[i]) <= t) return true;
            return false;
        }

        public Geometry BuildGeometry()
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(Points[0], false, false);
                if (Points.Count == 2) c.LineTo(Points[1], true, true);
                else
                {
                    for (int i = 1; i < Points.Count - 1; i++)
                    {
                        var mid = new Point((Points[i].X + Points[i + 1].X) / 2, (Points[i].Y + Points[i + 1].Y) / 2);
                        c.QuadraticBezierTo(Points[i], mid, true, true);
                    }
                    c.LineTo(Points[^1], true, true);
                }
            }
            g.Freeze();
            return g;
        }

        public override void Render(DrawingContext dc, RenderEnv env)
        {
            if (IsEmpty) return;
            var col = Marker ? Color.FromArgb(110, Color.R, Color.G, Color.B) : Color;
            var pen = new Pen(B(col), Width)
            {
                StartLineCap = Marker ? PenLineCap.Square : PenLineCap.Round,
                EndLineCap = Marker ? PenLineCap.Square : PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            dc.DrawGeometry(null, pen, BuildGeometry());
        }

        /// <summary>Add a point with light smoothing so strokes feel fluid instead of jittery.</summary>
        public void AddPoint(Point p)
        {
            if (Points.Count > 0 && (p - Points[^1]).Length < 1.5) return;
            if (Points.Count >= 2)
            {
                var last = Points[^1];
                p = new Point(last.X * 0.25 + p.X * 0.75, last.Y * 0.25 + p.Y * 0.75);
            }
            Points.Add(p);
        }
    }

    public sealed class CounterAnnotation : Annotation
    {
        public Point Center;
        public int Number;

        double Radius => SizeLevel switch { 0 => 11, 1 => 15, _ => 21 };

        public override void Move(Vector d) => Center += d;
        public override Rect Bounds => new Rect(Center.X - Radius - 3, Center.Y - Radius - 3, Radius * 2 + 6, Radius * 2 + 6);
        public override bool HitTest(Point p, double tol) => (p - Center).Length <= Radius + tol;

        public override void Render(DrawingContext dc, RenderEnv env)
        {
            dc.DrawEllipse(B(Color), new Pen(B(Colors.White), Math.Max(1.5, Radius * 0.09)), Center, Radius, Radius);
            var ft = new FormattedText(Number.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                KFonts.UiBold, Radius * 1.12, B(Contrast(Color)), 1.0);
            dc.DrawText(ft, new Point(Center.X - ft.Width / 2, Center.Y - ft.Height / 2));
        }
    }

    public enum BlurKind { Pixelate = 0, Gaussian = 1, Redact = 2, Focus = 3 }

    /// <summary>Region privacy/emphasis effects: mosaic, smooth blur, solid redaction, or blur everything around.</summary>
    public sealed class BlurAnnotation : Annotation
    {
        public Point A, B2;
        public BlurKind Kind;
        ImageSource _cache;
        Rect _cacheSrc = Rect.Empty;
        string _cacheKey;

        public Rect R => new Rect(A, B2);
        public override bool HasShadow => Kind == BlurKind.Redact;
        public override bool IsEmpty => R.Width < 3 || R.Height < 3;

        protected override void DeepCopy() { _cache = null; _cacheKey = null; }

        public override Point[] Handles { get { var r = R; return new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft }; } }

        public override void SetHandle(int i, Point p, bool shift)
        {
            var r = R;
            Point opp = i switch { 0 => r.BottomRight, 1 => r.BottomLeft, 2 => r.TopLeft, _ => r.TopRight };
            A = opp; B2 = p;
        }

        public override void Move(Vector d) { A += d; B2 += d; }
        public override Rect Bounds => R;

        public override bool HitTest(Point p, double tol)
        {
            var outer = R; outer.Inflate(tol + 3, tol + 3);
            if (!outer.Contains(p)) return false;
            if (Kind != BlurKind.Focus) return true;
            // the focused area stays drawable: only its edge grabs the annotation
            var inner = R; inner.Inflate(-(tol + 5), -(tol + 5));
            return inner.IsEmpty || !inner.Contains(p);
        }

        double Radius => Kind == BlurKind.Pixelate ? SizeLevel switch { 0 => 6, 1 => 10, _ => 16 } : SizeLevel switch { 0 => 7, 1 => 13, _ => 22 };
        static double Corner(Rect r) => Math.Min(6, Math.Min(r.Width, r.Height) / 2);

        public override void Render(DrawingContext dc, RenderEnv env)
        {
            if (IsEmpty) return;
            var r = R;
            if (Kind == BlurKind.Redact)
            {
                dc.DrawRoundedRectangle(B(Color.FromRgb(22, 22, 24)), null, r, Corner(r), Corner(r));
                return;
            }
            if (env?.Base == null) return;
            var img = env.Base;
            double s = img.Scale;

            if (Kind == BlurKind.Focus)
            {
                var full = BlurMath.FullBlur(img, (int)Math.Round(Radius * s));
                var baseRect = new Rect(env.BaseOrigin.X, env.BaseOrigin.Y, img.DipWidth, img.DipHeight);
                var clip = new GeometryGroup { FillRule = FillRule.EvenOdd };
                clip.Children.Add(new RectangleGeometry(baseRect));
                clip.Children.Add(new RectangleGeometry(r, Corner(r) + 2, Corner(r) + 2));
                dc.PushClip(clip);
                dc.DrawImage(full, baseRect);
                dc.DrawRectangle(B(Color.FromArgb(46, 0, 0, 0)), null, baseRect);
                dc.Pop();
                return;
            }

            // pixel rect of the region (plus margin for blur so edges sample real neighbours)
            int margin = Kind == BlurKind.Gaussian ? (int)Math.Ceiling(Radius * s * 2) : 0;
            var px = new Int32Rect(
                (int)Math.Floor((r.X - env.BaseOrigin.X) * s) - margin, (int)Math.Floor((r.Y - env.BaseOrigin.Y) * s) - margin,
                (int)Math.Ceiling(r.Width * s) + 2 * margin, (int)Math.Ceiling(r.Height * s) + 2 * margin);
            px = img.Clamp(px);
            string key = $"{Kind}{SizeLevel}{px.X},{px.Y},{px.Width},{px.Height}";
            if (_cache == null || _cacheKey != key)
            {
                _cache = Kind == BlurKind.Pixelate
                    ? BlurMath.Pixelate(img, px, Math.Max(4, (int)Math.Round(Radius * s)))
                    : BlurMath.Gaussian(img, px, (int)Math.Round(Radius * s));
                _cacheKey = key;
                _cacheSrc = new Rect(px.X / s + env.BaseOrigin.X, px.Y / s + env.BaseOrigin.Y, px.Width / s, px.Height / s);
            }
            dc.PushClip(new RectangleGeometry(r, Corner(r), Corner(r)));
            dc.DrawImage(_cache, _cacheSrc);
            dc.Pop();
        }
    }
}
