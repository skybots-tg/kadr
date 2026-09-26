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
            Title = uninstall ? "Удаление Кадра" : "Установка Кадра";
            Width = 460; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new StackPanel { Margin = new Thickness(32, 28, 32, 26) };
            if (App.AppIconImage != null)
                root.Children.Add(new Image { Source = App.AppIconImage, Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-4, 0, 0, 14) });

            if (!uninstall)
            {
                root.Children.Add(Text("Кадр", 26, weight: FontWeights.SemiBold));
                root.Children.Add(Text("Скриншоты как на Mac — для Windows", 14, dim: true));
                var list = new StackPanel { Margin = new Thickness(0, 18, 0, 6) };
                foreach (var (keys, what) in new[]
                {
                    (new[] { "PrtSc" }, "снимок области, экран замирает"),
                    (new[] { "Пробел" }, "снимок окна с тенью"),
                    (new[] { "A", "T", "B" }, "стрелки, текст, размытие на месте"),
                    (new[] { "Enter" }, "скопировать и готово"),
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
                autoRow.Children.Add(Text("Запускать вместе с Windows", 13.5));
                Grid.SetColumn(auto, 1);
                autoRow.Children.Add(auto);
                root.Children.Add(autoRow);

                bool update = Installer.IsInstalled && !promo;
                var hint = Text(update ? "Кадр уже установлен — версия будет обновлена." : "Установится только для вас, права администратора не нужны.", 12, dim: true);
                hint.Margin = new Thickness(0, 10, 0, 0);
                root.Children.Add(hint);

                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
                var portable = new PillButton("Без установки", primary: false) { Margin = new Thickness(0, 0, 8, 0) };
                var install = new PillButton(update ? "Обновить" : "Установить", primary: true);
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
                        hint.Text = "Не удалось установить: " + ex.Message;
                        hint.Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 97));
                    }
                };
                buttons.Children.Add(portable);
                buttons.Children.Add(install);
                root.Children.Add(buttons);
            }
            else
            {
                root.Children.Add(Text("Удалить Кадр?", 22, weight: FontWeights.SemiBold));
                var sub = Text("Приложение, ярлык, автозапуск и настройки будут удалены. Ваши снимки останутся в папке.", 13.5, dim: true);
                sub.Margin = new Thickness(0, 6, 0, 0);
                root.Children.Add(sub);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
                var cancel = new PillButton("Отмена", primary: false) { Margin = new Thickness(0, 0, 8, 0) };
                var remove = new PillButton("Удалить", primary: true);
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
