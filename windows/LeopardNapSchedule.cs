using System;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace WinterIsland
{
    internal sealed class LeopardNapSchedule
    {
        private const int Slots = 5;
        private const int MinutesPerSlot = 24 * 60 / Slots;
        private static readonly int[] Durations = { 5, 10, 5, 30, 10 };
        private static readonly TimeSpan MinimumGap = TimeSpan.FromHours(2.5);
        private readonly string path;
        private DateTime day;
        private int occupied;
        private DateTimeOffset lastStart, sleepUntil;

        public LeopardNapSchedule() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinterIsland", "leopard-naps.xml"))
        {
        }

        internal LeopardNapSchedule(string schedulePath)
        {
            path = schedulePath;
            try
            {
                if (!File.Exists(path)) return;
                var record = XElement.Load(path);
                DateTime.TryParseExact((string)record.Attribute("day"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out day);
                Int32.TryParse((string)record.Attribute("slots"), NumberStyles.None, CultureInfo.InvariantCulture, out occupied);
                occupied &= (1 << Slots) - 1;
                lastStart = ReadTime(record, "lastStart");
                sleepUntil = ReadTime(record, "sleepUntil");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (XmlException) { }
        }

        public bool SleepingAt(DateTimeOffset now)
        {
            return now < sleepUntil && now >= lastStart;
        }

        public bool CanStart(DateTimeOffset now)
        {
            DateTime today = now.Date;
            if (today < day || SleepingAt(now) || now - lastStart < MinimumGap) return false;
            int slot = SlotAt(now);
            if (today == day && (occupied & (1 << slot)) != 0) return false;

            int ordinal = (int)(today.Ticks / TimeSpan.TicksPerDay);
            int delay = 9 + (ordinal * 31 + slot * 73) % 46;
            return now.TimeOfDay.TotalMinutes - slot * MinutesPerSlot >= delay;
        }

        public bool TryStart(DateTimeOffset now)
        {
            if (!CanStart(now)) return false;
            DateTime today = now.Date;
            int slot = SlotAt(now);
            int mask = (today == day ? occupied : 0) | (1 << slot);
            int phase = (int)(today.Ticks / TimeSpan.TicksPerDay) % Durations.Length;
            DateTimeOffset until = now.AddMinutes(Durations[(slot + phase) % Durations.Length]);
            if (!Write(today, mask, now, until)) return false;
            day = today;
            occupied = mask;
            lastStart = now;
            sleepUntil = until;
            return true;
        }

        public void EndEarly(DateTimeOffset now)
        {
            if (!SleepingAt(now)) return;
            sleepUntil = now;
            Write(day, occupied, lastStart, sleepUntil);
        }

        private bool Write(DateTime date, int mask, DateTimeOffset start, DateTimeOffset end)
        {
            string pending = path + ".pending";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                new XElement("naps",
                    new XAttribute("day", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    new XAttribute("slots", mask),
                    new XAttribute("lastStart", start.ToString("o", CultureInfo.InvariantCulture)),
                    new XAttribute("sleepUntil", end.ToString("o", CultureInfo.InvariantCulture))).Save(pending);
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static int SlotAt(DateTimeOffset now)
        {
            return (int)(now.TimeOfDay.TotalMinutes / MinutesPerSlot);
        }

        private static DateTimeOffset ReadTime(XElement record, string name)
        {
            DateTimeOffset value;
            DateTimeOffset.TryParseExact((string)record.Attribute(name), "o", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out value);
            return value;
        }
    }
}
