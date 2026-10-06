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
            Title = L.T("Кадр — настройки", "Kadr — settings");
            Width = 600; Height = 760; MinWidth = 520; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new StackPanel { Margin = new Thickness(28, 22, 28, 28) };

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            if (App.AppIconImage != null) head.Children.Add(new Image { Source = App.AppIconImage, Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0) });
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Text(L.AppName, 22, weight: FontWeights.SemiBold));
            titles.Children.Add(Text(L.T($"Скриншоты как на Mac · версия {Installer.Version}", $"macOS-style screenshots · version {Installer.Version}"), 12.5, dim: true));
            head.Children.Add(titles);
            root.Children.Add(head);

            // hotkeys
            root.Children.Add(Section(L.T("Горячие клавиши", "Hotkeys"), L.T("Нажмите на сочетание и введите новое. Esc — отмена, Backspace — удалить.", "Click a shortcut and press a new one. Esc cancels, Backspace removes.")));
            root.Children.Add(Card(_hotkeyCard));
            BuildHotkeys();
            var reset = Link(L.T("Вернуть стандартные", "Restore defaults"));
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
            root.Children.Add(Section(L.T("После снимка", "After capture")));
            var folderRow = FolderRow();
            root.Children.Add(Card(
                ToggleRow(L.T("Копировать в буфер обмена", "Copy to clipboard"), L.T("PNG с прозрачностью — вставляется в мессенджеры и документы", "PNG with transparency — pastes into chats and documents"), s.CopyToClipboard, v => s.CopyToClipboard = v),
                ToggleRow(L.T("Сохранять в папку", "Save to folder"), null, s.SaveToFolder, v => s.SaveToFolder = v),
                folderRow,
                ToggleRow(L.T("Показывать миниатюру", "Show thumbnail"), L.T("Карточка в углу: клик — редактор, можно перетащить в чат", "Corner card: click to edit, drag into a chat"), s.ShowThumbnail, v => s.ShowThumbnail = v),
                ToggleRow(L.T("Звук затвора", "Shutter sound"), null, s.PlaySound, v => s.PlaySound = v)));

            root.Children.Add(Section(L.T("Снимок", "Capture")));
            root.Children.Add(Card(
                ToggleRow(L.T("Тень у снимков окон", "Window shot shadow"), L.T("Мягкая тень, как на Mac. Alt+клик — без тени", "Soft macOS-style shadow. Alt+click — no shadow"), s.WindowShadow, v => s.WindowShadow = v),
                CornersRow(),
                ToggleRow(L.T("Лупа при выделении", "Magnifier while selecting"), L.T("Увеличение, координаты и цвет пикселя под курсором", "Zoom, coordinates and pixel color under the cursor"), s.ShowMagnifier, v => s.ShowMagnifier = v)));

            root.Children.Add(Section(L.T("Система", "System")));
            var sys = new System.Collections.Generic.List<UIElement>
            {
                ToggleRow(L.T("Запускать вместе с Windows", "Start with Windows"), null, Installer.AutoStart, v => Installer.AutoStart = v),
                ToggleRow(L.T("Обновлять автоматически", "Update automatically"), L.T("Новые версии с GitHub ставятся сами, когда вы не делаете снимок", "New versions from GitHub install themselves while you are not capturing"), s.AutoUpdate, v => s.AutoUpdate = v),
                UpdateRow(),
                LanguageRow(),
            };
            if (Installer.IsInstalledCopy)
            {
                var un = new PillButton(L.T("Удалить Кадр", "Uninstall Kadr"), primary: false, danger: true) { HorizontalAlignment = HorizontalAlignment.Right };
                un.Click += () => App.ConfirmUninstall(this);
                sys.Add(Row(L.T("Удаление", "Uninstall"), L.T("Снимки в папке останутся", "Your screenshots stay in the folder"), un));
            }
            root.Children.Add(Card(sys.ToArray()));

            var gh = Link(L.T("Кадр на GitHub", "Kadr on GitHub"));
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
            var btn = new PillButton(L.T("Проверить", "Check"), false);
            bool busy = false;
            btn.Click += async () =>
            {
                if (busy) return;
                busy = true;
                try
                {
                    Status(L.T("Проверяю…", "Checking…"));
                    var r = await Updater.FetchLatestAsync();
                    Settings.Current.LastUpdateCheck = DateTime.Now;
                    if (!Updater.IsNewer(r)) { Status(L.T("У вас последняя версия", "You have the latest version")); return; }
                    if (!Installer.IsInstalledCopy)
                    {
                        Status(L.T($"Доступна версия {r.Version} — откройте страницу релиза", $"Version {r.Version} is available — see the release page"));
                        Process.Start(new ProcessStartInfo(r.PageUrl) { UseShellExecute = true });
                        return;
                    }
                    var progress = new Progress<double>(p => Status(L.T($"Скачиваю {r.Version}… {Math.Round(p * 100)}%", $"Downloading {r.Version}… {Math.Round(p * 100)}%")));
                    var path = await Updater.DownloadAsync(r, progress);
                    Status(L.T("Устанавливаю и перезапускаю…", "Installing and restarting…"));
                    await System.Threading.Tasks.Task.Delay(400);
                    Close();
                    App.Instance.ApplyUpdate(r, path);
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                    Status(L.T("Не удалось: ", "Failed: ") + ex.Message);
                }
                finally { busy = false; }
            };
            var g = Row(L.T($"Версия {Installer.Version}", $"Version {Installer.Version}"), null, btn);
            ((StackPanel)g.Children[0]).Children.Add(status);
            return g;
        }

        Grid LanguageRow()
        {
            var seg = new Segmented(new (FrameworkElement, string)[]
            {
                (new TextBlock { Text = "Русский", FontSize = 12.5, Foreground = Brushes.White }, null),
                (new TextBlock { Text = "English", FontSize = 12.5, Foreground = Brushes.White }, null),
            }, null);
            seg.Selected = L.En ? 1 : 0;
            seg.Picked += i =>
            {
                if (i == seg.Selected) return;
                Settings.Current.Language = i == 1 ? "en" : "ru";
                Settings.Save();
                App.Instance.ApplyLanguage();
                // the window is built from strings once: reopen it in the new language
                Close();
                Dispatcher.BeginInvoke(ShowSingle);
            };
            return Row(L.T("Язык", "Language"), null, seg);
        }

        static Grid CornersRow()
        {
            var seg = new Segmented(new (FrameworkElement, string)[]
            {
                (new TextBlock { Text = L.T("Классика", "Classic"), FontSize = 12.5, Foreground = Brushes.White }, L.T("Как в macOS до 26-й версии", "As in macOS before 26")),
                (new TextBlock { Text = "macOS 26", FontSize = 12.5, Foreground = Brushes.White }, L.T("Круглее, как в macOS 26", "Rounder, as in macOS 26")),
            }, null);
            seg.Selected = Settings.Current.WindowCorners == 0 ? 0 : 1;
            seg.Picked += i => { seg.Selected = i; Settings.Current.WindowCorners = i; Settings.Save(); };
            return Row(L.T("Скругление углов окна", "Window corners"), L.T("Одинаковое для окон любого размера", "Same for windows of any size"), seg);
        }

        Grid FolderRow()
        {
            var path = new TextBlock { FontSize = 12, Foreground = TextDim, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, Margin = new Thickness(0, 0, 12, 0) };
            void Refresh() { path.Text = Settings.Current.Folder; path.ToolTip = Settings.Current.Folder; }
            Refresh();
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            btns.Children.Add(path);
            var open = new PillButton(L.T("Открыть", "Open"), false) { Margin = new Thickness(0, 0, 6, 0) };
            open.Click += () => { Directory.CreateDirectory(Settings.Current.Folder); Process.Start("explorer.exe", $"\"{Settings.Current.Folder}\""); };
            var change = new PillButton(L.T("Изменить…", "Change…"), false);
            change.Click += () =>
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Settings.Current.Folder, Title = L.T("Куда сохранять снимки", "Where to save screenshots") };
                if (dlg.ShowDialog(this) == true) { Settings.Current.Folder = dlg.FolderName; Settings.Save(); Refresh(); }
            };
            btns.Children.Add(open);
            btns.Children.Add(change);
            return Row(L.T("Папка", "Folder"), null, btns);
        }

        // ------------------------------------------------------------------ hotkeys

        void BuildHotkeys()
        {
            _hotkeyCard.Children.Clear();
            _recordingChip = null;
            var actions = new[] { HotkeyAction.Region, HotkeyAction.Window, HotkeyAction.FullScreen };
            string[] subs =
            {
                L.T("Экран замирает. Пробел — режим окна", "The screen freezes. Space — window mode"),
                L.T("Сразу выбор окна — снимок с тенью", "Pick a window right away — shot with shadow"),
                L.T("Монитор под курсором целиком", "The whole monitor under the cursor"),
            };
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
                : new TextBlock { Text = L.T("+ Добавить", "+ Add"), FontSize = 12, Foreground = TextDim, Margin = new Thickness(4, 1, 4, 1) };
            var chip = new Border
            {
                Child = content, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 5, 4, 5), Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Transparent, BorderBrush = binding == null ? Theme.Hairline : Brushes.Transparent, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, ToolTip = binding == null ? L.T("Добавить сочетание", "Add a shortcut") : L.T("Изменить", "Change"),
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
            chip.Child = new TextBlock { Text = L.T("Нажмите сочетание…", "Press a shortcut…"), FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(6, 1, 6, 1) };
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
