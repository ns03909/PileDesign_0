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

        /// <summary>
        /// 鋼材のヤング係数に「基礎部材の強度と変形性能」の値を用いる
        /// （既定 false = 製品カタログの値をそのまま使う）。
        ///
        /// カタログ値はメーカーで食い違っており、異形棒鋼は 200,000（JIS・三谷セキサン）と
        /// 205,000（ジャパンパイル）、鋼管は 200,000（JIS）と 205,000（三谷 Hi-SC105）に割れる。
        /// カタログに拠らず指針で統一して検討したい場合にこちらへ切り替える。
        ///
        /// 既製杭は指針が E ではなく<b>ヤング係数比 n = 5 そのもの</b>を規定しているため、
        /// E ではなく n を固定する（<see cref="GuideModularRatioForPrecast"/>）。
        /// EI・EA だけでなく N-M 曲線・M-φ まで一貫して効かせるため、
        /// 製品を断面へ反映する時点で E を差し替える方式を採る。
        /// </summary>
        public static bool UseGuideYoungsModulus { get; set; }

        /// <summary>
        /// 基礎部材の強度と変形性能が規定する既製杭のヤング係数比 n（PC鋼材・鉄筋とも 5 で固定）。
        /// </summary>
        public const double GuideModularRatioForPrecast = 5.0;

        /// <summary>
        /// 基礎部材の強度と変形性能が規定する鋼材のヤング係数 (N/mm²)。
        /// 場所打ち系の鉄筋・鋼管と、SC杭の鋼管に適用する。
        /// </summary>
        public const double GuideSteelYoungsModulus = 205000.0;

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
        /// 場所打ち鉄筋コンクリート杭のコンクリート許容せん断応力度を、
        /// 基礎部材のせん断耐力式に代えて、告示 平13国交告第1113号(第8) の
        /// 長期・短期許容せん断応力度で算定する（使用限界=長期、損傷限界=短期=長期×1.5）。
        /// 許容せん断力 Q = fs·b·j（軸力・M/(Q·d) 非依存）。安全限界・M-φ・解析には影響しない。
        /// </summary>
        public static bool UseNotification1113Shear { get; set; }

        /// <summary>
        /// 場所打ち鉄筋コンクリート杭の安全限界曲げ強度の算定で、コンクリートの応力ひずみ関係を
        /// バイリニアに代えて e関数法で設定する（RC基礎構造部材の耐震設計指針(案) 5.4.1 準拠）。
        /// 圧縮限界ひずみ εcu=0.003・圧縮材料強度 ξFc は共通。安全限界 NM 曲線および
        /// M-φ 端点 Mu0（→ 解析）に影響するため、変更時は解析結果をリセットする。
        /// </summary>
        public static bool UseInsituUltimateEFunction { get; set; }

        /// <summary>
        /// コンクリート系杭の解析用 M-φ 関係を、指針ポリリニア（Mcr-My-β1·Mu0 等の折線）に
        /// 代えてファイバーモデル（断面分割積分、各曲率で軸力つり合いを解く）で算定する。
        /// 対象: 場所打ちRC / 場所打ち鋼管コンクリート（RC部・鋼管コンクリート部）/ PHC / PRC / SC /
        /// コンクリート充填鋼管部（AbstractPileSection 系すべて）。鋼管杭の鋼管部は
        /// SteelPipeSection（別系統、M-φ が既に厳密）のため対象外。
        /// β1・β2 の指針低減係数は乗じない「素の」断面応答となる。FEM で負勾配ばねとならないよう
        /// 単調非減少化＋最小勾配床の後処理を施した曲線を用いる。
        /// M-φ（→ 非線形 FEM 解析）に影響するため、変更時は解析結果をリセットする。
        /// </summary>
        public static bool UseFiberMPhi { get; set; }

        /// <summary>
        /// 告示1113(第8) の長期許容応力度の区分（圧縮・せん断で共用）。
        /// 圧縮 1: Fc/4、2: min(Fc/4.5, 6.0)（短期 2 倍）。
        /// せん断 1: Fc/40、2: Fc/45 とアーチ項 (3/4)(0.49+Fc/100) の小さい方（短期 1.5 倍）。
        /// </summary>
        public static int Notification1113CompressionCase { get; set; } = 1;

        /// <summary>
        /// 解説書準拠（告示1113 圧縮）オプションが有効なとき、限界状態の表示名を
        /// 許容応力度設計の用語（長期許容 / 短期許容）で表示するかどうか。
        /// 使用限界⇔長期許容、損傷限界⇔短期許容 の呼称は本フラグに追随する。
        /// </summary>
        public static bool UseAllowableStressLabels => UseNotification1113Compression || UseNotification1113Shear;

        /// <summary>使用限界の表示名（オプションON時は「長期許容」）。</summary>
        public static string ServiceLimitLabel => UseAllowableStressLabels ? "長期許容" : "使用限界";

        /// <summary>損傷限界の表示名（オプションON時は「短期許容」）。</summary>
        public static string DamageLimitLabel => UseAllowableStressLabels ? "短期許容" : "損傷限界";

        /// <summary>
        /// 表示文字列中の「使用限界」「損傷限界」を、オプションON時に
        /// 「長期許容」「短期許容」へ置換する。UI・グラフ凡例・計算書の表示専用。
        /// 内部キー・プロパティ名には使用しないこと。
        /// </summary>
        public static string MapLimitStateText(string text)
        {
            if (!UseAllowableStressLabels || string.IsNullOrEmpty(text)) return text;
            return text.Replace("使用限界", "長期許容").Replace("損傷限界", "短期許容");
        }

        /// <summary>
        /// キャッシュキー用シグネチャ。オプションが変わるとキー文字列が変わり、
        /// M-φ キャッシュやひずみ応力プロファイルキャッシュが正しく再計算される。
        /// </summary>
        public static string Signature()
            => $"CMO:T{(IgnoreTensileStrength ? 1 : 0)}C{(UseReducedCompression ? 1 : 0)}" +
               $"R{(RebarYieldAt11F ? 1 : 0)}P{(SteelPipeYieldAt11F ? 1 : 0)}" +
               $"E{(UseUnitGsiForConcreteE ? 1 : 0)}" +
               $"K{(UseNotification1113Compression ? Notification1113CompressionCase : 0)}" +
               $"Q{(UseNotification1113Shear ? Notification1113CompressionCase : 0)}" +
               $"U{(UseInsituUltimateEFunction ? 1 : 0)}" +
               $"F{(UseFiberMPhi ? 1 : 0)}";
    }
}
