using SteeringTrainer.Infrastructure.Persistence;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SteeringTrainer.App.ViewModels
{
    public class HistoryViewModel : ViewModelBase
    {
        private readonly HeelToeHistoryRepository _heelToeRepo = new();
        private readonly ThrottleTrackingHistoryRepository _trackingRepo = new();

        public ObservableCollection<HeelToeHistoryEntry> HeelToeHistoryList { get; } = new();
        public ObservableCollection<ThrottleTrackingHistoryEntry> TrackingHistoryList { get; } = new();

        private string _heelToeSummary = "履歴読み込み中...";
        public string HeelToeSummary
        {
            get => _heelToeSummary;
            private set => Set(ref _heelToeSummary, value);
        }

        private string _trackingSummary = "履歴読み込み中...";
        public string TrackingSummary
        {
            get => _trackingSummary;
            private set => Set(ref _trackingSummary, value);
        }

        public ICommand RefreshCommand { get; }

        public HistoryViewModel()
        {
            RefreshCommand = new RelayCommand(LoadHistory);
            LoadHistory();
        }

        public void LoadHistory()
        {
            _ = Task.Run(() =>
            {
                var htList = _heelToeRepo.LoadAll();
                var trList = _trackingRepo.LoadAll();

                App.Current?.Dispatcher.InvokeAsync(() =>
                {
                    HeelToeHistoryList.Clear();
                    foreach (var item in htList.OrderByDescending(x => x.Id))
                    {
                        HeelToeHistoryList.Add(item);
                    }

                    TrackingHistoryList.Clear();
                    foreach (var item in trList)
                    {
                        TrackingHistoryList.Add(item);
                    }

                    if (htList.Count > 0)
                    {
                        var avgScore = htList.Average(x => x.Score);
                        var bestScore = htList.Max(x => x.Score);
                        HeelToeSummary = $"総回数: {htList.Count} 回  |  最高点: {bestScore:F1} 点  |  平均点: {avgScore:F1} 点";
                    }
                    else
                    {
                        HeelToeSummary = "まだヒール＆トゥの履歴がありません";
                    }

                    if (trList.Count > 0)
                    {
                        var avgScore = trList.Average(x => x.TotalScore);
                        var bestScore = trList.Max(x => x.TotalScore);
                        var avgLag = trList.Average(x => x.PhaseLagSeconds);
                        TrackingSummary = $"総回数: {trList.Count} 回  |  最高点: {bestScore:F1} 点  |  平均点: {avgScore:F1} 点  |  平均位相差: {avgLag:F2} 秒";
                    }
                    else
                    {
                        TrackingSummary = "まだアクセル波形追従の履歴がありません";
                    }
                });
            });
        }
    }
}
