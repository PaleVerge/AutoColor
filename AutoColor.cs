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
        [STAThread] private static void Main()
        {
            bool created;
            using (Mutex instance = new Mutex(true, "AutoColor.Win11.ThemeSwitcher", out created))
            {
                if (!created) return;
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
        internal static Settings Load()
        {
            Settings r = new Settings(); if (!File.Exists(FileName)) return r;
            foreach (string line in File.ReadAllLines(FileName)) { int i = line.IndexOf('='); if (i < 1) continue; string k = line.Substring(0, i), v = line.Substring(i + 1); bool b; double d;
                if (k == "FollowSun" && Boolean.TryParse(v, out b)) r.FollowSun = b;
                else if (k == "StartWithWindows" && Boolean.TryParse(v, out b)) r.StartWithWindows = b;
                else if (k == "DayTime") r.DayTime = v; else if (k == "NightTime") r.NightTime = v;
                else if (k == "Latitude" && Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.Latitude = d;
                else if (k == "Longitude" && Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.Longitude = d; }
            return r;
        }
        internal void Save() { Directory.CreateDirectory(Path.GetDirectoryName(FileName)); File.WriteAllLines(FileName, new[] { "FollowSun=" + FollowSun, "DayTime=" + DayTime, "NightTime=" + NightTime, "Latitude=" + Latitude.ToString(CultureInfo.InvariantCulture), "Longitude=" + Longitude.ToString(CultureInfo.InvariantCulture), "StartWithWindows=" + StartWithWindows }); }
    }

    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly NotifyIcon tray; private readonly System.Threading.Timer timer; private Settings settings; private bool quitting;
        internal TrayApplication()
        {
            settings = Settings.Load(); tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Auto Color", Visible = true };
            ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("立即切换为日间主题", null, delegate { Theme.Apply(true); Reschedule(); }); menu.Items.Add("立即切换为夜间主题", null, delegate { Theme.Apply(false); Reschedule(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("设置…", null, delegate { ShowSettings(); }); menu.Items.Add("退出", null, delegate { Quit(); }); tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowSettings(); };
            timer = new System.Threading.Timer(delegate { OnTimer(); }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite); Reschedule();
        }
        private void OnTimer() { if (quitting) return; try { ApplyForNow(); } finally { Reschedule(); } }
        private void ApplyForNow() { DateTime now = DateTime.Now, day, night; GetSchedule(now.Date, out day, out night); Theme.Apply(IsDaytime(now, day, night)); }
        private static bool IsDaytime(DateTime now, DateTime day, DateTime night) { return day <= night ? now >= day && now < night : now >= day || now < night; }
        private void GetSchedule(DateTime date, out DateTime day, out DateTime night)
        { if (settings.FollowSun) { day = SunTimes.GetSunrise(date, settings.Latitude, settings.Longitude); night = SunTimes.GetSunset(date, settings.Latitude, settings.Longitude); return; } TimeSpan d, n; if (!TimeSpan.TryParse(settings.DayTime, out d)) d = new TimeSpan(7, 0, 0); if (!TimeSpan.TryParse(settings.NightTime, out n)) n = new TimeSpan(19, 0, 0); day = date.Add(d); night = date.Add(n); }
        private void Reschedule()
        { if (quitting) return; DateTime now = DateTime.Now, day, night; GetSchedule(now.Date, out day, out night); DateTime next = day > now ? day : (night > now ? night : DateTime.MinValue); if (next == DateTime.MinValue) { GetSchedule(now.Date.AddDays(1), out day, out night); next = day < night ? day : night; } TimeSpan due = next - now + TimeSpan.FromSeconds(1); if (due < TimeSpan.FromSeconds(1)) due = TimeSpan.FromSeconds(1); timer.Change(due, System.Threading.Timeout.InfiniteTimeSpan); }
        private void ShowSettings() { using (SettingsForm form = new SettingsForm(settings)) { if (form.ShowDialog() != DialogResult.OK) return; settings = form.Value; settings.Save(); Startup.SetEnabled(settings.StartWithWindows); ApplyForNow(); Reschedule(); } }
        private void Quit() { quitting = true; timer.Dispose(); tray.Visible = false; tray.Dispose(); ExitThread(); }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly RadioButton fixedMode = new RadioButton { Text = "按自定义时间" }, sunMode = new RadioButton { Text = "跟随日出 / 日落" };
        private readonly TextBox dayTime = new TextBox(), nightTime = new TextBox(), latitude = new TextBox(), longitude = new TextBox(); private readonly CheckBox startup = new CheckBox { Text = "开机时自动启动（当前用户）" }; internal Settings Value { get; private set; }
        internal SettingsForm(Settings source)
        {
            Text = "Auto Color 设置"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ClientSize = new Size(385, 260); Font = SystemFonts.MessageBoxFont;
            fixedMode.Location = new Point(18, 18); fixedMode.Checked = !source.FollowSun; sunMode.Location = new Point(18, 89); sunMode.Checked = source.FollowSun;
            AddLabel("日间主题开始（HH:mm）", 38); dayTime.Location = new Point(205, 35); dayTime.Size = new Size(90, 23); dayTime.Text = source.DayTime;
            AddLabel("夜间主题开始（HH:mm）", 63); nightTime.Location = new Point(205, 60); nightTime.Size = new Size(90, 23); nightTime.Text = source.NightTime;
            AddLabel("纬度（北纬为正）", 109); latitude.Location = new Point(205, 106); latitude.Size = new Size(90, 23); latitude.Text = source.Latitude.ToString(CultureInfo.InvariantCulture);
            AddLabel("经度（东经为正）", 134); longitude.Location = new Point(205, 131); longitude.Size = new Size(90, 23); longitude.Text = source.Longitude.ToString(CultureInfo.InvariantCulture);
            startup.Location = new Point(18, 169); startup.Checked = source.StartWithWindows;
            Button ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(207, 213), Size = new Size(75, 27) }, cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(292, 213), Size = new Size(75, 27) };
            Controls.AddRange(new Control[] { fixedMode, sunMode, dayTime, nightTime, latitude, longitude, startup, ok, cancel }); AcceptButton = ok; CancelButton = cancel; ok.Click += delegate { Save(); }; fixedMode.CheckedChanged += delegate { UpdateMode(); }; UpdateMode();
        }
        private void AddLabel(string text, int y) { Controls.Add(new Label { Text = text, Location = new Point(38, y), AutoSize = true }); }
        private void UpdateMode() { dayTime.Enabled = nightTime.Enabled = fixedMode.Checked; latitude.Enabled = longitude.Enabled = sunMode.Checked; }
        private void Save() { TimeSpan ignored; double lat, lng; if (!TimeSpan.TryParse(dayTime.Text, out ignored) || !TimeSpan.TryParse(nightTime.Text, out ignored) || !Double.TryParse(latitude.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out lat) || !Double.TryParse(longitude.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out lng) || lat < -90 || lat > 90 || lng < -180 || lng > 180) { MessageBox.Show("请填写有效时间（HH:mm）与经纬度。", "Auto Color", MessageBoxButtons.OK, MessageBoxIcon.Warning); DialogResult = DialogResult.None; return; } Value = new Settings { FollowSun = sunMode.Checked, DayTime = dayTime.Text, NightTime = nightTime.Text, Latitude = lat, Longitude = lng, StartWithWindows = startup.Checked }; }
    }

    internal static class Startup { internal static void SetEnabled(bool enabled) { using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true)) { if (enabled) key.SetValue("AutoColor", "\"" + Application.ExecutablePath + "\""); else key.DeleteValue("AutoColor", false); } } }
    internal static class Theme
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
        internal static void Apply(bool light) { using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize")) { key.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord); key.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord); } UIntPtr result; SendMessageTimeout(new IntPtr(0xffff), 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 2, 5000, out result); }
    }
    internal static class SunTimes
    {
        internal static DateTime GetSunrise(DateTime d, double lat, double lng) { return Calculate(d, lat, lng, true); } internal static DateTime GetSunset(DateTime d, double lat, double lng) { return Calculate(d, lat, lng, false); }
        private static DateTime Calculate(DateTime date, double latitude, double longitude, bool sunrise)
        { int n = date.DayOfYear; double lngHour = longitude / 15.0, t = n + ((sunrise ? 6 : 18) - lngHour) / 24.0, m = 0.9856 * t - 3.289, l = Normalize(m + 1.916 * Math.Sin(Rad(m)) + .020 * Math.Sin(2 * Rad(m)) + 282.634, 360), ra = Normalize(Deg(Math.Atan(.91764 * Math.Tan(Rad(l)))), 360), lq = Math.Floor(l / 90) * 90, raq = Math.Floor(ra / 90) * 90; ra = (ra + lq - raq) / 15; double sinDec = .39782 * Math.Sin(Rad(l)), cosDec = Math.Cos(Math.Asin(sinDec)), cosH = (Math.Cos(Rad(90.833)) - sinDec * Math.Sin(Rad(latitude))) / (cosDec * Math.Cos(Rad(latitude))); if (cosH > 1 || cosH < -1) return date.Add(sunrise ? new TimeSpan(6, 0, 0) : new TimeSpan(18, 0, 0)); double h = sunrise ? 360 - Deg(Math.Acos(cosH)) : Deg(Math.Acos(cosH)); h /= 15; double localHours = Normalize(h + ra - .06571 * t - 6.622 - lngHour, 24) + TimeZoneInfo.Local.GetUtcOffset(date).TotalHours; return date.AddHours(Normalize(localHours, 24)); }
        private static double Rad(double v) { return v * Math.PI / 180; } private static double Deg(double v) { return v * 180 / Math.PI; } private static double Normalize(double v, double max) { v %= max; return v < 0 ? v + max : v; }
    }
}
