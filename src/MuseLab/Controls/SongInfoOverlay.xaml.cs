using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MuseLab.Controls
{
    public partial class SongInfoOverlay : UserControl
    {
        private bool _isEditMode = false;
        private Point? _dragStartPoint = null;
        private Thickness _originalMargin;

        public SongInfoOverlay()
        {
            InitializeComponent();
        }

        public void SetSongInfo(string title, string difficulty, string level)
        {
            SongText.Text = title;
            DifficultyText.Text = difficulty;
            LevelText.Text = level;
        }

        public void SetDifficultyColor(string hexColor)
        {
            DifficultyBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
        }

        public void SetEditMode(bool active, bool showBorder)
        {
            _isEditMode = active;

            if (active)
            {
                RootBorder.BorderBrush = new SolidColorBrush(Colors.Yellow);
                RootBorder.BorderThickness = new Thickness(2);
                RootBorder.Cursor = Cursors.Hand;
            }
            else
            {
                if (showBorder)
                {
                    RootBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D9CDB"));
                    RootBorder.BorderThickness = new Thickness(2);
                }
                else
                {
                    RootBorder.BorderBrush = null;
                    RootBorder.BorderThickness = new Thickness(0);
                }
                RootBorder.Cursor = Cursors.Arrow;
            }
        }

        public void SetOutlineBorder(bool show)
        {
            if (_isEditMode) return;

            if (show)
            {
                RootBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D9CDB"));
                RootBorder.BorderThickness = new Thickness(2);
            }
            else
            {
                RootBorder.BorderBrush = null;
                RootBorder.BorderThickness = new Thickness(0);
            }
        }

        public void SetDifficultyVisible(bool visible) =>
            DifficultyBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        public void SetLevelVisible(bool visible) =>
            LevelText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isEditMode) return;
            _dragStartPoint = e.GetPosition(Application.Current.MainWindow);
            _originalMargin = Margin;
            RootBorder.CaptureMouse();
            e.Handled = true;
        }

        private void RootBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isEditMode) return;
            _dragStartPoint = null;
            RootBorder.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void RootBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isEditMode || _dragStartPoint == null || !RootBorder.IsMouseCaptured) return;

            var mainWindow = Application.Current.MainWindow;
            Point currentPosition = e.GetPosition(mainWindow);
            double deltaX = currentPosition.X - _dragStartPoint.Value.X;
            double deltaY = currentPosition.Y - _dragStartPoint.Value.Y;

            double newRight = _originalMargin.Right - deltaX;
            double newBottom = _originalMargin.Bottom - deltaY;

            newRight = Math.Max(10, Math.Min(newRight, mainWindow.ActualWidth - ActualWidth - 10));
            newBottom = Math.Max(10, Math.Min(newBottom, mainWindow.ActualHeight - ActualHeight - 10));

            Margin = new Thickness(0, 0, newRight, newBottom);
            e.Handled = true;
        }
    }
}
