using System;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Threading;

namespace VenomNetGuard
{
    public partial class NotificationWindow : Window
    {
        private DispatcherTimer _closeTimer;
        private int _alertCount = 1;

        public NotificationWindow(string title, string message, string severity)
        {
            InitializeComponent();

            // Připnutí do pravého dolního rohu obrazovky (nad lištu Windows)
            var workArea = SystemParameters.WorkArea;
            this.Left = workArea.Right - this.Width - 15;
            this.Top = workArea.Bottom - this.Height - 15;

            UpdateAlert(title, message, severity, true);

            // Časovač: Za 10 sekund se okno samo nekompromisně zničí
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _closeTimer.Tick += (s, e) => this.Close();
            _closeTimer.Start();
        }

        public void UpdateAlert(string title, string message, string severity, bool isFirst = false)
        {
            if (!isFirst) _alertCount++;
            if (title == "[ SYSTEM DELAY ]")
            {
                TxtTitle.Text = $"[ SYSTEM DELAY - {_alertCount} - ]";
            }
            else
            {
                TxtTitle.Text = title;
            }
            TxtMessage.Text = message;
            TxtCounter.Text = $"ZACHYCENO: {_alertCount}";

            if (severity == "WARN")
            {
                MainBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB800"));
                TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB800"));
            }
            else if (severity == "INFO") 
            {
                MainBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00A2FF"));
                TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00A2FF"));
            }
            else // CRIT
            {
                MainBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF003C"));
                TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF003C"));
            }
            if (title != "[ SYSTEM DELAY ]" || isFirst)
            {
                LogDebugData(title, message, severity, isFirst);
                SystemSounds.Exclamation.Play();
            }
            
        }
        private void LogDebugData(string title, string message, string severity, bool isFirst)
        {
            try
            {
                // Vytvoří složku 'DebugLogs' hned vedle .exe souboru aplikace
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugLogs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                // Unikátní název souboru: např. alert_20260316_143022_a1b2c3d4.txt
                string randomHash = Guid.NewGuid().ToString().Substring(0, 8);
                string fileName = $"alert_{DateTime.Now:yyyyMMdd_HHmmss}_{randomHash}.txt";
                string filePath = Path.Combine(logDir, fileName);

                // Poskládání všech dat do textu včetně StackTrace (odkud se to reálně zavolalo)
                string logContent = "=== VENOM NOTIFICATION DEBUG ===\r\n" +
                                    $"Čas zachycení : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n" +
                                    $"Je první okno : {isFirst}\r\n" +
                                    $"Počet alertů  : {_alertCount}\r\n" +
                                    $"Závažnost     : {severity}\r\n" +
                                    $"Titulek       : {title}\r\n" +
                                    $"Zpráva        : {message}\r\n" +
                                    $"\r\n--- STACK TRACE (KDO TO ZAVOLAL) ---\r\n" +
                                    $"{Environment.StackTrace}\r\n";

                File.WriteAllText(filePath, logContent);
            }
            catch
            {
                // Pokud zápis selže, program nespadne
            }
        }
        protected override void OnClosed(EventArgs e)
        {
            _closeTimer?.Stop();
            base.OnClosed(e);
        }
    }
}