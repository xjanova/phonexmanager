using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using PhoneRomFlashTool.ViewModels;

namespace PhoneRomFlashTool.Views
{
    public partial class MainWindow : Window
    {
        private readonly Random _random = new();
        private readonly DispatcherTimer _fireflyTimer;

        // Auto-scroll control
        private bool _autoScrollEnabled = true;
        private DateTime _lastManualScroll = DateTime.MinValue;
        private readonly TimeSpan _autoScrollPauseTime = TimeSpan.FromSeconds(5);

        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;
            Loaded += MainWindow_Loaded;

            // Initialize firefly animation timer
            _fireflyTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _fireflyTimer.Tick += CreateFirefly;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Start firefly animation
            _fireflyTimer.Start();

            // Create initial fireflies
            for (int i = 0; i < 15; i++)
            {
                CreateFirefly(null, EventArgs.Empty);
            }

            // Setup auto-scroll for LogListBox
            SetupLogAutoScroll();

            DebugWindow.LogInfo("PhoneX Manager started");
        }

        private void SetupLogAutoScroll()
        {
            if (LogListBox == null) return;

            // Subscribe to collection changes for auto-scroll
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.LogMessages.CollectionChanged += LogMessages_CollectionChanged;
            }

            // Detect manual scrolling
            LogListBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(LogListBox_ScrollChanged));
        }

        private void LogMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // Check if we should auto-scroll (not paused by manual scroll)
                if (DateTime.Now - _lastManualScroll > _autoScrollPauseTime)
                {
                    _autoScrollEnabled = true;
                }

                if (_autoScrollEnabled)
                {
                    ScrollLogToBottom();
                }
            }
        }

        private void LogListBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // If user scrolled manually (not at bottom), pause auto-scroll
            if (e.ExtentHeightChange == 0) // User-initiated scroll
            {
                var scrollViewer = GetScrollViewer(LogListBox);
                if (scrollViewer != null)
                {
                    bool isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 10;

                    if (!isAtBottom)
                    {
                        _autoScrollEnabled = false;
                        _lastManualScroll = DateTime.Now;
                    }
                    else
                    {
                        _autoScrollEnabled = true;
                    }
                }
            }
        }

        private void ScrollLogToBottom()
        {
            if (LogListBox == null || LogListBox.Items.Count == 0) return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var scrollViewer = GetScrollViewer(LogListBox);
                scrollViewer?.ScrollToEnd();
            }));
        }

        private static ScrollViewer? GetScrollViewer(DependencyObject element)
        {
            if (element is ScrollViewer scrollViewer)
                return scrollViewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                var result = GetScrollViewer(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void CreateFirefly(object? sender, EventArgs e)
        {
            if (FireflyCanvas == null) return;

            // Limit max fireflies
            if (FireflyCanvas.Children.Count > 25)
            {
                if (FireflyCanvas.Children.Count > 0)
                    FireflyCanvas.Children.RemoveAt(0);
            }

            // Random size
            double size = _random.Next(2, 6);

            // Create firefly ellipse
            var firefly = new Ellipse
            {
                Width = size,
                Height = size,
                Opacity = 0
            };

            // Random color from theme (cyan, purple, white)
            var colors = new[] { "#00D4FF", "#7B2CBF", "#4DE8FF", "#9D4EDD", "#FFFFFF" };
            var color = (Color)ColorConverter.ConvertFromString(colors[_random.Next(colors.Length)]);
            firefly.Fill = new SolidColorBrush(color);

            // Add glow effect
            firefly.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = color,
                BlurRadius = size * 3,
                ShadowDepth = 0,
                Opacity = 0.8
            };

            // Random starting position
            double startX = _random.Next(0, (int)Math.Max(100, ActualWidth - 50));
            double startY = _random.Next(0, (int)Math.Max(100, ActualHeight - 50));

            Canvas.SetLeft(firefly, startX);
            Canvas.SetTop(firefly, startY);

            FireflyCanvas.Children.Add(firefly);

            // Create animations
            var duration = TimeSpan.FromSeconds(_random.Next(8, 20));

            // Fade in/out animation
            var fadeIn = new DoubleAnimation(0, 0.6 + _random.NextDouble() * 0.4, TimeSpan.FromSeconds(2))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeOut = new DoubleAnimation(0, duration)
            {
                BeginTime = duration - TimeSpan.FromSeconds(2),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            // Movement animation - X
            double endX = startX + _random.Next(-200, 200);
            var moveX = new DoubleAnimation(startX, endX, duration)
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            // Movement animation - Y (float upward)
            double endY = startY - _random.Next(50, 200);
            var moveY = new DoubleAnimation(startY, endY, duration)
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            // Pulsing opacity
            var pulse = new DoubleAnimation(0.3, 1, TimeSpan.FromSeconds(1 + _random.NextDouble()))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            };

            // Start animations
            firefly.BeginAnimation(OpacityProperty, fadeIn);
            firefly.BeginAnimation(Canvas.LeftProperty, moveX);
            firefly.BeginAnimation(Canvas.TopProperty, moveY);

            // Schedule fade out
            var fadeOutTimer = new DispatcherTimer { Interval = duration - TimeSpan.FromSeconds(3) };
            fadeOutTimer.Tick += (s, args) =>
            {
                fadeOutTimer.Stop();
                firefly.BeginAnimation(OpacityProperty, fadeOut);
            };
            fadeOutTimer.Start();

            // Remove after animation
            var removeTimer = new DispatcherTimer { Interval = duration + TimeSpan.FromSeconds(1) };
            removeTimer.Tick += (s, args) =>
            {
                removeTimer.Stop();
                FireflyCanvas.Children.Remove(firefly);
            };
            removeTimer.Start();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _fireflyTimer.Stop();

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Cleanup();
            }

            DebugWindow.LogInfo("PhoneX Manager closed");
        }

        // Custom Title Bar - Drag to move
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaximizeButton_Click(sender, e);
            }
            else
            {
                DragMove();
            }
        }

        // Minimize button
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // Maximize/Restore button
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        // Close button
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Show Debug Panel
        private void ShowDebugPanel_Click(object sender, RoutedEventArgs e)
        {
            var debugWindow = DebugWindow.Instance;
            debugWindow.Owner = this;
            debugWindow.Show();
            debugWindow.Activate();
        }
    }
}
