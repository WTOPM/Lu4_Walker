using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Interop;
using System.Threading;
using System.Windows.Forms;

namespace LU4_Walker
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        private const uint WM_KEYDOWN = 0x0100;
        private const int VK_NUM_DIV = 0x6F;
        private const int HOTKEY_ID_SCREENSHOT = 0x9000;
        private const int HOTKEY_ID_START = 0x9001;
        private const int HOTKEY_ID_STOP = 0x9002;
        private const int HOTKEY_ID_CHECK_PIXEL = 0x9003;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_F12 = 0x7B;
        private const uint VK_PAGE_UP = 0x21;
        private const uint VK_PAGE_DOWN = 0x22;
        private const uint VK_END = 0x23;
        private const int WM_HOTKEY = 0x0312;
        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X, Y;
        }

        private IntPtr BSFGhWnd = IntPtr.Zero;
        private DispatcherTimer findMonster;
        private Dictionary<string, IntPtr> lu4Windows;
        private HwndSource hwndSource;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTimer();
            LoadLU4Processes();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Topmost = true;
        }

        private void InitializeTimer()
        {
            findMonster = new DispatcherTimer();
            findMonster.Tick += new EventHandler(findMonster_Tick);
            findMonster.Interval = new TimeSpan(0, 0, 0, 0, 200);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                hwndSource = PresentationSource.FromVisual(this) as HwndSource;
                if (hwndSource != null)
                {
                    hwndSource.AddHook(WndProc);
                    RegisterHotKey(hwndSource.Handle, HOTKEY_ID_SCREENSHOT, MOD_CONTROL, VK_F12);
                    RegisterHotKey(hwndSource.Handle, HOTKEY_ID_START, 0, VK_PAGE_UP);
                    RegisterHotKey(hwndSource.Handle, HOTKEY_ID_STOP, 0, VK_PAGE_DOWN);
                    RegisterHotKey(hwndSource.Handle, HOTKEY_ID_CHECK_PIXEL, 0, VK_END);
                }
            }
            catch { }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (hwndSource != null)
                {
                    UnregisterHotKey(hwndSource.Handle, HOTKEY_ID_SCREENSHOT);
                    UnregisterHotKey(hwndSource.Handle, HOTKEY_ID_START);
                    UnregisterHotKey(hwndSource.Handle, HOTKEY_ID_STOP);
                    UnregisterHotKey(hwndSource.Handle, HOTKEY_ID_CHECK_PIXEL);
                    hwndSource.RemoveHook(WndProc);
                    hwndSource = null;
                }
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (ProcessComboBox.SelectedItem != null)
                {
                    string selectedWindow = ProcessComboBox.SelectedItem.ToString();
                    IntPtr hWnd = lu4Windows[selectedWindow];
                    SetForegroundWindow(hWnd);
                }

                if (hotkeyId == HOTKEY_ID_SCREENSHOT)
                {
                    TakeScreenshot();
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_START)
                {
                    StartButton_Click(null, null);
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_STOP)
                {
                    StopButton_Click(null, null);
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_CHECK_PIXEL)
                {
                    CheckPixelColorButton_Click(null, null);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void LoadLU4Processes()
        {
            lu4Windows = new Dictionary<string, IntPtr>();
            ProcessComboBox.Items.Clear();

            foreach (Process proc in Process.GetProcesses())
            {
                if (proc.MainWindowTitle.Contains("LU4") && proc.MainWindowHandle != IntPtr.Zero && proc.MainWindowTitle != "LU4 Walker")
                {
                    string displayName = $"{proc.MainWindowTitle} (PID: {proc.Id})";
                    lu4Windows[displayName] = proc.MainWindowHandle;
                    ProcessComboBox.Items.Add(displayName);
                }
            }

            if (lu4Windows.Count > 0)
            {
                ProcessComboBox.SelectedIndex = 0;
                StartButton.IsEnabled = true;
                ScreenshotButton.IsEnabled = true;
                CheckPixelColorButton.IsEnabled = true;
            }
            else
            {
                StartButton.IsEnabled = false;
                ScreenshotButton.IsEnabled = false;
                CheckPixelColorButton.IsEnabled = false;
                PixelColorTextBox.Text = "";
            }
        }

        private void findMonster_Tick(object sender, EventArgs e)
        {
            if (BSFGhWnd != IntPtr.Zero)
            {
                PostMessage(BSFGhWnd, WM_KEYDOWN, VK_NUM_DIV, 0);
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessComboBox.SelectedItem != null)
            {
                string selectedWindow = ProcessComboBox.SelectedItem.ToString();
                BSFGhWnd = lu4Windows[selectedWindow];
                findMonster.Start();
                StartButton.Visibility = Visibility.Collapsed;
                StopButton.Visibility = Visibility.Visible;
                Topmost = false;
                WindowState = WindowState.Minimized;
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            findMonster.Stop();
            StartButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
            Topmost = true;
            WindowState = WindowState.Normal;
            PixelColorTextBox.Text = "";
        }

        private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (findMonster.IsEnabled)
            {
                findMonster.Stop();
                StartButton.Visibility = Visibility.Visible;
                StopButton.Visibility = Visibility.Collapsed;
                Topmost = true;
                WindowState = WindowState.Normal;
                PixelColorTextBox.Text = "";
            }
        }

        private void ProcessComboBox_DropDownOpened(object sender, EventArgs e)
        {
            LoadLU4Processes();
        }

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessComboBox.SelectedItem != null)
            {
                string selectedWindow = ProcessComboBox.SelectedItem.ToString();
                IntPtr hWnd = lu4Windows[selectedWindow];
                SetForegroundWindow(hWnd);
            }
            TakeScreenshot();
        }

        private void CheckPixelColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessComboBox.SelectedItem == null)
            {
                PixelColorTextBox.Text = "";
                return;
            }

            string selectedWindow = ProcessComboBox.SelectedItem.ToString();
            IntPtr hWnd = lu4Windows[selectedWindow];

            if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);

            if (!GetClientRect(hWnd, out RECT clientRect))
            {
                PixelColorTextBox.Text = "";
                return;
            }

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;

            if (width <= 0 || height <= 0)
            {
                PixelColorTextBox.Text = "";
                return;
            }

            POINT topLeft = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(hWnd, ref topLeft))
            {
                PixelColorTextBox.Text = "";
                return;
            }

            try
            {
                using (Bitmap bitmap = new Bitmap(width, height))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CopyFromScreen(topLeft.X, topLeft.Y, 0, 0, new System.Drawing.Size(width, height));
                    }

                    int searchHeight = Math.Min(100, height);
                    bool found = false;

                    for (int yy = 0; yy < searchHeight; yy++)
                    {
                        for (int xx = 0; xx < width; xx++)
                        {
                            Color pixel = bitmap.GetPixel(xx, yy);
                            if (pixel.R > 200 && pixel.G < 80 && pixel.B < 80)
                            {
                                SetCursorPos(topLeft.X + xx, topLeft.Y + yy);
                                PixelColorTextBox.Text = $"HP моба найден: R={pixel.R}, G={pixel.G}, B={pixel.B} @ ({xx},{yy})";
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }

                    if (!found)
                    {
                        PixelColorTextBox.Text = "Моб не выделен (полоска HP не найдена)";
                    }
                }
            }
            catch
            {
                PixelColorTextBox.Text = "Ошибка при поиске HP.";
            }
        }

        private void TakeScreenshot()
        {
            if (ProcessComboBox.SelectedItem == null) return;

            string selectedWindow = ProcessComboBox.SelectedItem.ToString();
            IntPtr hWnd = lu4Windows[selectedWindow];

            if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
            Thread.Sleep(50);

            if (!GetClientRect(hWnd, out RECT clientRect)) return;

            POINT topLeft = new POINT { X = clientRect.Left, Y = clientRect.Top };
            if (!ClientToScreen(hWnd, ref topLeft)) return;

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;

            if (width <= 0 || height <= 0) return;

            try
            {
                using (Bitmap bitmap = new Bitmap(width, height))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        float dpiScaleX = graphics.DpiX / 96.0f;
                        float dpiScaleY = graphics.DpiY / 96.0f;
                        int scaledX = (int)(topLeft.X / dpiScaleX);
                        int scaledY = (int)(topLeft.Y / dpiScaleY);
                        int scaledWidth = (int)(width / dpiScaleX);
                        int scaledHeight = (int)(height / dpiScaleY);

                        graphics.CopyFromScreen(scaledX, scaledY, 0, 0, new System.Drawing.Size(scaledWidth, scaledHeight));
                    }

                    string exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    string datePrefix = DateTime.Now.ToString("dd.MM.yyyy");
                    int nextNumber = 1;

                    string[] existingFiles = Directory.GetFiles(exePath, $"{datePrefix} - *.bmp");
                    if (existingFiles.Length > 0)
                    {
                        nextNumber = existingFiles
                            .Select(f => Path.GetFileNameWithoutExtension(f).Split('-').Last().Trim())
                            .Select(n => int.TryParse(n, out int num) ? num : 0)
                            .DefaultIfEmpty(0)
                            .Max() + 1;
                    }

                    string fileName = $"{datePrefix} - {nextNumber:D3}.bmp";
                    string filePath = Path.Combine(exePath, fileName);
                    bitmap.Save(filePath, ImageFormat.Bmp);
                }
            }
            catch { }
        }
    }
}
