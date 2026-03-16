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
using System.Diagnostics.Eventing.Reader;
using Properties = VenomNetGuard.Properties;
namespace VenomNetGuard
{
    public partial class MainWindow : Window
    {
        private NotificationWindow _customNotifier = null;
        private DateTime _globalSilenceUntil = DateTime.MinValue;


        //private System.Windows.Threading.DispatcherTimer _defenderTimer;
        public ObservableCollection<SecurityEvent> SecurityEvents { get; set; }
        private EventLogWatcher _defenderWatcher;
        private EventLog _securityLog;
        private Forms.NotifyIcon _notifyIcon;
        private DateTime _lastAlertTime = DateTime.MinValue;
        private DateTime _silencedUntil = DateTime.MinValue;
        private int _totalWarningsCount = 0;
        private bool _isRealExit = false;
        private readonly string _saveFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nexus_data.json");
        private System.Windows.Threading.DispatcherTimer _trayRetryTimer;
        private bool _hasShownTrayNotification = false;
        private static System.Threading.Mutex _appMutex = null;
        private void MarkAllReviewed_Click(object sender, RoutedEventArgs e)
        {
            SecurityEvents.Clear();
            _totalWarningsCount = 0;
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
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                e.Cancel = true;
                this.WindowState = WindowState.Minimized;
                return;
            }
            base.OnClosing(e);
        }
        public MainWindow()
        {
            bool createdNew;
            _appMutex = new System.Threading.Mutex(true, "VenomNetGuard_Core_Mutex", out createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show("Venom NetGuard už běží na pozadí v Tray liště!", "Venom NetGuard", MessageBoxButton.OK, MessageBoxImage.Warning);
                System.Windows.Application.Current.Shutdown();
                return;
            }
            InitializeComponent();
            UpdateNavUI("Dashboard");
            _notifyIcon = new Forms.NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Shield;
            _notifyIcon.Text = Properties.Resources.TrayIconText;
            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
            _notifyIcon.BalloonTipClicked += NotifyIcon_BalloonTipClicked;
            
            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.BackColor = Color.FromArgb(18, 18, 22);
            contextMenu.ForeColor = Color.White;
            var exitMenuItem = new Forms.ToolStripMenuItem(Properties.Resources.CtxTerminateNexus);

            exitMenuItem.Click += (s, e) => {
                _isRealExit = true;
                System.Windows.Application.Current.Shutdown();
            };

            contextMenu.Items.Add(exitMenuItem);
            _notifyIcon.ContextMenuStrip = contextMenu;

            SecurityEvents = new ObservableCollection<SecurityEvent>();
            System.Windows.Data.CollectionViewSource.GetDefaultView(SecurityEvents).Filter = FilterSecurityEvents;
            this.DataContext = this;

            LoadData();
            this.Loaded += MainWindow_Loaded;

            string[] args = Environment.GetCommandLineArgs();
            if (Array.Exists(args, arg => arg.Equals("-autostart", StringComparison.OrdinalIgnoreCase)))
            {
                _hasShownTrayNotification = true;
                this.WindowState = WindowState.Minimized;
                // this.ShowInTaskbar = false;
                StartTrayRetryMechanism();
            }
            else
            {
                this.WindowState = WindowState.Normal;
                this.ShowInTaskbar = true;
                _notifyIcon.Visible = true;
            }
        }

