namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 場所打ち系杭の材料モデル化オプション（プロジェクト全体に適用）。
    ///
    /// コンクリート (InsituConcrete)・鉄筋 (MainBars)・鋼管 (InsituSteelPipe) の
    /// 応力ひずみ関係に影響する。いずれも安全限界 NM 曲線だけでなく M-φ 関係
    /// （→ 非線形 FEM 解析）にも使われるため、値を変更したら解析結果・各種キャッシュを
    /// 破棄する必要がある。
    ///
    /// 値は <see cref="FundamentalInput"/>（基本設定）から
    /// MainWindowViewModel.ApplyConcreteModelOptions() 経由で同期される。
    /// コンクリートは new 箇所が多く引数で渡しきれないため static 状態として保持する
    /// （M-φ キャッシュ PileSection._mphiCache と同様）。
    /// 鉄筋・鋼管は「どの断面に適用するか」の限定が必要なため、static フラグを
    /// 各断面コンストラクタで読み取り、対象材料インスタンスの
    /// <c>YieldAt11F</c>/<c>PerfectBilinear11F</c> に転写する方式を採る。
    /// </summary>
    internal static class ConcreteModelOptions
    {
        /// <summary>圧縮側折れ点応力度の低減係数（0.85·Fc）。</summary>
        public const double CompressionReductionFactor = 0.85;

        /// <summary>引張側の降伏応力度を 0 とする（コンクリートの引張負担を無視する）。</summary>
        public static bool IgnoreTensileStrength { get; set; }

        /// <summary>圧縮側の折れ点応力度を 0.85·Fc とする（既定の Gsi·Fc に代えて、Gsi を乗じない）。</summary>
        public static bool UseReducedCompression { get; set; }

        /// <summary>
        /// 鉄筋（場所打ち RC 杭・場所打ち鋼管コンクリート杭）を 1.1×F で降伏する
        /// 完全バイリニア型とする（降伏応力度を σy → 1.1·σy に引き上げる）。
        /// </summary>
        public static bool RebarYieldAt11F { get; set; }

        /// <summary>
        /// 鋼管（場所打ち鋼管コンクリート杭）を 1.1×F で降伏する完全バイリニア型とする
        /// （ひずみ硬化・破断応力を廃し、±1.1F で頭打ち）。
        /// </summary>
        public static bool SteelPipeYieldAt11F { get; set; }

        /// <summary>
        /// コンクリートのヤング係数 Ec の算定で ξ(=Gsi) を 1.0 として計算する
        /// （強度側 Gsi·Fc 等には従来どおり実際の ξ を用いる）。
        /// </summary>
        public static bool UseUnitGsiForConcreteE { get; set; }

        /// <summary>
        /// 場所打ち系コンクリートの使用限界・損傷限界の許容圧縮応力度を、
        /// 基礎部材の (1/3)ξFc・(2/3)ξFc に代えて、告示 平13国交告第1113号(第8) の
        /// 長期・短期許容圧縮応力度で算定する（使用限界=長期、損傷限界=短期）。
        /// せん断・安全限界・M-φ・解析には影響しない（使用/損傷限界 NM のみ）。
        /// </summary>
        public static bool UseNotification1113Compression { get; set; }

        /// <summary>
        /// 告示1113(第8) の長期許容圧縮応力度の区分。
        /// 1: Fc/4、2: min(Fc/4.5, 6.0)。短期はいずれも長期の 2 倍。
        /// </summary>
        public static int Notification1113CompressionCase { get; set; } = 1;

        /// <summary>
        /// キャッシュキー用シグネチャ。オプションが変わるとキー文字列が変わり、
        /// M-φ キャッシュやひずみ応力プロファイルキャッシュが正しく再計算される。
        /// </summary>
        public static string Signature()
            => $"CMO:T{(IgnoreTensileStrength ? 1 : 0)}C{(UseReducedCompression ? 1 : 0)}" +
               $"R{(RebarYieldAt11F ? 1 : 0)}P{(SteelPipeYieldAt11F ? 1 : 0)}" +
               $"E{(UseUnitGsiForConcreteE ? 1 : 0)}" +
               $"K{(UseNotification1113Compression ? Notification1113CompressionCase : 0)}";
    }
}
