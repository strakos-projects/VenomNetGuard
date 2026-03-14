using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Drawing;
using System.IO; // PŘIDÁNO: Pro práci se soubory
using System.Text.Json; // PŘIDÁNO: Pro ukládání do JSON
using System.Collections.Generic; // PŘIDÁNO: Pro List<T>
using Forms = System.Windows.Forms;

using Properties = VenomNetGuard.Properties;
namespace VenomNetGuard
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<SecurityEvent> SecurityEvents { get; set; }
        private EventLog _securityLog;
        private Forms.NotifyIcon _notifyIcon;
        private DateTime _lastAlertTime = DateTime.MinValue;
        private DateTime _silencedUntil = DateTime.MinValue;
        private int _totalWarningsCount = 0;

        private readonly string _saveFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nexus_data.json");

        private void MarkAllReviewed_Click(object sender, RoutedEventArgs e)
        {
            SecurityEvents.Clear();
            UpdateDashboardCounters();
            SaveData(); 
        }

        private void UpdateDashboardCounters()
        {
            int pendingCount = 0;
            bool isCompromised = false;
            bool hasWarning = false;

            foreach (var ev in SecurityEvents)
            {
                if (ev.Severity == "CRIT") { pendingCount++; isCompromised = true; }
                else if (ev.Severity == "WARN") { pendingCount++; hasWarning = true; }
            }

            TxtPendingCount.Text = pendingCount.ToString("D2");
            TxtWarningsCount.Text = _totalWarningsCount.ToString("D2");

            if (isCompromised)
            {
                TxtStatus.Text = "ALERT";
                TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF003C")); // Červená
            }
            else if (hasWarning)
            {
                TxtStatus.Text = "WARNING";
                TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB800")); // Žlutá
            }
            else
            {
                TxtStatus.Text = Properties.Resources.TxtSecure;
                TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00A2FF")); // Modrá
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _notifyIcon = new Forms.NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Shield;
            _notifyIcon.Text = Properties.Resources.TrayIconText;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
            _notifyIcon.BalloonTipClicked += NotifyIcon_BalloonTipClicked;

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.BackColor = Color.FromArgb(18, 18, 22);
            contextMenu.ForeColor = Color.White;

            var exitMenuItem = new Forms.ToolStripMenuItem(Properties.Resources.CtxTerminateNexus);
            exitMenuItem.Click += (s, e) => { System.Windows.Application.Current.Shutdown(); };

            contextMenu.Items.Add(exitMenuItem);
            _notifyIcon.ContextMenuStrip = contextMenu;
            SecurityEvents = new ObservableCollection<SecurityEvent>();
            this.DataContext = this;

            LoadData();

            this.Loaded += MainWindow_Loaded;
        }

        private void SaveData()
        {
            try
            {
                var data = new AppData
                {
                    EnableAlerts = ChkEnableAlerts.IsChecked == true,
                    AlertCrit = ChkAlertCrit.IsChecked == true,
                    AlertWarn = ChkAlertWarn.IsChecked == true,
                    TotalWarningsCount = _totalWarningsCount,
                    SavedEvents = new List<SecurityEvent>(SecurityEvents)
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_saveFilePath, json);
            }
            catch (Exception)
            {
            }
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_saveFilePath))
                {
                    string json = File.ReadAllText(_saveFilePath);
                    var data = JsonSerializer.Deserialize<AppData>(json);

                    if (data != null)
                    {
                        ChkEnableAlerts.IsChecked = data.EnableAlerts;
                        ChkAlertCrit.IsChecked = data.AlertCrit;
                        ChkAlertWarn.IsChecked = data.AlertWarn;
                        _totalWarningsCount = data.TotalWarningsCount;

                        // Obnova logů
                        SecurityEvents.Clear();
                        foreach (var ev in data.SavedEvents)
                        {
                            SecurityEvents.Add(ev);
                        }

                        UpdateDashboardCounters();
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void NotifyIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            _silencedUntil = DateTime.Now.AddSeconds(5);
            NotifyIcon_DoubleClick(sender, e);
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            _silencedUntil = DateTime.Now.AddSeconds(5);

            this.Show();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                this.ShowInTaskbar = false;
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveData();

            _notifyIcon.Dispose();
            base.OnClosed(e);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ChkRunAtStartup.Checked -= ChkRunAtStartup_Checked;
            ChkRunAtStartup.Unchecked -= ChkRunAtStartup_Unchecked;
            ChkRunAtStartup.IsChecked = IsStartupTaskRegistered();
            ChkRunAtStartup.Checked += ChkRunAtStartup_Checked;
            ChkRunAtStartup.Unchecked += ChkRunAtStartup_Unchecked;
            StartEventLogMonitoring();
        }

        private void StartEventLogMonitoring()
        {
            try
            {
                _securityLog = new EventLog("Security");
                _securityLog.EnableRaisingEvents = true;
                _securityLog.EntryWritten += SecurityLog_EntryWritten;

                SecurityEvents.Add(new SecurityEvent
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Severity = "INFO",
                    SourceIP = "SYSTEM",
                    TargetInfo = Properties.Resources.LogNexusInitialized
                });

                _notifyIcon.ShowBalloonTip(
                    3000,
                    Properties.Resources.BalloonTitleCore,
                    Properties.Resources.BalloonMsgActivated,
                    Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                string errorMsg = string.Format(Properties.Resources.MsgBoxAccessDeniedMsg, ex.Message);

                System.Windows.MessageBox.Show(
                    errorMsg,
                    Properties.Resources.MsgBoxAccessDeniedTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SecurityLog_EntryWritten(object sender, EntryWrittenEventArgs e)
        {
            long eventId = e.Entry.InstanceId & 0x3FFFFFFF;

            if (eventId == 5140 || eventId == 4625)
            {
                string message = e.Entry.Message;
                string sourceIp = ExtractIpAddress(message);

                string severity = eventId == 4625 ? "WARN" : "CRIT";
                string target = eventId == 4625 ? Properties.Resources.LogTargetFailedLogon : Properties.Resources.LogTargetShareAccessed;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    SecurityEvents.Add(new SecurityEvent
                    {
                        Timestamp = e.Entry.TimeGenerated.ToString("HH:mm:ss"),
                        Severity = severity,
                        SourceIP = sourceIp,
                        TargetInfo = target
                    });
                    if (severity == "WARN") _totalWarningsCount++;
                    UpdateDashboardCounters();

                    SaveData();

                    bool isAlertsEnabled = false;
                    bool notifyCrit = false;
                    bool notifyWarn = false;

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        isAlertsEnabled = ChkEnableAlerts.IsChecked == true;
                        notifyCrit = ChkAlertCrit.IsChecked == true;
                        notifyWarn = ChkAlertWarn.IsChecked == true;
                    });

                    if (isAlertsEnabled && DateTime.Now > _silencedUntil)
                    {
                        if ((severity == "CRIT" && notifyCrit) || (severity == "WARN" && notifyWarn))
                        {
                            if ((DateTime.Now - _lastAlertTime).TotalSeconds >= 8)
                            {
                                Forms.ToolTipIcon iconType = severity == "CRIT" ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Warning;

                                string alertTitle = string.Format(Properties.Resources.BalloonAlertTitle, severity);
                                string alertMsg = string.Format(Properties.Resources.BalloonAlertMsg, target, sourceIp);

                                _notifyIcon.ShowBalloonTip(3000, alertTitle, alertMsg, iconType);

                                _lastAlertTime = DateTime.Now;
                            }
                        }
                    }
                });
            }
        }

        private string ExtractIpAddress(string message)
        {
            Match match = Regex.Match(message, @"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b");
            return match.Success ? match.Value : Properties.Resources.UnknownIp;
        }

        private void ReviewAction_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SecurityEvent selectedEvent)
            {
                SecurityEvents.Remove(selectedEvent);
                UpdateDashboardCounters();
                SaveData(); 
            }
        }
        // PŘEPÍNÁNÍ ZÁLOŽEK
        private void NavDashboard_Click(object sender, RoutedEventArgs e)
        {
            GridDashboard.Visibility = Visibility.Visible;
            GridSettings.Visibility = Visibility.Collapsed;
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            GridDashboard.Visibility = Visibility.Collapsed;
            GridSettings.Visibility = Visibility.Visible;
        }

        private void ChkRunAtStartup_Checked(object sender, RoutedEventArgs e)
        {
            SetStartupTask(true);
            SaveData();
        }

        private void ChkRunAtStartup_Unchecked(object sender, RoutedEventArgs e)
        {
            SetStartupTask(false);
            SaveData();
        }

        private void SetStartupTask(bool enable)
        {
            try
            {
                string taskName = "VenomNetGuard_AutoStart";
                string exePath = Process.GetCurrentProcess().MainModule.FileName;

                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                if (enable)
                {
                    psi.Arguments = $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f";
                }
                else
                {
                    // Smaže úlohu
                    psi.Arguments = $"/delete /tn \"{taskName}\" /f";
                }

                Process p = Process.Start(psi);
                p.WaitForExit();
            }
            catch (Exception)
            {
            }
        }
        private bool IsStartupTaskRegistered()
        {
            try
            {
                string taskName = "VenomNetGuard_AutoStart";

                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe")
                {
                    Arguments = $"/query /tn \"{taskName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public class SecurityEvent
    {
        public string Timestamp { get; set; }
        public string Severity { get; set; }
        public string SourceIP { get; set; }
        public string TargetInfo { get; set; }
    }

    public class AppData
    {
        public bool EnableAlerts { get; set; }
        public bool AlertCrit { get; set; }
        public bool AlertWarn { get; set; }
        public int TotalWarningsCount { get; set; }
        public List<SecurityEvent> SavedEvents { get; set; } = new List<SecurityEvent>();
    }
}