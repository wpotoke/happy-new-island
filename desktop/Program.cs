using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace WinterIsland;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return AppBuilder.Configure<IslandApp>().UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
    }
}

internal sealed class IslandApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new IslandWindow(desktop.Shutdown);
            desktop.MainWindow = window;
            bool check = desktop.Args?.Contains("--smoke-test") == true;
            string? preview = desktop.Args?.SkipWhile(arg => arg != "--preview").Skip(1).FirstOrDefault();
            if (!check && preview == null)
            {
                var menu = new NativeMenu();
                var show = new NativeMenuItem(IslandWindow.Word("Показать остров", "Show island"));
                show.Click += (_, _) => window.RevealDetails();
                var pin = new NativeMenuItem(IslandWindow.Word("Закрепить или спрятать", "Pin or hide"));
                pin.Click += (_, _) => window.TogglePin();
                var exit = new NativeMenuItem(IslandWindow.Word("Выйти", "Quit"));
                exit.Click += (_, _) => desktop.Shutdown();
                menu.Items.Add(show);
                menu.Items.Add(pin);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(exit);
                using var iconBitmap = WinterArt.Image("app-icon.png");
                var icon = new TrayIcon
                {
                    Icon = new WindowIcon(iconBitmap), ToolTipText = "Happy New Island", Menu = menu,
                    IsVisible = true
                };
                icon.Clicked += (_, _) => window.RevealDetails();
                TrayIcon.SetIcons(this, new TrayIcons { icon });
            }
            else window.VerifyAfterOpening(preview);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
