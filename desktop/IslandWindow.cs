using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace WinterIsland;

internal sealed class IslandWindow : Window
{
    private const int CapsuleWidth = 342, CapsuleHeight = 62, PanelWidth = 476, PanelHeight = 325;
    private static readonly Color BackgroundColor = Color.Parse("#0B0C0F");
    private static readonly IBrush Surface = new SolidColorBrush(BackgroundColor);
    private static readonly IBrush Hairline = WinterArt.Brush("#32363B");
    private static readonly IBrush Ink = WinterArt.Brush("#F3F4F2");
    private static readonly IBrush Subtle = WinterArt.Brush("#ABB8BB");
    private static readonly IBrush Gold = WinterArt.Brush("#FFE4B7");
    private static readonly IBrush Finished = WinterArt.Brush("#B8E5C9");
    private static readonly bool Russian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
    private enum Page { Closed, Apps, Pet, Modes, Timer }

    private readonly Action<int> exit;
    private readonly DesktopProfile settings = DesktopProfile.Load();
    private readonly DesktopPlatform platform = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly FocusCountdown focus = new();
    private readonly DispatcherTimer heartbeat, motion;
    private readonly GarlandView garland = new();
    private readonly FallingSnow flakes = new();
    private readonly PortableLeopard leopard = new();
    private readonly SnowballGame snowball = new();
    private readonly Border details, focusBar;
    private readonly StackPanel capsulePins = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel layout = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock time = Text("", 14, Gold);
    private readonly TextBlock date = Text("", 10, Subtle);
    private readonly TextBlock battery = Text("", 12, Ink);
    private readonly TextBlock remaining = Text("00:25:00", 35, Gold);
    private readonly TextBlock mediaNote = Text("", 12, Subtle);
    private readonly TextBlock message = Text("", 12, Subtle);
    private readonly Button calendar, caret, startTimer;
    private readonly TextBox hours = TimeField("00"), minutes = TimeField("25"), seconds = TimeField("00");
    private readonly Dictionary<string, Bitmap?> shortcutIcons = new(StringComparer.Ordinal);
    private readonly HashSet<string> loadingIcons = [];
    private FloatingLayer? catLayer, snowLayer, gameLayer;
    private DrawSurface? inlinePet;
    private Page page;
    private bool pinned, chord, edgeLatched, playing, game, shuttingDown, readingBattery, readingMedia, dialog, effectsAvailable;
    private double keepUntil = 6, edgeSince = -1, previousFrame, nextBattery, nextMedia, nextGarland, lastGameHit;
    private long minuteShown = -1;
    private int timerSecondsShown = -1;
    private PixelPoint? pointer;
    private PixelSize gameScreen;
    private string? player;

    public static string Word(string ru, string en) => Russian ? ru : en;

