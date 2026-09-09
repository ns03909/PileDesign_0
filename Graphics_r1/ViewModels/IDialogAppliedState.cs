namespace PileDesign.ViewModels
{
    /// <summary>
    /// ダイアログが<b>何か適用したか</b>を呼び出し側へ伝える。
    ///
    /// <c>OpenDialogWindowWithUndo</c> は閉じたあとに必ず Undo を 1 段積み、
    /// そこは全編集の集約点なので「入力が変更されています。再解析が必要です」も立つ。
    /// キャンセルで閉じても同じで、何も変えていないのに脚部にその表示が残っていた。
    ///
    /// これを実装したダイアログは、適用していないときに開く前の状態へ戻される。
    /// 実装しないダイアログは従来どおり「編集されたかもしれない」扱いになる。
    /// </summary>
    internal interface IDialogAppliedState
    {
        /// <summary>OK などで実際に適用したなら true。キャンセルなら false。</summary>
        bool AppliedChanges { get; }
    }
}
