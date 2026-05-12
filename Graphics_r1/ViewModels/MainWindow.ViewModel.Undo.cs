using CommunityToolkit.Mvvm.Input; // 追加

namespace PileDesign.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>
    /// 外部（code-behind等）からPropertyChanged通知を発火するための公開メソッド
    /// </summary>
    public void RaisePropertyChanged(string propertyName) => OnPropertyChanged(propertyName);

    private void RaiseUndoStateChanged()
    {
        // CommunityToolkit の IRelayCommand を使って CanExecute 再評価を通知
        (UndoCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        (RedoCommand as IRelayCommand)?.NotifyCanExecuteChanged();

        // ステータスバー情報の更新
        OnPropertyChanged(nameof(UndoRedoStatusText));
        OnPropertyChanged(nameof(UndoToolTip));
        OnPropertyChanged(nameof(RedoToolTip));
        OnPropertyChanged(nameof(PileCountText));
        OnPropertyChanged(nameof(AnalysisStatusText));
        OnPropertyChanged(nameof(AnalysisStatusItems));
    }
}