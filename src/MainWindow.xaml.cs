using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Extensions.Configuration;

namespace HTrack
{
    public class AppSettings
    {
        public ColorSettings Colors { get; set; } = new ColorSettings();
        public WindowSettings Window { get; set; } = new WindowSettings();
    }

    public class ColorSettings
    {
        public string Completed { get; set; } = "#4CAF50";
        public string Incomplete { get; set; } = "#E0E0E0";
        public string Pending { get; set; } = "#FF9800";
        public string Outline { get; set; } = "#BDBDBD";
        public string Background { get; set; } = "#F5F5F5";
        public double BackgroundOpacity { get; set; } = 0.95;
    }

    public class WindowSettings
    {
        public bool AllowTransparentBackground { get; set; } = false;
    }

    public partial class MainWindow : Window
    {
        #region Windows API for Desktop Pinning
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        #endregion

        private DateTime currentYear = DateTime.Now;
        private Border[,] dayBoxes;
        private Random random = new Random(); // For demo data
        private AppSettings appSettings;

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetAsDesktopChild();
            SetAsToolWindow();
            SetNoActivate();
            CreateHabitGrid();
            GenerateDemoData();
            
            // Load and apply configuration after UI is set up
            LoadConfiguration();
            ApplyConfiguration();
        }

        private void LoadConfiguration()
        {
            try
            {
                string configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .Build();

                appSettings = new AppSettings();
                
                // Manually map configuration values
                var colorsSection = configuration.GetSection("Colors");
                
                if (colorsSection.Exists())
                {
                    appSettings.Colors.Completed = colorsSection["Completed"] ?? appSettings.Colors.Completed;
                    appSettings.Colors.Incomplete = colorsSection["Incomplete"] ?? appSettings.Colors.Incomplete;
                    appSettings.Colors.Pending = colorsSection["Pending"] ?? appSettings.Colors.Pending;
                    appSettings.Colors.Outline = colorsSection["Outline"] ?? appSettings.Colors.Outline;
                    appSettings.Colors.Background = colorsSection["Background"] ?? appSettings.Colors.Background;
                                       
                    if (double.TryParse(colorsSection["BackgroundOpacity"], out double opacity))
                    {
                        appSettings.Colors.BackgroundOpacity = opacity;
                    }                   
                }

                var windowSection = configuration.GetSection("Window");
                
                if (windowSection.Exists())
                {
                    if (bool.TryParse(windowSection["AllowTransparentBackground"], out bool allowTransparent))
                    {
                        appSettings.Window.AllowTransparentBackground = allowTransparent;
                    }                    
                }
            }
            catch (Exception ex)
            {
                appSettings = new AppSettings();
            }
        }

        private void ApplyConfiguration()
        {
            try
            {
                // Apply background color and opacity
                ApplyBackgroundSettings();
                
                // Apply color scheme
                ApplyColorScheme();
                
                // Apply border settings
                ApplyBorderSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply configuration: {ex.Message}");
            }
        }

        private void ApplyBorderSettings()
        {           
            // Find the main border and adjust its opacity based on transparency setting
            var mainBorder = this.FindName("MainBorder") as Border;
            if (mainBorder != null)
            {
                if (appSettings.Window.AllowTransparentBackground)
                {
                    mainBorder.Opacity = 1.0; // Full opacity when transparent (the brush itself is transparent)
                }
                else
                {
                    mainBorder.Opacity = appSettings.Colors.BackgroundOpacity;
                }
            }
        }

        private void ApplyBackgroundSettings()
        {
            if (appSettings.Window.AllowTransparentBackground)
            {
                this.Background = Brushes.Transparent;
            }
            else
            {
                var backgroundColor = (Color)ColorConverter.ConvertFromString(appSettings.Colors.Background);
                this.Background = new SolidColorBrush(backgroundColor);
            }
        }

        private void ApplyColorScheme()
        {            
            var completedColor = (Color)ColorConverter.ConvertFromString(appSettings.Colors.Completed);
            
            // Handle transparent incomplete color
            Color incompleteColor;
            if (appSettings.Colors.Incomplete.ToLower() == "transparent")
            {
                incompleteColor = Colors.Transparent;
            }
            else
            {
                incompleteColor = (Color)ColorConverter.ConvertFromString(appSettings.Colors.Incomplete);
            }
            
            var pendingColor = (Color)ColorConverter.ConvertFromString(appSettings.Colors.Pending);
            var outlineColor = (Color)ColorConverter.ConvertFromString(appSettings.Colors.Outline);
            var backgroundColor = (Color)ColorConverter.ConvertFromString(appSettings.Colors.Background);

            UpdateColors(completedColor, incompleteColor, pendingColor, outlineColor);
            
            // Set background brush - make transparent if requested
            Brush backgroundBrush;
            if (appSettings.Window.AllowTransparentBackground)
            {
                backgroundBrush = Brushes.Transparent;
            }
            else
            {
                backgroundBrush = new SolidColorBrush(backgroundColor)
                {
                    Opacity = appSettings.Colors.BackgroundOpacity
                };
            }
            
            Application.Current.Resources["BackgroundBrush"] = backgroundBrush;
            
            // Set outline brush for day box borders
            var outlineBrush = new SolidColorBrush(outlineColor);
            Application.Current.Resources["OutlineBrush"] = outlineBrush;
        }

