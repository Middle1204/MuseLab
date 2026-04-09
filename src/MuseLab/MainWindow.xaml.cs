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
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Animation;

readonly struct RECT
{
    public readonly int Left;
    public readonly int Top;
    public readonly int Right;
    public readonly int Bottom;
}


namespace MuseLab
{
    public class SongApiResponse
    {
        public string? query { get; set; }
        public List<BestMatch>? bestMatch { get; set; }
        public List<BestMatch>? top5 { get; set; }
    }

    public class BestMatch
    {
        public string? title { get; set; }
        public string? course { get; set; }
        public double level { get; set; }
        public string? composer { get; set; }
        public int notes { get; set; }
        public string? bpm { get; set; }
        public string? _searchTitle { get; set; }
        public double score { get; set; }
    }

    public class SongSearchResult
    {
        public string Title { get; set; } = string.Empty;
        public string Course { get; set; } = string.Empty;
        public double Level { get; set; }
        public string Composer { get; set; } = string.Empty;
        public int Notes { get; set; }
        public string Bpm { get; set; } = string.Empty;
    }

    public partial class MainWindow : Window
    {

        DispatcherTimer timer;
        private static readonly HttpClient httpClient = new HttpClient();

        Process? cachedProcess = null;
        private DiscordIpcReader? _discordReader;
        private CancellationTokenSource? _discordCts;

        private DispatcherTimer? _searchDebounceTimer;
        private const int SearchDebounceMs = 300;

        bool isSettingsOpen = false;
        bool isEditMode = false;
        private Point? _dragStartPoint = null;
        private Thickness _originalMargin;
        private bool isSongInfoEnabled = true;
        void ToggleSettings()
        {
            isSettingsOpen = !isSettingsOpen;

            if (isSettingsOpen)
            {
                SettingsPanel.Visibility = Visibility.Visible;
                var slideIn = (Storyboard)FindResource("SettingsSlideIn");
                slideIn.Begin();
            }
            else
            {
                var slideOut = (Storyboard)FindResource("SettingsSlideOut");
                slideOut.Completed += (s, e) =>
                {
                    SettingsPanel.Visibility = Visibility.Collapsed;
                };
                slideOut.Begin();

                CloseSearchResultsPanel();
                DisableEditMode();
            }

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

            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(SearchDebounceMs);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

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
            _searchDebounceTimer?.Stop();
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

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchResultsPanel.Visibility != Visibility.Visible)
            {
                SearchResultsPanel.Visibility = Visibility.Visible;
                var slideIn = (Storyboard)FindResource("SearchSlideIn");
                slideIn.Begin();

                string searchQuery = SearchBox?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(searchQuery))
                {
                    SearchPromptText.Visibility = Visibility.Visible;
                    NoResultsText.Visibility = Visibility.Collapsed;
                    SearchResultsList.ItemsSource = null;
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer?.Start();
        }

        private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _searchDebounceTimer?.Stop();
            _ = PerformSearchAsync();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = PerformSearchAsync();
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            _ = PerformSearchAsync();
        }

        private void CloseSearchButton_Click(object sender, RoutedEventArgs e)
        {
            CloseSearchResultsPanel();
        }

        private void CloseSearchResultsPanel()
        {
            var slideOut = (Storyboard)FindResource("SearchSlideOut");
            slideOut.Completed += (s, e) =>
            {
                SearchResultsPanel.Visibility = Visibility.Collapsed;
            };
            slideOut.Begin();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ToggleEditModeButton_Click(object sender, RoutedEventArgs e)
        {
            isEditMode = !isEditMode;

            if (isEditMode)
            {
                ToggleEditModeButton.Content = "편집 완료";
                ToggleEditModeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745"));
                SongInfoBorder.BorderBrush = new SolidColorBrush(Colors.Yellow);
                SongInfoBorder.BorderThickness = new Thickness(2);
                SongInfoBorder.Cursor = Cursors.Hand;
            }
            else
            {
                ToggleEditModeButton.Content = "오버레이 편집";
                ToggleEditModeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6C757D"));

                if (ShowBorderCheck?.IsChecked == true)
                {
                    SongInfoBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D9CDB"));
                    SongInfoBorder.BorderThickness = new Thickness(2);
                }
                else
                {
                    SongInfoBorder.BorderBrush = null;
                    SongInfoBorder.BorderThickness = new Thickness(0);
                }

                SongInfoBorder.Cursor = Cursors.Arrow;
            }
        }

        private void DisableEditMode()
        {
            if (isEditMode)
            {
                isEditMode = false;
                ToggleEditModeButton.Content = "오버레이 편집";
                ToggleEditModeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6C757D"));

                if (ShowBorderCheck?.IsChecked == true)
                {
                    SongInfoBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D9CDB"));
                    SongInfoBorder.BorderThickness = new Thickness(2);
                }
                else
                {
                    SongInfoBorder.BorderBrush = null;
                    SongInfoBorder.BorderThickness = new Thickness(0);
                }

                SongInfoBorder.Cursor = Cursors.Arrow;
            }
        }

        private void SongInfoToggle_Click(object sender, RoutedEventArgs e)
        {
            isSongInfoEnabled = !isSongInfoEnabled;
            UpdateSongInfoStatus();
        }

        private void SongInfoToggle_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (SongInfoDetailPanel.Visibility == Visibility.Visible)
            {
                SongInfoDetailPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                SongInfoDetailPanel.Visibility = Visibility.Visible;
            }
            e.Handled = true;
        }

