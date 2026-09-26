using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kadr.Core;
using Kadr.Editor;

namespace Kadr.UI
{
    public sealed class SettingsWindow : DarkWindow
    {
        static SettingsWindow _open;
        readonly StackPanel _hotkeyCard = new();
        Border _recordingChip;

        public static void ShowSingle()
        {
            if (_open != null) { _open.Activate(); return; }
            _open = new SettingsWindow();
            _open.Closed += (_, _) => _open = null;
            _open.Show();
            Native.ForceForeground(new System.Windows.Interop.WindowInteropHelper(_open).Handle);
            _open.Activate();
        }

        /// <summary>Content without the scroll viewer — used to render README images.</summary>
        public StackPanel PromoRoot { get; private set; }
        public static SettingsWindow CreateForPromo() => new();

        SettingsWindow()
        {
            Title = "Кадр — настройки";
            Width = 600; Height = 760; MinWidth = 520; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new StackPanel { Margin = new Thickness(28, 22, 28, 28) };

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            if (App.AppIconImage != null) head.Children.Add(new Image { Source = App.AppIconImage, Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0) });
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Text("Кадр", 22, weight: FontWeights.SemiBold));
            titles.Children.Add(Text($"Скриншоты как на Mac · версия {Installer.Version}", 12.5, dim: true));
            head.Children.Add(titles);
            root.Children.Add(head);

            // hotkeys
            root.Children.Add(Section("Горячие клавиши", "Нажмите на сочетание и введите новое. Esc — отмена, Backspace — удалить."));
            root.Children.Add(Card(_hotkeyCard));
            BuildHotkeys();
            var reset = Link("Вернуть стандартные");
            reset.MouseLeftButtonUp += (_, _) =>
            {
                Settings.Current.Hotkeys = Hotkeys.Defaults();
                Settings.Current.HotkeysCustomized = false;
                Commit();
            };
            reset.Margin = new Thickness(4, 8, 0, 0);
            root.Children.Add(reset);

            // after capture
            var s = Settings.Current;
            root.Children.Add(Section("После снимка"));
            var folderRow = FolderRow();
            root.Children.Add(Card(
                ToggleRow("Копировать в буфер обмена", "PNG с прозрачностью — вставляется в мессенджеры и документы", s.CopyToClipboard, v => s.CopyToClipboard = v),
                ToggleRow("Сохранять в папку", null, s.SaveToFolder, v => s.SaveToFolder = v),
                folderRow,
                ToggleRow("Показывать миниатюру", "Карточка в углу: клик — редактор, можно перетащить в чат", s.ShowThumbnail, v => s.ShowThumbnail = v),
                ToggleRow("Звук затвора", null, s.PlaySound, v => s.PlaySound = v)));

            root.Children.Add(Section("Снимок"));
            root.Children.Add(Card(
                ToggleRow("Тень у снимков окон", "Мягкая тень, как на Mac. Alt+клик — без тени", s.WindowShadow, v => s.WindowShadow = v),
                CornersRow(),
                ToggleRow("Лупа при выделении", "Увеличение, координаты и цвет пикселя под курсором", s.ShowMagnifier, v => s.ShowMagnifier = v)));

            root.Children.Add(Section("Система"));
            var sys = new System.Collections.Generic.List<UIElement>
            {
                ToggleRow("Запускать вместе с Windows", null, Installer.AutoStart, v => Installer.AutoStart = v),
                ToggleRow("Обновлять автоматически", "Новые версии с GitHub ставятся сами, когда вы не делаете снимок", s.AutoUpdate, v => s.AutoUpdate = v),
                UpdateRow(),
            };
            if (Installer.IsInstalledCopy)
            {
                var un = new PillButton("Удалить Кадр", primary: false, danger: true) { HorizontalAlignment = HorizontalAlignment.Right };
                un.Click += () => App.ConfirmUninstall(this);
                sys.Add(Row("Удаление", "Снимки в папке останутся", un));
            }
            root.Children.Add(Card(sys.ToArray()));

            var gh = Link("Кадр на GitHub");
            gh.Margin = new Thickness(4, 16, 0, 0);
            gh.MouseLeftButtonUp += (_, _) => { try { Process.Start(new ProcessStartInfo("https://github.com/skybots-tg/kadr") { UseShellExecute = true }); } catch { } };
            root.Children.Add(gh);

