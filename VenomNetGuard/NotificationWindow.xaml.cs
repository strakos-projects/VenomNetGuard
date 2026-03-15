using System;
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

            TxtTitle.Text = title;
            TxtMessage.Text = message;
            TxtCounter.Text = $"ZACHYCENO: {_alertCount}";

            if (severity == "WARN")
            {
                MainBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB800"));
                TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB800"));
            }
            else
            {
                MainBorder.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF003C"));
                TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF003C"));
            }

            // Výstražný zvuk při každé změně (včetně prvního otevření)
            SystemSounds.Exclamation.Play();
        }

        protected override void OnClosed(EventArgs e)
        {
            _closeTimer?.Stop();
            base.OnClosed(e);
        }
    }
}