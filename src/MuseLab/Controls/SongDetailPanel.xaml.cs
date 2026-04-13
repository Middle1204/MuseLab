using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MuseLab.Controls
{
    public partial class SongDetailPanel : UserControl
    {
        public event RoutedEventHandler? CloseRequested;

        private static readonly Random _rng = new Random();

        private static readonly string[] DummyLengths =
            { "1:45", "2:00", "2:15", "2:30", "2:45", "3:00", "3:30" };

        private static readonly string[] DummyPackages =
            { "치유는 포기했어", "오리지널" };

        private static readonly (string date, string package)[] DummyReleaseDates =
        {
            ("2021.03.15", ""),
            ("2021.09.22", ""),
            ("2022.04.01", ""),
            ("2022.11.10", ""),
            ("2023.02.28", ""),
            ("2023.07.05", ""),
            ("2024.01.17", ""),
        };

        private static readonly string[] DummyCharacters =
            { "악마리쟈", "바니걸 린" };

        private static readonly string[] DummyPets =
            { "악마리쟈친구", "마녀", "용" };

        private static readonly string[] DummyHiddenUnlocks =
        {
            "난이도 선택 화면에서 마스터 버튼 연타",
            "선곡 화면에서 곡 재킷을 길게 누르기",
        };

        public SongDetailPanel()
        {
            InitializeComponent();
        }

        public void LoadSong(SongSearchResult song)
        {
            // 기본정보
            DetailTitle.Text = song.Title;
            DetailComposer.Text = song.Composer;
            DetailCourse.Text = song.Course;
            DetailLevel.Text = song.Level.ToString();
            DetailNotes.Text = song.Notes.ToString();
            DetailBpm.Text = song.Bpm;

            // 난이도 배지
            bool isHidden = song.Course.Equals("hidden", StringComparison.OrdinalIgnoreCase);
            DetailCourseBadge.Background = song.Course.ToLower() switch
            {
                "hidden" => (SolidColorBrush)Application.Current.Resources["CourseHiddenBrush"],
                "master" => (SolidColorBrush)Application.Current.Resources["CourseMasterBrush"],
                _        => (SolidColorBrush)Application.Current.Resources["CourseDefaultBrush"]
            };

            // 더미 스탯
            DetailLength.Text = DummyLengths[_rng.Next(DummyLengths.Length)];

            // 패키지 & 출시일
            DetailPackage.Text = DummyPackages[_rng.Next(DummyPackages.Length)];
            var release = DummyReleaseDates[_rng.Next(DummyReleaseDates.Length)];
            DetailReleaseDate.Text = release.date;

            // 점수조합
            DetailCharacter.Text = DummyCharacters[_rng.Next(DummyCharacters.Length)];
            DetailPet.Text = DummyPets[_rng.Next(DummyPets.Length)];

            // 히든 해금방법
            if (isHidden)
            {
                DetailHiddenUnlock.Text = DummyHiddenUnlocks[_rng.Next(DummyHiddenUnlocks.Length)];
                HiddenUnlockSection.Visibility = Visibility.Visible;
            }
            else
            {
                HiddenUnlockSection.Visibility = Visibility.Collapsed;
            }

            // 정확도 (더미)
            double accuracy = 85.0 + _rng.NextDouble() * 15.0;
            AccuracyLabel.Text = $"{accuracy:F2}%";
            SetAccuracyRank(accuracy);
        }

        private void SetAccuracyRank(double accuracy)
        {
            string rank;
            string color;

            if (accuracy >= 100.0) { rank = "S"; color = "#FFFFD700"; }      
            else if (accuracy >= 95.0) { rank = "S"; color = "#FFC0C0C0"; }  
            else if (accuracy >= 90.0) { rank = "S"; color = "#FFFF69B4"; }  
            else if (accuracy >= 80.0) { rank = "A"; color = "#FF9B59B6"; } 
            else if (accuracy >= 70.0) { rank = "B"; color = "#FF5DADE2"; }  
            else if (accuracy >= 60.0) { rank = "C"; color = "#FF82E0AA"; }  
            else                       { rank = "D"; color = "#FF888888"; }  

            AccuracyRank.Text = rank;
            AccuracyRankBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        private void CloseDetailButton_Click(object sender, RoutedEventArgs e) =>
            CloseRequested?.Invoke(this, e);
    }
}

