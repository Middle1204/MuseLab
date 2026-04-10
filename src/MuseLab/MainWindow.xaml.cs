using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
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
    public class SongApiResponse
    {
        public string? query { get; set; }
        public FilterInfo? filters { get; set; }
        public List<SongResult>? results { get; set; }
    }

    public class FilterInfo
    {
        public string? course { get; set; }
        public double? levelMin { get; set; }
        public double? levelMax { get; set; }
    }

    public class SongResult
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

    public class SongSearchResult : INotifyPropertyChanged
    {
        public string Title { get; set; } = string.Empty;
        public string Course { get; set; } = string.Empty;
        public double Level { get; set; }
        public string Composer { get; set; } = string.Empty;
        public int Notes { get; set; }
        public string Bpm { get; set; } = string.Empty;

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHighlighted)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class MainWindow : Window
    {
        private DispatcherTimer _trackTimer;
        private static readonly HttpClient httpClient = new HttpClient();

        private Process? cachedProcess = null;
        private DiscordIpcReader? _discordReader;
        private CancellationTokenSource? _discordCts;

        private DispatcherTimer? _searchDebounceTimer;
        private const int SearchDebounceMs = 300;

        private bool isSettingsOpen = false;
        private bool isEditMode = false;
        private bool isSongInfoEnabled = true;

        public MainWindow()
        {
            InitializeComponent();

            _trackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _trackTimer.Tick += TrackGameWindow;
            _trackTimer.Start();

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
            _searchDebounceTimer.Tick += (s, e) => { _searchDebounceTimer.Stop(); _ = PerformSearchAsync(); };

            _discordCts = new CancellationTokenSource();
            _discordReader = new DiscordIpcReader();
            Task.Run(() => _discordReader.Start(_discordCts.Token), _discordCts.Token);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle.ToInt64() | WS_EX_TRANSPARENT | WS_EX_LAYERED));
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            _trackTimer?.Stop();
            _searchDebounceTimer?.Stop();
            cachedProcess?.Dispose();
            _discordCts?.Cancel();
            _discordCts?.Dispose();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.F1) ToggleSettings();
        }

        // ─── 설정 패널 ───────────────────────────────────────────────────

        void ToggleSettings()
        {
            isSettingsOpen = !isSettingsOpen;

            if (isSettingsOpen)
            {
                SettingsPanelWrapper.Visibility = Visibility.Visible;
                ((Storyboard)FindResource("SettingsSlideIn")).Begin();
            }
            else
            {
                var slideOut = (Storyboard)FindResource("SettingsSlideOut");
                slideOut.Completed += (s, e) => SettingsPanelWrapper.Visibility = Visibility.Collapsed;
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
            SetWindowLongPtr(hwnd, -20, new IntPtr(enable ? exStyle | 0x20 : exStyle & ~0x20));
        }

        // ─── SettingsPanel 이벤트 핸들러 ────────────────────────────────

        private void SettingsPanelControl_ExitRequested(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        private void SettingsPanelControl_SearchRequested(object sender, RoutedEventArgs e) =>
            _ = PerformSearchAsync();

        private void SettingsPanelControl_SearchTextChanged(object sender, string text)
        {
            _searchDebounceTimer?.Stop();
            if (!string.IsNullOrEmpty(text)) _searchDebounceTimer?.Start();
        }

        private void SettingsPanelControl_SearchFocused(object sender, string text)
        {
            if (SearchResultsWrapper.Visibility != Visibility.Visible)
            {
                SearchResultsWrapper.Visibility = Visibility.Visible;
                ((Storyboard)FindResource("SearchSlideIn")).Begin();

                if (string.IsNullOrEmpty(text))
                    SearchResultsControl.ShowPrompt();
            }
        }

        private void SettingsPanelControl_SongInfoToggled(object sender, EventArgs e)
        {
            isSongInfoEnabled = !isSongInfoEnabled;
            SongInfoControl.Visibility = isSongInfoEnabled ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanelControl.SetSongInfoEnabled(isSongInfoEnabled);
        }

        private void SettingsPanelControl_DifficultyVisibilityChanged(object sender, bool visible) =>
            SongInfoControl.SetDifficultyVisible(visible);

        private void SettingsPanelControl_LevelVisibilityChanged(object sender, bool visible) =>
            SongInfoControl.SetLevelVisible(visible);

        private void SettingsPanelControl_BorderVisibilityChanged(object sender, bool show) =>
            SongInfoControl.SetOutlineBorder(show);

        private void SettingsPanelControl_EditModeToggled(object sender, RoutedEventArgs e)
        {
            isEditMode = !isEditMode;
            bool showBorder = SettingsPanelControl.IsShowBorderChecked;
            SongInfoControl.SetEditMode(isEditMode, showBorder);
            SettingsPanelControl.SetEditModeActive(isEditMode);
        }

        private void DisableEditMode()
        {
            if (!isEditMode) return;
            isEditMode = false;
            bool showBorder = SettingsPanelControl.IsShowBorderChecked;
            SongInfoControl.SetEditMode(false, showBorder);
            SettingsPanelControl.SetEditModeActive(false);
        }

        // ─── SearchResultsPanel 이벤트 핸들러 ───────────────────────────

        private void SearchResultsControl_CloseRequested(object sender, RoutedEventArgs e) =>
            CloseSearchResultsPanel();

        private void CloseSearchResultsPanel()
        {
            var slideOut = (Storyboard)FindResource("SearchSlideOut");
            slideOut.Completed += (s, e) => SearchResultsWrapper.Visibility = Visibility.Collapsed;
            slideOut.Begin();
        }

        // ─── 검색 ────────────────────────────────────────────────────────

        private async Task PerformSearchAsync()
        {
            string searchQuery = SettingsPanelControl.SearchText;
            string levelMin = SettingsPanelControl.LevelMin;
            string levelMax = SettingsPanelControl.LevelMax;
            string course = SettingsPanelControl.SelectedCourse;

            try
            {
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(levelMin))
                    queryParams.Add($"levelMin={Uri.EscapeDataString(levelMin)}");
                if (!string.IsNullOrEmpty(levelMax))
                    queryParams.Add($"levelMax={Uri.EscapeDataString(levelMax)}");
                if (!string.IsNullOrEmpty(course))
                    queryParams.Add($"course={Uri.EscapeDataString(course)}");

                string queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;

                string apiUrl;
                if (!string.IsNullOrEmpty(searchQuery))
                    apiUrl = $"http://localhost:3000/songs/{Uri.EscapeDataString(searchQuery)}{queryString}";
                else if (queryParams.Count > 0)
                    apiUrl = $"http://localhost:3000/songs{queryString}";
                else
                {
                    if (SearchResultsWrapper.Visibility == Visibility.Visible)
                        SearchResultsControl.ShowPrompt();
                    return;
                }

                Debug.WriteLine($"API Request: {apiUrl}");
                var response = await httpClient.GetAsync(apiUrl);
                Debug.WriteLine($"API Response Status: {response.StatusCode}");

                var results = new ObservableCollection<SongSearchResult>();

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"API Response JSON: {json}");

                    var apiResponse = JsonSerializer.Deserialize<SongApiResponse>(json);

                    if (apiResponse?.results != null)
                    {
                        foreach (var song in apiResponse.results)
                        {
                            results.Add(new SongSearchResult
                            {
                                Title = song.title ?? "Unknown",
                                Course = song.course ?? "Unknown",
                                Level = song.level,
                                Composer = song.composer ?? "Unknown",
                                Notes = song.notes,
                                Bpm = song.bpm ?? "Unknown"
                            });
                        }
                    }
                }

                SearchResultsControl.SetResults(results);

                if (SearchResultsWrapper.Visibility != Visibility.Visible)
                {
                    SearchResultsWrapper.Visibility = Visibility.Visible;
                    ((Storyboard)FindResource("SearchSlideIn")).Begin();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Search error: {ex.Message}");
                SearchResultsControl.SetResults(new ObservableCollection<SongSearchResult>());

                if (SearchResultsWrapper.Visibility != Visibility.Visible)
                {
                    SearchResultsWrapper.Visibility = Visibility.Visible;
                    ((Storyboard)FindResource("SearchSlideIn")).Begin();
                }
            }
        }

        // ─── 게임 창 추적 ────────────────────────────────────────────────

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
                Debug.WriteLine($"TrackGameWindow error: {ex.Message}");
            }
        }

        // ─── Win32 API ───────────────────────────────────────────────────

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : (IntPtr)SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    }

    public static class ScrollViewerBehavior
    {
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset",
                typeof(double),
                typeof(ScrollViewerBehavior),
                new PropertyMetadata(0.0, (d, e) =>
                {
                    if (d is System.Windows.Controls.ScrollViewer sv)
                        sv.ScrollToVerticalOffset((double)e.NewValue);
                }));

        public static double GetVerticalOffset(DependencyObject obj) =>
            (double)obj.GetValue(VerticalOffsetProperty);

        public static void SetVerticalOffset(DependencyObject obj, double value) =>
            obj.SetValue(VerticalOffsetProperty, value);
    }
}
