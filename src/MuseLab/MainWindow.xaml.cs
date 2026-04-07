using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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

readonly struct RECT
{
    public readonly int Left;
    public readonly int Top;
    public readonly int Right;
    public readonly int Bottom;
}


namespace MuseLab
{
    public class SongSearchResult
    {
        public string SongTitle { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
    }

    public partial class MainWindow : Window
    {

        DispatcherTimer timer;

        Process? cachedProcess = null;
        private DiscordIpcReader? _discordReader;
        private CancellationTokenSource? _discordCts;

        bool isSettingsOpen = false;
        void ToggleSettings()
        {
            isSettingsOpen = !isSettingsOpen;

            SettingsPanel.Visibility = isSettingsOpen
                ? Visibility.Visible
                : Visibility.Collapsed;

            SetClickThrough(!isSettingsOpen);
        }

        void SetClickThrough(bool enable)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLongPtr(hwnd, -20).ToInt32();

            if (enable)
                SetWindowLongPtr(hwnd, -20, new IntPtr(exStyle | 0x20)); // 클릭 통과
            else
                SetWindowLongPtr(hwnd, -20, new IntPtr(exStyle & ~0x20)); // 클릭 가능
        }

        void TrackGameWindow(object sender, EventArgs e)
        {
            try
            {
                if (cachedProcess == null || cachedProcess.HasExited)
                {
                    var processes = Process.GetProcessesByName("MuseDash");
                    if (processes.Length == 0) return;

                    cachedProcess?.Dispose();
                    cachedProcess = processes[0];
                }

                IntPtr hwnd = cachedProcess.MainWindowHandle;
                if (hwnd == IntPtr.Zero) return;

                RECT rect;
                int result = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(RECT)));
                if (result != 0) return;

                var source = PresentationSource.FromVisual(this);
                if (source == null) return;
                
                double dpiX = source.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
                double dpiY = source.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

                this.Left = rect.Left * dpiX;
                this.Top = rect.Top * dpiY;
                this.Width = (rect.Right - rect.Left) * dpiX;
                this.Height = (rect.Bottom - rect.Top) * dpiY;
            }
            catch (Exception ex)
            {
                // 로깅 또는 에러 처리
                Debug.WriteLine($"TrackGameWindow error: {ex.Message}");
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += TrackGameWindow;
            timer.Start();

            _discordCts = new CancellationTokenSource();
            _discordReader = new DiscordIpcReader();
            Task.Run(() => _discordReader.Start(_discordCts.Token), _discordCts.Token);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);

            SetWindowLongPtr(hwnd, GWL_EXSTYLE, 
                new IntPtr(exStyle.ToInt64() | WS_EX_TRANSPARENT | WS_EX_LAYERED));
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            
            timer?.Stop();
            cachedProcess?.Dispose();
            _discordCts?.Cancel();
            _discordCts?.Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.F1)
            {
                ToggleSettings();
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformSearch();
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            PerformSearch();
        }

        private void PerformSearch()
        {
            string searchQuery = SearchBox?.Text?.Trim() ?? string.Empty;
            
            if (string.IsNullOrEmpty(searchQuery))
            {
                SearchResultsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // 검색 기능 연동 필요 (api)
            var results = new ObservableCollection<SongSearchResult>();
            
            SearchResultsList.ItemsSource = results;
            
            if (results.Count == 0)
            {
                NoResultsText.Visibility = Visibility.Visible;
            }
            else
            {
                NoResultsText.Visibility = Visibility.Collapsed;
            }
            
            SearchResultsPanel.Visibility = Visibility.Visible;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : (IntPtr)SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
        }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        out RECT pvAttribute,
        int cbAttribute);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;


    }
}
