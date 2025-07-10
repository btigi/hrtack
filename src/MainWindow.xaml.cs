using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HTrack
{
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
                        ToolTip = "Click to toggle completion"
                    };

                    // Calculate the actual date this box represents
                    var dayIndex = week * 7 + day - startDayOfWeek;
                    
                    if (dayIndex >= 0 && dayIndex < totalDays)
                    {
                        DateTime boxDate = startDate.AddDays(dayIndex);
                        dayBox.Tag = "Incomplete";
                        dayBox.ToolTip = $"{boxDate:MMM dd, yyyy}\nClick to toggle";
                        
                        dayBox.MouseLeftButtonDown += DayBox_Click;
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
                        break;
                    case "Pending":
                        dayBox.Tag = "Completed";
                        break;
                    case "Completed":
                        dayBox.Tag = "Incomplete";
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
                                dayBox.Tag = "Completed";
                            else if (randomValue < 80)
                                dayBox.Tag = "Pending";

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
            var incompleteBrush = new SolidColorBrush(incompleteColor);
            var pendingBrush = new SolidColorBrush(pendingColor);
            var outlineBrush = new SolidColorBrush(outlineColor);

            Application.Current.Resources["CompletedBrush"] = completedBrush;
            Application.Current.Resources["IncompleteBrush"] = incompleteBrush;
            Application.Current.Resources["PendingBrush"] = pendingBrush;
            Application.Current.Resources["OutlineBrush"] = outlineBrush;
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

        private void ExitApp(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}