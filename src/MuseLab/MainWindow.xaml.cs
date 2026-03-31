using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Diagnostics;
using System.Windows.Threading;

struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}




namespace MuseLab
{

    public partial class MainWindow : Window
    {

        DispatcherTimer timer;

        void TrackGameWindow(object sender, EventArgs e)
        {
            var processes = Process.GetProcessesByName("MuseDash");

            if (processes.Length == 0)
            {
                return; // 게임 안 켜짐
            }

            var process = processes[0];
            IntPtr hwnd = process.MainWindowHandle;

            if (hwnd == IntPtr.Zero)
                return;

            if (GetWindowRect(hwnd, out RECT rect))
            {
                // 오버레이 위치 맞추기
                this.Left = rect.Left;
                this.Top = rect.Top;

                this.Width = rect.Right - rect.Left;
                this.Height = rect.Bottom - rect.Top;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += TrackGameWindow;
            timer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, -20);

            // WS_EX_TRANSPARENT (클릭 통과) + WS_EX_LAYERED
            SetWindowLong(hwnd, -20, exStyle | 0x80000 | 0x20);
        }

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);


    }
}
