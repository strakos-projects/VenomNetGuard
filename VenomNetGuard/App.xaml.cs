using System.Configuration;
using System.Data;
using System.Windows;
using System.Globalization; // PŘIDÁNO
using System.Threading;

namespace VenomNetGuard
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        CultureInfo lang = new CultureInfo("en");
        protected override void OnStartup(StartupEventArgs e)
        {
     
            /**/CultureInfo lang = new CultureInfo("en");
            Thread.CurrentThread.CurrentCulture = lang;
            Thread.CurrentThread.CurrentUICulture = lang;
            base.OnStartup(e);
        }
    }

}
