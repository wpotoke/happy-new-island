using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinterIsland
{
    internal sealed class Island : Form
    {
        private static readonly Color Surface = Color.FromArgb(7, 7, 8);
        private static readonly Color Hover = Color.FromArgb(28, 30, 34);
        private static readonly Color Edge = Color.FromArgb(60, 64, 68);
        private static readonly Color Accent = Color.FromArgb(247, 226, 188);
        private static readonly Color Ink = Color.FromArgb(238, 250, 246);
        private static readonly Color Muted = Color.FromArgb(152, 189, 184);
        private static readonly Color PlayerSurface = Color.FromArgb(17, 19, 21);
        private const string PinDragFormat = "HappyNewIsland.PinOrder";
        private const int RevealHotkey = 0x39FC;
        private const int ToolWindowStyle = 0x80, AppWindowStyle = 0x40000;
        private const int EdgeDelay = 450, LeaveDelay = 2000, LauncherDelay = 2300, SlideDuration = 160;

        private enum RevealSource { None, Edge, Launcher, Pinned }
        private enum PanelSection { Windows, Companion, Modes, Focus }

        private readonly BackgroundPriority priorities = new BackgroundPriority();
        private readonly TaskbarVisibility taskbar = new TaskbarVisibility();
        private readonly List<AppPin> pins;
        private readonly List<Button> headerPins = new List<Button>();
        private readonly List<TelegramPinButton> telegramPins = new List<TelegramPinButton>();
        private readonly TelegramUnreadReader telegramReader = new TelegramUnreadReader();
        private readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer clockTimer = new Timer();
        private readonly Timer fileHover = new Timer { Interval = 440 };
        private readonly Timer revealTimer = new Timer { Interval = 65 };
        private readonly Timer slideTimer = new Timer { Interval = 15 };
        private readonly Stopwatch revealClock = Stopwatch.StartNew();
        private readonly FocusCountdown focus = new FocusCountdown();
        private readonly List<SnowSurface> overlappingWindows = new List<SnowSurface>(1);
        private readonly NotifyIcon tray;
        private readonly Icon appIcon;
        private readonly Panel header, body;
        private readonly Panel windowList, pinnedRow;
        private readonly Panel footer;
        private readonly Panel headerSeparator, mediaPanel, sectionDivider;
        private readonly CompanionSection companionSection;
        private readonly ModesSection modesSection;
        private readonly FocusSection focusSection;
        private readonly FocusClockButton clock;
        private readonly Button language;
        private readonly Button quick;
        private readonly BatteryButton battery;
        private readonly Button panels;
        private readonly MediaButton mediaCompact, mediaDetails;
        private readonly Button mediaPrevious, mediaToggle, mediaNext, mediaCompactDismiss;
        private readonly Button addPin, taskbarToggle;
        private readonly Button gameButton;
        private readonly NavButton windowsTab, modesTab, petTab, timerTab;
        private readonly ToolStripItem taskbarMenuItem, taskbarTrayItem;
        private readonly ContextMenuStrip pinnedMenu, windowMenu, pinMenu, focusMenu;

        private readonly Font smallFont;
        private readonly Font uiFont;
        private readonly Font titleFont;
        private readonly Font symbolFont;
        private readonly float scale;
        private readonly int compactHeight;
        private readonly int panelWidth, compactWidth;
        private readonly int mediaExtraWidth, panelLeft, separatorLeft, clockLeft, batteryLeft, pinsLeft;
        private readonly WinterScene scene;
        private readonly WindowsMedia media;
        private readonly DesktopGame desktopGame;

        private readonly bool openExpanded, hideTaskbarAtStart;

        private readonly List<ContextMenuStrip> menus = new List<ContextMenuStrip>();
        private IslandModes modes;
        private PanelSection section;
        private AppPin movingPin;
        private int dropSlot = -1, pinOffset;
        private NavButton dropMarker;
        private Control hoverControl;
        private PinFileTarget hoverFile;
        private TelegramUnread telegramUnread;
        private bool readingTelegram;

        private bool expanded, closing, mediaCommandPending;
        private bool revealKeyRegistered, edgeLatched;
        private RevealSource revealed;
        private long edgeSince = -1, awaySince = -1, holdUntil, slideSince;
        private int slideOffset;

        public Island(bool startExpanded, bool startHiddenTaskbar)
        {
            openExpanded = startExpanded;
            hideTaskbarAtStart = startHiddenTaskbar;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) scale = screen.DpiX / 96f;
            compactHeight = Px(42);
            pins = AppPins.Load();
            modes = IslandModes.Load();

            smallFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            uiFont = new Font("Segoe UI Semibold", 9f, FontStyle.Regular);
            titleFont = new Font("Segoe UI Semibold", 13f, FontStyle.Regular);
            symbolFont = new Font("Segoe MDL2 Assets", 12f, FontStyle.Regular);
            Font = smallFont;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            ResizeRedraw = true;
            KeyPreview = true;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Surface;
            ForeColor = Ink;
            Text = Program.AppName;
            TopMost = true;
            tips.InitialDelay = 350;
            tips.ReshowDelay = 80;

            int available = (int)(Screen.PrimaryScreen.Bounds.Width / scale) - 16;
            compactWidth = Px(Math.Min(298, available));
            panelWidth = Px(Math.Min(488, available));
            ClientSize = new Size(compactWidth, compactHeight);

            header = new Panel
            {
                BackColor = Surface,
                Location = new Point(Px(14), Px(8)),
                Size = new Size(compactWidth - Px(28), Px(26))
            };
            Controls.Add(header);

            int x = 0;
            AddToHeader(header, CreateButton("", "Пуск", delegate
            {
                Collapse();
                WindowsShell.Shortcut(Keys.ControlKey, Keys.Escape);
            }, true), ref x, 24);
            AddDivider(header, ref x);
            pinsLeft = x;
            x += Px(30) * 3;
            int dismissWidth = Px(23);
            int mediaWidth = Math.Min(Px(143), panelWidth - compactWidth + x - pinsLeft - dismissWidth);
            mediaExtraWidth = mediaWidth + dismissWidth - (x - pinsLeft);
            mediaCompact = new MediaButton(scale, false) { Visible = false };
            ConfigureButton(mediaCompact, "", "Перейти в окно источника", delegate { OpenMediaWindow(mediaCompact); });
            mediaCompact.SetBounds(pinsLeft, 0, mediaWidth, Px(26));
            header.Controls.Add(mediaCompact);
            mediaCompactDismiss = new NavButton { Visible = false };
            ConfigureButton(mediaCompactDismiss, "\uE711", "Убрать медиакарточку", delegate { media.Dismiss(); });
            mediaCompactDismiss.Font = symbolFont;
            mediaCompactDismiss.SetBounds(pinsLeft + mediaWidth, 0, dismissWidth, Px(26));
            header.Controls.Add(mediaCompactDismiss);
            panels = CreateButton("\uE70D", "Приложения и открытые окна", ToggleExpanded);
            panels.Font = symbolFont;
            AddToHeader(header, panels, ref x, 23);
            panelLeft = panels.Left;
            headerSeparator = AddDivider(header, ref x);
            separatorLeft = headerSeparator.Left;

            quick = CreateButton("\uE713", "Быстрые настройки Windows", delegate
            {
                Collapse();
                WindowsShell.Shortcut(Keys.LWin, Keys.A);
            });
            quick.Font = symbolFont;

            clock = new FocusClockButton(scale);
            ConfigureButton(clock, "", "Календарь и таймер фокуса", delegate
            {
                Collapse();
                WindowsShell.Shortcut(Keys.LWin, Keys.N);
            });
            clock.ForeColor = Accent;
            AddToHeader(header, clock, ref x, 44);
            clockLeft = clock.Left;
            battery = new BatteryButton(scale);
            ConfigureButton(battery, "", "Батарея и питание", delegate
            {
                Collapse();
                WindowsShell.Launch("ms-settings:powersleep");
            });
            battery.TextAlign = ContentAlignment.MiddleLeft;
            battery.Padding = new Padding(Px(20), 0, 0, 0);
            AddToHeader(header, battery, ref x, 54);
            batteryLeft = battery.Left;

            body = new Panel {
                Location = new Point(Px(5), compactHeight + Px(8)),
                Size = new Size(panelWidth - Px(10), Px(320)),
                BackColor = Surface,
                Visible = false
            };
            Controls.Add(body);

            pinnedRow = new Panel { BackColor = Surface };
            pinnedRow.SetBounds(Px(12), Px(5), body.Width - Px(66), Px(32));
            body.Controls.Add(pinnedRow);
            addPin = CreateButton("+", "Добавить приложение", PickApplication);
            addPin.Font = titleFont;
            addPin.ForeColor = Accent;
            addPin.SetBounds(body.Width - Px(48), Px(5), Px(35), Px(32));
            body.Controls.Add(addPin);

            language = CreateButton("RU", "Язык ввода", delegate
            {
                Collapse();
                WindowsShell.Shortcut(Keys.LWin, Keys.Space);
            });
            language.SetBounds(body.Width - Px(56), Px(48), Px(38), Px(25));
            body.Controls.Add(language);
            quick.SetBounds(body.Width - Px(101), Px(48), Px(36), Px(25));
            body.Controls.Add(quick);
            bool roomForActions = body.Width >= Px(370);
            gameButton = CreateButton("Шарик", "Играть в шарик", ShowGame);
            gameButton.SetBounds(body.Width - Px(165), Px(48), Px(59), Px(25));
            gameButton.Visible = roomForActions;
            body.Controls.Add(gameButton);
            int tabWidth = Math.Max(Px(33), Math.Min(Px(56), (body.Width - Px(roomForActions ? 190 : 125)) / 4));
            int tabsLeft = Px(12);
            windowsTab = (NavButton)CreateButton("Окна", "Открытые окна", ShowWindows);
            windowsTab.SetBounds(tabsLeft, Px(47), tabWidth, Px(26));
            petTab = (NavButton)CreateButton("Барс", "Кормление и игра с барсом", ShowCompanion);
            petTab.SetBounds(tabsLeft + tabWidth, Px(47), tabWidth, Px(26));
            modesTab = (NavButton)CreateButton("Режимы", "Режимы оформления", ShowModes);
            modesTab.SetBounds(tabsLeft + tabWidth * 2, Px(47), tabWidth + Px(8), Px(26));
            timerTab = (NavButton)CreateButton("Таймер", "Таймер фокуса", ShowTimerSection);
            timerTab.SetBounds(modesTab.Right, Px(47), tabWidth + Px(6), Px(26));
            body.Controls.AddRange(new Control[] { windowsTab, petTab, modesTab, timerTab });
            sectionDivider = new Panel { BackColor = Color.FromArgb(34, 38, 42) };
            sectionDivider.SetBounds(tabsLeft, Px(78), body.Width - tabsLeft * 2, 1);
            body.Controls.Add(sectionDivider);

            mediaPanel = new Panel
            {
                BackColor = PlayerSurface, Visible = false,
                Location = new Point(Px(12), Px(43)),
                Size = new Size(body.Width - Px(24), Px(49))
            };
            using (GraphicsPath shape = Shape(mediaPanel.ClientRectangle, Px(10)))
                mediaPanel.Region = new Region(shape);
            body.Controls.Add(mediaPanel);

            mediaDetails = new MediaButton(scale, true);
            ConfigureButton(mediaDetails, "", "Перейти в окно источника", delegate { OpenMediaWindow(mediaDetails); });
            mediaDetails.BackColor = PlayerSurface;
            mediaDetails.SetBounds(0, 0, mediaPanel.Width - Px(155), mediaPanel.Height);
            mediaPanel.Controls.Add(mediaDetails);
            mediaPrevious = AddMediaControl("\uE892", "Предыдущий трек", MediaCommand.Previous, 144);
            mediaToggle = AddMediaControl("\uE768", "Воспроизвести", MediaCommand.Toggle, 110);
            mediaToggle.ForeColor = Accent;
            mediaNext = AddMediaControl("\uE893", "Следующий трек", MediaCommand.Next, 76);
            Button dismiss = CreateButton("\uE711", "Убрать медиакарточку", delegate { media.Dismiss(); });
            dismiss.Font = symbolFont;
            dismiss.BackColor = PlayerSurface;
            dismiss.ForeColor = Muted;
            dismiss.SetBounds(mediaPanel.Width - Px(39), Px(9), Px(31), Px(31));
            mediaPanel.Controls.Add(dismiss);

            windowList = new Panel
            {
                BackColor = Surface, AutoScroll = true,
                Location = new Point(Px(12), Px(83)),
                Size = new Size(body.Width - Px(24), Px(152))
            };
            body.Controls.Add(windowList);
            companionSection = new CompanionSection(windowList.Width, scale, titleFont, uiFont, smallFont,
                food => Guard(delegate { FeedCompanion(food); }),
                delegate { Guard(ToggleCompanionPlay); }, delegate { Guard(ToggleSnowball); })
                { Left = windowList.Left, Visible = false };
            modesSection = new ModesSection(windowList.Width, scale, uiFont, smallFont,
                index => Guard(delegate { SelectMode(index); }),
                delegate { Guard(EditModes); }) { Left = windowList.Left, Visible = false };
            focusSection = new FocusSection(windowList.Width, scale, titleFont, uiFont, smallFont,
                seconds => Guard(delegate { StartFocus(seconds); }), delegate { Guard(PauseFocus); }, delegate { Guard(ResetFocus); })
                { Left = windowList.Left, Visible = false };
            body.Controls.AddRange(new Control[] { companionSection, modesSection, focusSection });
            footer = new Panel { Size = new Size(body.Width, Px(47)), BackColor = Surface };
            body.Controls.Add(footer);

            int actionX = Px(15);
            AddAction(CreateButton("Ярлыки", "Все закреплённые приложения", ShowPinnedMenu), ref actionX, 68);
            AddAction(CreateButton("Трей", "Системный трей Windows", OpenTray), ref actionX, 55);
            AddAction(CreateButton("Задачи", "Диспетчер задач", delegate
            {
                Collapse();
                WindowsShell.Launch(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe"));
            }), ref actionX, 64);

            AddAction(CreateButton("Стол", "Показать рабочий стол", delegate
            {
                Collapse();
                WindowsShell.Shortcut(Keys.LWin, Keys.D);
            }), ref actionX, 55);

            taskbarToggle = CreateButton("Скрыть панель", "Скрыть панель задач Windows", ToggleTaskbar);
            AddAction(taskbarToggle, ref actionX, 111);

            var exit = CreateButton("Выйти", "Закрыть остров", Close);
            exit.SetBounds(body.Width - Px(67), Px(7), Px(56), Px(30));
            footer.Controls.Add(exit);

            var menu = NewMenu();
            AddMenuAction(menu, "Открытые окна", ShowWindows);
            AddMenuAction(menu, "Добавить приложение…", PickApplication);
            AddMenuAction(menu, "Режимы", ShowModes);
            AddMenuAction(menu, "Барс", ShowCompanion);
            AddMenuAction(menu, "Таймер фокуса", ShowTimerSection);
            AddMenuAction(menu, "Играть в шарик", ShowGame);
            taskbarMenuItem = AddMenuAction(menu, "Скрыть панель задач Windows", ToggleTaskbar);
            AddMenuAction(menu, "Системный трей", OpenTray);
            AddMenuAction(menu, "Закрыть остров", Close);
            header.ContextMenuStrip = menu;
            ContextMenuStrip = menu;

            appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Icon = appIcon;
            var trayMenu = NewMenu();
            AddMenuAction(trayMenu, "Открыть остров", ShowIsland);
            AddMenuAction(trayMenu, "Режимы", ShowModes);
            AddMenuAction(trayMenu, "Играть в шарик", ShowGame);
            taskbarTrayItem = AddMenuAction(trayMenu, "Скрыть панель задач Windows", ToggleTaskbar);
            AddMenuAction(trayMenu, "Закрыть остров", Close);
            tray = new NotifyIcon { Text = Program.AppName, Icon = appIcon, ContextMenuStrip = trayMenu };
            tray.DoubleClick += delegate { Guard(ShowIsland); };
            pinnedMenu = NewMenu();
            windowMenu = NewMenu();
            pinMenu = NewMenu();
            focusMenu = NewMenu();
            focusMenu.Opening += PrepareFocusMenu;
            clock.ContextMenuStrip = focusMenu;
            windowMenu.Opening += PrepareWindowMenu;
            pinMenu.Opening += PreparePinMenu;

            scene = new WinterScene(this, scale, modes.Current, modes.GrowthDays,
                delegate { return Enabled && !closing && desktopGame != null && !desktopGame.Active; },
                delegate { Guard(delegate { FeedCompanion(LeopardFood.Meat); }); });
            scene.Frame += AnimatePanel;
            media = new WindowsMedia();
            media.Changed += PresentMedia;
            desktopGame = new DesktopGame(this, scale, delegate { return RectangleToScreen(CapsuleBounds()); }, scene.HitLeopard);
            desktopGame.Finished += delegate(int best) { Guard(delegate { GameFinished(best); }); };
            fileHover.Tick += delegate { RevealFileTarget(); };
            revealTimer.Tick += delegate { RefreshReveal(); };
            slideTimer.Tick += delegate { SlideIntoPlace(); };
            RegisterDropSurface(header);
            RegisterDropSurface(panels);
            RegisterDropSurface(pinnedRow);
            pinnedRow.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollPins(e.Delta); };

            RefreshPins();
            modesSection.Present(modes);
            clockTimer.Interval = 1000;
            clockTimer.Tick += delegate { RefreshStatus(); };
            RefreshStatus();
            clockTimer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Position();
            revealKeyRegistered = RegisterHotKey(Handle, RevealHotkey, 0x4003, (uint)Keys.G);
            if (revealKeyRegistered) tray.Text = Program.AppName + " · Закрепить Ctrl+Alt+G";
            tray.Visible = appIcon != null;
            if (openExpanded) Guard(ShowIsland);
            RefreshReveal();
            scene.Start();
            media.Start();
            if (hideTaskbarAtStart) Guard(delegate { SetTaskbarHidden(true); });
            RefreshReveal();
            revealTimer.Start();
            RefreshTelegramBadge();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle = (parameters.ExStyle | ToolWindowStyle) & ~AppWindowStyle;
                return parameters;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (scale <= 0 || ClientSize.Width < 2 || compactHeight < 2) return;
            using (GraphicsPath shape = Shape(CapsuleBounds(), Px(20)))
            {
                Region previous = Region;
                var region = new Region(shape);
                Rectangle details = ExpandedBounds();
                if (details.Height > 1)
                {
                    using (GraphicsPath panelShape = Shape(details, Px(18))) region.Union(panelShape);
                }
                Region = region;
                if (previous != null) previous.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Edge))
            {
                DrawOutline(e.Graphics, pen, CapsuleBounds(), Px(20));
                Rectangle details = ExpandedBounds();
                if (details.Height > 1) DrawOutline(e.Graphics, pen, details, Px(18));
            }
        }

        private Rectangle CapsuleBounds()
        {
            int width = expanded ? compactWidth : ClientSize.Width;
            return new Rectangle((ClientSize.Width - width) / 2, 0, width, compactHeight);
        }

        private Rectangle ExpandedBounds()
        {
            int top = compactHeight + Px(4);
            return expanded ? new Rectangle(0, top, ClientSize.Width, ClientSize.Height - top) : Rectangle.Empty;
        }

        private static void DrawOutline(Graphics canvas, Pen pen, Rectangle bounds, int radius)
        {
            bounds.Width--;
            bounds.Height--;
            using (GraphicsPath path = Shape(bounds, radius)) canvas.DrawPath(pen, path);
        }

        protected override void WndProc(ref Message message)
        {
            if (Program.ShowIsland != 0 && message.Msg == Program.ShowIsland)
            {
                Guard(ShowIsland);
                return;
            }
            if (message.Msg == 0x0312 && message.WParam.ToInt64() == TaskbarVisibility.Hotkey)
            {
                Guard(delegate { SetTaskbarHidden(false); });
                return;
            }
            if (message.Msg == 0x0312 && message.WParam.ToInt64() == RevealHotkey)
            {
                Guard(ToggleReveal);
                return;
            }
            if (message.Msg == 0x0312 && desktopGame != null && desktopGame.HandleHotkey((int)message.WParam.ToInt64())) return;
            base.WndProc(ref message);
        }

        private void ShowIsland()
        {
            RevealPanel(revealed == RevealSource.Pinned ? RevealSource.Pinned : RevealSource.Launcher, false);
            Expand();
            Activate();
        }

        protected override bool ProcessCmdKey(ref Message message, Keys key)
        {
            if (desktopGame != null && desktopGame.HandleKey(key)) return true;
            if (key == Keys.Escape && scene != null && scene.Playing)
            {
                scene.StopPlaying();
                RefreshCompanion();
                awaySince = -1;
                return true;
            }
            if (key == Keys.Escape && (revealed == RevealSource.Edge || revealed == RevealSource.Launcher))
            {
                DismissReveal();
                return true;
            }
            if (key == Keys.Escape && expanded)
            {
                Collapse();
                return true;
            }
            return base.ProcessCmdKey(ref message, key);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!e.Cancel)
            {
                desktopGame.Stop();
                priorities.Restore();
            }
        }

        protected override void Dispose(bool disposing)
        {
            bool release = disposing && !closing;
            if (release)
            {
                closing = true;
                clockTimer.Dispose();
                fileHover.Dispose();
                revealTimer.Dispose();
                slideTimer.Dispose();
                if (revealKeyRegistered && IsHandleCreated) UnregisterHotKey(Handle, RevealHotkey);
                desktopGame.Dispose();
                taskbar.Dispose();
                tray.ContextMenuStrip = null;
                tray.Dispose();
                scene.Dispose();
                media.Dispose();
                header.ContextMenuStrip = null;
                ContextMenuStrip = null;
                foreach (ContextMenuStrip menu in menus) if (menu.Visible) menu.Close();
            }
            base.Dispose(disposing);
            if (release)
            {
                tips.Dispose();
                if (appIcon != null) appIcon.Dispose();
                foreach (Bitmap icon in icons.Values) if (icon != null) icon.Dispose();
                icons.Clear();
                foreach (IDisposable menu in menus) menu.Dispose();
                menus.Clear();
                smallFont.Dispose();
                uiFont.Dispose();
                titleFont.Dispose();
                symbolFont.Dispose();
            }
        }

        private void Position()
        {
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(screen.Left + (screen.Width - Width) / 2, CapsuleTop(screen) - slideOffset);
            Rectangle details = ExpandedBounds();
            scene.Align(CapsuleBounds().Width, compactHeight, details.IsEmpty ? Rectangle.Empty : RectangleToScreen(details));
        }

        private int CapsuleTop(Rectangle workArea) { return workArea.Top + Px(modes.Current.Drifts ? 19 : 9); }

        private void RefreshReveal()
        {
            if (closing || !IsHandleCreated || !IsWindowEnabled(Handle) || movingPin != null || menus.Any(menu => menu.Visible))
                return;
            UpdateReveal(Screen.PrimaryScreen, Cursor.Position, revealClock.ElapsedMilliseconds);
        }

        private void UpdateReveal(Screen display, Point pointer, long now)
        {
            if (revealed == RevealSource.Pinned)
            {
                scene.LightSnow = true;
                ShowPanel(false);
                return;
            }
            Rectangle work = display.WorkingArea;
            int width = compactWidth + (media.Current == null ? 0 : mediaExtraWidth);
            int margin = modes.Current.Garland || modes.Current.Drifts ? Px(7) : 0;
            int above = modes.Current.Drifts ? Px(19) : margin;
            int left = work.Left + (work.Width - width) / 2, top = CapsuleTop(work);
            Rectangle footprint = Rectangle.FromLTRB(left - margin, top - above,
                left + width + margin, top + compactHeight + margin);
            bool fullScreen = WindowsShell.ForegroundIsFullScreen(display.Bounds);
            overlappingWindows.Clear();
            if (!fullScreen) WindowsShell.SnowSurfaces(footprint, overlappingWindows, true);
            bool covered = fullScreen || overlappingWindows.Count != 0;
            scene.LightSnow = covered;

            bool edge = pointer.Y >= display.Bounds.Top && pointer.Y < display.Bounds.Top + Px(2) &&
                Math.Abs(pointer.X - (work.Left + work.Width / 2)) <= Px(58);
            if (!edge) edgeLatched = false;
            if (!edge || fullScreen || !covered) edgeSince = -1;
            if (!covered)
            {
                revealed = RevealSource.None;
                ShowPanel(false);
                return;
            }

            if (revealed == RevealSource.Launcher || (revealed == RevealSource.Edge && !fullScreen))
            {
                Rectangle hoverArea = Rectangle.FromLTRB(Left - Px(8), display.Bounds.Top, Right + Px(8),
                    Bottom + slideOffset + Px(8));
                if (hoverArea.Contains(pointer) || scene.IsPointerOverLeopard(pointer))
                {
                    holdUntil = 0;
                    awaySince = -1;
                }
                else if (awaySince < 0) awaySince = now;
                if (now < holdUntil || awaySince < 0 || now - awaySince < LeaveDelay) return;
            }

            revealed = RevealSource.None;
            if (Visible)
            {
                slideTimer.Stop();
                slideOffset = 0;
                Hide();
                Collapse();
            }
            if (!edge || fullScreen || edgeLatched) return;
            if (edgeSince < 0) edgeSince = now;
            if (now - edgeSince >= EdgeDelay) RevealPanel(RevealSource.Edge, true);
        }

        private void ToggleReveal()
        {
            if (!IsWindowEnabled(Handle)) return;
            if (revealed == RevealSource.Pinned) DismissReveal();
            else RevealPanel(RevealSource.Pinned, true);
        }

        private void DismissReveal()
        {
            revealed = RevealSource.None;
            edgeSince = -1;
            edgeLatched = true;
            RefreshReveal();
        }

        private void RevealPanel(RevealSource source, bool slide)
        {
            revealed = source;
            awaySince = -1;
            holdUntil = source == RevealSource.Launcher ? revealClock.ElapsedMilliseconds + LauncherDelay : 0;
            edgeLatched = true;
            scene.LightSnow = true;
            ShowPanel(slide);
        }

        private void ShowPanel(bool slide)
        {
            if (Visible && WindowState != FormWindowState.Minimized) return;
            if (slide)
            {
                slideSince = revealClock.ElapsedMilliseconds;
                slideOffset = compactHeight;
            }
            Position();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Position();
            if (slide) slideTimer.Start();
            RefreshTelegramBadge();
        }

        private void SlideIntoPlace()
        {
            double remaining = Math.Max(0, 1 - (revealClock.ElapsedMilliseconds - slideSince) / (double)SlideDuration);
            slideOffset = (int)Math.Round(compactHeight * remaining * remaining * remaining);
            Position();
            if (slideOffset == 0) slideTimer.Stop();
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        private void RefreshStatus()
        {
            if (Visible && WindowState != FormWindowState.Minimized) Position();
            clock.PresentTime(DateTime.Now);
            RefreshFocus();
            if (battery.Read(SystemInformation.PowerStatus)) tips.SetToolTip(battery, battery.AccessibleName);
            string lang = InputLanguage.CurrentInputLanguage.Culture.TwoLetterISOLanguageName.ToUpperInvariant();
            if (language.Text != lang) language.Text = lang;
            RefreshTaskbarLabel();
            RefreshTelegramBadge();
        }

        private async void RefreshTelegramBadge()
        {
            if (closing || readingTelegram || !IsHandleCreated || !Visible ||
                !telegramPins.Exists(button => button.Visible)) return;
            readingTelegram = true;
            try
            {
                int dpi = Px(96);
                TelegramUnread next = await Task.Run(delegate { return telegramReader.Read(dpi); });
                if (closing || (next.Count == telegramUnread.Count && next.Tone == telegramUnread.Tone)) return;
                telegramUnread = next;
                foreach (TelegramPinButton button in telegramPins) PresentTelegramBadge(button);
            }
            finally { readingTelegram = false; }
        }

        private void PresentTelegramBadge(TelegramPinButton button)
        {
            button.Present(telegramUnread);
            string hint = ((AppPin)button.Tag).Name;
            if (telegramUnread.Tone != TelegramBadgeTone.None)
            {
                if (telegramUnread.Count > 0)
                    hint += " · непрочитано " + telegramUnread.Count.ToString("N0", CultureInfo.CurrentCulture);
                hint += telegramUnread.Tone == TelegramBadgeTone.Muted
                    ? "\nНепрочитанное в тихих чатах"
                    : "\nЕсть непрочитанное в чатах с уведомлениями";
            }
            button.AccessibleName = hint.Replace('\n', ' ');
            tips.SetToolTip(button, hint);
        }

        private void RefreshFocus()
        {
            double time = revealClock.Elapsed.TotalSeconds;
            if (focus.Tick(time)) scene.FocusEnded();
            float fraction = focus.Progress(time);
            clock.PresentFocus(focus.Stage, fraction);
            if (focusSection.Visible) focusSection.Present(focus.Stage, focus.SecondsLeft(time), fraction);
            string hint = clock.CalendarDate + "\n" +
                (focus.Stage == FocusStage.Idle ? "Таймер во вкладке острова" : FocusText(time));
            if (!String.Equals(tips.GetToolTip(clock), hint, StringComparison.Ordinal))
                tips.SetToolTip(clock, hint);
            if (focusMenu.Visible && focusMenu.Items.Count > 0 && focusMenu.Items[0] is ToolStripLabel)
                focusMenu.Items[0].Text = FocusText(time);
        }

        private string FocusText(double time)
        {
            if (focus.Stage == FocusStage.Completed) return "Фокус завершён";
            return (focus.Stage == FocusStage.Paused ? "Пауза · " : "Фокус · ") + FocusSection.FormatDuration(focus.SecondsLeft(time));
        }

        private void StartFocus(int seconds)
        {
            focus.Start(seconds, revealClock.Elapsed.TotalSeconds);
            RefreshFocus();
        }

        private void PauseFocus()
        {
            RefreshFocus();
            focus.PauseOrContinue(revealClock.Elapsed.TotalSeconds);
            RefreshFocus();
        }

        private void ResetFocus()
        {
            focus.Reset();
            RefreshFocus();
        }

        private void PrepareFocusMenu(object sender, System.ComponentModel.CancelEventArgs e)
        {
            RefreshFocus();
            ClearItems(focusMenu);
            if (focus.Stage != FocusStage.Idle)
            {
                var label = new ToolStripLabel(FocusText(revealClock.Elapsed.TotalSeconds));
                focusMenu.Items.Add(label);
                label.ForeColor = Accent;
                if (focus.Stage == FocusStage.Running || focus.Stage == FocusStage.Paused)
                    AddMenuAction(focusMenu, focus.Stage == FocusStage.Paused ? "Продолжить" : "Пауза", PauseFocus);
                AddMenuAction(focusMenu, "Сбросить таймер", ResetFocus);
                focusMenu.Items.Add(new ToolStripSeparator());
            }
            AddMenuAction(focusMenu, "Свой интервал…", ShowTimerSection);
            foreach (int minutes in new[] { 15, 25, 45 })
            {
                int duration = minutes;
                AddMenuAction(focusMenu, duration + " минут", delegate { StartFocus(duration * 60); });
            }
            e.Cancel = false;
        }

        private void ToggleExpanded() { if (expanded) Collapse(); else Expand(); }

        private void Expand()
        {
            if (section == PanelSection.Windows) RefreshWindows();
            expanded = true;
            LayoutIsland();
        }

        private void RefreshWindows()
        {
            if (windowMenu.Visible) windowMenu.Close();
            foreach (Control control in windowList.Controls.Cast<Control>().ToArray()) control.Dispose();
            IList<WindowEntry> windows = WindowsShell.OpenWindows();
            int items = Math.Max(1, Math.Min(windows.Count, 4));
            int rowHeight = Px(34);
            int innerWidth = windowList.Width - (windows.Count > 4 ? SystemInformation.VerticalScrollBarWidth : 0);
            for (int i = 0; i < windows.Count; i++)
            {
                WindowEntry entry = windows[i];
                var select = CreateButton(entry.Title, entry.Title, delegate { Collapse(); WindowsShell.Focus(entry); });
                select.AutoEllipsis = true;
                select.TextAlign = ContentAlignment.MiddleLeft;
                select.Image = GetIcon(entry.Executable);
                select.ImageAlign = ContentAlignment.MiddleLeft;
                select.TextImageRelation = TextImageRelation.ImageBeforeText;
                select.Padding = new Padding(Px(7), 0, Px(5), 0);
                select.ForeColor = priorities.IsReduced(entry) ? Accent : Ink;
                select.SetBounds(0, i * rowHeight, innerWidth - Px(38), rowHeight - Px(2));
                windowList.Controls.Add(select);

                select.Tag = entry;
                select.ContextMenuStrip = windowMenu;
                var extra = CreateButton("···", "Действия для окна", null);
                extra.Tag = entry;
                extra.SetBounds(innerWidth - Px(37), i * rowHeight, Px(34), rowHeight - Px(2));
                extra.Click += delegate { windowMenu.Show(extra, new Point(0, extra.Height)); };
                windowList.Controls.Add(extra);
            }
            if (windows.Count == 0)
            {
                windowList.Controls.Add(new Label
                {
                    Location = new Point(Px(14), Px(8)), AutoSize = true,
                    Text = "Нет открытых окон", ForeColor = Muted
                });
            }
            windowList.Height = items * rowHeight;
            windowList.AutoScrollPosition = Point.Empty;
        }

        private void Collapse()
        {
            if (!expanded) return;
            expanded = false;
            LayoutIsland();
        }

        private void LayoutIsland()
        {
            bool hasMedia = media.Current != null;
            bool compactPlayer = hasMedia && !expanded;
            int extra = compactPlayer ? mediaExtraWidth : 0;
            int capsuleWidth = compactWidth + extra;
            foreach (Button pin in headerPins) pin.Visible = !compactPlayer;
            mediaCompact.Visible = compactPlayer;
            mediaCompactDismiss.Visible = compactPlayer;
            panels.Left = panelLeft + extra;
            headerSeparator.Left = separatorLeft + extra;
            clock.Left = clockLeft + extra;
            battery.Left = batteryLeft + extra;
            panels.Text = expanded ? "\uE70E" : "\uE70D";
            header.Width = capsuleWidth - Px(28);
            body.Visible = expanded;
            if (expanded)
            {
                mediaPanel.Visible = hasMedia;
                int offset = hasMedia ? Px(56) : 0;
                quick.Top = language.Top = Px(48) + offset;
                gameButton.Top = quick.Top;
                windowsTab.Top = modesTab.Top = petTab.Top = timerTab.Top = Px(47) + offset;
                sectionDivider.Top = Px(78) + offset;
                windowsTab.Selected = section == PanelSection.Windows;
                petTab.Selected = section == PanelSection.Companion;
                modesTab.Selected = section == PanelSection.Modes;
                timerTab.Selected = section == PanelSection.Focus;
                windowList.Visible = windowsTab.Selected;
                companionSection.Visible = petTab.Selected;
                modesSection.Visible = modesTab.Selected;
                focusSection.Visible = timerTab.Selected;
                Control shown = windowsTab.Selected ? (Control)windowList :
                    (petTab.Selected ? (Control)companionSection : (modesTab.Selected ? (Control)modesSection : focusSection));
                shown.Top = Px(86) + offset;
                if (petTab.Selected) RefreshCompanion();
                if (timerTab.Selected) RefreshFocus();
                footer.Top = shown.Bottom + Px(6);
                body.Height = footer.Bottom + Px(3);
                ClientSize = new Size(panelWidth, body.Bottom + Px(5));
            }
            else ClientSize = new Size(capsuleWidth, compactHeight);
            header.Left = (Width - header.Width) / 2;
            Position();
            Invalidate();
        }

        private Button AddMediaControl(string glyph, string label, MediaCommand command, int right)
        {
            var button = new NavButton();
            ConfigureButton(button, glyph, label, null);
            button.Font = symbolFont;
            button.BackColor = PlayerSurface;
            button.SetBounds(mediaPanel.Width - Px(right), Px(9), Px(32), Px(31));
            button.Click += async delegate
            {
                if (mediaCommandPending) return;
                mediaCommandPending = true;
                SetMediaControls(media.Current);
                try
                {
                    bool accepted = await media.Send(command);
                    if (!accepted && !closing && button.Visible)
                        tips.Show("Плеер не принял команду", button, 1600);
                }
                finally
                {
                    mediaCommandPending = false;
                    if (!closing) SetMediaControls(media.Current);
                }
            };
            mediaPanel.Controls.Add(button);
            return button;
        }

        private void PresentMedia(MediaCard current)
        {
            mediaCompact.Present(current);
            mediaDetails.Present(current);
            string title = current == null ? "" : "Перейти в окно источника\n" + current.Title;
            tips.SetToolTip(mediaCompact, title);
            tips.SetToolTip(mediaDetails, title);
            SetMediaControls(current);
            LayoutIsland();
        }

        private void SetMediaControls(MediaCard current)
        {
            bool ready = current != null && !mediaCommandPending;
            bool playing = current != null && current.Playing;
            mediaPrevious.Enabled = ready && current.CanPrevious;
            mediaNext.Enabled = ready && current.CanNext;
            mediaToggle.Enabled = ready && (current.CanToggle || (playing ? current.CanPause : current.CanPlay));
            mediaToggle.Text = playing ? "\uE769" : "\uE768";
            string action = playing ? "Пауза" : "Воспроизвести";
            mediaToggle.AccessibleName = action;
            tips.SetToolTip(mediaToggle, action);
        }

        private void AnimatePanel()
        {
            if (expanded && section == PanelSection.Companion) RefreshCompanion();
            MediaCard card = media.Current;
            if (card == null || !card.Playing) return;
            if (expanded) mediaDetails.Invalidate();
            else mediaCompact.Invalidate();
        }

        private void OpenTray()
        {
            Collapse();
            if (taskbar.Hidden) SetTaskbarHidden(false);
            WindowsShell.Shortcut(Keys.LWin, Keys.B);
        }

        private void OpenMediaWindow(Control anchor)
        {
            MediaCard card = media.Current;
            if (card == null) return;
            Activate();
            if (WindowsShell.FocusMedia(card)) Collapse();
            else tips.Show("Окно источника сейчас недоступно", anchor, 1800);
        }

        private void ToggleTaskbar() { SetTaskbarHidden(!taskbar.Hidden); }

        private void ShowWindows() { ShowSection(PanelSection.Windows); }
        private void ShowCompanion() { ShowSection(PanelSection.Companion); }
        private void ShowModes() { ShowSection(PanelSection.Modes); }
        private void ShowTimerSection() { ShowSection(PanelSection.Focus); }

        private void ShowSection(PanelSection next)
        {
            section = next;
            if (!expanded || !Visible) ShowIsland();
            else Expand();
        }

        private void SelectMode(int selected)
        {
            if (selected < 0 || selected >= modes.Items.Count || selected == modes.Active) return;
            IslandModes choice = modes.Copy();
            choice.Active = selected;
            ApplyModes(choice);
        }

        private void EditModes()
        {
            using (var editor = new ModeEditor(modes) { Icon = appIcon })
                if (editor.ShowDialog(this) == DialogResult.OK) ApplyModes(editor.Result);
        }

        private void ApplyModes(IslandModes choice)
        {
            choice.Save();
            modes = choice;
            scene.Apply(modes.Current);
            Position();
            modesSection.Present(modes);
            RefreshCompanion();
        }

        private void RefreshCompanion()
        {
            companionSection.Present(modes.GrowthDays, modes.Current.Leopard, !desktopGame.Active && scene.CanFeed,
                !desktopGame.Active && scene.CanPlay, scene.Playing, scene.PlayingToy,
                modes.FavoriteKnown ? modes.FavoriteFood : -1);
        }

        private void FeedCompanion(LeopardFood food)
        {
            if (closing || desktopGame.Active || !scene.CanFeed) return;
            IslandModes record = modes.Copy();
            record.RecordMeal(DateTime.Today);
            bool favorite = record.RecordTaste(food);
            record.Save();
            modes = record;
            scene.Feed(record.GrowthDays, food, favorite);
            RefreshCompanion();
        }

        private void ToggleCompanionPlay()
        {
            if (closing) return;
            if (scene.Playing) scene.StopPlaying();
            else StartCompanionPlay(false);
            RefreshCompanion();
            awaySince = -1;
        }

        private void ToggleSnowball()
        {
            if (closing) return;
            if (scene.PlayingToy) scene.StopPlaying();
            else StartCompanionPlay(true);
            RefreshCompanion();
            awaySince = -1;
        }

        private void StartCompanionPlay(bool snowball)
        {
            if (desktopGame.Active || !modes.Current.Leopard || (!scene.Playing && !scene.CanPlay)) return;
            scene.StartPlaying(snowball);
            Collapse();
            WindowsShell.ShowDesktop();
            ShowPanel(false);
            Activate();
        }

        private void ShowGame()
        {
            if (desktopGame.Active) { desktopGame.Stop(); return; }
            Collapse();
            WindowsShell.ShowDesktop();
            WindowState = FormWindowState.Normal;
            Show();
            Position();
            Activate();
            desktopGame.Start(modes.Best);
            gameButton.Text = "Стоп";
        }

        private void GameFinished(int best)
        {
            gameButton.Text = "Шарик";
            if (best <= modes.Best) return;
            IslandModes record = modes.Copy();
            record.Best = best;
            record.Save();
            modes = record;
        }

        private void SetTaskbarHidden(bool hidden)
        {
            if (hidden) taskbar.Hide(Handle);
            else taskbar.Restore();
            RefreshTaskbarLabel();
        }

        private void RefreshTaskbarLabel()
        {
            bool hidden = taskbar.Hidden;
            string label = hidden ? "Показать панель" : "Скрыть панель";
            if (taskbarToggle.Text == label) return;
            string caption = label + " задач Windows";
            taskbarToggle.Text = label;
            taskbarMenuItem.Text = taskbarTrayItem.Text = caption;
            taskbarToggle.AccessibleName = caption;
            tips.SetToolTip(taskbarToggle, hidden && taskbar.HasHotkey ? caption + " (Ctrl+Alt+Shift+T)" : caption);
        }

        private void ShowPinnedMenu()
        {
            if (pinnedMenu.Visible) pinnedMenu.Close();
            ClearItems(pinnedMenu);
            foreach (AppPin pin in pins)
            {
                AppPin current = pin;
                AddMenuAction(pinnedMenu, current.Name, delegate { Collapse(); WindowsShell.Launch(current.Target); });
            }
            if (pinnedMenu.Items.Count != 0) pinnedMenu.Items.Add(new ToolStripSeparator());
            AddMenuAction(pinnedMenu, "Добавить приложение…", PickApplication);
            pinnedMenu.Show(Cursor.Position);
        }

        private void PrepareWindowMenu(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Control source = windowMenu.SourceControl;
            WindowEntry window = source == null ? null : source.Tag as WindowEntry;
            e.Cancel = closing || window == null;
            if (e.Cancel) return;
            ClearItems(windowMenu);
            AddMenuAction(windowMenu, "Закрепить приложение", delegate { AddPin(AppPins.FromWindow(window)); });
            AddMenuAction(windowMenu, priorities.IsReduced(window) ? "Вернуть приоритет" : "Снизить приоритет",
                delegate { priorities.Toggle(window); Expand(); });
            AddMenuAction(windowMenu, "Закрыть окно", delegate { WindowsShell.CloseWindow(window); Collapse(); });
        }

        private void PreparePinMenu(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Control source = pinMenu.SourceControl;
            AppPin pin = source == null ? null : source.Tag as AppPin;
            e.Cancel = closing || pin == null;
            if (e.Cancel) return;
            ClearItems(pinMenu);
            AddMenuAction(pinMenu, "Переместить в начало", delegate { AddPin(pin); }).Enabled = pins.IndexOf(pin) > 0;
            AddMenuAction(pinMenu, "Открепить", delegate { SetPins(pins.Where(item => item != pin).ToList()); });
        }

        private ToolStripItem AddMenuAction(ContextMenuStrip menu, string text, Action action)
        {
            return menu.Items.Add(text, null, delegate
            {
                if (closing || !IsHandleCreated) return;
                BeginInvoke(new MethodInvoker(delegate { if (!closing) Guard(action); }));
            });
        }

        private static void ClearItems(ContextMenuStrip menu)
        {
            while (menu.Items.Count != 0)
            {
                ToolStripItem item = menu.Items[0];
                menu.Items.RemoveAt(0);
                item.Dispose();
            }
        }

        private ContextMenuStrip NewMenu()
        {
            var menu = new ContextMenuStrip
            {
                Font = smallFont,
                ForeColor = Ink,
                BackColor = Surface,
                ShowImageMargin = false,
                Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false }
            };
            menu.ItemAdded += delegate(object sender, ToolStripItemEventArgs e) { e.Item.ForeColor = Ink; };
            menus.Add(menu);
            return menu;
        }

        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color CheckBackground { get { return Accent; } }
            public override Color CheckSelectedBackground { get { return Accent; } }
            public override Color CheckPressedBackground { get { return Accent; } }
            public override Color MenuBorder { get { return Edge; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemBorder { get { return Hover; } }
        }

        private Button CreateButton(string text, string hint, Action action, bool start = false)
        {
            Button button = start ? new FlakeButton() : new NavButton();
            ConfigureButton(button, text, hint, action);
            return button;
        }

        private void ConfigureButton(Button button, string text, string hint, Action action)
        {
            button.Text = text;
            button.AccessibleName = hint;
            button.Font = uiFont;
            button.BackColor = Surface;
            button.ForeColor = Ink;
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;

            button.Cursor = Cursors.Hand;
            if (action != null) button.Click += delegate { Guard(action); };
            tips.SetToolTip(button, hint);
        }

        private sealed class FlakeButton : NavButton
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float radius = Math.Min(Width, Height) * 0.29f;
                float x = Width / 2f, y = Height / 2f;
                using (var stroke = new Pen(Accent, 1.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    for (int i = 0; i < 6; i++)
                    {
                        double angle = i * Math.PI / 3 - Math.PI / 2;
                        float dx = (float)Math.Cos(angle), dy = (float)Math.Sin(angle);
                        e.Graphics.DrawLine(stroke, x, y, x + dx * radius, y + dy * radius);
                        float sx = x + dx * radius * .58f, sy = y + dy * radius * .58f;
                        e.Graphics.DrawLine(stroke, sx, sy,
                            sx + (dx - dy) * radius * .23f, sy + (dy + dx) * radius * .23f);
                        e.Graphics.DrawLine(stroke, sx, sy,
                            sx + (dx + dy) * radius * .23f, sy + (dy - dx) * radius * .23f);
                    }
                }
            }
        }

        private void AddToHeader(Panel header, Button item, ref int x, int width)
        {
            item.SetBounds(x, 0, Px(width), Px(26));
            header.Controls.Add(item);
            x += Px(width + 4);
        }

        private Panel AddDivider(Panel header, ref int x)
        {
            var divider = new Panel { BackColor = Edge };
            divider.SetBounds(x + Px(3), Px(7), Math.Max(1, Px(1)), Px(14));
            header.Controls.Add(divider);
            x += Px(8);
            return divider;
        }

        private void AddAction(Button item, ref int x, int width)
        {
            item.SetBounds(x, Px(7), Px(width), Px(30));
            footer.Controls.Add(item);
            x += Px(width + 4);
        }

        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void PickApplication()
        {
            using (var picker = new AppPicker())
                if (picker.ShowDialog(this) == DialogResult.OK) AddPin(picker.Selected);
        }

        private void AddPin(AppPin pin)
        {
            if (pin == null) return;
            var ordered = new List<AppPin>(pins);
            AppPin existing = ordered.FirstOrDefault(item => String.Equals(item.Target, pin.Target, StringComparison.OrdinalIgnoreCase));
            if (existing != null) ordered.Remove(existing);
            ordered.Insert(0, existing ?? pin);
            SetPins(ordered);
        }

        private void SetPins(List<AppPin> ordered)
        {
            AppPins.Save(ordered);
            pins.Clear();
            pins.AddRange(ordered);
            RefreshPins();
            LayoutIsland();
        }

        private void RefreshPins()
        {
            if (pinMenu.Visible) pinMenu.Close();
            ClearDropState();
            foreach (Button button in headerPins) button.Dispose();
            headerPins.Clear();
            foreach (Control button in pinnedRow.Controls.Cast<Control>().ToArray()) button.Dispose();
            telegramPins.Clear();

            for (int i = 0; i < pins.Count; i++)
            {
                AppPin pin = pins[i];
                bool telegram = new PinFileTarget(pin).IsTelegram;
                if (i < 3)
                {
                    Button headerPin = PinButton(pin, telegram);
                    headerPin.SetBounds(pinsLeft + i * Px(30), 0, Px(26), Px(26));
                    header.Controls.Add(headerPin);
                    headerPins.Add(headerPin);
                }
                Button button = PinButton(pin, telegram);
                button.MouseWheel += delegate(object sender, MouseEventArgs e) { ScrollPins(e.Delta); };
                pinnedRow.Controls.Add(button);
            }
            PositionPinnedRow();
        }

        private int PinCapacity { get { return Math.Max(1, pinnedRow.Width / Px(36)); } }

        private void PositionPinnedRow()
        {
            int count = Math.Min(pins.Count, PinCapacity);
            pinOffset = Math.Max(0, Math.Min(pins.Count - count, pinOffset));
            int step = Math.Min(Px(40), pinnedRow.Width / Math.Max(1, count));
            for (int i = 0; i < pinnedRow.Controls.Count; i++)
            {
                Control button = pinnedRow.Controls[i];
                int slot = i - pinOffset;
                button.Visible = slot >= 0 && slot < count;
                if (slot >= 0 && slot < count) button.SetBounds(slot * step, 0, step, pinnedRow.Height);
            }
            tips.SetToolTip(pinnedRow, pins.Count > count ? "Колёсико прокручивает закреплённые приложения" : "");
        }

        private void ScrollPins(int delta)
        {
            if (pins.Count <= PinCapacity || delta == 0) return;
            pinOffset -= Math.Sign(delta);
            PositionPinnedRow();
        }

        private Button PinButton(AppPin pin, bool telegram)
        {
            NavButton button = telegram ? new TelegramPinButton(scale) : new NavButton();
            ConfigureButton(button, "", pin.Name, delegate { Collapse(); WindowsShell.Launch(pin.Target); });
            button.Tag = pin;
            button.ContextMenuStrip = pinMenu;
            button.Image = GetIcon(pin.IconPath);
            if (button.Image == null && pin.IconPath != pin.Target)
                button.Image = GetIcon(pin.Target);
            if (button.Image == null)
            {
                button.Text = pin.Name.Substring(0, 1);
                button.ForeColor = Accent;
            }
            Point? origin = null;
            button.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) origin = e.Location; };
            button.MouseUp += delegate { origin = null; };
            button.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || !origin.HasValue || movingPin != null) return;
                Size drag = SystemInformation.DragSize;
                Rectangle stillClick = new Rectangle(origin.Value.X - drag.Width / 2, origin.Value.Y - drag.Height / 2, drag.Width, drag.Height);
                if (stillClick.Contains(e.Location)) return;
                origin = null;
                Guard(delegate { DragPin(button, pin); });
            };
            RegisterDropSurface(button);
            if (telegram)
            {
                var telegramPin = (TelegramPinButton)button;
                telegramPins.Add(telegramPin);
                PresentTelegramBadge(telegramPin);
            }
            return button;
        }

        private void DragPin(NavButton source, AppPin pin)
        {
            source.CancelClickForDrag();
            movingPin = pin;
            dropSlot = -1;
            List<AppPin> updated = null;
            try
            {
                var data = new DataObject(PinDragFormat, pin.Target);
                if (source.DoDragDrop(data, DragDropEffects.Move) == DragDropEffects.Move)
                    updated = PinOrder.Move(pins, pin, dropSlot);
            }
            finally
            {
                movingPin = null;
                dropSlot = -1;
                ClearDropState();
                source.EndPinDrag();
            }
            if (updated != null && !closing) BeginInvoke(new MethodInvoker(delegate { if (!closing) Guard(delegate { SetPins(updated); }); }));
        }

        private void RegisterDropSurface(Control surface)
        {
            surface.AllowDrop = true;
            surface.DragEnter += delegate(object sender, DragEventArgs e) { UpdateDrop(surface, e, false); };
            surface.DragOver += delegate(object sender, DragEventArgs e) { UpdateDrop(surface, e, false); };
            surface.DragLeave += delegate { ClearDropState(); };
            surface.DragDrop += delegate(object sender, DragEventArgs e)
            {
                UpdateDrop(surface, e, true);
                ClearDropState();
            };
        }

        private void UpdateDrop(Control surface, DragEventArgs e, bool commit)
        {
            e.Effect = DragDropEffects.None;
            if (closing || e.Data == null) return;
            var point = new Point(e.X, e.Y);
            var button = surface as NavButton;
            if (movingPin != null && e.Data.GetDataPresent(PinDragFormat, false))
            {
                if (button == null || !(button.Tag is AppPin))
                    button = surface.Controls.OfType<NavButton>().Where(child => child.Visible && child.Tag is AppPin)
                        .OrderBy(child => Math.Abs(child.PointToScreen(new Point(child.Width / 2, 0)).X - point.X)).FirstOrDefault();
                AppPin anchor = button == null ? null : button.Tag as AppPin;
                if (anchor == null) return;
                bool after = button.PointToClient(point).X >= button.Width / 2;
                e.Effect = DragDropEffects.Move;
                if (commit) dropSlot = pins.IndexOf(anchor) + (after ? 1 : 0);
                else MarkDrop(button, after ? 1 : -1);
                return;
            }

            if (!e.Data.GetDataPresent(DataFormats.FileDrop, false)) return;
            AppPin pin = button == null ? null : button.Tag as AppPin;
            if (pin == null)
            {
                if (!commit && !expanded && (surface == header || surface == panels)) ArmFileHover(surface, null);
                return;
            }

            if (hoverControl != surface) ArmFileHover(surface, pin);
            if (hoverFile == null) return;
            MarkDrop(button, 2);
            if (!hoverFile.CanDropOnIcon || (e.AllowedEffect & DragDropEffects.Copy) == 0) return;
            e.Effect = DragDropEffects.Copy;
            if (!commit) return;
            PinFileTarget destination = hoverFile;
            var files = e.Data.GetData(DataFormats.FileDrop, false) as string[];
            if (files == null || files.Length == 0) { e.Effect = DragDropEffects.None; return; }
            BeginInvoke(new MethodInvoker(delegate
            {
                if (!closing) Guard(delegate { Collapse(); destination.OfferTelegramFiles(files); });
            }));
        }

        private void MarkDrop(NavButton button, int mark)
        {
            if (dropMarker != null && dropMarker != button) dropMarker.DropMark = 0;
            dropMarker = button;
            if (button != null) button.DropMark = mark;
        }

        private void ArmFileHover(Control surface, AppPin pin)
        {
            if (hoverControl == surface) return;
            fileHover.Stop();
            hoverControl = surface;
            hoverFile = pin == null ? null : new PinFileTarget(pin);
            fileHover.Start();
        }

        private void RevealFileTarget()
        {
            fileHover.Stop();
            if (closing || hoverControl == null || !hoverControl.Visible ||
                !hoverControl.RectangleToScreen(hoverControl.ClientRectangle).Contains(Cursor.Position)) return;
            PinFileTarget destination = hoverFile;
            Guard(delegate
            {
                if (destination == null) { if (!expanded) Expand(); }
                else
                {
                    destination.Reveal();
                    if (expanded && !destination.CanDropOnIcon) Collapse();
                }
            });
        }

        private void ClearDropState()
        {
            if (!closing) fileHover.Stop();
            hoverControl = null;
            hoverFile = null;
            MarkDrop(null, 0);
        }

        private Bitmap GetIcon(string path)
        {
            if (String.IsNullOrEmpty(path)) return null;
            Bitmap icon;
            if (icons.TryGetValue(path, out icon)) return icon;
            using (Bitmap original = WindowsShell.IconBitmap(path))
            {
                icon = null;
                if (original != null)
                {
                    int size = Px(21);
                    icon = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics raster = Graphics.FromImage(icon))
                    {
                        raster.Clear(Color.Transparent);
                        raster.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        raster.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        raster.DrawImage(original, new Rectangle(0, 0, size, size));
                    }
                }
            }
            icons[path] = icon;
            return icon;
        }

        private sealed class TelegramPinButton : NavButton
        {
            private static readonly Color RedBadge = Color.FromArgb(213, 40, 55);
            private static readonly Color GrayBadge = Color.FromArgb(99, 105, 115);
            private readonly float scale;
            private readonly Font badgeFont;
            private TelegramBadgeTone tone;
            private string number = String.Empty;

            public TelegramPinButton(float scale)
            {
                this.scale = scale;
                badgeFont = new Font("Segoe UI", 8.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            }

            public void Present(TelegramUnread unread)
            {
                tone = unread.Tone;
                number = unread.Count == 0 ? String.Empty : unread.Count > 99 ? "99+" :
                    unread.Count.ToString(CultureInfo.InvariantCulture);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (tone == TelegramBadgeTone.None || Width < 3 || Height < 3) return;
                int side = Pixel(number.Length == 0 ? 8 : 13);
                int width = side + Pixel(Math.Max(0, number.Length - 1) * 3);
                int iconSize = Image == null ? Pixel(20) : Image.Width;
                int right = Math.Min(Width - 1, (Width + iconSize) / 2 + Pixel(3));
                int left = Math.Max(1, right - width);
                int top = Math.Max(1, (Height - iconSize) / 2 - Pixel(2));
                if (right <= left || top + side >= Height) return;
                var badge = new Rectangle(left, top, right - left, side);
                using (var shape = Shape(badge, side / 2))
                using (var fill = new SolidBrush(tone == TelegramBadgeTone.Muted ? GrayBadge : RedBadge))
                using (var rim = new Pen(Surface, Math.Max(1, scale)))
                {
                    e.Graphics.FillPath(fill, shape);
                    e.Graphics.DrawPath(rim, shape);
                }
                if (number.Length > 0)
                    TextRenderer.DrawText(e.Graphics, number, badgeFont, badge, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                        TextFormatFlags.NoPadding);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) badgeFont.Dispose();
                base.Dispose(disposing);
            }

            private int Pixel(int logical) { return (int)Math.Round(logical * scale); }
        }

        private sealed class BatteryButton : NavButton
        {
            private readonly float dpi;
            private int percent = -2;
            private bool charging;

            public BatteryButton(float scale) { dpi = scale; }

            public bool Read(PowerStatus status)
            {
                BatteryChargeStatus flags = status.BatteryChargeStatus;
                float level = status.BatteryLifePercent;
                bool known = flags != BatteryChargeStatus.Unknown && (flags & BatteryChargeStatus.NoSystemBattery) == 0 && level >= 0 && level <= 1;
                int next = known ? (int)Math.Round(level * 100) : -1;
                bool nextCharging = known && (flags & BatteryChargeStatus.Charging) != 0;
                if (percent == next && charging == nextCharging) return false;
                percent = next;
                charging = nextCharging;
                Text = known ? percent.ToString(CultureInfo.CurrentCulture) + "%" : "—";
                AccessibleName = known ? "Заряд " + Text + (charging ? ", идёт зарядка" : "") : "Батарея и питание";
                ForeColor = known && percent <= 15 && !charging ? Color.FromArgb(244, 169, 154) : Ink;
                Invalidate();
                return true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics canvas = e.Graphics;
                int height = (int)Math.Round(8 * dpi);
                var casing = new Rectangle((int)Math.Round(3 * dpi), (Height - height) / 2,
                    (int)Math.Round(12.5f * dpi), height);
                Color color = charging ? Color.FromArgb(158, 215, 185) : ForeColor;
                using (var outline = new Pen(Color.FromArgb(183, color), Math.Max(1f, .85f * dpi)))
                using (var fill = new SolidBrush(color))
                using (var round = Shape(casing, Math.Max(1, (int)Math.Round(2.55 * dpi))))
                {
                    canvas.DrawPath(outline, round);
                    canvas.FillEllipse(fill, casing.Right + .95f * dpi, casing.Top + 2.4f * dpi, 1.45f * dpi, casing.Height - 4.8f * dpi);
                    if (percent > 0)
                    {
                        var cell = Rectangle.Inflate(casing, -(int)Math.Ceiling(dpi * 1.52), -(int)Math.Ceiling(dpi * 1.32));
                        cell.Width = Math.Max(1, (int)Math.Round(cell.Width * percent / 100f));
                        using (var roundedFill = Shape(cell, Math.Max(1, (int)Math.Round(1.32 * dpi))))
                            canvas.FillPath(fill, roundedFill);
                    }
                }
            }
        }

        private sealed class MediaButton : NavButton
        {
            private readonly float dpi;
            private readonly bool showDetail;
            private MediaCard card;

            public MediaButton(float scale, bool withDetail)
            {
                dpi = scale;
                showDetail = withDetail;
            }

            public void Present(MediaCard current)
            {
                card = current;
                if (current != null)
                {
                    string status = current.Playing ? "Идёт воспроизведение" : "Пауза";
                    AccessibleName = "Перейти в окно источника. " + status + ". " + current.Title;
                }
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (card == null || Width < 2 || Height < 2) return;
                Graphics canvas = e.Graphics;
                int inset = Pixel(showDetail ? 8 : 2);
                int side = Math.Min(Pixel(showDetail ? 35 : 22), Height - 2);
                var art = new Rectangle(inset, (Height - side) / 2, side, side);
                DrawCover(canvas, art);

                int textLeft = art.Right + Pixel(showDetail ? 9 : 6);
                int textWidth = Math.Max(1, Width - textLeft - Pixel(25));
                canvas.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var format = new StringFormat(StringFormatFlags.NoWrap)
                {
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter
                })
                using (var ink = new SolidBrush(Ink))
                {
                    if (showDetail && !String.IsNullOrEmpty(card.Detail))
                    {
                        int line = Pixel(17);
                        int top = (Height - 2 * line) / 2;
                        canvas.DrawString(card.Title, Font, ink, new Rectangle(textLeft, top, textWidth, line), format);
                        using (var muted = new SolidBrush(Color.FromArgb(164, 174, 173)))
                            canvas.DrawString(card.Detail, Font, muted,
                                new Rectangle(textLeft, top + line, textWidth, line), format);
                    }
                    else canvas.DrawString(card.Title, Font, ink, new Rectangle(textLeft, 0, textWidth, Height), format);
                }
                DrawPlayback(canvas);
            }

            private void DrawCover(Graphics canvas, Rectangle art)
            {
                using (GraphicsPath crop = Shape(art, Pixel(4)))
                {
                    if (card.Cover != null)
                    {
                        GraphicsState state = canvas.Save();
                        canvas.SetClip(crop, CombineMode.Intersect);
                        canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        canvas.DrawImage(card.Cover, art);
                        canvas.Restore(state);
                    }
                    else
                    {
                        using (var fill = new LinearGradientBrush(art, Color.FromArgb(58, 53, 47),
                            Color.FromArgb(29, 36, 36), 45f)) canvas.FillPath(fill, crop);
                        float x = art.Left + art.Width * .56f, y = art.Top + art.Height * .3f;
                        float unit = art.Height * .14f;
                        using (var note = new Pen(Accent, Math.Max(1f, dpi)))
                        using (var fill = new SolidBrush(Accent))
                        {
                            canvas.DrawLines(note, new[] { new PointF(x, y + unit * 2.4f),
                                new PointF(x, y), new PointF(x + unit, y + unit * .5f) });
                            canvas.FillEllipse(fill, x - unit * 1.15f, y + unit * 1.9f, unit * 1.4f, unit);
                        }
                    }
                }
            }

            private void DrawPlayback(Graphics canvas)
            {
                float x = Width - Pixel(19), center = Height / 2f;
                using (var bar = new Pen(card.Playing ? Accent : Muted, Math.Max(1f, dpi * 1.5f))
                {
                    StartCap = LineCap.Round, EndCap = LineCap.Round
                })
                {
                    if (!card.Playing)
                    {
                        x += 3 * dpi;
                        canvas.DrawLine(bar, x, center - 2.3f * dpi, x, center + 2.3f * dpi);
                        x += 4 * dpi;
                        canvas.DrawLine(bar, x, center - 2.3f * dpi, x, center + 2.3f * dpi);
                        return;
                    }
                    double time = unchecked((uint)Environment.TickCount) * .009;
                    for (int i = 0; i < 4; i++, x += 3.5f * dpi)
                    {
                        float length = (float)(1.4 + 3.0 * (.5 + .5 * Math.Sin(time * (1 + .13 * i) + i * 1.57))) * dpi;
                        canvas.DrawLine(bar, x, center - length, x, center + length);
                    }
                }
            }

            private int Pixel(int logical) { return (int)Math.Round(logical * dpi); }
        }

        private sealed class FocusClockButton : NavButton
        {
            private static readonly CultureInfo CalendarCulture = CultureInfo.GetCultureInfo("ru-RU");
            private readonly float scale;
            private readonly Font dateFont;
            private FocusStage stage;
            private float fraction;
            private long minuteShown = -1;
            private string hoursAndMinutes = String.Empty;
            private string dayAndMonth = String.Empty;

            public FocusClockButton(float scale)
            {
                this.scale = scale;
                dateFont = new Font("Segoe UI Semibold", 9.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            }

            public string CalendarDate { get; private set; }

            public void PresentTime(DateTime now)
            {
                long minute = now.Ticks / TimeSpan.TicksPerMinute;
                if (minute == minuteShown) return;
                minuteShown = minute;
                hoursAndMinutes = now.ToString("HH:mm", CultureInfo.InvariantCulture);
                dayAndMonth = now.ToString("dd.MM", CultureInfo.InvariantCulture);
                CalendarDate = now.ToString("dddd, d MMMM yyyy", CalendarCulture);
                AccessibleName = hoursAndMinutes + ", " + CalendarDate + ". Календарь и таймер фокуса";
                Invalidate();
            }

            public void PresentFocus(FocusStage value, float progress)
            {
                if (stage == value && Math.Abs(progress - fraction) < .0004f) return;
                stage = value;
                fraction = progress;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var canvas = e.Graphics;
                canvas.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var align = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var timeInk = new SolidBrush(ForeColor))
                using (var dateInk = new SolidBrush(Color.FromArgb(185, 193, 193)))
                {
                    canvas.DrawString(hoursAndMinutes, Font, timeInk, new RectangleF(0, 0, Width, 13.5f * scale), align);
                    canvas.DrawString(dayAndMonth, dateFont, dateInk, new RectangleF(0, 12 * scale, Width, 10.5f * scale), align);
                }
                if (stage == FocusStage.Idle || Width < 12) return;
                float x = 5 * scale, y = Height - 2.9f * scale;
                float width = Width - x * 2;
                using (var track = new Pen(Color.FromArgb(72, 77, 74), Math.Max(1, 1.1f * scale)))
                using (var value = new Pen(stage == FocusStage.Completed ? Color.FromArgb(171, 224, 196) : Accent,
                    Math.Max(1.3f, 1.45f * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    canvas.DrawLine(track, x, y, x + width, y);
                    canvas.DrawLine(value, x, y, x + Math.Max(1.1f, width * fraction), y);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) dateFont.Dispose();
                base.Dispose(disposing);
            }
        }

        private class NavButton : Button
        {
            private bool selected;
            private bool hovered;

            public bool Selected
            {
                get { return selected; }
                set
                {
                    if (selected == value) return;
                    selected = value;
                    Invalidate();
                }
            }
            private bool pressed;
            private bool pinDrag;
            private int dropMark;

            public int DropMark
            {
                set { if (dropMark != value) { dropMark = value; Invalidate(); } }
            }

            public void CancelClickForDrag()
            {
                pinDrag = true;
                Capture = false;
                pressed = false;
                Invalidate();
            }

            public void EndPinDrag() { pinDrag = false; }

            protected override void OnClick(EventArgs e)
            {
                if (!pinDrag) base.OnClick(e);
            }

            public NavButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                hovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hovered = false;
                pressed = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                pressed = e.Button == MouseButtons.Left;
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                pressed = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics canvas = e.Graphics;
                canvas.Clear(BackColor);
                canvas.SmoothingMode = SmoothingMode.AntiAlias;

                if (Enabled && Width > 2 && Height > 2 &&
                    (selected || hovered || pressed || (Focused && ShowFocusCues)))
                {
                    using (var area = Shape(new Rectangle(1, 1, Width - 2, Height - 2),
                        Math.Max(4, Height / 3)))
                    using (var fill = new SolidBrush(pressed ? Edge : (selected ? Color.FromArgb(39, 33, 26) : Hover)))
                    {
                        canvas.FillPath(fill, area);
                        if (Focused && ShowFocusCues)
                        {
                            using (var ring = new Pen(Color.FromArgb(103, 91, 75)))
                                canvas.DrawPath(ring, area);
                        }
                    }
                }

                if (Width <= 0 || Height <= 0) return;
                Rectangle textBox = ClientRectangle;
                bool left = TextAlign == ContentAlignment.MiddleLeft;
                if (Image != null)
                {
                    int x = left ? Padding.Left : (Width - Image.Width) / 2;
                    int y = (Height - Image.Height) / 2;
                    canvas.DrawImageUnscaled(Image, x, y);
                    if (left)
                        textBox = new Rectangle(x + Image.Width + 9, 0,
                            Math.Max(1, Width - x - Image.Width - 13), Height);
                }
                else if (left)
                {
                    textBox.X += Padding.Left;
                    textBox.Width = Math.Max(1, textBox.Width - Padding.Horizontal);
                }

                if (!String.IsNullOrEmpty(Text))
                {
                    canvas.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (var brush = new SolidBrush(Enabled ? (selected ? Accent : ForeColor) : Muted))
                    using (var format = new StringFormat(StringFormatFlags.NoWrap))
                    {
                        format.Alignment = left ? StringAlignment.Near : StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        format.Trimming = StringTrimming.EllipsisCharacter;
                        canvas.DrawString(Text, Font, brush, textBox, format);
                    }
                }
                if (selected && Width > 12)
                    using (var line = new Pen(Accent)) canvas.DrawLine(line, Width / 4, Height - 2, Width * 3 / 4, Height - 2);
                if (dropMark == 2)
                {
                    using (var pen = new Pen(Accent))
                    using (var ring = Shape(new Rectangle(2, 2, Width - 5, Height - 5), Math.Max(3, Height / 3)))
                        canvas.DrawPath(pen, ring);
                }
                else if (dropMark != 0)
                {
                    int x = dropMark < 0 ? 1 : Width - 2;
                    using (var pen = new Pen(Accent, 2f)) canvas.DrawLine(pen, x, 4, x, Height - 4);
                }
            }

        }

        internal static GraphicsPath Shape(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2);
            var shape = new GraphicsPath();
            shape.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            shape.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            shape.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            shape.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            shape.CloseFigure();
            return shape;
        }

        private int Px(int logical) { return (int)Math.Round(logical * scale); }
    }
}
