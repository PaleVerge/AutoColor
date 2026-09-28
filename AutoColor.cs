using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutoColor
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread] private static void Main()
        {
            bool created;
            using (Mutex instance = new Mutex(true, "AutoColor.Win11.ThemeSwitcher", out created))
            {
                if (!created) return;
                if (Environment.OSVersion.Version.Major >= 6)
                    SetProcessDPIAware();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new TrayApplication());
            }
        }
    }

    internal sealed class Settings
    {
        internal bool FollowSun = false, StartWithWindows = false;
        internal string DayTime = "07:00", NightTime = "19:00";
        internal double Latitude = 31.2304, Longitude = 121.4737;
        private static readonly string FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoColor", "settings.ini");
        internal static readonly TimeSpan DefaultDay = new TimeSpan(7, 0, 0), DefaultNight = new TimeSpan(19, 0, 0);
        private static readonly string[] ClockFormats = { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" };
        internal static bool TryParseClock(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (String.IsNullOrEmpty(text)) return false;
            TimeSpan parsed;
            if (!TimeSpan.TryParseExact(text.Trim(), ClockFormats, CultureInfo.InvariantCulture, out parsed)) return false;
            if (parsed < TimeSpan.Zero || parsed >= TimeSpan.FromHours(24)) return false;
            time = parsed;
            return true;
        }
        internal static string FormatClock(TimeSpan time) { return time.ToString(@"hh\:mm", CultureInfo.InvariantCulture); }
        internal static Settings Load()
        {
            Settings r = new Settings();
            try
            {
                if (!File.Exists(FileName)) return r;
                foreach (string line in File.ReadAllLines(FileName))
                {
                    int i = line.IndexOf('='); if (i < 1) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    bool b; double d;
                    if (k == "FollowSun" && Boolean.TryParse(v, out b)) r.FollowSun = b;
                    else if (k == "StartWithWindows" && Boolean.TryParse(v, out b)) r.StartWithWindows = b;
                    else if (k == "DayTime") r.DayTime = v;
                    else if (k == "NightTime") r.NightTime = v;
                    else if (k == "Latitude" && Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.Latitude = d;
                    else if (k == "Longitude" && Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.Longitude = d;
                }
            }
            catch { /* corrupt or unreadable settings: keep defaults */ }
            return r;
        }
        internal void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FileName));
            File.WriteAllLines(FileName, new[]
            {
                "FollowSun=" + FollowSun,
                "DayTime=" + DayTime,
                "NightTime=" + NightTime,
                "Latitude=" + Latitude.ToString(CultureInfo.InvariantCulture),
                "Longitude=" + Longitude.ToString(CultureInfo.InvariantCulture),
                "StartWithWindows=" + StartWithWindows
            });
        }
    }

    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly NotifyIcon tray; private readonly System.Threading.Timer timer; private readonly Icon icon; private Settings settings; private bool quitting;
        internal TrayApplication()
        {
            settings = Settings.Load();
            Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            icon = extracted != null ? extracted : (Icon)SystemIcons.Application.Clone();
            tray = new NotifyIcon { Icon = icon, Text = "Auto Color", Visible = true };
            ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("立即切换为日间主题", null, delegate { Theme.Apply(true); Reschedule(); }); menu.Items.Add("立即切换为夜间主题", null, delegate { Theme.Apply(false); Reschedule(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("设置…", null, delegate { ShowSettings(); }); menu.Items.Add("退出", null, delegate { Quit(); }); tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowSettings(); };
            timer = new System.Threading.Timer(delegate { OnTimer(); }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); SystemEvents.PowerModeChanged += OnPowerModeChanged; SystemEvents.TimeChanged += OnTimeChanged; Reschedule(); ApplyForNow();
        }
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode != PowerModes.Resume) return; OnTimer(); }
        private void OnTimeChanged(object sender, EventArgs e) { OnTimer(); }
        private void OnTimer()
        {
            if (quitting) return;
            try { ApplyForNow(); }
            finally { SafeReschedule(); }
        }
        private void SafeReschedule() { try { Reschedule(); } catch (ObjectDisposedException) { } catch (InvalidOperationException) { } }
        private void ApplyForNow()
        {
            try
            {
                DateTime now = DateTime.Now, day, night;
                GetSchedule(now.Date, out day, out night);
                Theme.Apply(IsDaytime(now, day, night));
            }
            catch { /* registry or schedule failure: keep running */ }
        }
        private static bool IsDaytime(DateTime now, DateTime day, DateTime night) { return day <= night ? now >= day && now < night : now >= day || now < night; }
        private void GetSchedule(DateTime date, out DateTime day, out DateTime night)
        {
            if (settings.FollowSun) { day = SunTimes.GetSunrise(date, settings.Latitude, settings.Longitude); night = SunTimes.GetSunset(date, settings.Latitude, settings.Longitude); return; }
            TimeSpan d, n;
            if (!Settings.TryParseClock(settings.DayTime, out d)) d = Settings.DefaultDay;
            if (!Settings.TryParseClock(settings.NightTime, out n)) n = Settings.DefaultNight;
            day = date.Add(d); night = date.Add(n);
        }
        private void Reschedule()
        {
            if (quitting) return;
            DateTime now = DateTime.Now, day, night;
            GetSchedule(now.Date, out day, out night);
            DateTime next = day > now ? day : (night > now ? night : DateTime.MinValue);
            if (next == DateTime.MinValue) { GetSchedule(now.Date.AddDays(1), out day, out night); next = day < night ? day : night; }
            TimeSpan due = next - now + TimeSpan.FromSeconds(1);
            if (due < TimeSpan.FromSeconds(1)) due = TimeSpan.FromSeconds(1);
            timer.Change(due, System.Threading.Timeout.InfiniteTimeSpan);
        }
        private void ShowSettings() { using (SettingsForm form = new SettingsForm(settings)) { if (form.ShowDialog() != DialogResult.OK || form.Value == null) return; settings = form.Value; try { settings.Save(); Startup.SetEnabled(settings.StartWithWindows); } catch { } ApplyForNow(); Reschedule(); } }
        private void Quit()
        {
            quitting = true;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
            try { timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); } catch (ObjectDisposedException) { }
            try { timer.Dispose(); } catch (ObjectDisposedException) { }
            tray.Visible = false; tray.Dispose(); icon.Dispose(); ExitThread();
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly RadioButton fixedMode = new RadioButton { Text = "按自定义时间", AutoSize = true }, sunMode = new RadioButton { Text = "跟随日出 / 日落", AutoSize = true };
        private readonly TextBox dayTime = new TextBox(), nightTime = new TextBox(), latitude = new TextBox(), longitude = new TextBox();
        private readonly CheckBox startup = new CheckBox { Text = "开机时自动启动（当前用户）", AutoSize = true };
        internal Settings Value { get; private set; }

        internal SettingsForm(Settings source)
        {
            Text = "Auto Color 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(6f, 13f);
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            fixedMode.Checked = !source.FollowSun;
            sunMode.Checked = source.FollowSun;
            startup.Checked = source.StartWithWindows;
            dayTime.Text = source.DayTime;
            nightTime.Text = source.NightTime;
            latitude.Text = source.Latitude.ToString(CultureInfo.InvariantCulture);
            longitude.Text = source.Longitude.ToString(CultureInfo.InvariantCulture);
            foreach (TextBox tb in new[] { dayTime, nightTime, latitude, longitude })
            {
                tb.Width = 120;
                tb.Anchor = AnchorStyles.Left;
            }

            TableLayoutPanel root = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            root.Controls.Add(fixedMode, 0, root.RowCount++);
            root.Controls.Add(Row("日间主题开始（HH:mm）", dayTime), 0, root.RowCount++);
            root.Controls.Add(Row("夜间主题开始（HH:mm）", nightTime), 0, root.RowCount++);
            root.Controls.Add(sunMode, 0, root.RowCount++);
            root.Controls.Add(Row("纬度（北纬为正）", latitude), 0, root.RowCount++);
            root.Controls.Add(Row("经度（东经为正）", longitude), 0, root.RowCount++);
            root.Controls.Add(startup, 0, root.RowCount++);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Anchor = AnchorStyles.Right,
                Padding = new Padding(0, 12, 0, 0)
            };
            Button ok = new Button { Text = "保存", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(88, 30) };
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(88, 30) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            root.Controls.Add(buttons, 0, root.RowCount++);

            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;
            ok.Click += delegate { Save(); };
            fixedMode.CheckedChanged += delegate { UpdateMode(); };
            UpdateMode();
        }

        private static Control Row(string labelText, Control field)
        {
            TableLayoutPanel row = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Margin = new Padding(18, 4, 0, 4),
                Dock = DockStyle.Fill
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label label = new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 16, 0) };
            row.Controls.Add(label, 0, 0);
            row.Controls.Add(field, 1, 0);
            return row;
        }

        private void UpdateMode()
        {
            dayTime.Enabled = nightTime.Enabled = fixedMode.Checked;
            latitude.Enabled = longitude.Enabled = sunMode.Checked;
        }

        private void Save()
        {
            TimeSpan day = Settings.DefaultDay, night = Settings.DefaultNight;
            double lat = 31.2304, lng = 121.4737;
            TimeSpan parsedDay = default(TimeSpan), parsedNight = default(TimeSpan);
            double parsedLat = 0, parsedLng = 0;
            bool timesOk = Settings.TryParseClock(dayTime.Text, out parsedDay) && Settings.TryParseClock(nightTime.Text, out parsedNight);
            bool geoOk = Double.TryParse(latitude.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedLat)
                && Double.TryParse(longitude.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedLng)
                && parsedLat >= -90 && parsedLat <= 90 && parsedLng >= -180 && parsedLng <= 180;

            if (fixedMode.Checked && !timesOk)
            {
                MessageBox.Show("请填写有效时间（HH:mm，00:00–23:59）。", "Auto Color", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (sunMode.Checked && !geoOk)
            {
                MessageBox.Show("请填写有效经纬度（纬度 -90–90，经度 -180–180）。", "Auto Color", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            if (timesOk) { day = parsedDay; night = parsedNight; }
            if (geoOk) { lat = parsedLat; lng = parsedLng; }

            Value = new Settings
            {
                FollowSun = sunMode.Checked,
                DayTime = Settings.FormatClock(day),
                NightTime = Settings.FormatClock(night),
                Latitude = lat,
                Longitude = lng,
                StartWithWindows = startup.Checked
            };
        }
    }

    internal static class Startup { internal static void SetEnabled(bool enabled) { using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) { if (key == null) return; if (enabled) key.SetValue("AutoColor", "\"" + Application.ExecutablePath + "\""); else key.DeleteValue("AutoColor", false); } } }
    internal static class Theme
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
        internal static void Apply(bool light) { using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize")) { key.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord); key.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord); } UIntPtr result; SendMessageTimeout(new IntPtr(0xffff), 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 2, 5000, out result); }
    }
    internal static class SunTimes
    {
        internal static DateTime GetSunrise(DateTime d, double lat, double lng) { return Calculate(d, lat, lng, true); } internal static DateTime GetSunset(DateTime d, double lat, double lng) { return Calculate(d, lat, lng, false); }
        private static DateTime Calculate(DateTime date, double latitude, double longitude, bool sunrise)
        {
            int n = date.DayOfYear;
            double lngHour = longitude / 15.0;
            double t = n + ((sunrise ? 6 : 18) - lngHour) / 24.0;
            double m = 0.9856 * t - 3.289;
            double l = Normalize(m + 1.916 * Math.Sin(Rad(m)) + .020 * Math.Sin(2 * Rad(m)) + 282.634, 360);
            double ra = Normalize(Deg(Math.Atan(.91764 * Math.Tan(Rad(l)))), 360);
            double lq = Math.Floor(l / 90) * 90, raq = Math.Floor(ra / 90) * 90;
            ra = (ra + lq - raq) / 15;
            double sinDec = .39782 * Math.Sin(Rad(l)), cosDec = Math.Cos(Math.Asin(sinDec));
            double cosH = (Math.Cos(Rad(90.833)) - sinDec * Math.Sin(Rad(latitude))) / (cosDec * Math.Cos(Rad(latitude)));
            if (cosH > 1 || cosH < -1)
            {
                // No rise/set event: distinguish midnight sun (polar day) from polar night
                // by whether the sun stays above the horizon at local midnight.
                double sinAltMidnight = sinDec * Math.Sin(Rad(latitude)) - cosDec * Math.Cos(Rad(latitude));
                bool polarDay = sinAltMidnight > Math.Sin(Rad(-0.833));
                if (polarDay) return sunrise ? date : date.AddDays(1); // always day
                return date; // both equal → always night
            }
            double h = sunrise ? 360 - Deg(Math.Acos(cosH)) : Deg(Math.Acos(cosH));
            h /= 15;
            double ut = Normalize(h + ra - .06571 * t - 6.622 - lngHour, 24);
            // Use a provisional noon offset, then refine with the offset at the event itself (DST).
            double localHours = Normalize(ut + TimeZoneInfo.Local.GetUtcOffset(date.AddHours(12)).TotalHours, 24);
            DateTime approx = date.AddHours(localHours);
            return date.AddHours(Normalize(ut + TimeZoneInfo.Local.GetUtcOffset(approx).TotalHours, 24));
        }
        private static double Rad(double v) { return v * Math.PI / 180; } private static double Deg(double v) { return v * 180 / Math.PI; } private static double Normalize(double v, double max) { v %= max; return v < 0 ? v + max : v; }
    }
}
