using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private Dictionary<TextBlock, DispatcherTimer> _tooltipTimers = new Dictionary<TextBlock, DispatcherTimer>();
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

        private void LevelBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.')
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private string _selectedCourse = "";
        private void CourseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton)
            {
                CourseAllButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3A3A3A"));
                CourseMasterButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3A3A3A"));
                CourseHiddenButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3A3A3A"));

                clickedButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D9CDB"));

                if (clickedButton == CourseAllButton)
                    _selectedCourse = "";
                else if (clickedButton == CourseMasterButton)
                    _selectedCourse = "master";
                else if (clickedButton == CourseHiddenButton)
                    _selectedCourse = "hidden";
            }
        }

        private void RandomPickButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchResultsList.ItemsSource is ObservableCollection<SongSearchResult> results && results.Count > 0)
            {
                foreach (var result in results)
                {
                    result.IsHighlighted = false;
                }

                var random = new Random();
                int randomIndex = random.Next(results.Count);
                var selectedSong = results[randomIndex];
                selectedSong.IsHighlighted = true;

                Dispatcher.InvokeAsync(() =>
                {
                    SearchResultsList.UpdateLayout();

                    var container = SearchResultsList.ItemContainerGenerator.ContainerFromIndex(randomIndex) as FrameworkElement;

                    if (container != null)
                    {
                        var transform = container.TransformToAncestor(SearchResultsScrollViewer);
                        var position = transform.Transform(new Point(0, 0));

                        double viewportHeight = SearchResultsScrollViewer.ViewportHeight;
                        double containerHeight = container.ActualHeight;
                        double currentOffset = SearchResultsScrollViewer.VerticalOffset;

                        double targetOffset = currentOffset + position.Y - (viewportHeight / 2) + (containerHeight / 2);

                        targetOffset = Math.Max(0, Math.Min(targetOffset, SearchResultsScrollViewer.ScrollableHeight));

                        AnimateScroll(SearchResultsScrollViewer.VerticalOffset, targetOffset);
                    }
                    else
                    {
                        double totalHeight = 0;
                        double avgHeight = 0;
                        int measuredItems = Math.Min(5, SearchResultsList.Items.Count);

                        for (int i = 0; i < measuredItems; i++)
                        {
                            var item = SearchResultsList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                            if (item != null)
                            {
                                totalHeight += item.ActualHeight;
                            }
                        }

                        avgHeight = measuredItems > 0 ? totalHeight / measuredItems : 140;

                        double viewportHeight = SearchResultsScrollViewer.ViewportHeight;
                        double targetOffset = (randomIndex * avgHeight) - (viewportHeight / 2) + (avgHeight / 2);

                        targetOffset = Math.Max(0, Math.Min(targetOffset, SearchResultsScrollViewer.ScrollableHeight));

                        AnimateScroll(SearchResultsScrollViewer.VerticalOffset, targetOffset);
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void SongTitle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                textBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D9CDB"));
            }
        }

        private void SongTitle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is SongSearchResult song)
            {
                try
                {
                    textBlock.Foreground = new SolidColorBrush(Colors.White);

                    Clipboard.SetText(song.Title);

                    if (_tooltipTimers.ContainsKey(textBlock))
                    {
                        _tooltipTimers[textBlock].Stop();
                        _tooltipTimers.Remove(textBlock);
                    }

                    textBlock.SetValue(ToolTipService.IsEnabledProperty, false);
                    textBlock.SetValue(ToolTipService.IsEnabledProperty, true);
                    textBlock.ToolTip = "복사됨!";

                    var tooltipTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };
                    tooltipTimer.Tick += (s, args) =>
                    {
                        textBlock.ToolTip = "클릭하여 복사";
                        if (_tooltipTimers.ContainsKey(textBlock))
                        {
                            _tooltipTimers.Remove(textBlock);
                        }
                        tooltipTimer.Stop();
                    };
                    _tooltipTimers[textBlock] = tooltipTimer;
                    tooltipTimer.Start();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Clipboard error: {ex.Message}");
                }
            }
        }

        private void SongTitle_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                textBlock.Foreground = new SolidColorBrush(Colors.White);

                if (_tooltipTimers.ContainsKey(textBlock))
                {
                    _tooltipTimers[textBlock].Stop();
                    _tooltipTimers.Remove(textBlock);
                    textBlock.ToolTip = "클릭하여 복사";
                }
            }
        }

        private void AnimateScroll(double fromValue, double toValue)
        {
            var animation = new DoubleAnimation
            {
                From = fromValue,
                To = toValue,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            Storyboard.SetTarget(animation, SearchResultsScrollViewer);
            Storyboard.SetTargetProperty(animation, new PropertyPath(ScrollViewerBehavior.VerticalOffsetProperty));
            storyboard.Begin();
        }

        private static T? FindVisualChild<T>(DependencyObject parent, int index) where T : DependencyObject
        {
            if (parent is ItemsControl itemsControl && index < itemsControl.Items.Count)
            {
                var item = itemsControl.Items[index];
                var container = itemsControl.ItemContainerGenerator.ContainerFromItem(item);

                if (container is ContentPresenter presenter)
                {
                    return presenter as T;
                }
            }
            return null;
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
            string levelMin = LevelMinBox?.Text?.Trim() ?? string.Empty;
            string levelMax = LevelMaxBox?.Text?.Trim() ?? string.Empty;
            string course = _selectedCourse;

            SearchPromptText.Visibility = Visibility.Collapsed;

            try
            {
                string apiUrl;
                var queryParams = new List<string>();

                if (!string.IsNullOrEmpty(levelMin))
                    queryParams.Add($"levelMin={Uri.EscapeDataString(levelMin)}");
                if (!string.IsNullOrEmpty(levelMax))
                    queryParams.Add($"levelMax={Uri.EscapeDataString(levelMax)}");
                if (!string.IsNullOrEmpty(course))
                    queryParams.Add($"course={Uri.EscapeDataString(course)}");

                string queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;

                if (!string.IsNullOrEmpty(searchQuery))
                {
                    apiUrl = $"http://localhost:3000/songs/{Uri.EscapeDataString(searchQuery)}{queryString}";
                }
                else if (queryParams.Count > 0)
                {
                    apiUrl = $"http://localhost:3000/songs{queryString}";
                }
                else
                {
                    if (SearchResultsPanel.Visibility == Visibility.Visible)
                    {
                        SearchPromptText.Visibility = Visibility.Visible;
                        NoResultsText.Visibility = Visibility.Collapsed;
                        SearchResultsList.ItemsSource = null;
                    }
                    return;
                }

                var response = await httpClient.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"API Response JSON: {json}");

                    var apiResponse = System.Text.Json.JsonSerializer.Deserialize<SongApiResponse>(json);

                    var results = new ObservableCollection<SongSearchResult>();

                    if (apiResponse?.results != null && apiResponse.results.Count > 0)
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

    public static class ScrollViewerBehavior
    {
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset",
                typeof(double),
                typeof(ScrollViewerBehavior),
                new PropertyMetadata(0.0, OnVerticalOffsetChanged));

        public static double GetVerticalOffset(DependencyObject obj)
        {
            return (double)obj.GetValue(VerticalOffsetProperty);
        }

        public static void SetVerticalOffset(DependencyObject obj, double value)
        {
            obj.SetValue(VerticalOffsetProperty, value);
        }

        private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
            }
        }
    }
}
