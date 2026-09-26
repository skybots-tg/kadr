using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Kadr.UI
{
    /// <summary>Stroke icons on a 24×24 grid, drawn to one consistent weight.</summary>
    public static class Icons
    {
        public static readonly Brush Ink = Frozen(Color.FromRgb(236, 236, 240));

        public const string Arrow = "M6.5 17.5 L17 7 M9.5 6.5 H17.5 V14.5";
        public const string Rect = "M7 5 H17 A2 2 0 0 1 19 7 V17 A2 2 0 0 1 17 19 H7 A2 2 0 0 1 5 17 V7 A2 2 0 0 1 7 5 Z";
        public const string Ellipse = "M12 4.5 A7.5 7.5 0 1 1 11.99 4.5 Z";
        public const string Pen = "M4.5 19.5 L5.4 15.6 L15.6 5.4 A2.1 2.1 0 0 1 18.6 8.4 L8.4 18.6 Z M14 7 L17 10";
        public const string Marker = "M9 11 L3.5 16.5 V19.5 H11.5 L14 17 M20.5 12.5 L16.6 16.4 A1.9 1.9 0 0 1 13.9 16.4 L8.6 11.1 A1.9 1.9 0 0 1 8.6 8.4 L12.5 4.5";
        public const string Text = "M6 7.5 V5.5 H18 V7.5 M12 5.5 V18.5 M9.5 18.5 H14.5";
        public const string Blur = "M12 3.5 C12 3.5 5.5 10.2 5.5 14.5 A6.5 6.5 0 0 0 18.5 14.5 C18.5 10.2 12 3.5 12 3.5 Z M9 15 A3 3 0 0 0 12 18";
        public const string Undo = "M9 14 L4.5 9.5 L9 5 M4.5 9.5 H14 A5 5 0 0 1 14 19.5 H11";
        public const string Redo = "M15 14 L19.5 9.5 L15 5 M19.5 9.5 H10 A5 5 0 0 0 10 19.5 H13";
        public const string Copy = "M10 8.5 H18.5 A1.5 1.5 0 0 1 20 10 V18.5 A1.5 1.5 0 0 1 18.5 20 H10 A1.5 1.5 0 0 1 8.5 18.5 V10 A1.5 1.5 0 0 1 10 8.5 Z M5.5 15.5 A1.5 1.5 0 0 1 4 14 V5.5 A1.5 1.5 0 0 1 5.5 4 H14 A1.5 1.5 0 0 1 15.5 5.5";
        public const string Save = "M20 15 V18 A2 2 0 0 1 18 20 H6 A2 2 0 0 1 4 18 V15 M7.5 10.5 L12 15 L16.5 10.5 M12 15 V4";
        public const string Pin = "M12 16.5 V21 M9 10.5 A2 2 0 0 1 7.9 12.3 L6.6 13 A2 2 0 0 0 5.5 14.8 V16.5 H18.5 V14.8 A2 2 0 0 0 17.4 13 L16.1 12.3 A2 2 0 0 1 15 10.5 V6.5 A1 1 0 0 1 16 5.5 A1.25 1.25 0 0 0 16 3 H8 A1.25 1.25 0 0 0 8 5.5 A1 1 0 0 1 9 6.5 Z";
        public const string Close = "M17 7 L7 17 M7 7 L17 17";
        public const string Check = "M19.5 7 L9.5 17 L4.5 12";
        public const string Edit = "M12 20 H20 M15.6 5.4 A2.1 2.1 0 0 1 18.6 8.4 L8 19 L4 20 L5 16 Z";
        public const string Folder = "M3.5 7.5 A2 2 0 0 1 5.5 5.5 H9.5 L11.5 7.5 H18.5 A2 2 0 0 1 20.5 9.5 V16.5 A2 2 0 0 1 18.5 18.5 H5.5 A2 2 0 0 1 3.5 16.5 Z";

        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static FrameworkElement Make(string data, double size = 18, Brush stroke = null, double thickness = 1.7)
        {
            var path = new Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke ?? Ink,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
        }

        public static FrameworkElement Counter(double size = 18)
        {
            var g = new Grid { Width = 24, Height = 24 };
            g.Children.Add(new Ellipse { Width = 16, Height = 16, Stroke = Ink, StrokeThickness = 1.7 });
            g.Children.Add(new TextBlock
            {
                Text = "1", Foreground = Ink, FontWeight = FontWeights.Bold, FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontFamily = Editor.KFonts.Family, Margin = new Thickness(0, -1, 0, 0),
            });
            return new Viewbox { Width = size, Height = size, Child = g };
        }
    }
}
