using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Diagnostics;

namespace MuseLab.Controls
{
    public partial class SearchResultsPanel : UserControl
    {
        public event RoutedEventHandler? CloseRequested;

        private Dictionary<TextBlock, DispatcherTimer> _tooltipTimers = new();

        public SearchResultsPanel()
        {
            InitializeComponent();
        }

        public void SetResults(ObservableCollection<SongSearchResult> results)
        {
            SearchResultsList.ItemsSource = results;
            NoResultsText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchPromptText.Visibility = Visibility.Collapsed;
        }

        public void ShowPrompt()
        {
            SearchPromptText.Visibility = Visibility.Visible;
            NoResultsText.Visibility = Visibility.Collapsed;
            SearchResultsList.ItemsSource = null;
        }

        private void CloseSearchButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, e);
        }

        private void RandomPickButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchResultsList.ItemsSource is ObservableCollection<SongSearchResult> results && results.Count > 0)
            {
                foreach (var result in results)
                    result.IsHighlighted = false;

                var random = new Random();
                int randomIndex = random.Next(results.Count);
                results[randomIndex].IsHighlighted = true;

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
                        int measuredItems = Math.Min(5, SearchResultsList.Items.Count);
                        for (int i = 0; i < measuredItems; i++)
                        {
                            var item = SearchResultsList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                            if (item != null) totalHeight += item.ActualHeight;
                        }

                        double avgHeight = measuredItems > 0 ? totalHeight / measuredItems : 140;
                        double viewportHeight = SearchResultsScrollViewer.ViewportHeight;
                        double targetOffset = (randomIndex * avgHeight) - (viewportHeight / 2) + (avgHeight / 2);

                        targetOffset = Math.Max(0, Math.Min(targetOffset, SearchResultsScrollViewer.ScrollableHeight));
                        AnimateScroll(SearchResultsScrollViewer.VerticalOffset, targetOffset);
                    }
                }, DispatcherPriority.Loaded);
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

        private void SongTitle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock)
                textBlock.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D9CDB"));
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

                    var tooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    tooltipTimer.Tick += (s, args) =>
                    {
                        textBlock.ToolTip = "클릭하여 복사";
                        _tooltipTimers.Remove(textBlock);
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
    }
}
