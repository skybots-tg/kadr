using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kadr.Core;

namespace Kadr.UI
{
    /// <summary>First-run installer card and the uninstall confirmation, sharing one compact layout.</summary>
    public sealed class InstallWindow : DarkWindow
    {
        public enum Result { None, Installed, Portable, Uninstalled }
        public Result Outcome { get; private set; }

        public static InstallWindow ForInstall(bool promo = false) => new(uninstall: false, promo);
        public static InstallWindow ForUninstall() => new(uninstall: true, false);

        InstallWindow(bool uninstall, bool promo)
        {
            Title = uninstall ? L.T("Удаление Кадра", "Uninstall Kadr") : L.T("Установка Кадра", "Install Kadr");
            Width = 460; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new StackPanel { Margin = new Thickness(32, 28, 32, 26) };
            if (App.AppIconImage != null)
                root.Children.Add(new Image { Source = App.AppIconImage, Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-4, 0, 0, 14) });

            if (!uninstall)
            {
                root.Children.Add(Text(L.AppName, 26, weight: FontWeights.SemiBold));
                root.Children.Add(Text(L.T("Скриншоты как на Mac — для Windows", "macOS-style screenshots for Windows"), 14, dim: true));
                var list = new StackPanel { Margin = new Thickness(0, 18, 0, 6) };
                foreach (var (keys, what) in new[]
                {
                    (new[] { "PrtSc" }, L.T("снимок области, экран замирает", "capture a region on a frozen screen")),
                    (new[] { L.T("Пробел", "Space") }, L.T("снимок окна с тенью", "window shot with shadow")),
                    (new[] { "A", "T", "B" }, L.T("стрелки, текст, размытие на месте", "arrows, text, blur in place")),
                    (new[] { "Enter" }, L.T("скопировать и готово", "copy and done")),
                })
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                    var caps = KeyCaps.Make(keys);
                    caps.MinWidth = 96;
                    row.Children.Add(caps);
                    row.Children.Add(Text(what, 13.5));
                    list.Children.Add(row);
                }
                root.Children.Add(list);

                var auto = new ToggleSwitch(true);
                var autoRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
                autoRow.ColumnDefinitions.Add(new ColumnDefinition());
                autoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                autoRow.Children.Add(Text(L.T("Запускать вместе с Windows", "Start with Windows"), 13.5));
                Grid.SetColumn(auto, 1);
                autoRow.Children.Add(auto);
                root.Children.Add(autoRow);

                bool update = Installer.IsInstalled && !promo;
                var hint = Text(update ? L.T("Кадр уже установлен — версия будет обновлена.", "Kadr is already installed — it will be updated.") : L.T("Установится только для вас, права администратора не нужны.", "Installs for you only, no admin rights needed."), 12, dim: true);
                hint.Margin = new Thickness(0, 10, 0, 0);
                root.Children.Add(hint);

                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
                var portable = new PillButton(L.T("Без установки", "Run without installing"), primary: false) { Margin = new Thickness(0, 0, 8, 0) };
                var install = new PillButton(update ? L.T("Обновить", "Update") : L.T("Установить", "Install"), primary: true);
                portable.Click += () => { Outcome = Result.Portable; Close(); };
                install.Click += () =>
                {
                    try
                    {
                        Installer.Install(auto.IsOn);
                        Outcome = Result.Installed;
                        Close();
                    }
                    catch (Exception ex)
                    {
                        App.Log(ex);
                        hint.Text = L.T("Не удалось установить: ", "Installation failed: ") + ex.Message;
                        hint.Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 97));
                    }
                };
                buttons.Children.Add(portable);
                buttons.Children.Add(install);
                root.Children.Add(buttons);
            }
            else
            {
                root.Children.Add(Text(L.T("Удалить Кадр?", "Uninstall Kadr?"), 22, weight: FontWeights.SemiBold));
                var sub = Text(L.T("Приложение, ярлык, автозапуск и настройки будут удалены. Ваши снимки останутся в папке.", "The app, its shortcut, autostart and settings will be removed. Your screenshots stay in their folder."), 13.5, dim: true);
                sub.Margin = new Thickness(0, 6, 0, 0);
                root.Children.Add(sub);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
                var cancel = new PillButton(L.T("Отмена", "Cancel"), primary: false) { Margin = new Thickness(0, 0, 8, 0) };
                var remove = new PillButton(L.T("Удалить", "Uninstall"), primary: true);
                cancel.Click += Close;
                remove.Click += () =>
                {
                    try { Installer.Uninstall(); } catch (Exception ex) { App.Log(ex); }
                    Outcome = Result.Uninstalled;
                    Close();
                };
                buttons.Children.Add(cancel);
                buttons.Children.Add(remove);
                root.Children.Add(buttons);
            }
            Content = root;
        }
    }
}