        // PŘIDÁNO: Importy pro Windows API (vložte kamkoliv do třídy MainWindow)
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            SaveData();
        }
        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Data.CollectionViewSource.GetDefaultView(SecurityEvents).Refresh();
            SaveData(); 
        }

        private bool FilterSecurityEvents(object item)
        {
            if (item is SecurityEvent ev)
            {
                if (ev.Severity == "CRIT" && ChkFilterCrit.IsChecked == true) return true;
                if (ev.Severity == "WARN" && ChkFilterWarn.IsChecked == true) return true;
                if (ev.Severity == "INFO" && ChkFilterInfo.IsChecked == true) return true;
                return false; 
            }
            return true;
        }
        private async void StartTrayRetryMechanism()
        {
            int maxRetries = 60;

            for (int i = 0; i < maxRetries; i++)
            {
                IntPtr trayWnd = FindWindow("Shell_TrayWnd", null);

                if (trayWnd != IntPtr.Zero)
                {
                    IntPtr trayNotifyWnd = FindWindowEx(trayWnd, IntPtr.Zero, "TrayNotifyWnd", null);

                    if (trayNotifyWnd != IntPtr.Zero)
                    {
                        await System.Threading.Tasks.Task.Delay(500);

                        try
                        {
                            // PŘIDÁNO: Teď máme jistotu, že lišta Windows existuje! Můžeme ikonu bezpečně zapnout.
                            _notifyIcon.Visible = true;
                            _notifyIcon.ShowBalloonTip(3000, Properties.Resources.BalloonTitleCore, Properties.Resources.BalloonMsgActivated, Forms.ToolTipIcon.Info);
                        }
                        catch
                        {
                        }

                        return;
                    }
                }
                await System.Threading.Tasks.Task.Delay(1000);
            }
        }
        private void ChkEnableCFA_Click(object sender, RoutedEventArgs e)
        {
            string state = ChkEnableCFA.IsChecked == true ? "1" : "0";
            RunPowerShellCommand($"Set-MpPreference -EnableControlledFolderAccess {state}");
        }

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "Vyberte složku, kterou chcete chránit před ransomwarem";
                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                {
                    string path = dialog.SelectedPath;
                    RunPowerShellCommand($"Add-MpPreference -ControlledFolderAccessProtectedFolders \"{path}\"");
                    if (!ListProtectedFolders.Items.Contains(path))
                        ListProtectedFolders.Items.Add(path);
                }
            }
        }
        private void RunPowerShellCommand(string command)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe")
                {
                    Arguments = $"-Command \"{command}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch (Exception)
            {
            }
        }
        private void BtnRemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (ListProtectedFolders.SelectedItem is string path)
            {
                RunPowerShellCommand($"Remove-MpPreference -ControlledFolderAccessProtectedFolders \"{path}\"");
                ListProtectedFolders.Items.Remove(path);
            }
        }

        private void BtnAddApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Vyberte povolenou aplikaci (např. Visual Studio)",
                Filter = "Spustitelné soubory (*.exe)|*.exe|Všechny soubory (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                string path = dialog.FileName;
                RunPowerShellCommand($"Add-MpPreference -ControlledFolderAccessAllowedApplications \"{path}\"");
                if (!ListAllowedApps.Items.Contains(path))
                    ListAllowedApps.Items.Add(path);
            }
        }

        private void BtnRemoveApp_Click(object sender, RoutedEventArgs e)
        {
            if (ListAllowedApps.SelectedItem is string path)
            {
                RunPowerShellCommand($"Remove-MpPreference -ControlledFolderAccessAllowedApplications \"{path}\"");
                ListAllowedApps.Items.Remove(path);
            }
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
                    AlertInfo = ChkAlertInfo.IsChecked == true,

                    FilterCrit = ChkFilterCrit.IsChecked == true,
                    FilterWarn = ChkFilterWarn.IsChecked == true, 
                    FilterInfo = ChkFilterInfo.IsChecked == true, 

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
        private void UpdateNavUI(string activeGrid)
        {
            var activeBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00FFCC"));
            var inactiveBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4A4A5A"));
            var activeBg = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1F1F2E"));
            var transparent = System.Windows.Media.Brushes.Transparent;

            BtnNavDashboard.Foreground = inactiveBrush; BtnNavDashboard.Background = transparent; BtnNavDashboard.BorderThickness = new Thickness(0);
            BtnNavSettings.Foreground = inactiveBrush; BtnNavSettings.Background = transparent; BtnNavSettings.BorderThickness = new Thickness(0);
            BtnNavShield.Foreground = inactiveBrush; BtnNavShield.Background = transparent; BtnNavShield.BorderThickness = new Thickness(0);

            if (activeGrid == "Dashboard") { BtnNavDashboard.Foreground = activeBrush; BtnNavDashboard.Background = activeBg; BtnNavDashboard.BorderThickness = new Thickness(1, 0, 0, 0); }
            else if (activeGrid == "Settings") { BtnNavSettings.Foreground = activeBrush; BtnNavSettings.Background = activeBg; BtnNavSettings.BorderThickness = new Thickness(1, 0, 0, 0); }
            else if (activeGrid == "Shield") { BtnNavShield.Foreground = activeBrush; BtnNavShield.Background = activeBg; BtnNavShield.BorderThickness = new Thickness(1, 0, 0, 0); }
        }
        private void NavDashboard_Click(object sender, RoutedEventArgs e)
        {
            GridDashboard.Visibility = Visibility.Visible; GridSettings.Visibility = Visibility.Collapsed; GridShield.Visibility = Visibility.Collapsed;
            UpdateNavUI("Dashboard");
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            GridDashboard.Visibility = Visibility.Collapsed; GridSettings.Visibility = Visibility.Visible; GridShield.Visibility = Visibility.Collapsed;
            UpdateNavUI("Settings");
        }

        private void NavShield_Click(object sender, RoutedEventArgs e)
        {
            GridDashboard.Visibility = Visibility.Collapsed; GridSettings.Visibility = Visibility.Collapsed; GridShield.Visibility = Visibility.Visible;
            UpdateNavUI("Shield");
            LoadDefenderSettings();
        }

        private void LoadDefenderSettings()
        {
            try
            {
                ProcessStartInfo psiStatus = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"(Get-MpPreference).EnableControlledFolderAccess\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, WindowStyle = ProcessWindowStyle.Hidden };

                using (Process p = Process.Start(psiStatus))
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    ChkEnableCFA.IsChecked = (output == "1");
                }

                ProcessStartInfo psiFolders = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"(Get-MpPreference).ControlledFolderAccessProtectedFolders\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, WindowStyle = ProcessWindowStyle.Hidden };

                using (Process p = Process.Start(psiFolders))
                {
                    string[] folders = p.StandardOutput.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    ListProtectedFolders.Items.Clear();
                    foreach (string f in folders)
                        if (!string.IsNullOrWhiteSpace(f)) ListProtectedFolders.Items.Add(f.Trim());
                }

                ProcessStartInfo psiApps = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"(Get-MpPreference).ControlledFolderAccessAllowedApplications\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, WindowStyle = ProcessWindowStyle.Hidden };

                using (Process p = Process.Start(psiApps))
                {
                    string[] apps = p.StandardOutput.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    ListAllowedApps.Items.Clear();
                    foreach (string a in apps)
                        if (!string.IsNullOrWhiteSpace(a)) ListAllowedApps.Items.Add(a.Trim());
                }
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
                        ChkAlertInfo.IsChecked = data.AlertInfo;

                        ChkFilterCrit.IsChecked = data.FilterCrit;
                        ChkFilterWarn.IsChecked = data.FilterWarn;
                        ChkFilterInfo.IsChecked = data.FilterInfo;

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
            // this.ShowInTaskbar = true;
            this.Activate(); 
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                // this.ShowInTaskbar = false;
                if (!_hasShownTrayNotification && _notifyIcon.Visible)
                {
                    _notifyIcon.ShowBalloonTip(
                        3000,
                        Properties.Resources.BalloonTitleCore,
                        Properties.Resources.BalloonMsgMinimized,
                        Forms.ToolTipIcon.Info);

                    _hasShownTrayNotification = true; 
                }
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
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
            StartEventLogMonitoring();
        }

        private void DefenderLog_EventWritten(object sender, EventRecordWrittenEventArgs e)
        {
            if (e.EventException != null || e.EventRecord == null) return;

            string timestamp = e.EventRecord.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string processName = ExtractBlockedProcess(e.EventRecord);
            string targetText = $"{Properties.Resources.LogTargetFolderBlocked} (Proces: {processName})";
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                SecurityEvents.Add(new SecurityEvent
                {
                    Timestamp = timestamp,
                    Severity = "CRIT",
                    SourceIP = "CFA SHIELD",
                    TargetInfo = targetText
                });

                UpdateDashboardCounters();
                SaveData();

                bool isAlertsEnabled = ChkEnableAlerts.IsChecked == true;
                bool notifyCrit = ChkAlertCrit.IsChecked == true;

                /*if (isAlertsEnabled && notifyCrit && DateTime.Now > _silencedUntil)
                {
                    if ((DateTime.Now - _lastAlertTime).TotalSeconds >= 8)
                    {
                        string alertTitle = string.Format(Properties.Resources.BalloonAlertTitle, "CRIT");
                        string alertMsg = string.Format(Properties.Resources.BalloonAlertMsg, Properties.Resources.LogTargetFolderBlocked, "LOCAL PROCESS");
                        if (_notifyIcon.Visible)
                        {
                            _notifyIcon.ShowBalloonTip(3000, alertTitle, alertMsg, Forms.ToolTipIcon.Error);
                        _lastAlertTime = DateTime.Now;
                        }
                    }
                }*/
                if (isAlertsEnabled && notifyCrit)
                {
                    string alertTitle = string.Format(Properties.Resources.BalloonAlertTitle, "CRIT");
                    string alertMsg = string.Format(Properties.Resources.BalloonAlertMsg, Properties.Resources.LogTargetFolderBlocked, "LOCAL PROCESS");

                    ShowCustomNotification(alertTitle, alertMsg, "CRIT");
                }
            }));
        }
        private void StartEventLogMonitoring()
        {
            try
            {
                string startTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                string queryStr = $"*[System[(EventID=1123 or EventID=1124) and TimeCreated[@SystemTime >= '{startTime}']]]";
                EventLogQuery query = new EventLogQuery("Microsoft-Windows-Windows Defender/Operational", PathType.LogName, queryStr);

                _defenderWatcher = new EventLogWatcher(query);
                _defenderWatcher.EventRecordWritten += DefenderLog_EventWritten;
                _defenderWatcher.Enabled = true;
                // ------------------------------------------------------------------

                _securityLog = new EventLog("Security");
                _securityLog.EnableRaisingEvents = true;
                _securityLog.EntryWritten += SecurityLog_EntryWritten;

                SecurityEvents.Add(new SecurityEvent
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Severity = "INFO",
                    SourceIP = "SYSTEM",
                    TargetInfo = Properties.Resources.LogNexusInitialized
                });
                if (_notifyIcon.Visible)
                {
                    _notifyIcon.ShowBalloonTip(3000, Properties.Resources.BalloonTitleCore, Properties.Resources.BalloonMsgActivated, Forms.ToolTipIcon.Info);

                } }
            catch (Exception ex)
            {
                string errorMsg = string.Format(Properties.Resources.MsgBoxAccessDeniedMsg, ex.Message);
                System.Windows.MessageBox.Show(errorMsg, Properties.Resources.MsgBoxAccessDeniedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SecurityLog_EntryWritten(object sender, EntryWrittenEventArgs e)
        {
            if ((DateTime.Now - e.Entry.TimeGenerated).TotalSeconds > 60)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowCustomNotification("[ SYSTEM DELAY ]", "Windows odeslal staré/zpožděné události z mezipaměti. Aplikace je z bezpečnostních důvodů ignorovala.", "WARN");
                }));
                return;

            }
            long eventId = e.Entry.InstanceId & 0x3FFFFFFF;
            if (eventId == 5140 || eventId == 4625 || eventId == 4720 || eventId == 1102 || eventId == 4624)
            {
                string message = e.Entry.Message;
                string sourceIp = ExtractIpAddress(message);
                string severity = "";
                string target = "";

                switch (eventId)
                {
                    case 4624:
                        if (Regex.IsMatch(message, @"(?:Logon Type|Typ přihlášení):\s*3", RegexOptions.IgnoreCase))
                        {
                            severity = "INFO";
                            // PŘEPSÁNO NA LOKALIZACI:
                            target = $"{Properties.Resources.LogTargetNetworkAccess} (Účet: {ExtractAccountName(message)})";
                        }
                        break;
                    case 4625: severity = "WARN"; target = $"{Properties.Resources.LogTargetFailedLogon} (Účet: {ExtractAccountName(message)})"; break;
                    case 5140: severity = "CRIT"; target = Properties.Resources.LogTargetShareAccessed; break;
                    case 4720: severity = "CRIT"; target = Properties.Resources.LogTargetUserCreated; break;
                    case 1102: severity = "CRIT"; target = Properties.Resources.LogTargetLogCleared; break;
                }
                // Pokud to byla 4624, ale ne ze sítě (např. běžné přihlášení Windows služby), tak ji tiše zahodíme
                if (string.IsNullOrEmpty(severity)) return;

                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    SecurityEvents.Add(new SecurityEvent
                    {
                        Timestamp = e.Entry.TimeGenerated.ToString("yyyy-MM-dd HH:mm:ss"),
                        Severity = severity,
                        SourceIP = sourceIp,
                        TargetInfo = target
                    });

                    if (severity == "WARN") _totalWarningsCount++;
                    UpdateDashboardCounters();
                    SaveData();

                    bool isAlertsEnabled = ChkEnableAlerts.IsChecked == true;
                    bool notifyCrit = ChkAlertCrit.IsChecked == true;
                    bool notifyWarn = ChkAlertWarn.IsChecked == true;
                    bool notifyInfo = ChkAlertInfo.IsChecked == true;
                    /*
                    if (isAlertsEnabled && DateTime.Now > _silencedUntil)
                    {
                        if ((severity == "CRIT" && notifyCrit) || (severity == "WARN" && notifyWarn))
                        {
                            if ((DateTime.Now - _lastAlertTime).TotalSeconds >= 8)
                            {
                                Forms.ToolTipIcon iconType = severity == "CRIT" ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Warning;
                                string alertTitle = string.Format(Properties.Resources.BalloonAlertTitle, severity);
                                string alertMsg = string.Format(Properties.Resources.BalloonAlertMsg, target, sourceIp);

                                if (_notifyIcon.Visible)
                                {
                                    
                                    _notifyIcon.ShowBalloonTip(3000, alertTitle, alertMsg, iconType);
                                  _lastAlertTime = DateTime.Now;}
                              
                            }
                        }
                    }*/
                    if (isAlertsEnabled)
                    {
                        if ((severity == "CRIT" && notifyCrit) ||
                            (severity == "WARN" && notifyWarn) ||
                            (severity == "INFO" && notifyInfo))
                        {

                            string alertTitle = severity == "INFO" ? Properties.Resources.AlertTitleInfo : string.Format(Properties.Resources.BalloonAlertTitle, severity);
                            string alertMsg = string.Format(Properties.Resources.BalloonAlertMsg, target, sourceIp);
                            ShowCustomNotification(alertTitle, alertMsg, severity);
                        }
                    }
                }));
            }
        }
        private string ExtractBlockedProcess(EventRecord ev)
        {
            try
            {
                string msg = ev.FormatDescription();
                if (!string.IsNullOrEmpty(msg))
                {
                    Match m = Regex.Match(msg, @"([a-zA-Z]:\\[^\s]+\.exe)", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        return System.IO.Path.GetFileName(m.Groups[1].Value); 
                    }
                }
                foreach (var prop in ev.Properties)
                {
                    string val = prop.Value?.ToString();
                    if (val != null && val.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return System.IO.Path.GetFileName(val);
                    }
                }
            }
            catch { }
            return "Neznámý proces";
        }
        private string ExtractAccountName(string message)
        {
            try
            {
                MatchCollection matches = Regex.Matches(message, @"(?:Account Name|Název účtu):\s*([^\s]+)");

                if (matches.Count >= 2)
                    return matches[1].Groups[1].Value.Trim();
                else if (matches.Count == 1)
                    return matches[0].Groups[1].Value.Trim();
            }
            catch { }

            return "Neznámý";
        }
        private string ExtractIpAddress(string message)
        {
            try
            {
                Match match = Regex.Match(message, @"(?:Source Network Address|Zdrojová síťová adresa|Síťová adresa zdroje|Source Address|Adresa zdroje).*?:\s*([^\s]+)", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    string ip = match.Groups[1].Value.Trim();

                    if (ip == "-" || ip == "::1") return "127.0.0.1";

                    return ip;
                }

                Match ipv4 = Regex.Match(message, @"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b");
                if (ipv4.Success) return ipv4.Value;

                Match ipv6 = Regex.Match(message, @"(?:[A-Fa-f0-9]{1,4}:){2,7}[A-Fa-f0-9]{1,4}(?:%\d+)?", RegexOptions.IgnoreCase);
                if (ipv6.Success) return ipv6.Value;
            }
            catch { }

            return Properties.Resources.UnknownIp;
        }

        private void ReviewAction_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SecurityEvent selectedEvent)
            {
                if (selectedEvent.Severity == "WARN" && _totalWarningsCount > 0)
                {
                    _totalWarningsCount--;
                }
                SecurityEvents.Remove(selectedEvent);
                UpdateDashboardCounters();
                SaveData(); 
            }
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
                    psi.Arguments = $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\" -autostart\" /sc onlogon /rl highest /f";
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

        private void ShowCustomNotification(string title, string message, string severity)
        {
            // Pokud jsme v tichém režimu (umlčeno na 20s po zavření), zprávu úplně ignorujeme
            if (DateTime.Now < _globalSilenceUntil) return;

            if (_customNotifier != null && _customNotifier.IsLoaded)
            {
                // Okno už existuje (jsme uvnitř 10s okna). Jen přepíšeme text na nejnovější a pípne.
                _customNotifier.UpdateAlert(title, message, severity);
            }
            else
            {
                // Vytváříme nové vyskakovací okno
                _customNotifier = new NotificationWindow(title, message, severity);

                // MAGIE: Jakmile se okno po svých 10s samo zavře, zablokujeme další okna na 20 sekund!
                _customNotifier.Closed += (s, ev) =>
                {
                    _globalSilenceUntil = DateTime.Now.AddSeconds(20);
                    _customNotifier = null;
                };

                _customNotifier.Show();
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
        public bool EnableAlerts { get; set; } = true;
        public bool AlertCrit { get; set; } = true;
        public bool AlertWarn { get; set; } = false;
        public bool AlertInfo { get; set; } = false;

        public bool FilterCrit { get; set; } = true;
        public bool FilterWarn { get; set; } = true;
        public bool FilterInfo { get; set; } = false;

        public int TotalWarningsCount { get; set; }
        public List<SecurityEvent> SavedEvents { get; set; } = new List<SecurityEvent>();
    }
}