            var scroll = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Application.Current.Resources["SlimScrollBar"]);
            Content = scroll;
            PromoRoot = root;
            Closed += (_, _) => { App.Instance?.Hook?.StopRecording(); Settings.Save(); };
            Deactivated += (_, _) => CancelRecording();
        }

        // ------------------------------------------------------------------ building blocks

        static FrameworkElement Section(string title, string sub = null)
        {
            var sp = new StackPanel { Margin = new Thickness(4, 18, 0, 8) };
            sp.Children.Add(Text(title, 14, weight: FontWeights.SemiBold));
            if (sub != null) sp.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = TextDim, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            return sp;
        }

        static Border Card(params UIElement[] rows)
        {
            var sp = new StackPanel();
            for (int i = 0; i < rows.Length; i++)
            {
                if (i > 0) sp.Children.Add(new Border { Height = 1, Background = Theme.Hairline, Margin = new Thickness(16, 0, 16, 0) });
                sp.Children.Add(rows[i]);
            }
            return Card(sp);
        }

        static Border Card(StackPanel content) => new()
        {
            Background = new SolidColorBrush(CardColor), CornerRadius = new CornerRadius(12),
            BorderBrush = Theme.Hairline, BorderThickness = new Thickness(1), Child = content,
        };

        static Grid Row(string title, string sub, UIElement right)
        {
            var g = new Grid { Margin = new Thickness(16, 12, 16, 12), MinHeight = 26 };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(Text(title, 13.5));
            if (sub != null) left.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = TextDim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            g.Children.Add(left);
            if (right is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return g;
        }

        static Grid ToggleRow(string title, string sub, bool value, Action<bool> set)
        {
            var t = new ToggleSwitch(value);
            t.Toggled += v => { set(v); Settings.Save(); };
            return Row(title, sub, t);
        }

        static TextBlock Link(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(100, 170, 255)), Cursor = Cursors.Hand };
            t.MouseEnter += (_, _) => t.TextDecorations = TextDecorations.Underline;
            t.MouseLeave += (_, _) => t.TextDecorations = null;
            return t;
        }

        Grid UpdateRow()
        {
            var status = new TextBlock { FontSize = 12, Foreground = TextDim, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            void Status(string t) { status.Text = t; status.Visibility = Visibility.Visible; }
            var btn = new PillButton("Проверить", false);
            bool busy = false;
            btn.Click += async () =>
            {
                if (busy) return;
                busy = true;
                try
                {
                    Status("Проверяю…");
                    var r = await Updater.FetchLatestAsync();
                    Settings.Current.LastUpdateCheck = DateTime.Now;
                    if (!Updater.IsNewer(r)) { Status("У вас последняя версия"); return; }
                    if (!Installer.IsInstalledCopy)
                    {
                        Status($"Доступна версия {r.Version} — откройте страницу релиза");
                        Process.Start(new ProcessStartInfo(r.PageUrl) { UseShellExecute = true });
                        return;
                    }
                    var progress = new Progress<double>(p => Status($"Скачиваю {r.Version}… {Math.Round(p * 100)}%"));
                    var path = await Updater.DownloadAsync(r, progress);
                    Status("Устанавливаю и перезапускаю…");
                    await System.Threading.Tasks.Task.Delay(400);
                    Close();
                    App.Instance.ApplyUpdate(r, path);
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                    Status("Не удалось: " + ex.Message);
                }
                finally { busy = false; }
            };
            var g = Row($"Версия {Installer.Version}", null, btn);
            ((StackPanel)g.Children[0]).Children.Add(status);
            return g;
        }

        static Grid CornersRow()
        {
            var seg = new Segmented(new (FrameworkElement, string)[]
            {
                (new TextBlock { Text = "Классика", FontSize = 12.5, Foreground = Brushes.White }, "Как в macOS до 26-й версии"),
                (new TextBlock { Text = "macOS 26", FontSize = 12.5, Foreground = Brushes.White }, "Круглее, как в macOS 26"),
            }, null);
            seg.Selected = Settings.Current.WindowCorners == 0 ? 0 : 1;
            seg.Picked += i => { seg.Selected = i; Settings.Current.WindowCorners = i; Settings.Save(); };
            return Row("Скругление углов окна", "Одинаковое для окон любого размера", seg);
        }

        Grid FolderRow()
        {
            var path = new TextBlock { FontSize = 12, Foreground = TextDim, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, Margin = new Thickness(0, 0, 12, 0) };
            void Refresh() { path.Text = Settings.Current.Folder; path.ToolTip = Settings.Current.Folder; }
            Refresh();
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            btns.Children.Add(path);
            var open = new PillButton("Открыть", false) { Margin = new Thickness(0, 0, 6, 0) };
            open.Click += () => { Directory.CreateDirectory(Settings.Current.Folder); Process.Start("explorer.exe", $"\"{Settings.Current.Folder}\""); };
            var change = new PillButton("Изменить…", false);
            change.Click += () =>
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Settings.Current.Folder, Title = "Куда сохранять снимки" };
                if (dlg.ShowDialog(this) == true) { Settings.Current.Folder = dlg.FolderName; Settings.Save(); Refresh(); }
            };
            btns.Children.Add(open);
            btns.Children.Add(change);
            return Row("Папка", null, btns);
        }

        // ------------------------------------------------------------------ hotkeys

        void BuildHotkeys()
        {
            _hotkeyCard.Children.Clear();
            _recordingChip = null;
            var actions = new[] { HotkeyAction.Region, HotkeyAction.Window, HotkeyAction.FullScreen };
            string[] subs = { "Экран замирает. Пробел — режим окна", "Сразу выбор окна — снимок с тенью", "Монитор под курсором целиком" };
            for (int i = 0; i < actions.Length; i++)
            {
                if (i > 0) _hotkeyCard.Children.Add(new Border { Height = 1, Background = Theme.Hairline, Margin = new Thickness(16, 0, 16, 0) });
                var a = actions[i];
                var chips = new StackPanel { Orientation = Orientation.Horizontal };
                var list = Settings.Current.Hotkeys.Where(b => b.Action == a).ToList();
                foreach (var b in list) chips.Children.Add(Chip(a, b));
                if (list.Count < 2) chips.Children.Add(Chip(a, null));
                _hotkeyCard.Children.Add(Row(Hotkeys.Title(a), subs[i], chips));
            }
        }

        Border Chip(HotkeyAction action, HotkeyBinding binding)
        {
            FrameworkElement content = binding != null
                ? KeyCaps.Make(Hotkeys.Parts(binding.Vk, binding.Mods))
                : new TextBlock { Text = "+ Добавить", FontSize = 12, Foreground = TextDim, Margin = new Thickness(4, 1, 4, 1) };
            var chip = new Border
            {
                Child = content, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 5, 4, 5), Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Transparent, BorderBrush = binding == null ? Theme.Hairline : Brushes.Transparent, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, ToolTip = binding == null ? "Добавить сочетание" : "Изменить",
            };
            chip.MouseEnter += (_, _) => { if (chip != _recordingChip) chip.Background = Theme.Hover; };
            chip.MouseLeave += (_, _) => { if (chip != _recordingChip) chip.Background = Brushes.Transparent; };
            chip.MouseLeftButtonUp += (_, _) => StartRecording(chip, action, binding);
            return chip;
        }

        void StartRecording(Border chip, HotkeyAction action, HotkeyBinding binding)
        {
            CancelRecording();
            _recordingChip = chip;
            chip.Child = new TextBlock { Text = "Нажмите сочетание…", FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(6, 1, 6, 1) };
            chip.BorderBrush = new SolidColorBrush(AnnotationSurface.Accent);
            chip.Background = new SolidColorBrush(Color.FromArgb(40, 10, 132, 255));
            App.Instance.Hook.Record((vk, mods) =>
            {
                _recordingChip = null;
                var list = Settings.Current.Hotkeys;
                if (vk == 0x1B && mods == Mods.None) { BuildHotkeys(); return; }                // Esc: cancel
                if ((vk == 0x08 || vk == 0x2E) && mods == Mods.None)                           // Backspace/Delete: remove
                {
                    if (binding != null) list.Remove(binding);
                    Commit();
                    return;
                }
                var nb = new HotkeyBinding(action, vk, mods);
                list.RemoveAll(b => b.SameKeys(nb));      // a combo belongs to one action only
                int idx = binding != null ? list.IndexOf(binding) : -1;
                if (idx >= 0) list[idx] = nb; else list.Add(nb);
                Commit();
            });
        }

        void CancelRecording()
        {
            if (_recordingChip == null) return;
            App.Instance?.Hook?.StopRecording();
            BuildHotkeys();
        }

        void Commit()
        {
            Settings.Current.HotkeysCustomized = true;
            Settings.Save();
            App.Instance.ApplyHotkeys();
            BuildHotkeys();
        }

    }
}
