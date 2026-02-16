using CommunityToolkit.Mvvm.Input; // 追加

namespace PileDesign.ViewModels;

public partial class MainWindowViewModel
{
    private void RaiseUndoStateChanged()
    {
        // CommunityToolkit の IRelayCommand を使って CanExecute 再評価を通知
        (UndoCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        (RedoCommand as IRelayCommand)?.NotifyCanExecuteChanged();

        // ステータスバー情報の更新
        OnPropertyChanged(nameof(UndoRedoStatusText));
        OnPropertyChanged(nameof(PileCountText));
        OnPropertyChanged(nameof(AnalysisStatusText));
    }
}