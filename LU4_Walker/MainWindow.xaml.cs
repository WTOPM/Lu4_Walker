using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Forms;
//using System.Windows.Media;

namespace LU4_Walker
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        private const int SW_RESTORE = 9;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
        private const uint MOUSEEVENTF_LEFTUP = 0x04;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        private Dictionary<string, IntPtr> lu4Windows = new();
        private CancellationTokenSource huntToken;
        private readonly Random rng = new();
        private IntPtr BSFGhWnd = IntPtr.Zero;
        private DateTime lastKeepAlive = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();
            LoadLU4Processes();
        }

        private void LoadLU4Processes()
        {
            lu4Windows.Clear();
            ProcessComboBox.Items.Clear();

            foreach (var proc in Process.GetProcesses())
            {
                if (proc.MainWindowTitle.Contains("LU4") && proc.MainWindowHandle != IntPtr.Zero)
                {
                    string name = $"{proc.MainWindowTitle} (PID: {proc.Id})";
                    lu4Windows[name] = proc.MainWindowHandle;
                    ProcessComboBox.Items.Add(name);
                }
            }

            if (lu4Windows.Count > 0)
                ProcessComboBox.SelectedIndex = 0;
        }

        private void SpamKey(byte vkCode, int count = 1, int minDelay = 20, int maxDelay = 60)
        {
            for (int i = 0; i < count; i++)
            {
                keybd_event(vkCode, 0, 0x0000, UIntPtr.Zero);
                Thread.Sleep(rng.Next(minDelay, maxDelay));
                keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(rng.Next(minDelay, maxDelay));
            }
        }

        private bool IsMonsterTargeted(IntPtr hWnd)
        {
            if (!GetClientRect(hWnd, out RECT rect)) return false;
            int width = rect.Right - rect.Left;
            if (width <= 0) return false;

            POINT topLeft = new() { X = 0, Y = 0 };
            if (!ClientToScreen(hWnd, ref topLeft)) return false;

            using Bitmap bmp = new(width, 5);
            using Graphics g = Graphics.FromImage(bmp);
            g.CopyFromScreen(topLeft.X, topLeft.Y, 0, 0, new System.Drawing.Size(width, 5));

            for (int x = 0; x < width; x++)
            {
                Color pixel = bmp.GetPixel(x, 0);
                if (pixel.R > 200 && pixel.G < 80 && pixel.B < 80)
                    return true;
            }
            return false;
        }

        private async Task StartHuntingLoop()
        {
            if (ProcessComboBox.SelectedItem == null) return;
            string selectedWindow = ProcessComboBox.SelectedItem.ToString();
            IntPtr hWnd = lu4Windows[selectedWindow];
            BSFGhWnd = hWnd;

            if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);

            huntToken = new CancellationTokenSource();
            CancellationToken token = huntToken.Token;

            while (!token.IsCancellationRequested)
            {
                // 1️⃣ Спам WASD по кругу для имитации активности
                SpamKey(0x57); // W
                await Task.Delay(50, token);
                SpamKey(0x41); // A
                await Task.Delay(50, token);
                SpamKey(0x53); // S
                await Task.Delay(50, token);
                SpamKey(0x44); // D
                await Task.Delay(50, token);

                SetForegroundWindow(hWnd);

                // 2️⃣ Захват цели
                for (int i = 0; i < 3; i++)
                {
                    SpamKey(0x36); // key 6
                    await Task.Delay(150, token);
                }

                await Task.Delay(400, token);

                // 3️⃣ Проверка захвата
                bool found = false;
                for (int i = 0; i < 10; i++)
                {
                    if (IsMonsterTargeted(hWnd)) { found = true; break; }
                    await Task.Delay(100, token);
                }

                if (!found) continue;

                // 4️⃣ Убийство
                while (IsMonsterTargeted(hWnd))
                {
                    SpamKey(0x31, 3); // key 1
                    await Task.Delay(250, token);
                }

                // 5️⃣ Loot / Buff
                for (int i = 0; i < 5; i++)
                {
                    SpamKey(0x37); // key 7
                    await Task.Delay(300, token);
                }

                await Task.Delay(500, token);
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            _ = StartHuntingLoop();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            huntToken?.Cancel();
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (huntToken != null && !huntToken.IsCancellationRequested)
                StopButton_Click(null, null);
        }

        protected override void OnClosed(EventArgs e)
        {
            huntToken?.Cancel();
            base.OnClosed(e);
        }
    }
}
