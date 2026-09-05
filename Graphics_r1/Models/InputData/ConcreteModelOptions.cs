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
        /// <summary>
        /// オプションが変わるたびに増える版数。
        ///
        /// <see cref="Signature()"/> は文字列を組み立てるので、曲線を引くたびに呼ぶには重い。
        /// キャッシュを持つ側 (<c>PileSection</c>) が「自分のキャッシュは古いか」を
        /// <b>整数の比較だけ</b>で判定できるようにする。
        ///
        /// これがあると、入力モデルをたどってキャッシュを捨てて回る必要がなくなる。
        /// ダイアログが編集している<b>複製</b>の断面はその走査から漏れるため、
        /// オプションを変えても前の設定で計算済みの曲線が描かれ続ける、という不具合が出ていた。
        /// </summary>
        public static int Version => _version;
        private static int _version;

        private static void Set(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            _version++;
        }

        private static void Set(ref int field, int value)
        {
            if (field == value) return;
            field = value;
            _version++;
        }

        /// <summary>圧縮側折れ点応力度の低減係数（0.85·Fc）。</summary>
        public const double CompressionReductionFactor = 0.85;

        /// <summary>引張側の降伏応力度を 0 とする（コンクリートの引張負担を無視する）。</summary>
        public static bool IgnoreTensileStrength
        {
            get => _ignoreTensileStrength;
            set => Set(ref _ignoreTensileStrength, value);
        }
        private static bool _ignoreTensileStrength;

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
        public static bool UseGuideYoungsModulus
        {
            get => _useGuideYoungsModulus;
            set => Set(ref _useGuideYoungsModulus, value);
        }
        private static bool _useGuideYoungsModulus;

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
        public static bool UseReducedCompression
        {
            get => _useReducedCompression;
            set => Set(ref _useReducedCompression, value);
        }
        private static bool _useReducedCompression;

        /// <summary>
        /// 鉄筋（場所打ち RC 杭・場所打ち鋼管コンクリート杭）を 1.1×F で降伏する
        /// 完全バイリニア型とする（降伏応力度を σy → 1.1·σy に引き上げる）。
        /// </summary>
        public static bool RebarYieldAt11F
        {
            get => _rebarYieldAt11F;
            set => Set(ref _rebarYieldAt11F, value);
        }
        private static bool _rebarYieldAt11F;

        /// <summary>
        /// 鋼管（場所打ち鋼管コンクリート杭）を 1.1×F で降伏する完全バイリニア型とする
        /// （ひずみ硬化・破断応力を廃し、±1.1F で頭打ち）。
        /// </summary>
        public static bool SteelPipeYieldAt11F
        {
            get => _steelPipeYieldAt11F;
            set => Set(ref _steelPipeYieldAt11F, value);
        }
        private static bool _steelPipeYieldAt11F;

        /// <summary>
        /// コンクリートのヤング係数 Ec の算定で ξ(=Gsi) を 1.0 として計算する
        /// （強度側 Gsi·Fc 等には従来どおり実際の ξ を用いる）。
        /// </summary>
        public static bool UseUnitGsiForConcreteE
        {
            get => _useUnitGsiForConcreteE;
            set => Set(ref _useUnitGsiForConcreteE, value);
        }
        private static bool _useUnitGsiForConcreteE;

        /// <summary>
        /// 場所打ち系コンクリートの使用限界・損傷限界の許容圧縮応力度を、
        /// 基礎部材の (1/3)ξFc・(2/3)ξFc に代えて、告示 平13国交告第1113号(第8) の
        /// 長期・短期許容圧縮応力度で算定する（使用限界=長期、損傷限界=短期）。
        /// せん断・安全限界・M-φ・解析には影響しない（使用/損傷限界 NM のみ）。
        /// </summary>
        public static bool UseNotification1113Compression
        {
            get => _useNotification1113Compression;
            set => Set(ref _useNotification1113Compression, value);
        }
        private static bool _useNotification1113Compression;

        /// <summary>
        /// 場所打ち鉄筋コンクリート杭のコンクリート許容せん断応力度を、
        /// 基礎部材のせん断耐力式に代えて、告示 平13国交告第1113号(第8) の
        /// 長期・短期許容せん断応力度で算定する（使用限界=長期、損傷限界=短期=長期×1.5）。
        /// 許容せん断力 Q = fs·b·j（軸力・M/(Q·d) 非依存）。安全限界・M-φ・解析には影響しない。
        /// </summary>
        public static bool UseNotification1113Shear
        {
            get => _useNotification1113Shear;
            set => Set(ref _useNotification1113Shear, value);
        }
        private static bool _useNotification1113Shear;

        /// <summary>
        /// 場所打ち鉄筋コンクリート杭の安全限界曲げ強度の算定で、コンクリートの応力ひずみ関係を
        /// バイリニアに代えて e関数法で設定する（RC基礎構造部材の耐震設計指針(案) 5.4.1 準拠）。
        /// 圧縮限界ひずみ εcu=0.003・圧縮材料強度 ξFc は共通。安全限界 NM 曲線および
        /// M-φ 端点 Mu0（→ 解析）に影響するため、変更時は解析結果をリセットする。
        /// </summary>
        public static bool UseInsituUltimateEFunction
        {
            get => _useInsituUltimateEFunction;
            set => Set(ref _useInsituUltimateEFunction, value);
        }
        private static bool _useInsituUltimateEFunction;

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
        public static bool UseFiberMPhi
        {
            get => _useFiberMPhi;
            set => Set(ref _useFiberMPhi, value);
        }
        private static bool _useFiberMPhi;

        // ─── 場所打ち鋼管コンクリート杭（KCTB / TB 工法）───
        //
        // BCJ評定-FD0356-08 が定めているのは、コンクリートの許容応力度（告示1113(第8) 打設方法(一)）、
        // 本体部の設計法（SRC規準2014 4章2節の累加）、腐食しろ 1mm、適用範囲・形状寸法 である。
        // 評定書に終局（安全限界）の規定は無く、評定申込事項 7 項目にも含まれない。
        // 評定書は終局（安全限界）について何も定めていない。3,000μ とも 5,000μ とも書いていない。
        // したがって εcu と「許容時の判定に鉄筋を用いない」は「評定に従う／反する」項目ではなく、
        // どの文献に依るかを設計者が選ぶ項目である。出典はジャパンパイル Technical Note Vol.1-5 および
        // 建設省総合技術開発プロジェクト 基礎WG 最終報告書（平成12年3月）資料4-7 である。
        // 混同を避けるため、評定で決まっている項目と決まっていない項目を別のフラグに分ける。

        /// <summary>
        /// 【評定書に規定が無い項目】場所打ち鋼管コンクリート杭の終局（安全限界）圧縮縁ひずみを
        /// εcu = 5,000μ とする（既定は 3,000μ）。
        ///
        /// 出典: 建設省総合技術開発プロジェクト 基礎WG 最終報告書 (平成12年3月) 資料4-7。
        /// 鋼管によるコンクリートの拘束効果があるため、3,000μ では限界曲率を過小評価する
        /// （7,000μ は実験値を上回る＝危険側）。値そのものは Technical Note Vol.1-5 p.2 にも示される。
        ///
        /// 安全限界 NM と M-φ の終点 (φu, Mu0)（→ 解析）に影響するため、変更時は解析結果をリセットする。
        /// 鋼管杭のコンクリート充填鋼管部は対象外。
        /// </summary>
        public static bool UseUltimateStrain5000ForSteelPipeConcrete
        {
            get => _useUltimateStrain5000ForSteelPipeConcrete;
            set => Set(ref _useUltimateStrain5000ForSteelPipeConcrete, value);
        }
        private static bool _useUltimateStrain5000ForSteelPipeConcrete;

        /// <summary>
        /// 【評定書に規定が無い項目】場所打ち鋼管コンクリート杭の許容時（使用限界・損傷限界）の判定を、
        /// コンクリートと鋼管のみで行う（鉄筋の許容応力度では限界状態を決めない）。
        ///
        /// 出典: ジャパンパイル Technical Note Vol.1-5 (2022年11月) p.2。
        /// 「圧縮側のコンクリートの応力度が σca に達した時、もしくは圧縮側または引張側の
        /// 鋼管の応力度が許容応力度 σsa（= 基準強度 Fs）に達した時」と定める。
        /// 鉄筋は耐力への寄与（断面積分）には従来どおり参入する。
        ///
        /// 使用・損傷限界 NM のみに効き、安全限界・M-φ・解析には影響しない。
        /// 鋼管杭のコンクリート充填鋼管部は対象外。
        /// </summary>
        public static bool ExcludeRebarFromAllowableLimitForSteelPipeConcrete
        {
            get => _excludeRebarFromAllowableLimitForSteelPipeConcrete;
            set => Set(ref _excludeRebarFromAllowableLimitForSteelPipeConcrete, value);
        }
        private static bool _excludeRebarFromAllowableLimitForSteelPipeConcrete;

        /// <summary>
        /// 場所打ち鋼管コンクリート杭の許容時（使用限界・損傷限界）N-M を、
        /// 断面分割積分（Technical Note Vol.1-5）で求める。
        ///
        /// <b>既定は true（従来どおりの断面分割積分）。</b>
        /// false にすると評定書 5.(3) の単純累加式
        /// （日本建築学会「鉄骨鉄筋コンクリート構造計算規準・同解説」2014 4章2節）で算定する。
        ///
        /// UI では評定書が定める単純累加を上段に置くため、この bool は
        /// 「代替側 = 断面分割積分」という向きで持つ。既定値を true にしてあるのは、
        /// 何も選ばない既存プロジェクトの挙動（断面分割積分）を変えないためである。
        ///
        /// 使用・損傷限界 NM のみに効き、安全限界・M-φ・解析には影響しない。
        /// </summary>
        public static bool UseFiberNMForSteelPipeConcrete
        {
            get => _useFiberNMForSteelPipeConcrete;
            set => Set(ref _useFiberNMForSteelPipeConcrete, value);
        }
        private static bool _useFiberNMForSteelPipeConcrete = true;

        /// <summary>
        /// BCJ評定-FD0356-08 が定める項目がすべて評定どおりに設定されているか。
        /// 適用範囲（φ700〜2700・板厚下限・鋼管長・腐食しろ 1mm・Fc 18〜45）の検査を
        /// 有効にするかの判定に使う。個別に切り替えると自動で追随する。
        /// </summary>
        public static bool FollowsKctbEvaluation =>
            UseNotification1113Compression
            && Notification1113CompressionCase == 1
            && !UseFiberNMForSteelPipeConcrete;

        /// <summary>
        /// 告示1113(第8) の長期許容応力度の区分（圧縮・せん断で共用）。
        /// 圧縮 1: Fc/4、2: min(Fc/4.5, 6.0)（短期 2 倍）。
        /// せん断 1: Fc/40、2: Fc/45 とアーチ項 (3/4)(0.49+Fc/100) の小さい方（短期 1.5 倍）。
        /// </summary>
        public static int Notification1113CompressionCase
        {
            get => _notification1113CompressionCase;
            set => Set(ref _notification1113CompressionCase, value);
        }
        private static int _notification1113CompressionCase = 1;

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
               $"F{(UseFiberMPhi ? 1 : 0)}" +
               $"Y{(UseGuideYoungsModulus ? 1 : 0)}" +
               $"J{(UseUltimateStrain5000ForSteelPipeConcrete ? 1 : 0)}" +
               $"{(ExcludeRebarFromAllowableLimitForSteelPipeConcrete ? 1 : 0)}" +
               $"{(UseFiberNMForSteelPipeConcrete ? 1 : 0)}";
    }
}