        private void UpdateSongInfoStatus()
        {
            if (isSongInfoEnabled)
            {
                SongInfoBorder.Visibility = Visibility.Visible;
                SongInfoToggleText.FontWeight = FontWeights.Bold;
            }
            else
            {
                SongInfoBorder.Visibility = Visibility.Collapsed;
                SongInfoToggleText.FontWeight = FontWeights.Normal;
            }
        }

        private void ShowBorderCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (ShowBorderCheck.IsChecked == true)
            {
                SongInfoBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D9CDB"));
                SongInfoBorder.BorderThickness = new Thickness(2);
            }
            else
            {
                if (!isEditMode)
                {
                    SongInfoBorder.BorderBrush = null;
                    SongInfoBorder.BorderThickness = new Thickness(0);
                }
            }
        }

        private void ShowDifficultyCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (DifficultyBorder != null)
            {
                DifficultyBorder.Visibility = ShowDifficultyCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ShowLevelCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (LevelText != null)
            {
                LevelText.Visibility = ShowLevelCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SongInfoBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode) return;

            _dragStartPoint = e.GetPosition(this);
            _originalMargin = SongInfoBorder.Margin;
            SongInfoBorder.CaptureMouse();
            e.Handled = true;
        }

        private void SongInfoBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isEditMode) return;

            _dragStartPoint = null;
            SongInfoBorder.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void SongInfoBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isEditMode || _dragStartPoint == null || !SongInfoBorder.IsMouseCaptured) return;

            Point currentPosition = e.GetPosition(this);
            double deltaX = currentPosition.X - _dragStartPoint.Value.X;
            double deltaY = currentPosition.Y - _dragStartPoint.Value.Y;

            double newRight = _originalMargin.Right - deltaX;
            double newBottom = _originalMargin.Bottom - deltaY;

            newRight = Math.Max(10, Math.Min(newRight, this.ActualWidth - SongInfoBorder.ActualWidth - 10));
            newBottom = Math.Max(10, Math.Min(newBottom, this.ActualHeight - SongInfoBorder.ActualHeight - 10));

            SongInfoBorder.Margin = new Thickness(0, 0, newRight, newBottom);
            e.Handled = true;
        }

        private async Task PerformSearchAsync()
        {
            string searchQuery = SearchBox?.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(searchQuery))
            {
                if (SearchResultsPanel.Visibility == Visibility.Visible)
                {
                    SearchPromptText.Visibility = Visibility.Visible;
                    NoResultsText.Visibility = Visibility.Collapsed;
                    SearchResultsList.ItemsSource = null;
                }
                return;
            }

            SearchPromptText.Visibility = Visibility.Collapsed;

            try
            {
                string apiUrl = $"http://localhost:3000/songs/{Uri.EscapeDataString(searchQuery)}";
                var response = await httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var apiResponse = System.Text.Json.JsonSerializer.Deserialize<SongApiResponse>(json);

                    var results = new ObservableCollection<SongSearchResult>();

                    if (apiResponse?.bestMatch != null && apiResponse.bestMatch.Count > 0)
                    {
                        foreach (var match in apiResponse.bestMatch)
                        {
                            results.Add(new SongSearchResult
                            {
                                Title = match.title ?? "Unknown",
                                Course = match.course ?? "Unknown",
                                Level = match.level,
                                Composer = match.composer ?? "Unknown",
                                Notes = match.notes,
                                Bpm = match.bpm ?? "Unknown"
                            });
                        }
                    }

                    SearchResultsList.ItemsSource = results;

                    if (results.Count == 0)
                    {
                        NoResultsText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        NoResultsText.Visibility = Visibility.Collapsed;
                    }

                    if (SearchResultsPanel.Visibility != Visibility.Visible)
                    {
                        SearchResultsPanel.Visibility = Visibility.Visible;
                        var slideIn = (Storyboard)FindResource("SearchSlideIn");
                        slideIn.Begin();
                    }
                }
                else
                {
                    NoResultsText.Visibility = Visibility.Visible;
                    SearchResultsList.ItemsSource = new ObservableCollection<SongSearchResult>();

                    if (SearchResultsPanel.Visibility != Visibility.Visible)
                    {
                        SearchResultsPanel.Visibility = Visibility.Visible;
                        var slideIn = (Storyboard)FindResource("SearchSlideIn");
                        slideIn.Begin();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Search error: {ex.Message}");
                NoResultsText.Visibility = Visibility.Visible;
                SearchResultsList.ItemsSource = new ObservableCollection<SongSearchResult>();

                if (SearchResultsPanel.Visibility != Visibility.Visible)
                {
                    SearchResultsPanel.Visibility = Visibility.Visible;
                    var slideIn = (Storyboard)FindResource("SearchSlideIn");
                    slideIn.Begin();
                }
            }
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
