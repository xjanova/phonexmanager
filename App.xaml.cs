using System.Windows;
using PhoneRomFlashTool.Views;

namespace PhoneRomFlashTool
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Show splash screen
            var splash = new SplashWindow();
            splash.Show();

            // Run loading animation
            await splash.RunLoadingAsync();

            // Create and show main window
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();

            // Close splash
            splash.Close();
        }
    }
}
