using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Kadr.Core;

namespace Kadr.UI
{
    /// <summary>Pixel magnifier that follows the cursor, with coordinates and color readout.</summary>
    public sealed class Loupe : StackPanel
    {
        const int Cells = 15;
        const double Cell = 8;
        const double D = Cells * Cell;
        readonly ImageBrush _brush;
        readonly TextBlock _info;
        readonly Border _swatch;
        readonly PixelImage _img;

        public Loupe(PixelImage img)
        {
            _img = img;
            IsHitTestVisible = false;
            Orientation = Orientation.Vertical;

            _brush = new ImageBrush(img.Bitmap) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            var zoom = new Rectangle { Width = D, Height = D, Fill = _brush };
            RenderOptions.SetBitmapScalingMode(zoom, BitmapScalingMode.NearestNeighbor);

            var grid = new DrawingGroup();
            using (var dc = grid.Open())
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), 0.6);
                for (int i = 1; i < Cells; i++)
                {
                    dc.DrawLine(pen, new Point(i * Cell, 0), new Point(i * Cell, D));
                    dc.DrawLine(pen, new Point(0, i * Cell), new Point(D, i * Cell));
                }
                double c = (Cells / 2) * Cell;
                dc.DrawRectangle(null, new Pen(Brushes.Black, 2.5), new Rect(c, c, Cell, Cell));
                dc.DrawRectangle(null, new Pen(Brushes.White, 1.2), new Rect(c, c, Cell, Cell));
            }
            var gridImg = new Image { Source = new DrawingImage(grid), Width = D, Height = D };

            var lens = new Grid { Width = D, Height = D, Clip = new EllipseGeometry(new Point(D / 2, D / 2), D / 2, D / 2) };
            lens.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(20, 20, 22)) });
            lens.Children.Add(zoom);
            lens.Children.Add(gridImg);

            var ring = new Ellipse { Width = D, Height = D, Stroke = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), StrokeThickness = 3 };
            var outer = new Ellipse { Width = D + 2, Height = D + 2, Stroke = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), StrokeThickness = 1, Margin = new Thickness(-1) };
            var lensHost = new Grid { Width = D, Height = D, Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.45 } };
            lensHost.Children.Add(lens);
            lensHost.Children.Add(ring);
            lensHost.Children.Add(outer);
            Children.Add(lensHost);

            _swatch = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            _info = new TextBlock { Foreground = Brushes.White, FontSize = 11.5, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center };
            var infoRow = new StackPanel { Orientation = Orientation.Horizontal };
            infoRow.Children.Add(_swatch);
            infoRow.Children.Add(_info);
            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(225, 28, 28, 32)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4, 9, 5),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = infoRow,
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
            };
            Children.Add(pill);
        }

        public static double Size => D;

        public void Update(int px, int py)
        {
            int half = Cells / 2;
            _brush.Viewbox = new Rect(px - half, py - half, Cells, Cells);
            var c = _img.GetPixel(px, py);
            _swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            _info.Text = $"{px}, {py}  #{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }
}
