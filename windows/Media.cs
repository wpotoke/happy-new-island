using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;
using UiTimer = System.Windows.Forms.Timer;

namespace WinterIsland
{
    internal enum MediaCommand { Previous, Toggle, Next }

    internal sealed class MediaCard
    {
        public string Title;
        public string Detail;
        public string SourceId;
        public Bitmap Cover;
        public bool Playing;
        public bool CanPlay;
        public bool CanPause;
        public bool CanToggle;
        public bool CanPrevious;
        public bool CanNext;
    }

    internal sealed class WindowsMedia : IDisposable
    {
        private readonly UiTimer timer = new UiTimer { Interval = 900 };
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private static readonly TimeSpan PauseGrace = TimeSpan.FromSeconds(8);
        private GlobalSystemMediaTransportControlsSessionManager manager;
        private GlobalSystemMediaTransportControlsSession session;
        private string source = "";
        private Bitmap cover;
        private string title = "", detail = "";
        private int dirty = 1;
        private bool busy, disposed;
        private bool thumbnailExpected;
        private DateTime lastConnect = DateTime.MinValue;
        private TimeSpan? lastPlaying;
        private bool wasPlaying;
        private string dismissedSource, dismissedTitle, dismissedDetail;

        public event Action<MediaCard> Changed;
        public MediaCard Current { get; private set; }

        public WindowsMedia()
        {
            timer.Tick += Refresh;
        }

        public void Start()
        {
            timer.Start();
            Refresh(this, EventArgs.Empty);
        }

