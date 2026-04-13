using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MuseLab.Controls
{
    public partial class SettingsPanel : UserControl
    {
        // 외부로 전달할 이벤트들
        public event RoutedEventHandler? ExitRequested;
        public event RoutedEventHandler? SearchRequested;
        public event EventHandler<string>? SearchTextChanged;
        public event EventHandler<string>? SearchFocused;
        public event EventHandler? SongInfoToggled;
        public event EventHandler<bool>? DifficultyVisibilityChanged;
        public event EventHandler<bool>? LevelVisibilityChanged;
        public event EventHandler<bool>? BorderVisibilityChanged;
        public event RoutedEventHandler? EditModeToggled;
        public event EventHandler<string>? CourseFilterChanged;

        private string _selectedCourse = "";
        private bool _isFilterOpen = false;
        private double _filterContentHeight = 0;

        public string SearchText => SearchBox?.Text?.Trim() ?? string.Empty;
        public string LevelMin => LevelMinBox?.Text?.Trim() ?? string.Empty;
        public string LevelMax => LevelMaxBox?.Text?.Trim() ?? string.Empty;
        public string SelectedCourse => _selectedCourse;
        public bool IsShowDifficultyChecked => ShowDifficultyCheck?.IsChecked == true;
        public bool IsShowLevelChecked => ShowLevelCheck?.IsChecked == true;
        public bool IsShowBorderChecked => ShowBorderCheck?.IsChecked == true;

        public SettingsPanel()
        {
            InitializeComponent();
            Loaded += (s, e) => MeasureFilterContent();
        }

        private void MeasureFilterContent()
        {
            FilterContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _filterContentHeight = FilterContent.DesiredSize.Height;
        }

        private void FilterToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isFilterOpen) CloseFilter();
            else OpenFilter();
        }

        private void OpenFilter()
        {
            if (_filterContentHeight == 0)
                MeasureFilterContent();
            if (_isFilterOpen) return;

            _isFilterOpen = true;
            FilterToggleArrow.Text = "▼";

            FilterPanel.BeginAnimation(HeightProperty, new DoubleAnimation
            {
                From = 0,
                To = _filterContentHeight,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            FilterTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                From = _filterContentHeight,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void CloseFilter()
        {
            if (!_isFilterOpen) return;

            _isFilterOpen = false;
            FilterToggleArrow.Text = "▲";

            FilterPanel.BeginAnimation(HeightProperty, new DoubleAnimation
            {
                From = _filterContentHeight,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
            FilterTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                From = 0,
                To = _filterContentHeight,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
        }

        public void SetEditModeActive(bool active)
        {
            if (active)
            {
                ToggleEditModeButton.Content = "편집 완료";
                ToggleEditModeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF28A745"));
            }
            else
            {
                ToggleEditModeButton.Content = "오버레이 편집";
                ToggleEditModeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6C757D"));
            }
        }

        public void SetSongInfoEnabled(bool enabled)
        {
            SongInfoToggleText.FontWeight = enabled ? FontWeights.Bold : FontWeights.Normal;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e) =>
            ExitRequested?.Invoke(this, e);

        private void SearchButton_Click(object sender, RoutedEventArgs e) =>
            SearchRequested?.Invoke(this, e);

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            OpenFilter();
            SearchFocused?.Invoke(this, SearchBox.Text);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
            SearchTextChanged?.Invoke(this, SearchBox.Text);

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SearchRequested?.Invoke(this, new RoutedEventArgs());
        }

        private void SongInfoToggle_Click(object sender, RoutedEventArgs e) =>
            SongInfoToggled?.Invoke(this, EventArgs.Empty);

        private void SongInfoToggle_RightClick(object sender, MouseButtonEventArgs e)
        {
            SongInfoDetailPanel.Visibility = SongInfoDetailPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            e.Handled = true;
        }

        private void ShowDifficultyCheck_Changed(object sender, RoutedEventArgs e) =>
            DifficultyVisibilityChanged?.Invoke(this, ShowDifficultyCheck.IsChecked == true);

        private void ShowLevelCheck_Changed(object sender, RoutedEventArgs e) =>
            LevelVisibilityChanged?.Invoke(this, ShowLevelCheck.IsChecked == true);

        private void ShowBorderCheck_Changed(object sender, RoutedEventArgs e) =>
            BorderVisibilityChanged?.Invoke(this, ShowBorderCheck.IsChecked == true);

        private void ToggleEditModeButton_Click(object sender, RoutedEventArgs e) =>
            EditModeToggled?.Invoke(this, e);

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

        private void CourseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton)
            {
                var inactive = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"];
                var active   = (SolidColorBrush)Application.Current.Resources["AccentBrush"];

                CourseAllButton.Background    = inactive;
                CourseMasterButton.Background = inactive;
                CourseHiddenButton.Background = inactive;
                clickedButton.Background      = active;

                if (clickedButton == CourseAllButton)          _selectedCourse = "";
                else if (clickedButton == CourseMasterButton)  _selectedCourse = "master";
                else if (clickedButton == CourseHiddenButton)  _selectedCourse = "hidden";

                CourseFilterChanged?.Invoke(this, _selectedCourse);
            }
        }
    }
}
