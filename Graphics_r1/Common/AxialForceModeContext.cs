namespace PileDesign.Common
{
    /// <summary>
    /// 地震時軸力の入力/表示モード (絶対 ⇔ 変動 = 絶対 − VL) を保持する静的コンテキスト。
    ///
    /// PileLayoutDataItem の AxialForceVL0 / AxialForceVLAdditional setter は、本フラグが true
    /// (変動モード) のとき、VL 変更に伴って AxialForceLevel1s / AxialForceLevel2s を「変動値が
    /// 保たれるように」調整する (絶対 = 新 VL + 旧変動)。false (絶対モード) では従来通り、
    /// L1/L2 の絶対値はそのまま保持される。
    ///
    /// モードはファイル別 (InputModel.IsAxialForceVariationMode) に永続化され、ファイルロード/
    /// 切替時に MainWindowViewModel から本フラグへ書き込まれる。
    ///
    /// 静的フラグである理由: PileLayoutDataItem は data model であり InputModel への参照を持たない。
    /// シングルトン的に単一プロセス内で 1 つしかロードされない (現状) ため、static で十分。
    /// </summary>
    public static class AxialForceModeContext
    {
        public static bool IsVariationMode { get; set; } = false;
    }
}
