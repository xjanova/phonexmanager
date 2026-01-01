using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace PhoneRomFlashTool.Views
{
    public partial class SplashWindow : Window
    {
        private readonly string[] _loadingMessages = new[]
        {
            "Initializing...",
            "Loading configuration...",
            "Checking tools...",
            "Starting ADB service...",
            "Loading device database...",
            "Preparing UI...",
            "Almost ready..."
        };

        public SplashWindow()
        {
            InitializeComponent();
        }

        public async Task RunLoadingAsync()
        {
            double totalWidth = 400; // Approximate width of progress container
            int steps = _loadingMessages.Length;
            double stepWidth = totalWidth / steps;

            for (int i = 0; i < steps; i++)
            {
                // Update loading text
                LoadingText.Text = _loadingMessages[i];

                // Animate progress bar
                double targetWidth = (i + 1) * stepWidth;
                var animation = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressFill.BeginAnimation(WidthProperty, animation);

                // Wait before next step
                await Task.Delay(400);
            }

            // Final message
            LoadingText.Text = "Ready!";
            await Task.Delay(300);
        }
    }
}