        private void SetAsDesktopChild()
        {
            ArrayList windowHandles = new ArrayList();
            Interop.EnumedWindow callback = Interop.EnumWindowCallback;
            Interop.EnumWindows(callback, windowHandles);

            foreach (IntPtr windowHandle in windowHandles)
            {
                IntPtr progmanHandle = Interop.FindWindowEx(windowHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (progmanHandle != IntPtr.Zero)
                {
                    var interopHelper = new WindowInteropHelper(this);
                    interopHelper.EnsureHandle();
                    interopHelper.Owner = progmanHandle;
                    break;
                }
            }            
        }

        public void SetAsToolWindow()
        {
            WindowInteropHelper wih = new WindowInteropHelper(this);
            IntPtr dwNew = new IntPtr(((long)Interop.GetWindowLong(wih.Handle, Interop.GWL_EXSTYLE).ToInt32() | 128L | 0x00200000L) & 4294705151L);
            Interop.SetWindowLong((nint)new HandleRef(this, wih.Handle), Interop.GWL_EXSTYLE, dwNew);
        }

        public void SetNoActivate()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            IntPtr style = Interop.GetWindowLong(hwnd, Interop.GWL_EXSTYLE);
            IntPtr newStyle = new IntPtr(style.ToInt64() | Interop.WS_EX_NOACTIVATE);
            Interop.SetWindowLong(hwnd, Interop.GWL_EXSTYLE, newStyle);
        }

        private void CreateHabitGrid()
        {
            HabitGrid.Children.Clear();
            
            // Leap year calc
            var year = currentYear.Year;
            var isLeapYear = DateTime.IsLeapYear(year);
            int totalDays = isLeapYear ? 366 : 365;
            
            // Calculate grid dimensions (7 rows for days of week, columns for weeks)
            int weeksNeeded = (int)Math.Ceiling(totalDays / 7.0);
            HabitGrid.Columns = weeksNeeded;
            
            // Initialize array to store day boxes
            dayBoxes = new Border[7, weeksNeeded];
            
            // Start date - January 1st of current year
            DateTime startDate = new DateTime(year, 1, 1);

            // Find which day of week January 1st falls on (0 = Sunday)
            var startDayOfWeek = (int)startDate.DayOfWeek;
            
            for (int week = 0; week < weeksNeeded; week++)
            {
                for (int day = 0; day < 7; day++)
                {
                    Border dayBox = new Border
                    {
                        Style = (Style)FindResource("DayBoxStyle"),
                        ToolTip = "Click to toggle completion",
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(2),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0080")),
                        Effect = new DropShadowEffect
                        {
                            Color = (Color)ColorConverter.ConvertFromString("#FF0080"),
                            BlurRadius = 6,
                            ShadowDepth = 0,
                            Opacity = 0.8
                        }
                    };

                    // Calculate the actual date this box represents
                    var dayIndex = week * 7 + day - startDayOfWeek;
                    
                    if (dayIndex >= 0 && dayIndex < totalDays)
                    {
                        DateTime boxDate = startDate.AddDays(dayIndex);
                        dayBox.Tag = "Incomplete";
                        dayBox.ToolTip = $"{boxDate:MMM dd, yyyy}\nClick to toggle";
                        
                        dayBox.MouseLeftButtonDown += DayBox_Click;
                        dayBox.MouseEnter += DayBox_MouseEnter;
                        dayBox.MouseLeave += DayBox_MouseLeave;
                        dayBox.DataContext = boxDate;
                    }
                    else
                    {
                        dayBox.Opacity = 0;
                    }
                    
                    dayBoxes[day, week] = dayBox;
                    HabitGrid.Children.Add(dayBox);
                }
            }
        }

