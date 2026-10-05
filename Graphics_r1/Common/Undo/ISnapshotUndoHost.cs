namespace PileDesign.Common.Undo
{
    /// <summary>
    /// 控え (スナップショット) 方式で元に戻す画面の ViewModel。
    /// 窓の段で控えを取る仕組み (<see cref="PileDesign.Common.UndoCapture"/>) に、履歴と控えの取り方を渡す。
    /// </summary>
    public interface ISnapshotUndoHost
    {
        /// <summary>この画面の履歴。</summary>
        UndoManager UndoManager { get; }

        /// <summary>いまの入力の控え (この画面の「元に戻す」が戻す形と同じもの)。</summary>
        object CaptureUndoSnapshot();
    }
}