    public IslandWindow(Action<int> exit)
    {
        this.exit = exit;
        effectsAvailable = platform.SupportsEdge;
        pinned = !settings.AutoHide || !effectsAvailable;
        Title = "Happy New Island";
        ShowInTaskbar = !effectsAvailable;
        ShowActivated = false;
        Topmost = effectsAvailable;
        Background = effectsAvailable ? Brushes.Transparent : Surface;
        TransparencyLevelHint = effectsAvailable ? [WindowTransparencyLevel.Transparent] : [WindowTransparencyLevel.None];
        SystemDecorations = effectsAvailable ? SystemDecorations.None : SystemDecorations.Full;
        CanResize = false;
        MinWidth = CapsuleWidth;
        Width = CapsuleWidth;
        Height = CapsuleHeight;

        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*,31,58,55"), Margin = new Thickness(9, 0) };
        Place(fields, ActionButton("❄", () => SetPage(page == Page.Closed ? Page.Apps : Page.Closed),
            Word("Приложения", "Apps")), 0);
        Place(fields, capsulePins, 1);
        caret = ActionButton("⌄", () => SetPage(page == Page.Closed ? Page.Apps : Page.Closed), Word("Раскрыть остров", "Expand island"));
        Place(fields, caret, 2);
        time.TextAlignment = date.TextAlignment = TextAlignment.Center;
        focusBar = new Border { Height = 2, Background = Gold, CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, IsVisible = false };
        var readings = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,3"), HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(date, 1);
        Grid.SetRow(focusBar, 2);
        readings.Children.Add(time);
        readings.Children.Add(date);
        readings.Children.Add(focusBar);
        calendar = ActionButton(readings, () => SetPage(Page.Timer));
        Place(fields, calendar, 3);
        battery.HorizontalAlignment = HorizontalAlignment.Center;
        battery.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(battery, Word("Заряд батареи", "Battery charge"));
        Place(fields, battery, 4);
        var shell = new Border { CornerRadius = new CornerRadius(23), Background = Surface, BorderBrush = Hairline,
            BorderThickness = new Thickness(1), Margin = new Thickness(7, 13, 7, 5), Child = fields };
        var capsule = new Grid { Width = CapsuleWidth, Height = CapsuleHeight, Children = { shell, garland } };
        layout.Children.Add(capsule);
        details = new Border { Width = PanelWidth - 12, Height = PanelHeight - 8, Margin = new Thickness(6, 0, 6, 8),
            Background = Surface, BorderBrush = Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22),
            Padding = new Thickness(16, 13), IsVisible = false };
        layout.Children.Add(details);
        Content = layout;
        startTimer = ActionButton(Word("Запустить", "Start"), StartOrPause);

