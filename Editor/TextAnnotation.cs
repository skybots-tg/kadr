using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Kadr.Editor
{
    public static class KFonts
    {
        public static readonly FontFamily Family = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly Typeface UiBold = new Typeface(Family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        public static readonly Typeface UiSemibold = new Typeface(Family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    }

    public enum TextStyleKind { Outline = 0, Pill = 1, Plain = 2 }

    public sealed class TextAnnotation : Annotation
    {
        public Point Position;      // top-left of the text layout
        public string Text = "";
        public TextStyleKind Style;
        public bool Editing;        // while editing an empty box we still show the caret

        public double FontSize => SizeLevel switch { 0 => 16, 1 => 22, _ => 32 };
        public override bool IsEmpty => string.IsNullOrWhiteSpace(Text);
        public override bool HasShadow => Style != TextStyleKind.Outline;

        public FormattedText Format(string text = null)
        {
            text ??= Text;
            if (string.IsNullOrEmpty(text)) text = " ";
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, KFonts.UiBold, FontSize, B(Color), 1.0);
        }

        Thickness Pad => Style == TextStyleKind.Pill
            ? new Thickness(FontSize * 0.45, FontSize * 0.18, FontSize * 0.45, FontSize * 0.2)
            : new Thickness(FontSize * 0.12);

        public Rect TextRect
        {
            get { var ft = Format(); return new Rect(Position.X, Position.Y, Math.Max(ft.WidthIncludingTrailingWhitespace, 4), ft.Height); }
        }

        public override Rect Bounds
        {
            get { var r = TextRect; var p = Pad; return new Rect(r.X - p.Left, r.Y - p.Top, r.Width + p.Left + p.Right, r.Height + p.Top + p.Bottom); }
        }

        public override bool HitTest(Point p, double tol) { var r = Bounds; r.Inflate(tol, tol); return r.Contains(p); }
        public override void Move(Vector d) => Position += d;

        public override void Render(DrawingContext dc, RenderEnv env)
        {
            if (IsEmpty && !Editing) return;
            string text = string.IsNullOrEmpty(Text) ? null : Text;
            var ft = Format(text ?? " ");
            switch (Style)
            {
                case TextStyleKind.Pill:
                {
                    var r = Bounds;
                    double rad = Math.Min(FontSize * 0.42, r.Height / 2);
                    dc.DrawRoundedRectangle(B(Color), null, r, rad, rad);
                    if (text != null) { ft.SetForegroundBrush(B(Contrast(Color))); dc.DrawText(ft, Position); }
                    break;
                }
                case TextStyleKind.Outline:
                {
                    if (text == null) break;
                    var geo = ft.BuildGeometry(Position);
                    var outline = new Pen(B(Contrast(Color)), FontSize * 0.16) { LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(null, outline, geo);
                    dc.DrawGeometry(B(Color), null, geo);
                    break;
                }
                default:
                    if (text != null) dc.DrawText(ft, Position);
                    break;
            }
        }
    }
}