        private async void Refresh(object sender, EventArgs e)
        {
            if (busy || disposed) return;
            busy = true;
            try
            {
                if (manager == null)
                {
                    if (DateTime.UtcNow - lastConnect < TimeSpan.FromSeconds(8)) return;
                    lastConnect = DateTime.UtcNow;
                    using (var timeout = new CancellationTokenSource(3500))
                        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(timeout.Token);
                    if (disposed) return;
                }

                var candidate = manager.GetCurrentSession();
                var playback = candidate == null ? null : candidate.GetPlaybackInfo();
                if (playback == null || playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    foreach (var item in manager.GetSessions())
                    {
                        var information = item.GetPlaybackInfo();
                        if (information == null || information.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            continue;
                        candidate = item;
                        playback = information;
                        break;
                    }
                }

                if (playback == null || (playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing &&
                                        playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused &&
                                        playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing))
                {
                    lastPlaying = null;
                    wasPlaying = false;
                    dismissedSource = null;
                    Attach(null);
                    Publish(null, null);
                    return;
                }

                bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                string nextSource = candidate.SourceAppUserModelId ?? "";
                bool sameSource = String.Equals(source, nextSource, StringComparison.OrdinalIgnoreCase);
                if (!sameSource) lastPlaying = null;
                if (playing)
                {
                    if ((!wasPlaying || !sameSource) && String.Equals(dismissedSource, nextSource, StringComparison.OrdinalIgnoreCase))
                        dismissedSource = null;
                    lastPlaying = elapsed.Elapsed;
                }
                wasPlaying = playing;
                if (!playing && (!lastPlaying.HasValue || elapsed.Elapsed - lastPlaying.Value >= PauseGrace))
                {
                    if (!sameSource) Attach(null);
                    Publish(null, null);
                    return;
                }

                Attach(candidate);
                Bitmap oldCover = null;
                if (Interlocked.Exchange(ref dirty, 0) != 0)
                {
                    try
                    {
                        GlobalSystemMediaTransportControlsSessionMediaProperties properties;
                        using (var timeout = new CancellationTokenSource(2200))
                            properties = await candidate.TryGetMediaPropertiesAsync().AsTask(timeout.Token);
                        if (disposed) return;

                        string nextTitle = FirstText(properties.Title, properties.Subtitle);
                        string nextDetail = FirstText(properties.Artist, properties.AlbumTitle, DisplaySource(source));
                        bool newTrack = title != nextTitle || detail != nextDetail;
                        bool hasThumbnail = properties.Thumbnail != null;
                        title = nextTitle;
                        detail = nextDetail;

                        if (newTrack || thumbnailExpected != hasThumbnail || (cover == null && hasThumbnail))
                        {
                            Bitmap next = await ReadCover(properties.Thumbnail);
                            if (disposed)
                            {
                                if (next != null) next.Dispose();
                                return;
                            }
                            oldCover = cover;
                            cover = next;
                            thumbnailExpected = hasThumbnail;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Exchange(ref dirty, 1);
                    }
                }

                string visibleTitle = FirstText(title, DisplaySource(source));
                bool dismissed = String.Equals(dismissedSource, source, StringComparison.OrdinalIgnoreCase) &&
                    dismissedTitle == visibleTitle && dismissedDetail == detail;
                if (dismissed || (!playing && String.IsNullOrEmpty(title)))
                {
                    Publish(null, oldCover);
                    return;
                }
                dismissedSource = null;

                var controls = playback.Controls;
                var card = new MediaCard
                {
                    Title = visibleTitle,
                    Detail = detail,
                    SourceId = source,
                    Cover = cover,
                    Playing = playing,
                    CanToggle = controls != null && controls.IsPlayPauseToggleEnabled,
                    CanPlay = controls != null && controls.IsPlayEnabled,
                    CanPause = controls != null && controls.IsPauseEnabled,
                    CanPrevious = controls != null && controls.IsPreviousEnabled,
                    CanNext = controls != null && controls.IsNextEnabled
                };
                Publish(card, oldCover);
            }
            catch (Exception error)
            {
                if (!IsMediaFailure(error)) throw;
                if (!disposed)
                {
                    lastPlaying = null;
                    wasPlaying = false;
                    Attach(null);
                    manager = null;
                    Publish(null, null);
                }
            }
            finally { busy = false; }
        }

        public void Dismiss()
        {
            if (Current == null) return;
            dismissedSource = Current.SourceId;
            dismissedTitle = Current.Title;
            dismissedDetail = Current.Detail;
            Publish(null, null);
        }

        private void Attach(GlobalSystemMediaTransportControlsSession next)
        {
            if (Object.ReferenceEquals(session, next)) return;
            Detach();
            string nextSource = next == null ? "" : next.SourceAppUserModelId ?? "";
            if (source != nextSource || next == null)
            {
                Bitmap old = cover;
                cover = null;
                title = detail = "";
                thumbnailExpected = false;
                Publish(null, old);
            }
            session = next;
            source = nextSource;
            if (session != null) session.MediaPropertiesChanged += PropertiesChanged;
            Interlocked.Exchange(ref dirty, 1);
        }

        private void Detach()
        {
            var previous = session;
            session = null;
            if (previous == null) return;
            try { previous.MediaPropertiesChanged -= PropertiesChanged; }
            catch (Exception error) { if (!IsMediaFailure(error)) throw; }
        }

        private void PropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            Interlocked.Exchange(ref dirty, 1);
        }

        private void Publish(MediaCard card, Bitmap released)
        {
            MediaCard old = Current;
            Current = card;
            if (!Equivalent(old, card))
            {
                Action<MediaCard> handler = Changed;
                if (handler != null) handler(card);
            }
            if (released != null) released.Dispose();
        }

        private static bool Equivalent(MediaCard a, MediaCard b)
        {
            if (a == null || b == null) return a == b;
            return a.Title == b.Title && a.Detail == b.Detail && a.SourceId == b.SourceId && a.Cover == b.Cover &&
                a.Playing == b.Playing && a.CanToggle == b.CanToggle && a.CanPlay == b.CanPlay &&
                a.CanPause == b.CanPause && a.CanPrevious == b.CanPrevious && a.CanNext == b.CanNext;
        }

        public async Task<bool> Send(MediaCommand command)
        {
            var target = session;
            var card = Current;
            if (target == null || card == null || disposed) return false;
            try
            {
                using (var timeout = new CancellationTokenSource(2000))
                {
                    bool accepted;
                    if (command == MediaCommand.Previous) accepted = await target.TrySkipPreviousAsync().AsTask(timeout.Token);
                    else if (command == MediaCommand.Next) accepted = await target.TrySkipNextAsync().AsTask(timeout.Token);
                    else if (card.CanToggle) accepted = await target.TryTogglePlayPauseAsync().AsTask(timeout.Token);
                    else if (card.Playing) accepted = await target.TryPauseAsync().AsTask(timeout.Token);
                    else accepted = await target.TryPlayAsync().AsTask(timeout.Token);
                    if (accepted) Refresh(this, EventArgs.Empty);
                    return accepted;
                }
            }
            catch (Exception error)
            {
                if (!IsMediaFailure(error)) throw;
                return false;
            }
        }

        private static async Task<Bitmap> ReadCover(IRandomAccessStreamReference thumbnail)
        {
            if (thumbnail == null) return null;
            try
            {
                using (var timeout = new CancellationTokenSource(1400))
                using (var original = await thumbnail.OpenReadAsync().AsTask(timeout.Token))
                {
                    if (original.Size == 0 || original.Size > 12 * 1024 * 1024) return null;
                    using (Stream stream = original.AsStreamForRead())
                    using (Image image = Image.FromStream(stream))
                    {
                        int side = Math.Min(image.Width, image.Height);
                        if (side <= 0 || image.Width > 4096 || image.Height > 4096) return null;
                        var bitmap = new Bitmap(80, 80, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        using (Graphics canvas = Graphics.FromImage(bitmap))
                        {
                            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            canvas.DrawImage(image, new Rectangle(0, 0, 80, 80),
                                (image.Width - side) / 2, (image.Height - side) / 2, side, side, GraphicsUnit.Pixel);
                        }
                        return bitmap;
                    }
                }
            }
            catch (Exception error)
            {
                if (!IsMediaFailure(error) && !(error is IOException) &&
                    !(error is ArgumentException) && !(error is OutOfMemoryException)) throw;
                return null;
            }
        }

        private static string FirstText(params string[] values)
        {
            foreach (string item in values)
                if (!String.IsNullOrWhiteSpace(item)) return item.Trim().Replace('\r', ' ').Replace('\n', ' ');
            return "";
        }

        private static string DisplaySource(string value)
        {
            if (value.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0) return "Spotify";
            if (value.IndexOf("firefox", StringComparison.OrdinalIgnoreCase) >= 0) return "Firefox";
            if (value.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0) return "Chrome";
            if (value.IndexOf("edge", StringComparison.OrdinalIgnoreCase) >= 0) return "Edge";
            if (value.IndexOf("telegram", StringComparison.OrdinalIgnoreCase) >= 0) return "Telegram";
            return "Медиа Windows";
        }

        private static bool IsMediaFailure(Exception error)
        {
            return error is System.Runtime.InteropServices.COMException ||
                error is UnauthorizedAccessException || error is OperationCanceledException ||
                error is InvalidOperationException || error is PlatformNotSupportedException;
        }

        public void Dispose()
        {
            disposed = true;
            timer.Stop();
            timer.Dispose();
            Changed = null;
            Detach();
            if (cover != null) cover.Dispose();
            manager = null;
            cover = null;
        }
    }
}
