using Microsoft.Maui.ApplicationModel;

namespace MDTadusMod
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
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