        private void DayBox_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border dayBox && dayBox.DataContext is DateTime)
            {
                // Cycle states: Incomplete -> Pending -> Completed -> Incomplete
                var currentState = dayBox.Tag?.ToString() ?? "Incomplete";
                
                switch (currentState)
                {
                    case "Incomplete":
                        dayBox.Tag = "Pending";
                        dayBox.Background = (Brush)Application.Current.Resources["PendingBrush"];
                        dayBox.BorderBrush = (Brush)Application.Current.Resources["PendingBrush"]; // Cyan border
                        dayBox.Effect = new DropShadowEffect
                        {
                            Color = (Color)ColorConverter.ConvertFromString("#00FFFF"),
                            BlurRadius = 8,
                            ShadowDepth = 0,
                            Opacity = 1.0
                        };
                        break;
                    case "Pending":
                        dayBox.Tag = "Completed";
                        dayBox.Background = (Brush)Application.Current.Resources["CompletedBrush"];
                        dayBox.BorderBrush = (Brush)Application.Current.Resources["CompletedBrush"]; // Green border
                        dayBox.Effect = new DropShadowEffect
                        {
                            Color = (Color)ColorConverter.ConvertFromString("#00FF00"),
                            BlurRadius = 8,
                            ShadowDepth = 0,
                            Opacity = 1.0
                        };
                        break;
                    case "Completed":
                        dayBox.Tag = "Incomplete";
                        dayBox.Background = (Brush)Application.Current.Resources["IncompleteBrush"];
                        dayBox.BorderBrush = (Brush)Application.Current.Resources["OutlineBrush"]; // Pink border
                        dayBox.Effect = new DropShadowEffect
                        {
                            Color = (Color)ColorConverter.ConvertFromString("#FF0080"),
                            BlurRadius = 6,
                            ShadowDepth = 0,
                            Opacity = 0.8
                        };
                        break;
                }

                var boxDate = (DateTime)dayBox.DataContext;
                dayBox.ToolTip = $"{boxDate:MMM dd, yyyy}\nStatus: {dayBox.Tag}\nClick to toggle";
            }
        }

        private void GenerateDemoData()
        {
            // Test data
            if (dayBoxes == null) 
                return;
            
            for (var week = 0; week < dayBoxes.GetLength(1); week++)
            {
                for (var day = 0; day < 7; day++)
                {
                    var dayBox = dayBoxes[day, week];
                    if (dayBox != null && dayBox.DataContext is DateTime boxDate)
                    {
                        if (boxDate <= DateTime.Now.Date)
                        {
                            int randomValue = random.Next(100);
                            if (randomValue < 60)
                            {
                                dayBox.Tag = "Completed";
                                dayBox.Background = (Brush)Application.Current.Resources["CompletedBrush"];
                                dayBox.BorderBrush = (Brush)Application.Current.Resources["CompletedBrush"];
                                dayBox.Effect = new DropShadowEffect
                                {
                                    Color = (Color)ColorConverter.ConvertFromString("#00FF00"),
                                    BlurRadius = 8,
                                    ShadowDepth = 0,
                                    Opacity = 1.0
                                };
                            }
                            else if (randomValue < 80)
                            {
                                dayBox.Tag = "Pending";
                                dayBox.Background = (Brush)Application.Current.Resources["PendingBrush"];
                                dayBox.BorderBrush = (Brush)Application.Current.Resources["PendingBrush"];
                                dayBox.Effect = new DropShadowEffect
                                {
                                    Color = (Color)ColorConverter.ConvertFromString("#00FFFF"),
                                    BlurRadius = 8,
                                    ShadowDepth = 0,
                                    Opacity = 1.0
                                };
                            }

                            dayBox.ToolTip = $"{boxDate:MMM dd, yyyy}\nStatus: {dayBox.Tag}\nClick to toggle";
                        }
                    }
                }
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public void UpdateColors(Color completedColor, Color incompleteColor, Color pendingColor, Color outlineColor)
        {
            var completedBrush = new SolidColorBrush(completedColor);
            var incompleteBrush = incompleteColor == Colors.Transparent ? Brushes.Transparent : new SolidColorBrush(incompleteColor);
            var pendingBrush = new SolidColorBrush(pendingColor);
            var outlineBrush = new SolidColorBrush(outlineColor);

            Application.Current.Resources["CompletedBrush"] = completedBrush;
            Application.Current.Resources["IncompleteBrush"] = incompleteBrush;
            Application.Current.Resources["PendingBrush"] = pendingBrush;
            Application.Current.Resources["OutlineBrush"] = outlineBrush;
            
            // Update all day box borders and backgrounds based on their state
            if (dayBoxes != null)
            {
                for (int day = 0; day < dayBoxes.GetLength(0); day++)
                {
                    for (int week = 0; week < dayBoxes.GetLength(1); week++)
                    {
                        var dayBox = dayBoxes[day, week];
                        if (dayBox != null)
                        {
                            dayBox.BorderThickness = new Thickness(1);
                            
                            // Apply background, border color, and glow effect based on state
                            var state = dayBox.Tag?.ToString() ?? "Incomplete";
                            switch (state)
                            {
                                case "Completed":
                                    dayBox.Background = completedBrush;
                                    dayBox.BorderBrush = completedBrush; // Green border for completed
                                    dayBox.Effect = new DropShadowEffect
                                    {
                                        Color = completedColor,
                                        BlurRadius = 8,
                                        ShadowDepth = 0,
                                        Opacity = 1.0
                                    };
                                    break;
                                case "Pending":
                                    dayBox.Background = pendingBrush;
                                    dayBox.BorderBrush = pendingBrush; // Cyan border for pending
                                    dayBox.Effect = new DropShadowEffect
                                    {
                                        Color = pendingColor,
                                        BlurRadius = 8,
                                        ShadowDepth = 0,
                                        Opacity = 1.0
                                    };
                                    break;
                                case "Incomplete":
                                default:
                                    dayBox.Background = incompleteBrush;
                                    dayBox.BorderBrush = outlineBrush; // Pink border for incomplete
                                    dayBox.Effect = new DropShadowEffect
                                    {
                                        Color = outlineColor,
                                        BlurRadius = 6,
                                        ShadowDepth = 0,
                                        Opacity = 0.8
                                    };
                                    break;
                            }
                        }
                    }
                }
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                if (e.Source == sender)
                {
                    this.DragMove();
                }
            }
        }

        private void DayBox_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border dayBox)
            {
                dayBox.Tag = dayBox.Tag ?? "Incomplete";
                var state = dayBox.Tag.ToString();
                
                // Make the box itself BRIGHT on hover
                Color brightColor;
                Color glowColor;
                switch (state)
                {
                    case "Completed":
                        brightColor = (Color)ColorConverter.ConvertFromString("#00FF00"); // Bright green background
                        glowColor = (Color)ColorConverter.ConvertFromString("#00FF00");
                        break;
                    case "Pending":
                        brightColor = (Color)ColorConverter.ConvertFromString("#00FFFF"); // Bright cyan background
                        glowColor = (Color)ColorConverter.ConvertFromString("#00FFFF");
                        break;
                    case "Incomplete":
                    default:
                        brightColor = (Color)ColorConverter.ConvertFromString("#FFFFFF"); // Pure white background
                        glowColor = (Color)ColorConverter.ConvertFromString("#FFFFFF");
                        break;
                }
                
                // Change the background to be BRIGHT
                dayBox.Background = new SolidColorBrush(brightColor);
                
                // Add a MASSIVE glow effect
                dayBox.Effect = new DropShadowEffect
                {
                    Color = glowColor,
                    BlurRadius = 40, // Even bigger glow
                    ShadowDepth = 0,
                    Opacity = 1.0
                };
                
                // Scale the box slightly larger for extra effect
                dayBox.RenderTransform = new ScaleTransform(1.2, 1.2);
            }
        }

        private void DayBox_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border dayBox)
            {
                // Restore original scale
                dayBox.RenderTransform = null;
                
                // Restore the original background and glow effect based on state
                var state = dayBox.Tag?.ToString() ?? "Incomplete";
                Color glowColor;
                Brush backgroundBrush;
                double blurRadius;
                double opacity;
                
                switch (state)
                {
                    case "Completed":
                        backgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00FF00"));
                        glowColor = (Color)ColorConverter.ConvertFromString("#00FF00");
                        blurRadius = 8;
                        opacity = 1.0;
                        break;
                    case "Pending":
                        backgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00FFFF"));
                        glowColor = (Color)ColorConverter.ConvertFromString("#00FFFF");
                        blurRadius = 8;
                        opacity = 1.0;
                        break;
                    case "Incomplete":
                    default:
                        backgroundBrush = Brushes.Transparent;
                        glowColor = (Color)ColorConverter.ConvertFromString("#FF0080");
                        blurRadius = 6;
                        opacity = 0.8;
                        break;
                }
                
                // Restore normal background
                dayBox.Background = backgroundBrush;
                
                // Restore normal glow
                dayBox.Effect = new DropShadowEffect
                {
                    Color = glowColor,
                    BlurRadius = blurRadius,
                    ShadowDepth = 0,
                    Opacity = opacity
                };
            }
        }

        private void ExitApp(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}