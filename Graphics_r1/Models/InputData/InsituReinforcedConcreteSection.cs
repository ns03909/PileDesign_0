using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    // 場所打ち鉄筋コンクリート杭断面クラス
    internal class InsituReinforcedConcreteSection : AbstractPileSection
    {
        public CircularSolidSection CircularSolidSectionConcrete { get; private set; }
        public CircularPipeSection CircularPipeSectionMainbars { get; private set; }

        public InsituConcrete InsituConcrete { get; private set; }
        public MainBars MainBars { get; private set; }

        public double MainBarArea { get; private set; }
        public double MainBarPCD { get; private set; }

        public double Ae { get; private set; }
        public double Ze { get; private set; }
        public double Ie { get; private set; }
        public double Ft { get; private set; }

        public List<double> ServiceLimitShearAxialForceThresholds { get; private set; } = [];

        // コンストラクタ
        // applyBodyMaterialOptions: 杭体断面のみ true。鉄筋 1.1F 完全バイリニア型オプションを適用する。
        //   杭頭接合部（PileTop の定着筋断面）などは false を渡してオプション対象外とする。
        /// <summary>
        /// 帯筋の既定のせん断補強筋比。<b>帯筋の入力を渡せない呼び出し用の仮値</b>。
        /// 杭体の断面では PileSection が実際の値を渡すので、ここが使われるのは
        /// 杭頭接合部の断面のように帯筋を持たない場合だけ。
        /// </summary>
        internal const double DefaultHoopPw = 0.002;

        /// <summary>帯筋の既定の降伏点 (N/mm²)。<see cref="DefaultHoopPw"/> と同じ扱い。</summary>
        internal const double DefaultHoopSigmaWy = 295.0;

        /// <summary>せん断補強筋比 pw。安全限界せん断の算定に使う。</summary>
        internal double HoopPw { get; }

        /// <summary>せん断補強筋の降伏点 σwy (N/mm²)。</summary>
        internal double HoopSigmaWy { get; }

        /// <summary>
        /// せん断補強筋の工法。null なら「標準」(建築基礎構造設計指針の式)。
        ///
        /// 工法を選んだときは 3 つの限界状態すべてが<b>工法の指針の式</b>に替わる。
        /// 等価正方形断面の取り方まで違うので、材料だけ差し替えてはいけない。
        /// </summary>
        internal ShearReinforcementMethodSpec? ShearMethod { get; }

        /// <summary>損傷限界 (一次設計) に使う算定式。工法が選択肢を持つ場合のみ意味を持つ。</summary>
        internal string HoopDamageFormula { get; } = ShearReinforcementMethods.DamageFormulaDamageLimit;

        /// <summary>終局限界に使う算定式。工法が選択肢を持つ場合のみ意味を持つ。</summary>
        internal string HoopUltimateFormula { get; } = ShearReinforcementMethods.UltimateFormulaArakawa;

        internal InsituReinforcedConcreteSection(
            InsituConcrete insituConcrete, MainBars mainBars, bool applyBodyMaterialOptions = true,
            double hoopPw = DefaultHoopPw, double hoopSigmaWy = DefaultHoopSigmaWy,
            string? hoopMethod = null, string? hoopDamageFormula = null, string? hoopUltimateFormula = null)
        {
            if (!string.IsNullOrEmpty(hoopDamageFormula)) HoopDamageFormula = hoopDamageFormula;
            if (!string.IsNullOrEmpty(hoopUltimateFormula)) HoopUltimateFormula = hoopUltimateFormula;
            // 帯筋 (せん断補強筋)。安全限界せん断の算定に要る。
            // 既定値は帯筋の入力を持たない呼び出し (杭頭接合部の断面など) 用で、
            // 杭体の断面は PileSection から実際の帯筋を渡す。
            HoopPw = hoopPw > 0 ? hoopPw : DefaultHoopPw;
            HoopSigmaWy = hoopSigmaWy > 0 ? hoopSigmaWy : DefaultHoopSigmaWy;
            ShearMethod = ShearReinforcementMethods.Get(hoopMethod);

            // 鉄筋 1.1F 完全バイリニア型オプション（降伏応力度 σy → 1.1σy）を適用（限界ひずみ再計算より前）
            if (applyBodyMaterialOptions)
                mainBars.YieldAt11F = ConcreteModelOptions.RebarYieldAt11F;

            PileDia = insituConcrete.DO;
            MainBarArea = mainBars.Ag; //mainBarArea;
            MainBarPCD = mainBars.PCD;  //mainBarPcd;

            CircularSolidSectionConcrete = new CircularSolidSection(PileDia);
            CircularPipeSectionMainbars = new CircularPipeSection(MainBarPCD, MainBarArea / Math.PI / MainBarPCD);

            InsituConcrete = insituConcrete;
            MainBars = mainBars;

            PositionCs = [-PileDia / 2.0, -MainBarPCD / 2.0,];
            PositionTs = [PileDia / 2.0, MainBarPCD / 2.0,];

            // プレストレスひずみ度
            Prestrains = [insituConcrete.Prestrain, mainBars.Prestrain];

            SetZeFtIe();

            // 使用限界状態ひずみ度
            ServiceLimitStrainCs = [insituConcrete.ServiceLimitStrainC, mainBars.ServiceLimitStrainC,];
            ServiceLimitStrainTs = [insituConcrete.ServiceLimitStrainT, mainBars.ServiceLimitStrainT,];

            // 損傷限界状態ひずみ度
            DamageLimitStrainCs = [insituConcrete.DamageLimitStrainC, mainBars.DamageLimitStrainC,];
            DamageLimitStrainTs = [insituConcrete.DamageLimitStrainT, mainBars.DamageLimitStrainT,];

            // 使用限界状態最大曲率
            CurvatureMaxServiceLimit = GetAllowableMaxCurvature(ServiceLimitStrainCs, PositionCs, ServiceLimitStrainTs, PositionTs);

            // 損傷限界最大曲率
            CurvatureMaxDamageLimit = GetAllowableMaxCurvature(DamageLimitStrainCs, PositionCs, DamageLimitStrainTs, PositionTs);

            // 使用限界最大曲率時の軸力
            AxialForceCurvatureMaxServiceLimit = GetAllowableForceAndMoment(0, true, CurvatureMaxServiceLimit).Item1;

            // 損傷限界最大曲率時の軸力
            AxialForceCurvatureMaxDamageLimit = GetAllowableForceAndMoment(1, true, CurvatureMaxDamageLimit).Item1;

            // 使用限界軸力閾値
            ServiceLimitAxialForceThresholds = [];

            // 使用限界曲げモーメント低減率
            ServiceLimitBeta = [1.0];

            // 損傷限界軸力閾値
            DamageLimitAxialForceThresholds =
            [
                -0.05 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                1.0 / 3.0 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                0.4 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
            ];

            // 損傷限界曲げモーメント低減率
            DamageLimitBeta = [0.0, 1.0, 0.65, 0.0];        // L2: β1=1.0, β2={1.0, 0.65}
            DamageLimitBetaL1 = [0.0, 1.0, 1.0, 0.0];       // L1: β2 を乗じない（β1=1.0 のみ）

            // 安全限界軸力低減率
            UltimateLimitAxialForceThresholds =
            [
                -0.05 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                1.0 / 4.0 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                1.0 / 3.0 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
                0.4 * insituConcrete.Gsi * insituConcrete.Fc * Math.PI * Math.Pow(PileDia, 2) / 4.0,
            ];

            // 安全限界曲げモーメント低減率
            UltimateLimitBeta = [0.0, 0.95 * 1.0, 0.95 * 1.0, 0.80 * 0.65, 0.0];

            // 低減前使用限界NMインタラクション
            UnfactoredServiceNM = GetServiceLimitMNInteraction();

            // 低減前損傷限界NMインタラクション
            UnfactoredDamageNM = GetDamageLimitMNInteraction();

            // 低減前安全限界NMインタラクション
            UnfactoredUltimateNM = GetUltimateMNInteraction();

            // 損傷限界閾値
            ServiceLimitBendingMomentThresholds = [];

            // 損傷限界閾値
            DamageLimitBendingMomentThresholds = GetDamageLimitBendingMomentThresholds();

            // 安全限界閾値
            UltimateLimitBendingMomentThresholds = GetUltimateLimitBendingMomentThresholds();

            // 低減後使用限界NMインタラクション
            FactoredServiceNM = GetFactoredMNInteraction(UnfactoredServiceNM, (ServiceLimitAxialForceThresholds, ServiceLimitBendingMomentThresholds), ServiceLimitBeta);

            // 低減後損傷限界NMインタラクション（レベル2: β1×β2）
            FactoredDamageNM = GetFactoredMNInteraction(UnfactoredDamageNM, (DamageLimitAxialForceThresholds, DamageLimitBendingMomentThresholds), DamageLimitBeta);
            // レベル1: β2 を乗じない（β1 のみ）
            FactoredDamageNMLevel1 = GetFactoredMNInteraction(UnfactoredDamageNM, (DamageLimitAxialForceThresholds, DamageLimitBendingMomentThresholds), DamageLimitBetaL1);

            // 低減後安全限界NMインタラクション。
            // 耐力の求め方 (バイリニア / e 関数) によらず、低減率と軸力制限は同じように課す。
            //
            // 以前は e 関数のとき低減後＝低減前としており、2 本が完全に重なるため
            // 画面では「低減後が描かれていない」ようにしか見えなかった。
            // 耐力の算定式と低減の扱いは別の話なので、算定式で低減の有無が変わらないようにする。
            FactoredUltimateNM = GetFactoredMNInteraction(
                UnfactoredUltimateNM,
                (UltimateLimitAxialForceThresholds, UltimateLimitBendingMomentThresholds),
                UltimateLimitBeta);

            // 低減前使用限界NQインタラクション
            UnfactoredServiceNQ = GetServiceLimitQNInteraction(3.0, false);

            // 低減前損傷限界NQインタラクション
            UnfactoredDamageNQ = GetDamageLimitQNInteraction(3.0, false);

            // 低減前安全限界NQインタラクション。帯筋は ctor で受け取った実際の値を使う。
            UnfactoredUltimateNQ = GetUltimateQNInteraction(3.0, HoopPw, HoopSigmaWy, false);

            // 低減前使用限界NQインタラクション
            FactoredServiceNQ = GetServiceLimitQNInteraction(3.0, true);

            // 低減前損傷限界NQインタラクション
            FactoredDamageNQ = GetDamageLimitQNInteraction(3.0, true);

            // 低減前安全限界NQインタラクション
            FactoredUltimateNQ = GetUltimateQNInteraction(3.0, HoopPw, HoopSigmaWy, true);


            // NOTE: ここで SteelYieldNM を重い計算で生成しない（ウィンドウ起動を阻害するため）
            // SteelYieldNM = GetSteelYieldMNInteraction();
            //

        }

        // ── せん断の共通量 ────────────────────────────────────────────────
        //
        // 円形断面を等価な矩形に置き換えて評価する。3 つの限界状態で同じ値を使う。
        // 以前は 3 か所に書き写されていた。

        /// <summary>等価な幅 b = πD/4。</summary>
        private double ShearB => Math.PI * PileDia / 4.0;

        /// <summary>有効せい d = 0.9D。</summary>
        private double ShearD => 0.9 * PileDia;

        /// <summary>応力中心距離 j = (7/8)d。</summary>
        private double ShearJ => 7.0 / 8.0 * ShearD;

        /// <summary>円形断面の断面形状係数 kc。使用限界と損傷限界の式に掛かる。</summary>
        private const double ShearKc = 0.72;

        // ── 低減係数 β1・β2 ──────────────────────────────────────────────
        //
        // β1 は材料強度や施工のばらつきを見込む係数、β2 は繰り返しを受けたあとの
        // 耐力低下を見込む係数。低減前 (isFactored = false) の曲線には掛けない。
        // 値は限界状態ごとに違い、場所打ち RC では次のとおり。
        //
        //   使用限界   β1 = 0.9、β2 なし
        //   損傷限界   β1 = 0.9、β2 = 0.75 (σ0 ≦ (1/3)·Gsi·Fc) / 0.65 (それ以外)
        //              L1 は β2 を掛けない
        //   安全限界   β1 = 0.8、β2 は損傷限界と同じ
        //
        // M-φ の折線終点は別の値 (0.95 / 0.80) を使う。同じ記号だが出所が違うので、
        // ここにまとめず各所に置いてある。

        /// <summary>繰り返しによる耐力低下を見込む係数 β2。軸応力度で 2 段。</summary>
        private double ShearBeta2(double sigma0)
            => sigma0 <= 1.0 / 3.0 * InsituConcrete.Gsi * InsituConcrete.Fc ? 0.75 : 0.65;

        /// <summary>
        /// 使用限界せん断力を返す。
        /// </summary>
        private double GetServiceLimitShear(double MonQd, double sigma0, bool isFactored)
        {
            double beta1 = 0.9;
            double kc = ShearKc;
            double b = ShearB;
            double j = ShearJ;
            if (ConcreteModelOptions.UseNotification1113Shear)
            {
                // 告示1113(第8): 使用限界=長期許容せん断応力度 fs。許容せん断力 Q = fs·b·j
                // （軸力・M/(Q·d) 非依存、低減係数を乗じないため 低減前/低減後 は同値）。
                return GetNotification1113LongTermShearStress() * b * j;
            }
            return (isFactored ? beta1 : 1.0) * 2.0 / 3.0 * 0.065 * kc * (49.0 + InsituConcrete.Gsi * InsituConcrete.Fc) / (MonQd + 1.7) * (1 + sigma0 / 14.7) * b * j;
        }

        /// <summary>
        /// 告示 平13国交告第1113号(第8) の場所打ちコンクリート長期許容せん断応力度 fs（N/mm²）。
        /// fs = min( Fc/40 ［区分2 は Fc/45］, (3/4)(0.49 + Fc/100) )。短期はこの 1.5 倍。
        /// </summary>
        private double GetNotification1113LongTermShearStress()
        {
            double Fc = InsituConcrete.Fc;
            double baseTerm = ConcreteModelOptions.Notification1113CompressionCase == 2 ? Fc / 45.0 : Fc / 40.0;
            double archTerm = 0.75 * (0.49 + Fc / 100.0);
            return Math.Min(baseTerm, archTerm);
        }

        /// <summary>
        /// 損傷限界せん断力を返す。
        /// </summary>
        private double GetDamageLimitShear(double MonQd, double sigma0, int level, bool isFactored)
        {
            double beta1 = 0.9;
            double beta2 = ShearBeta2(sigma0);
            // 地震動レベルは 1 か 2 のどちらか (画面のラジオボタン)。L1 は β2 を掛けない。
            // 以前は「L2 のとき β1·β2、それ以外も β1·β2」と同じ式を二度書いており、
            // 3 通りの場合分けがあるように読めた。
            double beta = level == 1 ? beta1 : beta1 * beta2;
            double kc = ShearKc;
            double b = ShearB;
            double j = ShearJ;
            if (ConcreteModelOptions.UseNotification1113Shear)
            {
                // 告示1113(第8): 損傷限界=短期許容せん断応力度 = 長期の 1.5 倍。Q = fs_短期·b·j
                // （レベル・軸力・M/(Q·d) 非依存、低減係数なし）。
                return 1.5 * GetNotification1113LongTermShearStress() * b * j;
            }
            return (isFactored ? beta : 1.0) * 0.065 * kc * (49.0 + InsituConcrete.Gsi * InsituConcrete.Fc) / (MonQd + 1.7) * (1 + sigma0 / 14.7) * b * j;
        }

        /// <summary>
        /// 安全限界せん断力を返す。
        /// </summary>
        private double GetUltimateLimitShear(double MonQd, double sigma0, double pt, double pw, double sigmaWy, bool isFactored)
        {
            double beta1 = 0.8;
            double beta2 = ShearBeta2(sigma0);
            double b = ShearB;
            double j = ShearJ;
            return (isFactored ? beta1 * beta2 : 1.0) * (0.053 * Math.Pow(pt, 0.23) * (18 + InsituConcrete.Gsi * InsituConcrete.Fc) / (MonQd + 0.12) + 0.85 * Math.Sqrt(pw * sigmaWy) + 0.1 * sigma0) * b * j;
        }

        // ── 高強度せん断補強筋の工法の式 ───────────────────────────────
        //
        // 工法を選んだときは、上の既定の式ではなく工法の指針の式を使う。
        // 置き換えは材料強度だけではない。断面の置き換え方から違う。
        //
        //                     既定 (基礎指針)        工法 (指針)
        //   等価な幅 b        π·D/4                 (B/2)·√π          ← 13% 違う
        //   有効せい d        0.9·D                 b − dt
        //   応力中心距離 j    (7/8)·d               (7/8)·d
        //   断面形状係数      kc = 0.72 を式に乗じる κ = 4/3 で Ac を割る
        //   軸応力度 σ0       N / Ae (換算断面積)    N / Ac (コンクリート全断面)
        //   コンクリート強度  ξ·Fc                  Fc (許容応力度は告示1113)
        //
        // dt は円形断面の引張縁から引張鉄筋重心までの距離で、主筋重心かぶり厚に等しい。

        /// <summary>工法の等価正方形断面の幅 b = (B/2)·√π (断面積が等しい正方形の一辺)。</summary>
        private double MethodB => PileDia / 2.0 * Math.Sqrt(Math.PI);

        /// <summary>引張縁から引張鉄筋重心までの距離 dt (＝主筋重心かぶり厚)。</summary>
        private double MethodDt => (PileDia - MainBarPCD) / 2.0;

        /// <summary>工法の有効せい d = D − dt (D = b)。</summary>
        private double MethodD => MethodB - MethodDt;

        /// <summary>工法の応力中心間距離 j = (7/8)·d。</summary>
        private double MethodJ => 7.0 / 8.0 * MethodD;

        /// <summary>円形断面の形状係数 κ = 4/3。</summary>
        private const double MethodKappa = 4.0 / 3.0;

        /// <summary>
        /// 工法の引張鉄筋比 pt (%)。
        ///
        /// 円形断面を断面積が等価な正方形断面に置換し、各辺の主筋本数を
        /// 「全主筋本数/4 + 1」として、<b>一辺に配置された本数だけ</b>を引張鉄筋とする
        /// (エムケーパイルリング785 指針 4章【解説】(2))。
        /// 既定の式が使う pt (全主筋の 1/4 を断面積で割ったもの) とは値が違う。
        /// </summary>
        private double MethodPt
        {
            get
            {
                double b = MethodB;
                double d = MethodD;
                if (!(b > 0) || !(d > 0) || MainBars.Number <= 0 || !(MainBars.Ag > 0)) return 0.0;
                double oneBarArea = MainBars.Ag / MainBars.Number;
                double at = (MainBars.Number / 4.0 + 1.0) * oneBarArea;
                return at * 100.0 / (b * d);
            }
        }

        /// <summary>
        /// 工法の使用限界せん断力 QAL = Lfs·Ac/κ。
        /// Lfs は告示1113(第8 第一号) の長期許容せん断応力度。軸力にも M/(Q·d) にも依らない。
        /// </summary>
        private double GetMethodServiceLimitShear()
            => GetNotification1113LongTermShearStress() * InsituConcrete.Ac / MethodKappa;

        /// <summary>
        /// 工法の損傷限界せん断力。工法と、選んだ算定式で違う。
        ///
        /// - エムケーパイルリング785 (3.2式): QAs = sfs·Ac/κ。<b>せん断補強筋は効かない</b>。
        /// - エムケーパイルリング785 (3.3式): QA = b·j·{ sfs + 0.5·wft·(pw − 0.001) }。
        ///   こちらを選んだときは<b>設計用せん断力を 1.5 倍に割り増す</b>のが指針の前提で、
        ///   割り増しは検定側 (<c>PileSection.ShearDesignMagnification</c>) が掛ける。
        /// - ウルボン (②式): 式の形は 3.3式 と同じ (RC規準 (15.6) 式の第2項を (pw−0.001) にしたもの)。
        /// </summary>
        private double GetMethodDamageLimitShear(ShearReinforcementMethodSpec spec, double pw)
        {
            double sfs = 1.5 * GetNotification1113LongTermShearStress();
            bool includesHoop = spec.DamageIncludesHoop
                || HoopDamageFormula == ShearReinforcementMethods.DamageFormulaSafetyShortTerm;
            if (!includesHoop)
                return sfs * InsituConcrete.Ac / MethodKappa;

            // pw は指針の頭打ちを掛ける。下限 (0.1%) を下回る配筋は式の適用外なので
            // 補強筋の項を 0 にして落とす (諸元表で適用範囲外として知らせる)。
            double pwUsed = Math.Min(pw, spec.DamagePwCap);
            double hoop = 0.5 * spec.ShortTermTensileStress * Math.Max(pwUsed - 0.001, 0.0);
            return MethodB * MethodJ * (sfs + hoop);
        }

        /// <summary>
        /// 主筋重心間距離 jt (等価正方形断面)。トラス・アーチ式で使う。
        /// 応力中心間距離 j = (7/8)d とは違う量なので取り違えないこと。
        /// </summary>
        private double MethodJt => MethodB - 2.0 * MethodDt;

        /// <summary>
        /// ウルボン ④式 (トラス・アーチ機構) による終局せん断強度。
        ///
        ///   Qsu2 = b·jt·pw·σwy + η·k1·(1−k2)·b·D·ν·Fc   ただし Qsu2 ≦ (ν·Fc/3)·b·jt
        ///
        /// <b>アーチ項 (第2項) は安全側に 0 として算定する。</b>
        /// k1 = {√((L/D)²+1) − (L/D)}/2 は部材長 L を要し、L は杭断面の入力に無い。
        /// k1 は L/D に対して単調に減少するので、L を長く見るほど小さくなる。
        /// L の読み方 (反曲点間距離か、区間長か) で値が変わり、短く見積もると危険側になるため、
        /// 上限の L (＝アーチ項 0) を採る。この形は ④式 の下限であり、
        /// どの読み方をしても指針の ④式 を上回らない。
        ///
        /// トラス項だけでも、せん断補強筋比が大きい範囲では ③式 を上回る
        /// (pw·σwy が √(pw·σwy) より速く伸びるため)。③式 と ④式 のどちらを使うかは
        /// 指針が設計者の選択としているので、プログラムが大きい方を勝手に採ることはしない。
        /// </summary>
        private double GetMethodTrussArchUltimateShear(ShearReinforcementMethodSpec spec, double pw)
        {
            double fc = InsituConcrete.Fc;
            double nu = 0.7 - fc / 200.0;                     // ν = 0.7 − Fc/200
            double pwUsed = Math.Min(pw, spec.UltimatePwCap); // 終局は 0.3% 上限
            double bjt = MethodB * MethodJt;
            if (!(bjt > 0) || !(nu > 0)) return 0.0;

            double truss = bjt * pwUsed * spec.UltimateSigmaWy;
            double cap = nu * fc / 3.0 * bjt;
            return Math.Min(truss, cap);
        }

        /// <summary>
        /// 工法の終局限界せん断力 (大野・荒川 min 式)。
        ///
        /// Qsu = β·{ η·0.053·pt^0.23·(Fc+18)/(M/(Q·d)+0.12) + c·√(pw·σwy) + 0.1·σ0 }·b·j
        ///
        /// 工法ごとに違うのは c (0.85 / 0.846)、σwy (785 / 1275)、pw の頭打ち
        /// (0.4% / 0.3%)、寸法効果の見方 (β=0.9 / η=B^(−1/4))、M/(Q·d) と σ0 の頭打ち。
        /// <paramref name="isFactored"/> が false のときは β を掛けない (低減前の曲線)。
        /// η は式の一部なので低減前でも掛ける。
        /// </summary>
        private double GetMethodUltimateShear(
            ShearReinforcementMethodSpec spec, double monQd, double n, double pw, bool isFactored)
        {
            // 水中・泥水打設 (告示1113 第8 第一号の区分(2)) では Fc を 0.9 倍する。
            double fc = InsituConcrete.Fc;
            double fcForShear = spec.ReduceUltimateFcForSlurry && ConcreteModelOptions.Notification1113CompressionCase == 2
                ? fc * 0.9
                : fc;

            double qd = Math.Clamp(monQd, spec.UltimateMonQdMin, spec.UltimateMonQdMax);
            double pwUsed = Math.Min(pw, spec.UltimatePwCap);

            // σ0 = N/Ac。引張側は式の適用外なので 0 で止める。上限は工法の規定による。
            double sigma0Cap = double.IsPositiveInfinity(spec.UltimateSigma0Cap)
                ? double.PositiveInfinity
                : spec.UltimateSigma0Cap * fc;
            double sigma0 = InsituConcrete.Ac > 0
                ? Math.Clamp(n / InsituConcrete.Ac, 0.0, sigma0Cap)
                : 0.0;

            double eta = spec.SizeEffectEta(PileDia);
            double beta = isFactored ? spec.UltimateBeta : 1.0;

            double term1 = eta * 0.053 * Math.Pow(MethodPt, 0.23) * (fcForShear + 18.0) / (qd + 0.12);
            double term2 = spec.UltimateHoopCoefficient * Math.Sqrt(pwUsed * spec.UltimateSigmaWy);
            double term3 = 0.1 * sigma0;
            return beta * (term1 + term2 + term3) * MethodB * MethodJ;
        }

        /// <summary>
        /// 工法の式で Q-N 曲線を作る。
        ///
        /// 軸力の掃引範囲は既定の式と揃えてある。限界状態ごとに横軸が変わると
        /// 検定側の補間がずれるため、範囲は変えずに式だけ差し替える。
        /// 工法の式には β2 (繰り返しによる低減) の段差が無いので、
        /// 既定の式が入れている閾値上の複製点も入れない。
        /// </summary>
        private (List<double>, List<double>) GetMethodQNInteraction(Func<double, double> shearAt, int iCount)
        {
            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                ns.Add(n);
                qs.Add(shearAt(n));
            }
            return (qs, ns);
        }

        /// <summary>
        /// 使用限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetServiceLimitQNInteraction(double MonQd, bool isFactored, int iCount = 100)
        {
            if (ShearMethod != null)
                return GetMethodQNInteraction(_ => GetMethodServiceLimitShear(), iCount);

            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                double q = GetServiceLimitShear(MonQd, n / Ae, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        /// <summary>
        /// 損傷限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetDamageLimitQNInteraction(double MonQd, bool isFactored, int level = 1, int iCount = 100)
        {
            if (ShearMethod != null)
                return GetMethodQNInteraction(_ => GetMethodDamageLimitShear(ShearMethod, HoopPw), iCount);

            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            // β2 は σ0=(1/3)ξFc で 0.75→0.65 に切り替わる (レベル2 のみ β2 を乗じる)。
            // 閾値をまたぐ区間では同一 N の 2 点 (切替前値・切替後値) を挿入し、
            // 低減後曲線の段差を斜めでなく垂直に描く (NM 曲線の複製点方式と同じ)。
            //
            // σ0 は閾値そのものを渡す。以前は N に直してから Ae で割り戻しており、
            // 6.75 が 6.749999999999999 になって 1 つ下に落ちていた。BitIncrement を
            // 足しても 6.75 に戻るだけで、2 点とも切替前 (β2=0.75) の値になっていた。
            double sigma0Threshold = 1.0 / 3.0 * InsituConcrete.Gsi * InsituConcrete.Fc;
            double nThreshold = sigma0Threshold * Ae;
            bool hasBetaStep = isFactored && level == 2;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                if (hasBetaStep && ns.Count > 0 && ns[^1] < nThreshold && n > nThreshold)
                {
                    ns.Add(nThreshold);
                    qs.Add(GetDamageLimitShear(MonQd, sigma0Threshold, level, isFactored));                    // σ0 ≤ 閾値側 (β2=0.75)
                    ns.Add(nThreshold);
                    qs.Add(GetDamageLimitShear(MonQd, Math.BitIncrement(sigma0Threshold), level, isFactored)); // σ0 > 閾値側 (β2=0.65)
                }
                double q = GetDamageLimitShear(MonQd, n / Ae, level, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }

        /// <summary>
        /// 安全限界QNを返す。
        /// </summary>
        public (List<double>, List<double>) GetUltimateQNInteraction(double MonQd, double pw, double sigmaWy, bool isFactored, int iCount = 100)
        {
            // 工法では σwy を指針が決めている (引数の sigmaWy は使わない)。
            // 終局用の σwy と短期許容応力度 wft は別の値なので、工法側を唯一の出所にする。
            if (ShearMethod != null)
            {
                // トラス・アーチ式は軸力の項を持たない (曲線は N によらず一定)。
                if (HoopUltimateFormula == ShearReinforcementMethods.UltimateFormulaTrussArch)
                    return GetMethodQNInteraction(_ => GetMethodTrussArchUltimateShear(ShearMethod, pw), iCount);

                return GetMethodQNInteraction(n => GetMethodUltimateShear(ShearMethod, MonQd, n, pw, isFactored), iCount);
            }

            List<double> ns = [];
            List<double> qs = [];
            double NMin = -0.05 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double NMax = 0.4 * InsituConcrete.Gsi * InsituConcrete.Fc * Ae;
            double pg = MainBarArea / InsituConcrete.Ac;
            double pt = 100 * pg / 4.0;
            // β2 は σ0=(1/3)ξFc で 0.75→0.65 に切り替わる。閾値をまたぐ区間では
            // 同一 N の 2 点 (切替前値・切替後値) を挿入し、低減後曲線の段差を
            // 斜めでなく垂直に描く (NM 曲線の複製点方式と同じ)。
            // σ0 は閾値そのものを渡す (損傷限界側と同じ理由。N に直して割り戻すと
            // 1 つ下に落ち、2 点とも切替前の値になる)。
            double sigma0Threshold = 1.0 / 3.0 * InsituConcrete.Gsi * InsituConcrete.Fc;
            double nThreshold = sigma0Threshold * Ae;
            for (int i = 0; i < iCount; i++)
            {
                double n = (NMin * (iCount - i) + NMax * i) / iCount;
                if (isFactored && ns.Count > 0 && ns[^1] < nThreshold && n > nThreshold)
                {
                    ns.Add(nThreshold);
                    qs.Add(GetUltimateLimitShear(MonQd, sigma0Threshold, pt, pw, sigmaWy, isFactored));                    // σ0 ≤ 閾値側 (β2=0.75)
                    ns.Add(nThreshold);
                    qs.Add(GetUltimateLimitShear(MonQd, Math.BitIncrement(sigma0Threshold), pt, pw, sigmaWy, isFactored)); // σ0 > 閾値側 (β2=0.65)
                }
                double q = GetUltimateLimitShear(MonQd, n / Ae, pt, pw, sigmaWy, isFactored);
                ns.Add(n);
                qs.Add(q);
            }
            return (qs, ns);
        }


        /// <summary>
        /// 主筋の降伏を終局側の圧縮縁ひずみで見たときの断面力と曲率。
        /// 中身は <see cref="AbstractPileSection.SteelYieldNMax(double, double)"/>。
        /// </summary>
        private (double N, double M, double epsilonC, double phi) GetSteelYieldNMax()
            => SteelYieldNMax(MainBars.RSigmaY / MainBars.Er, PileDia * 0.5 + MainBars.PCD * 0.5);

        internal (List<double> axialForces, List<double> bendingMoments, List<double> epsilonCs, List<double> curvatures)
        GetCrackMNInteraction(bool isLinear = false)
        {
            var axialForces = new List<double>();
            var bendingMoments = new List<double>();
            var epsilonCs = new List<double>();
            var curvatures = new List<double>();

            int div = DivisionNum > 0 ? DivisionNum : 60;

            // 走査する軸力範囲（表示に使っている閾値をそのまま採用）
            double NMin = UltimateLimitAxialForceThresholds[0];
            double NMax = UltimateLimitAxialForceThresholds[3];

            // 断面特性
            double lever = PileDia; // 圧縮縁-引張縁距離（直径）

            for (int i = 0; i <= div; i++)
            {
                double Ntarget = NMin + (NMax - NMin) * i / div;

                // 平均応力度（軸力による引張側応力度の増減をMcrへ反映）
                double sigma0e = Ntarget / Ae;

                // ひび割れ引張強度（Ft = 0.56*sqrt(ξ·Fc)）
                double FtLoc = Ft;

                double Mcr, phiCr, epsTcr;

                if (isLinear)
                {
                    // 線形法: εt,cr = Ft/Ec, Mcr = Ze*(Ft + σ0e), φcr = Mcr/(Ec*Ie)
                    epsTcr = FtLoc / InsituConcrete.Ec;
                    Mcr = Ze * (FtLoc + sigma0e);
                    if (Mcr < 0) { Mcr = 0; phiCr = 0; }
                    else { phiCr = Mcr / (InsituConcrete.Ec * Ie); }
                }
                else
                {
                    // 1) εt,cr は、積分に使う構成則で σ=-FtLoc となるひずみ度
                    epsTcr = InsituConcrete.GetCrackTensileStrain(ActiveUltimateConcreteLaw);
                    (Mcr, phiCr) = GetCrackMoment(Ntarget, isLinear);
                }

                // 参考: その時の圧縮縁ひずみ（εt = -epsTcr を満たすように εc = -εt + φ·lever）
                double epsC = -epsTcr + phiCr * lever;

                axialForces.Add(Ntarget);
                bendingMoments.Add(Mcr);
                epsilonCs.Add(epsC);
                curvatures.Add(phiCr);
            }

            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }

        /// <summary>
        /// 主筋が引張降伏する状態の N-M 相関。中身は
        /// <see cref="AbstractPileSection.SolveSteelYieldMNInteraction(double, double)"/>。
        /// </summary>
        internal (List<double> axialForces, List<double> bendingMoments, List<double> epsilonCs, List<double> curvatures)
            GetSteelYieldMNInteraction()
            => SolveSteelYieldMNInteraction(MainBars.RSigmaY / MainBars.Er, PileDia * 0.5 + MainBars.PCD * 0.5);

        // Ze, Ft, Ieのセット
        internal void SetZeFtIe()
        {
            double Ro = PileDia / 2.0;
            double I = Math.PI * Math.Pow(Ro, 4) / 4.0;
            double n = MainBars.Er / InsituConcrete.Ec;
            Ae = InsituConcrete.Ac + (n - 1) * MainBars.Ag;
            Ie = I + 1.0 / 2.0 * (n - 1) * MainBars.Ag * Math.Pow(MainBars.PCD / 2.0, 2);
            Ze = Ie / Ro;
            Ft = 0.56 * Math.Sqrt(InsituConcrete.Gsi * InsituConcrete.Fc);

        }

        // ひび割れモーメント、ひび割れ曲率を返すメソッド
        internal (double, double) GetCrackMoment(double Ntarget, bool isLinear)
        {
            // 安全ガード
            if (Ae <= 0) return (0, 0);

            // 平均応力度が -Ft 未満（強い引張）のときは Mcr=0
            if (Ntarget / Ae < -Ft) return (0, 0);

            if (isLinear)
            {
                double sigma0e = Ntarget / Ae; // N/mm2
                double Mcr = Ze * (Ft + sigma0e); // Nmm
                if (Mcr < 0) { return (0, 0); }
                double phiCr = Mcr / InsituConcrete.Ec / Ie;
                return (Mcr, phiCr);
            }
            else
            {
                // 引張縁のひずみ閾値は、積分に使う構成則で σ=-Ft となるひずみ度。
                // ここを取り違えると、ひび割れていない状態を「ひび割れ」と呼ぶ。
                double epsTcr = InsituConcrete.GetCrackTensileStrain(ActiveUltimateConcreteLaw); // >0（引張ひずみの絶対値）
                if (epsTcr <= 0) return (0, 0);
                double lever = PileDia;

                // 初期値
                double phi0 = Math.Max(1e-7, epsTcr / lever * 0.5);
                double phiMin = Math.Max(1e-8, phi0 * 0.1);
                double phiMaxFromCrack = (Math.Max(InsituConcrete.EpsilonCu, 0.003) + epsTcr) / lever;
                // フォールバック（万一）
                double phiMax = double.IsFinite(phiMaxFromCrack) && phiMaxFromCrack > phiMin * 1.2
                    ? phiMaxFromCrack
                    : phiMin * 100.0;

                // 収束条件
                const int maxIter = 60;
                double tolN = Math.Max(PileDesign.Constants.SectionSolverTolerances.CRACK_AXIAL_ABS_N,
                                       PileDesign.Constants.SectionSolverTolerances.CRACK_AXIAL_REL * Math.Abs(Ntarget)); // N
                const double tolPhiRel = 1e-6;

                double phi = phi0;
                for (int iter = 0; iter < maxIter; iter++)
                {
                    // ひび割れ条件: εt = -epsTcr → εc = -εt + φ*lever = -epsTcr + φ*lever
                    double epsC = -epsTcr + phi * lever;

                    (double N, double M) = GetUltimateForceAndMoment(epsC, phi);
                    double f = N - Ntarget;
                    if (Math.Abs(f) < tolN)
                        return (M, phi);

                    // 数値微分
                    double dPhi = Math.Max(phi * 1e-4, 1e-10);
                    double epsC2 = -epsTcr + (phi + dPhi) * lever;
                    (double N2, _) = GetUltimateForceAndMoment(epsC2, phi + dPhi);
                    double dNdPhi = (N2 - N) / dPhi;

                    if (Math.Abs(dNdPhi) < 1e-12) break; // 勾配消失

                    // Newton ステップ
                    double step = f / dNdPhi;
                    // ステップ制限（暴走抑止）
                    double limit = Math.Max(phi * 0.5, (phiMax - phiMin) * 0.1);
                    if (Math.Abs(step) > limit) step = Math.Sign(step) * limit;

                    double phiNext = Math.Clamp(phi - step, phiMin, phiMax);

                    if (Math.Abs(phiNext - phi) / (Math.Abs(phi) + 1e-12) < tolPhiRel)
                        return (M, phiNext);

                    phi = phiNext;
                }

                // 不収束：端点評価で返す
                double epsCedge = -epsTcr + phi * lever;
                (_, double Medge) = GetUltimateForceAndMoment(epsCedge, phi);
                return (Medge, phi);
            }
        }

        /// <summary>
        /// 降伏点 (My, φy) を 2 変数ニュートン法で求める。解法は
        /// <see cref="AbstractPileSection.SolveYieldPoint2DNewton(double, double, double)"/> にある
        /// (3 つの断面で同じ)。ここで渡すのは、この断面での引張降伏ひずみと
        /// 圧縮縁から主筋重心までの距離。
        /// </summary>
        private (bool hasYield, double My, double phiY) SolveYieldPoint2DNewton(double Ntarget)
            => SolveYieldPoint2DNewton(Ntarget,
                MainBars.RSigmaY / MainBars.Er,   // 引張降伏ひずみ (絶対値)
                PileDia * 0.5 + MainBarPCD * 0.5);  // 圧縮縁から主筋重心までの距離

        /// <summary>
        /// 2変数ニュートン法を優先的に使って降伏点を取得し、失敗時は既存手法にフォールバックするラッパ。
        /// </summary>
        internal (bool hasYield, double My, double phiY) GetSteelYieldPoint(double Ntarget)
        {
            var r = SolveYieldPoint2DNewton(Ntarget);
            if (r.hasYield) return r;

            // フォールバック（既存1変数近似）
            var (MyApprox, phiApprox) = GetSteelYieldMoment(Ntarget);
            if (phiApprox > 0 && MyApprox > 0)
                return (true, MyApprox, phiApprox);

            return (false, 0, 0);
        }

        /// <summary>
        /// 主筋が引張降伏する点 (M, φ)。解法は
        /// <see cref="AbstractPileSection.SolveSteelYieldMoment(double, double, double)"/>。
        /// </summary>
        internal (double M, double curvature) GetSteelYieldMoment(double Ntarget)
            => SolveSteelYieldMoment(Ntarget, MainBars.RSigmaY / MainBars.Er, PileDia * 0.5 + MainBars.PCD * 0.5);

        // C点を返すメソッド
        internal static double GetPhiC(double phiCr, double Mcr, double phiY, double My, double Mu0, double beta1)
        {
            double a1 = phiY - phiCr;
            double a2 = beta1 * Mu0 - Mcr;
            double a3 = (My - Mcr);
            // My≈Mcr（低鉄筋比・高軸力）でのゼロ除算→Inf を防ぐ。分母が微小なら降伏点曲率で代替。
            if (Math.Abs(a3) < 1e-9) return phiY;
            double phiC = phiCr + (phiY - phiCr) * (beta1 * Mu0 - Mcr) / (My - Mcr);
            return phiC;
        }

        // ある軸力時のM-φ関係を得るメソッド（IPileSectionCalculationインターフェース実装）
        // 解析用のため、e関数オプション時でも全区間（MCr・MY・Mu0）をバイリニアで算定し、
        // M-φ が単調（β1·Mu0 ≥ MY）＝正勾配ばねとなることを保証する（FEM 収束のため）。
        public override (List<double> Phis, List<double> Moments) GetMPhiRelationship(double Ntarget)
        {
            bool prevForceBilinear = _forceBilinearUltimate;
            _forceBilinearUltimate = true;
            try
            {
                (double MCr, double phiCr) = GetCrackMoment(Ntarget, false);
                (double MY, double phiY) = GetSteelYieldMoment(Ntarget);
                (double Mu0, double _) = GetUltimateMomentForSpecificN(Ntarget);

                double ag = Math.PI * PileDia * PileDia / 4.0;
                bool lowAxial = Ntarget / ag <= (1.0 / 3.0) * InsituConcrete.Gsi * InsituConcrete.Fc;
                double beta1 = lowAxial ? 0.95 : 0.80;
                double beta2 = lowAxial ? 1.0 : 0.65;
                double mEnd = beta1 * beta2 * Mu0;   // 折線の終点 (低減後の安全限界)
                bool hasCrack = MCr > 0 && phiCr > 0;  // 引張軸力でひび割れが定義できないときは点を持たない

                // 折線は FEM に単調化なしで渡り、途中の負勾配はそのまま負の接線剛性になる。
                // 以下の 2 つの分岐は、指針の折線式がそのままでは折り返す状況の扱い:
                //  - ひび割れが終点以上 (高軸力側で起きる。Mcr は N とともに増え、Mu0 は減る):
                //    断面は終点までひび割れないので、弾性線 Ec·Ie で終点まで
                //  - 降伏点が終点以上: 降伏点を省き、ひび割れ後勾配の延長で終点へ
                if (hasCrack && MCr >= mEnd)
                    return ([0.0, phiCr * (mEnd / MCr)], [0.0, mEnd]);

                double phiC = GetPhiC(phiCr, MCr, phiY, MY, Mu0, beta1);   // β1·Mu0 に達する曲率 (ひび割れ後勾配の延長)
                List<double> phis;
                List<double> Ms;

                if (lowAxial)
                {
                    bool hasYield = beta1 * Mu0 > MY && phiY > phiCr;
                    phis = [0.0]; Ms = [0.0];
                    if (hasCrack) { phis.Add(phiCr); Ms.Add(MCr); }
                    if (hasYield) { phis.Add(phiY); Ms.Add(MY); }
                    phis.Add(phiC); Ms.Add(mEnd);
                }
                else
                {
                    double phiCshort = phiCr + (phiC - phiCr) * (mEnd - MCr) / (beta1 * Mu0 - MCr);
                    phis = [0.0]; Ms = [0.0];
                    if (hasCrack) { phis.Add(phiCr); Ms.Add(MCr); }
                    phis.Add(phiCshort); Ms.Add(mEnd);
                }

                return (phis, Ms);
            }
            finally { _forceBilinearUltimate = prevForceBilinear; }
        }

        // ファイバーモデル M-φ（GetMPhiRelationshipFiber）は AbstractPileSection の共通実装を使用。
        // 掃引終点は既定（安全限界ソルバ、εc=0.003）、材料はガードにより常にバイリニア。

        //// ある軸力時のM-θ関係を得るメソッド
        /// 4点折線: [0, 極小値, θy, θu]
        /// 極小値(1e-8)により初期勾配 Mcr/1e-8 ≈ 実質剛体
        internal (List<double>, List<double>) GetMThetaRelationship(double Ntarget, double alpha = 32)
        {
            bool prevForceBilinear = _forceBilinearUltimate;
            _forceBilinearUltimate = true;   // 解析用のため常にバイリニア
            try
            {
            double beta1 = 0.95;
            (double MCr, double _) = GetCrackMoment(Ntarget, false);
            (double MY, double phiY) = GetSteelYieldMoment(Ntarget);
            (double Mu0, double _2) = GetUltimateMomentForSpecificN(Ntarget);

            // θy = 0.5 * α * D_bar * φy
            double D_bar = ExtractBarSizeNumber(MainBars.BarSize);
            double thetaY = 0.5 * alpha * D_bar * phiY;

            // θu = 1/100 rad（固定）
            double thetaU = 1.0 / 100.0;

            // θ[1] は初期勾配を十分大きくするための固定微小値
            double thetaSmall = 1e-8;

            // 安全ガード
            if (thetaY <= thetaSmall) thetaY = thetaSmall * 1.5;
            if (thetaU <= thetaY) thetaU = thetaY * 2.0;

            List<double> thetas = [0.0, thetaSmall, thetaY, thetaU];
            List<double> Ms = [0.0, MCr, MY, beta1 * Mu0];

            return (thetas, Ms);
            }
            finally { _forceBilinearUltimate = prevForceBilinear; }
        }

        public static double ExtractBarSizeNumber(string barSize)
        {
            if (string.IsNullOrEmpty(barSize)) return 0;
            var match = System.Text.RegularExpressions.Regex.Match(barSize, @"\d+(\.\d+)?");
            if (match.Success && PileDesign.Common.NumericText.TryParse(match.Value, out double value))
                return value;
            return 0;
        }

        // 最外縁の杭主筋が引張降伏するときのN、Mを返すメソッド
        internal (double, double) GetYieldForceAndMoment(double curvature)
        {
            double epsilonC = -MainBars.RSigmaY / MainBars.Er + curvature * (PileDia * 0.5 + MainBars.PCD * 0.5);
            // 最外縁の杭主筋が引張降伏
            double N, M;
            (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
            return (N, M);
        }

        /// <summary>
        /// 純引張時のひずみ度: 鉄筋の引張降伏ひずみ
        /// </summary>
        internal override double GetPureTensionStrain()
            => -MainBars.RSigmaY / MainBars.Er;

        // 軸力、曲げモーメント取得メソッド
        internal override (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature)
        {
            double epsilonC = GetAllowableCompressionEdgeStrain(limitStateNo, isCompressionSide, curvature);
            double epsilon0 = epsilonC - PileDia * 0.5 * curvature;
            MaterialLaw type = MaterialLaw.Linear;
            double N, M;
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(type, MainBars, epsilon0, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);

            N = result1.Item1 + result2.Item1 - result3.Item1;
            M = result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M, epsilonC);
        }

        // 解析用 M-φ/M-θ 端点算定・ファイバー掃引用のバイリニア固定ガード _forceBilinearUltimate は
        // AbstractPileSection の protected フィールドを使用。

        // 軸力、安全限界曲げモーメント取得メソッド
        internal override (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature)
        {
            double epsilon0 = epsilonC - PileDia * 0.5 * curvature;
            // 指針(案) 5.4.1 準拠オプション時はコンクリートを e関数法、既定はバイリニア。
            // ただし解析 M-φ 端点算定中（_forceBilinearUltimate）は収束のため常にバイリニア。
            MaterialLaw type = (ConcreteModelOptions.UseInsituUltimateEFunction && !_forceBilinearUltimate)
                ? MaterialLaw.EFunction : MaterialLaw.Bilinear;
            double N, M;
            var result1 = CircularSolidSectionConcrete.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);
            var result2 = CircularPipeSectionMainbars.GetForceAndMoment(type, MainBars, epsilon0, curvature);
            var result3 = CircularPipeSectionMainbars.GetForceAndMoment(type, InsituConcrete, epsilon0, curvature);

            N = result1.Item1 + result2.Item1 - result3.Item1;
            M = result1.Item2 + result2.Item2 - result3.Item2;
            return (N, M);
        }

        /// <summary>
        /// 指定した圧縮縁ひずみ εc と曲率 φ に対する断面のひずみ度・応力度分布を返す。
        /// コンクリート(実心)＋主筋(リング)。ultimate=true でバイリニア、false で線形。
        /// </summary>
        internal override SectionStrainStressProfile GetStrainStressProfile(
            double epsilonC, double curvature, bool ultimate, int division = 200)
        {
            double r = PileDia * 0.5;
            double epsilon0 = epsilonC - r * curvature;
            MaterialLaw type = ultimate ? MaterialLaw.Bilinear : MaterialLaw.Linear;

            var profile = new SectionStrainStressProfile { Radius = r };
            profile.Materials.Add(BuildSolidProfile(SectionMaterialKind.Concrete, "コンクリート",
                InsituConcrete, type, epsilon0, curvature, r, 0.0, 0.0, division));
            profile.Materials.Add(BuildRingProfile(SectionMaterialKind.MainBar, "主筋",
                MainBars, type, epsilon0, curvature, MainBarPCD * 0.5, 0.0, division));

            profile.CompressionEdgeStrain = epsilon0 + curvature * r;
            profile.TensionEdgeStrain = epsilon0 - curvature * r;
            return profile;
        }

        // N-M グラフは「ひび割れ開始」「引張鉄筋降伏開始」も描くため、その曲線も対象に加える。
        internal override IEnumerable<(string Name, (List<double> N, List<double> M, List<double> Eps, List<double> Phi) Curve, bool Ultimate)> GetProfileSourceCurves()
        {
            foreach (var c in base.GetProfileSourceCurves())
                yield return c;
            yield return ("ひび割れ開始", GetCrackMNInteraction(), false);
            yield return ("引張鉄筋降伏開始", GetSteelYieldMNInteraction(), true);
        }
    }


}
