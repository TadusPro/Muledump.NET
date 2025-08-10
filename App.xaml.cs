using System.Runtime.InteropServices;
using Microsoft.Maui.ApplicationModel;

namespace MDTadusMod
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterApplicationRestart(string? commandLine, int flags);

        public App()
        {
            InitializeComponent();

#if WINDOWS
            // Register for application restart
            RegisterApplicationRestart(null, 0);
#endif

            MainPage = new MainPage();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                throw (Exception)e.ExceptionObject;
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                throw e.Exception;
            };
        }

        protected override Window CreateWindow(IActivationState activationState)
        {
            var window = base.CreateWindow(activationState);

            window.Title = AppInfo.Current.Name;

            return window;
        }
    }
}
