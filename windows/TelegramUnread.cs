using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WinterIsland
{
    internal enum TelegramBadgeTone { None, Alert, Muted }

    internal struct TelegramUnread
    {
        public readonly TelegramBadgeTone Tone;
        public readonly int Count;

        public TelegramUnread(TelegramBadgeTone tone, int count)
        {
            Tone = tone;
            Count = count;
        }

        public static TelegramUnread Read(string caption, Bitmap icon)
        {
            TelegramBadgeTone tone = ReadTone(icon);
            return tone == TelegramBadgeTone.None ? new TelegramUnread() : new TelegramUnread(tone, ReadCount(caption));
        }

        private static int ReadCount(string caption)
        {
            if (String.IsNullOrEmpty(caption)) return 0;
            caption = caption.TrimEnd();
            if (!caption.EndsWith(")", StringComparison.Ordinal)) return 0;
            int start = caption.LastIndexOf(" (", StringComparison.Ordinal);
            if (start < 0) return 0;
            int count;
            return Int32.TryParse(caption.Substring(start + 2, caption.Length - start - 3),
                NumberStyles.None, CultureInfo.InvariantCulture, out count) ? count : 0;
        }

        private static TelegramBadgeTone ReadTone(Bitmap icon)
        {
            if (icon == null) return TelegramBadgeTone.None;
            int red = 0, gray = 0;
            // The Windows caption icon carries Telegram's unread badge in its lower half.
            for (int y = icon.Height * 2 / 5; y < icon.Height; y++)
                for (int x = 0; x < icon.Width; x++)
                {
                    Color color = icon.GetPixel(x, y);
                    if (color.A < 204) continue;
                    if (color.R >= 125 && color.R - color.G > 48 && color.R - color.B > 43) red++;
                    int low = Math.Min(color.R, Math.Min(color.G, color.B));
                    int high = Math.Max(color.R, Math.Max(color.G, color.B));
                    if (low >= 55 && high <= 224 && high - low <= 18) gray++;
                }
            int coverage = Math.Max(4, icon.Width * icon.Height / 32);
            if (red >= coverage && red >= gray) return TelegramBadgeTone.Alert;
            return gray >= coverage ? TelegramBadgeTone.Muted : TelegramBadgeTone.None;
        }
    }

    internal sealed class TelegramUnreadReader
    {
        private IntPtr window;
        private uint owner;
        private long nextSearch;

        public TelegramUnread Read(int dpi)
        {
            try
            {
                if (window == IntPtr.Zero || WindowsShell.WindowProcess(window) != owner)
                {
                    window = IntPtr.Zero;
                    long now = Stopwatch.GetTimestamp();
                    if (now < nextSearch) return new TelegramUnread();
                    nextSearch = now + 3 * Stopwatch.Frequency;
                    window = WindowsShell.FindTelegramWindow(out owner);
                }
                if (window == IntPtr.Zero) return new TelegramUnread();
                using (Bitmap icon = WindowsShell.SmallWindowIcon(window, dpi))
                    return TelegramUnread.Read(WindowsShell.WindowTitle(window), icon);
            }
            catch (ExternalException) { return new TelegramUnread(); }
            catch (ArgumentException) { return new TelegramUnread(); }
            catch (InvalidOperationException) { return new TelegramUnread(); }
        }
    }
}