        RenderPins();
        ApplyAppearance();
        ReadClock();
        KeyDown += (_, keys) =>
        {
            if (keys.Key == Key.Escape)
            {
                if (game) ToggleGame();
                else if (page != Page.Closed) SetPage(Page.Closed);
                keys.Handled = true;
            }
            else if (!platform.SupportsEdge && keys.Key == Key.G &&
                keys.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt))
            {
                TogglePin();
                keys.Handled = true;
            }
        };
        heartbeat = new DispatcherTimer(TimeSpan.FromMilliseconds(85), DispatcherPriority.Background, (_, _) => PollDesktop());
        motion = new DispatcherTimer(TimeSpan.FromMilliseconds(32), DispatcherPriority.Render, (_, _) => Frame());
        Opened += (_, _) =>
        {
            Align();
            if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
            {
                effectsAvailable = false;
                pinned = true;
                ShowInTaskbar = true;
                Topmost = false;
            }
            heartbeat.Start();
            motion.Start();
        };
        Closed += (_, _) => CleanUp();
    }

    private static TextBlock Text(string value, double size, IBrush color) => new()
    {
        Text = value, FontSize = size, Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static TextBox TimeField(string value) => new()
    {
        Text = value, Width = 54, MaxLength = 2, HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static Button ActionButton(object content, Action action, string? hint = null)
    {
        var button = new Button { Content = content, Background = Brushes.Transparent, Foreground = Ink,
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 4),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        button.Click += (_, _) => action();
        if (hint != null) { ToolTip.SetTip(button, hint); AutomationProperties.SetName(button, hint); }
        return button;
    }

    private static void Place(Grid grid, Control control, int column) { Grid.SetColumn(control, column); grid.Children.Add(control); }

    public void RevealDetails()
    {
        ShowIsland();
        SetPage(Page.Apps);
        Activate();
    }

    public void TogglePin()
    {
        pinned = !pinned;
        if (pinned || !effectsAvailable) ShowIsland();
        else if (!game && !playing) HideIsland();
        if (page == Page.Modes) RenderDetails();
    }

    private void ShowIsland()
    {
        keepUntil = clock.Elapsed.TotalSeconds + 2.4;
        Align();
        if (!IsVisible) Show();
        if (!motion.IsEnabled) { previousFrame = clock.Elapsed.TotalSeconds; motion.Start(); }
    }

    private void HideIsland()
    {
        Hide();
        motion.Stop();
        catLayer?.Hide(); snowLayer?.Hide(); gameLayer?.Hide();
    }

    private void Align()
    {
        if (Screens.Primary is not { } screen) return;
        PixelRect work = screen.WorkingArea;
        double scale = screen.Scaling;
        Position = new PixelPoint(work.X + (work.Width - (int)Math.Round(Width * scale)) / 2, work.Y + (int)Math.Round(3 * scale));
    }

    private void PollDesktop()
    {
        if (shuttingDown || Screens.Primary is not { } screen) return;
        double now = clock.Elapsed.TotalSeconds;
        PixelRect work = screen.WorkingArea;
        double dpi = screen.Scaling;
        pointer = platform.TryPointer(dpi, out PixelPoint mouse) ? mouse : null;
        bool pressed = platform.RevealChord();
        if (pressed && !chord) TogglePin();
        chord = pressed;

        if (effectsAvailable && settings.AutoHide && !pinned && !playing && !game && !dialog && pointer.HasValue)
        {
            bool atEdge = mouse.Y >= screen.Bounds.Y && mouse.Y <= work.Y + 4 * dpi &&
                Math.Abs(mouse.X - (work.X + work.Width * .5)) <= 61 * dpi;
            if (!atEdge) { edgeSince = -1; edgeLatched = false; }
            else if (edgeSince < 0) edgeSince = now;
            if (!IsVisible && atEdge && !edgeLatched && now - edgeSince > .39)
            {
                edgeLatched = true;
                ShowIsland();
            }
            if (IsVisible)
            {
                var area = new PixelRect(Position, new PixelSize((int)(Width * dpi), (int)(Height * dpi)));
                if (area.Contains(mouse) || (catLayer?.IsVisible == true && leopard.ScreenArea.Contains(mouse)) || atEdge)
                    keepUntil = now + 2.2;
                else if (now > keepUntil) HideIsland();
            }
        }
        ReadClock();
        if (IsVisible) ReadDesktopStatus(now);
    }

    private void ReadClock()
    {
        DateTime today = DateTime.Now;
        long minute = today.Ticks / TimeSpan.TicksPerMinute;
        if (minute != minuteShown)
        {
            minuteShown = minute;
            time.Text = today.ToString("HH:mm", CultureInfo.InvariantCulture);
            date.Text = today.ToString("dd.MM", CultureInfo.InvariantCulture);
            string hint = today.ToString("D", CultureInfo.CurrentCulture);
            ToolTip.SetTip(calendar, hint);
            AutomationProperties.SetName(calendar, time.Text + " · " + hint);
        }
        double now = clock.Elapsed.TotalSeconds;
        if (focus.Tick(now)) leopard.FocusEnded();
        focusBar.IsVisible = focus.Stage != FocusStage.Idle;
        focusBar.Width = Math.Max(1, 28 * focus.Progress(now));
        focusBar.Background = focus.Stage == FocusStage.Completed ? Finished : Gold;
        if (page == Page.Timer)
        {
            int duration = focus.Stage == FocusStage.Idle ? CustomDuration() : focus.SecondsLeft(now);
            duration = Math.Max(0, duration);
            if (timerSecondsShown != duration)
            {
                timerSecondsShown = duration;
                remaining.Text = FormattableString.Invariant($"{duration / 3600:00}:{duration % 3600 / 60:00}:{duration % 60:00}");
            }
            startTimer.Content = focus.Stage == FocusStage.Running ? Word("Пауза", "Pause") :
                focus.Stage == FocusStage.Paused ? Word("Продолжить", "Resume") : Word("Запустить", "Start");
        }
    }

    private async void ReadDesktopStatus(double now)
    {
        if (!readingBattery && now >= nextBattery)
        {
            nextBattery = now + 20;
            readingBattery = true;
            try
            {
                int? charge = await platform.BatteryAsync();
                if (!shuttingDown) battery.Text = charge.HasValue ? charge.Value.ToString(CultureInfo.InvariantCulture) + "%" : "";
            }
            finally { readingBattery = false; }
        }
        if (readingMedia || !platform.SupportsMedia || now < nextMedia || page != Page.Apps) return;
        readingMedia = true;
        nextMedia = now + 3;
        try
        {
            string? state = await DesktopPlatform.Capture("playerctl", "metadata", "--format", "{{playerName}}\t{{status}}\t{{title}}");
            if (shuttingDown) return;
            string[] fields = (state ?? "").Split('\t', 3);
            player = fields.Length == 3 ? fields[0] : null;
            mediaNote.Text = player == null ? "" : (fields[1] == "Playing" ? "▶  " : "Ⅱ  ") + fields[2];
        }
        finally { readingMedia = false; }
    }

    private void Frame()
    {
        if (!IsVisible || shuttingDown || Screens.Primary is not { } screen) return;
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - previousFrame, 0, .062);
        previousFrame = now;
        if (settings.Garland && now >= nextGarland)
        {
            garland.Moment = now;
            garland.InvalidateVisual();
            nextGarland = now + .09;
        }
        double dpi = screen.Scaling;
        PixelRect work = screen.WorkingArea;
        var center = new PixelPoint(work.X + work.Width / 2, Position.Y + (int)((CapsuleHeight + 69) * dpi));
        if (settings.Snow && effectsAvailable)
        {
            flakes.Advance(dt, CapsuleWidth, settings.Mode);
            snowLayer ??= new FloatingLayer(flakes.Paint);
            snowLayer.Place(platform, CapsuleWidth, 143, new PixelPoint(center.X - (int)(CapsuleWidth * dpi / 2),
                Position.Y + (int)((CapsuleHeight - 4) * dpi)));
        }
        if (settings.Leopard && (effectsAvailable || inlinePet?.IsVisible == true))
        {
            PixelPoint at = leopard.Advance(dt, center, dpi, CapsuleWidth, work, effectsAvailable ? pointer : null, playing, settings);
            if (effectsAvailable)
            {
                catLayer ??= new FloatingLayer(leopard.Paint);
                catLayer.Place(platform, PortableLeopard.Width, PortableLeopard.Height, at);
            }
            inlinePet?.InvalidateVisual();
        }
        if (game)
        {
            if (work.Size != gameScreen)
            {
                gameScreen = work.Size;
                snowball.Reset(work.Width / dpi, work.Height / dpi);
            }
            Point ball = snowball.Advance(dt, pointer.HasValue ? (pointer.Value.X - work.X) / dpi : work.Width / dpi / 2,
                CapsuleWidth, (Position.Y - work.Y) / dpi + CapsuleHeight);
            if (settings.Leopard && now - lastGameHit > 1.1 && leopard.ScreenArea.Contains(new PixelPoint(
                work.X + (int)(ball.X * dpi), work.Y + (int)(ball.Y * dpi))))
            {
                leopard.Shake();
                lastGameHit = now;
            }
            gameLayer ??= new FloatingLayer(snowball.Paint);
            if (!gameLayer.Place(platform, work.Width / dpi, work.Height / dpi, work.Position)) ToggleGame();
        }
    }

    private void SetPage(Page selected)
    {
        page = selected;
        details.IsVisible = selected != Page.Closed;
        caret.Content = selected == Page.Closed ? "⌄" : "⌃";
        Width = selected == Page.Closed ? CapsuleWidth : PanelWidth;
        Height = selected == Page.Closed ? CapsuleHeight : CapsuleHeight + PanelHeight;
        inlinePet = null;
        if (selected != Page.Closed) RenderDetails();
        keepUntil = clock.Elapsed.TotalSeconds + 2.5;
        Align();
    }

    private void RenderDetails()
    {
        inlinePet = null;
        message.Text = "";
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach (var (tab, label) in new[] { (Page.Apps, Word("Приложения", "Apps")),
            (Page.Pet, Word("Барс", "Leopard")), (Page.Modes, Word("Режимы", "Modes")), (Page.Timer, Word("Таймер", "Timer")) })
        {
            Button item = ActionButton(label, () => SetPage(tab));
            item.Foreground = tab == page ? Gold : Subtle;
            tabs.Children.Add(item);
        }
        var content = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4) };
        switch (page)
        {
            case Page.Apps: AppsPage(content); break;
            case Page.Pet: PetPage(content); break;
            case Page.Modes: ModesPage(content); break;
            case Page.Timer: TimerPage(content); break;
        }
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var frame = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tabs, Dock.Top);
        frame.Children.Add(tabs);
        Detach(message);
        DockPanel.SetDock(message, Dock.Bottom);
        frame.Children.Add(message);
        frame.Children.Add(scroll);
        details.Child = frame;
        ReadClock();
    }

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private static TextBlock Section(string title) => new() { Text = title, FontSize = 17, Foreground = Ink,
        FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 2) };

    private void AppsPage(StackPanel content)
    {
        content.Children.Add(Section(Word("Закреплённые", "Pinned apps")));
        var icons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (AppShortcut app in settings.Pins) icons.Children.Add(PinButton(app, true));
        icons.Children.Add(ActionButton("+", async () => await PickApp(), Word("Закрепить приложение", "Pin an app")));
        content.Children.Add(icons);
        if (platform.SupportsMedia)
        {
            content.Children.Add(Section(Word("Воспроизведение", "Playback")));
            if (mediaNote.Parent is Panel oldPanel) oldPanel.Children.Remove(mediaNote);
            content.Children.Add(mediaNote);
            var buttons = Row();
            buttons.Children.Add(ActionButton("◀", () => MediaCommand("previous"), Word("Предыдущий", "Previous")));
            buttons.Children.Add(ActionButton("▶ / Ⅱ", () => MediaCommand("play-pause"), Word("Воспроизведение или пауза", "Play or pause")));
            buttons.Children.Add(ActionButton("▶", () => MediaCommand("next"), Word("Следующий", "Next")));
            content.Children.Add(buttons);
        }
    }

    private void PetPage(StackPanel content)
    {
        content.Children.Add(Section(Word("Снежный барс", "Snow leopard")));
        if (!effectsAvailable && settings.Leopard)
        {
            inlinePet = new DrawSurface(leopard.Paint) { Width = 206, Height = 113, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(inlinePet);
        }
        content.Children.Add(Text(settings.GrowthDays >= 7 ? Word("Взрослый барс", "Adult leopard") :
            Word($"Рост · {settings.GrowthDays} из 7 дней", $"Growth · {settings.GrowthDays} of 7 days"), 12, Subtle));
        content.Children.Add(Section(Word("Покормить", "Feed")));
        var dishes = Row();
        dishes.Children.Add(ActionButton(Word("🥩  Мясо", "🥩  Meat"), () => Feed(0)));
        dishes.Children.Add(ActionButton(Word("🐟  Рыба", "🐟  Fish"), () => Feed(1)));
        dishes.Children.Add(ActionButton(Word("🍪  Лакомство", "🍪  Treat"), () => Feed(2)));
        content.Children.Add(dishes);
        var play = Row();
        play.Children.Add(ActionButton(playing ? Word("Закончить игру", "Stop playing") : Word("Поиграть с барсом", "Play with leopard"), () =>
        {
            if (!effectsAvailable || !settings.Leopard) { SetMessage(Word("Включи барса в режиме рабочего стола", "Enable the leopard in desktop mode")); return; }
            playing = !playing;
            SetPage(playing ? Page.Closed : Page.Pet);
        }));
        play.Children.Add(ActionButton(game ? Word("Остановить снежок", "Stop snowball") : Word("Снежок", "Snowball"), ToggleGame));
        content.Children.Add(play);
    }

    private void ModesPage(StackPanel content)
    {
        content.Children.Add(Section(Word("Настроение острова", "Island mood")));
        var presets = Row();
        foreach (var (mode, label) in new[] { (WinterMode.Holiday, Word("Праздник", "Holiday")),
            (WinterMode.Cozy, Word("Уют", "Cozy")), (WinterMode.Focus, Word("Фокус", "Focus")) })
        {
            var item = ActionButton(label, () => { settings.Apply(mode); ApplyAppearance(); SaveSettings(); RenderDetails(); });
            if (settings.Mode == mode) item.Foreground = Gold;
            presets.Children.Add(item);
        }
        content.Children.Add(presets);
        AddSwitch(content, Word("Снег", "Snow"), settings.Snow, value => settings.Snow = value);
        AddSwitch(content, Word("Гирлянда", "Garland"), settings.Garland, value => settings.Garland = value);
        AddSwitch(content, Word("Снежный барс", "Snow leopard"), settings.Leopard, value => settings.Leopard = value);
        if (effectsAvailable)
            AddSwitch(content, Word("Автоматически прятать остров", "Automatically hide island"),
                settings.AutoHide, value => { settings.AutoHide = value; pinned = !value; });
        content.Children.Add(ActionButton(Word("Выйти из Happy New Island", "Quit Happy New Island"), () => exit(0)));
    }

    private void AddSwitch(StackPanel panel, string label, bool value, Action<bool> change)
    {
        var toggle = new ToggleSwitch { Content = label, IsChecked = value, OnContent = "", OffContent = "" };
        toggle.IsCheckedChanged += (_, _) => { change(toggle.IsChecked == true); ApplyAppearance(); SaveSettings(); };
        panel.Children.Add(toggle);
    }

    private void TimerPage(StackPanel content)
    {
        content.Children.Add(Section(Word("Таймер фокуса", "Focus timer")));
        Detach(remaining); Detach(startTimer);
        content.Children.Add(remaining);
        var fields = Row();
        string[] labels = [Word("Часы", "Hours"), Word("Минуты", "Minutes"), Word("Секунды", "Seconds")];
        TextBox[] inputs = [hours, minutes, seconds];
        for (int i = 0; i < inputs.Length; i++)
        {
            Detach(inputs[i]);
            var field = new StackPanel { Spacing = 4 };
            field.Children.Add(Text(labels[i], 12, Subtle));
            AutomationProperties.SetName(inputs[i], labels[i]);
            field.Children.Add(inputs[i]);
            fields.Children.Add(field);
        }
        content.Children.Add(fields);
        var actions = Row();
        actions.Children.Add(startTimer);
        actions.Children.Add(ActionButton(Word("Сбросить", "Reset"), () => { focus.Reset(); ReadClock(); SetMessage(""); }));
        content.Children.Add(actions);
    }

    private static void Detach(Control control) { if (control.Parent is Panel panel) panel.Children.Remove(control); }

    private int CustomDuration() =>
        int.TryParse(hours.Text, out int h) && int.TryParse(minutes.Text, out int m) && int.TryParse(seconds.Text, out int s) &&
        h is >= 0 and <= 99 && m is >= 0 and <= 59 && s is >= 0 and <= 59 ? h * 3600 + m * 60 + s : 0;

    private void StartOrPause()
    {
        if (focus.Stage is FocusStage.Running or FocusStage.Paused) focus.PauseOrContinue(clock.Elapsed.TotalSeconds);
        else if (CustomDuration() is int duration && duration > 0) focus.Start(duration, clock.Elapsed.TotalSeconds);
        else { SetMessage(Word("Введи время от 00:00:01 до 99:59:59", "Enter a duration from 00:00:01 to 99:59:59")); return; }
        SetMessage("");
        ReadClock();
    }

    private void Feed(int food)
    {
        if (!settings.Leopard) { SetMessage(Word("Барс выключен в режимах", "The leopard is disabled in Modes")); return; }
        settings.Feed(DateOnly.FromDateTime(DateTime.Now));
        leopard.Feed();
        SaveSettings();
        RenderDetails();
        if (food == settings.FavoriteFood) SetMessage(Word("Любимое лакомство!", "A favorite!"));
    }

    private void ToggleGame()
    {
        if (!effectsAvailable) { SetMessage(Word("Снежок доступен в режиме рабочего стола", "Snowball requires desktop overlay mode")); return; }
        game = !game;
        gameScreen = default;
        if (game) { playing = false; SetPage(Page.Closed); }
        else { gameLayer?.Hide(); SetPage(Page.Pet); }
    }

    private void ApplyAppearance()
    {
        garland.IsVisible = settings.Garland;
        garland.Mode = settings.Mode;
        garland.Drifts = settings.Snow && settings.Mode == WinterMode.Holiday;
        garland.InvalidateVisual();
        if (!settings.Snow) snowLayer?.Hide();
        if (!settings.Leopard) { catLayer?.Hide(); playing = false; }
    }

    private void RenderPins()
    {
        capsulePins.Children.Clear();
        foreach (AppShortcut pin in settings.Pins.Take(3)) capsulePins.Children.Add(PinButton(pin, false));
    }

    private Button PinButton(AppShortcut app, bool wide)
    {
        var slot = new ContentControl { Content = Text(app.Name[..1].ToUpperInvariant(), 14, Gold) };
        ShowPinIcon(slot, app);
        Button button = ActionButton(slot, () => OpenApp(app), app.Name);
        button.Width = wide ? 41 : 30;
        button.Height = 33;
        button.Padding = new Thickness(3);
        if (wide)
        {
            var menu = new ContextMenu();
            menu.Items.Add(PinAction(Word("Влево", "Move left"), () => MovePin(app, -1)));
            menu.Items.Add(PinAction(Word("Вправо", "Move right"), () => MovePin(app, 1)));
            menu.Items.Add(PinAction(Word("Открепить", "Unpin"), () => { settings.Pins.Remove(app); PinsChanged(); }));
            button.ContextMenu = menu;
        }
        DragDrop.SetAllowDrop(button, true);
        button.AddHandler(DragDrop.DragOverEvent, (_, drop) =>
            drop.DragEffects = drop.DataTransfer.TryGetFiles()?.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None);
        button.AddHandler(DragDrop.DropEvent, (_, drop) =>
        {
            string[] paths = drop.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            if (paths.Length > 0) OpenApp(app, paths);
            drop.Handled = paths.Length > 0;
        });
        return button;
    }

    private async void ShowPinIcon(ContentControl slot, AppShortcut app)
    {
        if (!shortcutIcons.TryGetValue(app.Target, out Bitmap? current))
        {
            if (!loadingIcons.Add(app.Target)) return;
            try
            {
                string? path = await DesktopApps.IconFileAsync(app);
                if (shuttingDown) return;
                current = path != null ? new Bitmap(path) : null;
                shortcutIcons[app.Target] = current;
                if (current != null && page == Page.Apps)
                    Dispatcher.UIThread.Post(() => { if (!shuttingDown && page == Page.Apps) RenderDetails(); });
            }
            catch (IOException) { }
            catch (ArgumentException) { }
            finally { loadingIcons.Remove(app.Target); }
        }
        if (current != null) slot.Content = new Image { Source = current, Width = 23, Height = 23 };
    }

    private static MenuItem PinAction(string name, Action run)
    {
        var item = new MenuItem { Header = name };
        item.Click += (_, _) => run();
        return item;
    }

    private void MovePin(AppShortcut app, int shift)
    {
        int old = settings.Pins.IndexOf(app), next = Math.Clamp(old + shift, 0, Math.Max(0, settings.Pins.Count - 1));
        if (old < 0 || old == next) return;
        settings.Pins.RemoveAt(old);
        settings.Pins.Insert(next, app);
        PinsChanged();
    }

    private void PinsChanged() { SaveSettings(); RenderPins(); if (page == Page.Apps) RenderDetails(); }

    private void OpenApp(AppShortcut app, IReadOnlyList<string>? paths = null)
    {
        if (DesktopApps.Open(app, paths)) { SetPage(Page.Closed); if (!pinned && effectsAvailable) HideIsland(); }
        else SetMessage(Word("Не удалось открыть приложение", "Could not open the application"));
    }

    private async Task PickApp()
    {
        dialog = true;
        try
        {
            var catalog = DesktopApps.Discover();
            var chooser = new Window { Title = Word("Закрепить приложение", "Pin an app"), Width = 430, Height = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = true };
            var list = new ListBox { ItemsSource = catalog.Select(app => app.Name).ToArray() };
            var pin = ActionButton(Word("Закрепить", "Pin"), () =>
            {
                if (list.SelectedIndex >= 0) chooser.Close(catalog[list.SelectedIndex]);
            });
            var file = ActionButton(Word("Выбрать файл…", "Choose a file…"), async () =>
            {
                var items = await chooser.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Word("Выбрать ярлык или приложение", "Choose a shortcut or application"), AllowMultiple = false
                });
                if (items.FirstOrDefault()?.TryGetLocalPath() is string path) chooser.Close(DesktopApps.ShortcutFor(path));
            });
            var footer = Row();
            footer.Children.Add(pin); footer.Children.Add(file);
            DockPanel.SetDock(footer, Dock.Bottom);
            chooser.Content = new DockPanel { Margin = new Thickness(12), LastChildFill = true, Children = { footer, list } };
            AppShortcut? selected = await chooser.ShowDialog<AppShortcut?>(this);
            if (selected != null)
            {
                settings.Pins.RemoveAll(app => app.Target == selected.Target);
                settings.Pins.Insert(0, selected);
                if (settings.Pins.Count > 12) settings.Pins.RemoveAt(12);
                PinsChanged();
            }
        }
        finally { dialog = false; keepUntil = clock.Elapsed.TotalSeconds + 2.2; }
    }

    private void MediaCommand(string command)
    {
        if (player != null) DesktopPlatform.Launch("playerctl", "-p", player, command);
    }

    private void SetMessage(string value) => message.Text = value;
    private void SaveSettings() { if (!settings.Save()) SetMessage(Word("Не удалось сохранить настройки", "Could not save settings")); }

    public void VerifyAfterOpening(string? preview)
    {
        Opened += (_, _) => DispatcherTimer.RunOnce(() =>
        {
            try
            {
                SetPage(Page.Timer);
                StartOrPause();
                if (focus.Stage != FocusStage.Running || CustomDuration() != 1500 || string.IsNullOrEmpty(date.Text))
                    throw new InvalidOperationException("Clock and focus timer failed to initialize.");
                SetPage(Page.Closed);
                using var pet = new PortableLeopard();
                pet.Preview();
                if (preview != null)
                {
                    time.Text = "20:26";
                    date.Text = "31.12";
                    battery.Text = "82%";
                    Capture(layout, CapsuleWidth, CapsuleHeight, preview + "-panel.png");
                    var art = new DrawSurface(pet.Paint);
                    Capture(art, PortableLeopard.Width, PortableLeopard.Height, preview + "-leopard.png");
                }
                exit(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                exit(1);
            }
        }, TimeSpan.FromMilliseconds(720));
    }

    private static void Capture(Control visual, int width, int height, string output)
    {
        visual.Measure(new Size(width, height));
        visual.Arrange(new Rect(0, 0, width, height));
        using var target = new RenderTargetBitmap(new PixelSize(width * 2, height * 2), new Vector(192, 192));
        target.Render(visual);
        target.Save(output);
    }

    private void CleanUp()
    {
        if (shuttingDown) return;
        shuttingDown = true;
        heartbeat.Stop();
        motion.Stop();
        gameLayer?.Close(); snowLayer?.Close(); catLayer?.Close();
        leopard.Dispose();
        garland.Dispose();
        platform.Dispose();
        foreach (Bitmap? icon in shortcutIcons.Values) icon?.Dispose();
    }
}
