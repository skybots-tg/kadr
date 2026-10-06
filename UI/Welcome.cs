using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Kadr.Core;
using Kadr.Editor;

namespace Kadr.UI
{
    /// <summary>Small cheat-sheet card shown on first launch and from the tray menu.</summary>
    public static class Welcome
    {
        static Window _open;

        public static void Show()
        {
            var stack = new StackPanel { Width = 330 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            if (App.AppIconImage != null) head.Children.Add(new Image { Source = App.AppIconImage, Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) });
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = L.T("Кадр работает", "Kadr is running"), Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = KFonts.Family });
            titles.Children.Add(new TextBlock { Text = L.T("Живёт в трее · сочетания меняются в настройках", "Lives in the tray · hotkeys can be changed in the settings"), Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), FontSize = 11.5, FontFamily = KFonts.Family });
            head.Children.Add(titles);
            stack.Children.Add(head);

            void Row(string keys, string what)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var k = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 1, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Left, BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = keys, Foreground = Brushes.White, FontSize = 11.5, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontWeight = FontWeights.SemiBold },
                };
                var t = new TextBlock { Text = what, Foreground = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)), FontSize = 12.5, FontFamily = KFonts.Family, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(t, 1);
                g.Children.Add(k); g.Children.Add(t);
                stack.Children.Add(g);
            }
            Row(Hotkeys.FirstFor(HotkeyAction.Region) ?? "—", L.T("Снимок области", "Capture region"));
            Row(L.T("Пробел", "Space"), L.T("Режим окна (с тенью)", "Window mode (with shadow)"));
            Row(Hotkeys.FirstFor(HotkeyAction.FullScreen) ?? "—", L.T("Весь экран", "Full screen"));
            Row("A T B", L.T("Стрелка, текст, размытие…", "Arrow, text, blur…"));
            Row("Enter", L.T("Скопировать и закрыть", "Copy and close"));
            Present(stack, 9);
        }

        /// <summary>"Updated to X" card with the first lines of the release notes.</summary>
        public static void ShowUpdated(string version, string notes)
        {
            var stack = new StackPanel { Width = 330 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            if (App.AppIconImage != null) head.Children.Add(new Image { Source = App.AppIconImage, Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) });
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = L.T($"Кадр обновлён до {version}", $"Kadr updated to {version}"), Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = KFonts.Family });
            titles.Children.Add(new TextBlock { Text = L.T("Обновление установилось автоматически", "The update was installed automatically"), Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), FontSize = 11.5, FontFamily = KFonts.Family });
            head.Children.Add(titles);
            stack.Children.Add(head);
            foreach (var line in (notes ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "•  " + line, Foreground = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)), FontSize = 12.5,
                    FontFamily = KFonts.Family, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                });
            }
            Present(stack, 12);
        }

        static void Present(StackPanel stack, double seconds)
        {
            _open?.Close();
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(246, 30, 30, 34)), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 16, 18, 16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1), Child = stack, Margin = new Thickness(24),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.45 },
            };
            var tt = new TranslateTransform(0, 20);
            card.RenderTransform = tt;

            var w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false,
                SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Content = card, ShowActivated = false, Opacity = 0,
            };
            w.Loaded += (_, _) =>
            {
                var wa = SystemParameters.WorkArea;
                w.Left = wa.Right - w.ActualWidth + 6;
                w.Top = wa.Bottom - w.ActualHeight + 6;
                w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            void Hide()
            {
                timer.Stop();
                var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
                a.Completed += (_, _) => w.Close();
                w.BeginAnimation(UIElement.OpacityProperty, a);
            }
            timer.Tick += (_, _) => Hide();
            card.MouseLeftButtonUp += (_, _) => Hide();
            card.MouseEnter += (_, _) => timer.Stop();
            card.MouseLeave += (_, _) => timer.Start();
            w.Closed += (_, _) => { if (_open == w) _open = null; };
            _open = w;
            w.Show();
            timer.Start();
        }
    }
}
