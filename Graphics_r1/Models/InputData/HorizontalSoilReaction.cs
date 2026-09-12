using System;

namespace PileDesign.Models.InputData
{
    public class HorizontalSoilReactionItem
    {
        public string SoilType { get; set; }
        public double B { get; set; }
        public double ZTop { get; set; }
        public double ZBtm { get; set; }
        public double Xi { get; set; }
        public double ROnB { get; set; }
        public double Phi { get; set; }
        public double NValue { get; set; }
        public double Cu { get; set; }

        public string Name { get; set; }
        public double SigmaZPrimeTop { get; set; }
        public double SigmaZPrimeBtm { get; set; }

        public double PyFrontTop { get; set; } // 塑性地盤反力
        public double PyFrontBtm { get; set; } // 塑性地盤反力

        public double PyRearTop { get; set; } // 塑性地盤反力
        public double PyRearBtm { get; set; } // 塑性地盤反力

        public double E0 { get; set; }
        public double Kh0 { get; set; } // 基準水平地盤反力係数（自動計算値、または手入力オーバーライド値）
        public bool IsKh0Manual { get; set; } // Kh0 が手入力オーバーライドか（表示・判定用）
        public double Gamma { get; set; }


        // コンストラクタ
        public HorizontalSoilReactionItem()
        { }

        /// <summary>
        /// 基準水平地盤反力係数 kh0 = α ξ E0 (B/B0)^(-3/4) の α [1/m] (基礎指針'19 (6.6.12))。
        /// 初版から 60 になっていたが、文献・ヘルプ・計算書は 80。2026-09-11 に文献値へ直した
        /// (水平解析の地盤ばねがすべて 4/3 倍になる。Kh0AlphaTests)。
        /// </summary>
        internal const double Kh0Alpha = 80;

        // パラメータセット
        public void SetParameters(
            string name, string soilType, double gamma, double b, double e0,
            double zTop, double zBtm,
            double xi, double rOnB, double nValue, double phi, double cu,
            double sigmaZPrimeTop, double sigmaZPrimeBtm, double alpha = Kh0Alpha)
        {
            Name = name;
            SoilType = soilType;
            Gamma = gamma;
            B = b;
            E0 = e0;
            ZTop = zTop;
            ZBtm = zBtm;
            Xi = xi;
            ROnB = rOnB;
            NValue = nValue;
            Phi = phi;
            Cu = cu;
            SigmaZPrimeTop = sigmaZPrimeTop;
            SigmaZPrimeBtm = sigmaZPrimeBtm;

            Kh0 = GetKh0(alpha, xi, e0, b);
            PyFrontTop = GetPy(soilType, true, b, zTop, rOnB, phi, cu, sigmaZPrimeTop);
            PyFrontBtm = GetPy(soilType, true, b, zBtm, rOnB, phi, cu, sigmaZPrimeBtm);
            PyRearTop = GetPy(soilType, false, b, zTop, rOnB, phi, cu, sigmaZPrimeTop);
            PyRearBtm = GetPy(soilType, false, b, zBtm, rOnB, phi, cu, sigmaZPrimeBtm);
        }

        // DeepCopy メソッドの追加
        public HorizontalSoilReactionItem DeepCopy()
        {
            return new HorizontalSoilReactionItem()
            {
                Name = this.Name,
                SoilType = this.SoilType,
                Gamma = this.Gamma,
                B = this.B,
                E0 = this.E0,
                ZTop = this.ZTop,
                ZBtm = this.ZBtm,
                Xi = this.Xi,
                ROnB = this.ROnB,
                NValue = this.NValue,
                Phi = this.Phi,
                Cu = this.Cu,
                SigmaZPrimeTop = this.SigmaZPrimeTop,
                SigmaZPrimeBtm = this.SigmaZPrimeBtm,
                PyFrontTop = this.PyFrontTop,
                PyFrontBtm = this.PyFrontBtm,
                PyRearTop = this.PyRearTop,
                PyRearBtm = this.PyRearBtm,
                Kh0 = this.Kh0,
                IsKh0Manual = this.IsKh0Manual
            };
        }

        // 各メソッドの py 引数 / mode 引数について:
        //   mode = Linear           → py も kh 低減も使わない (kh = kh0 固定)
        //   mode = KhReduction      → kh 低減のみ。py は無視 (頭打ちなし)
        //   mode = KhReductionWithPy→ kh 低減 + py 頭打ち (従来の非線形)
        //
        // effect (群杭の影響) について:
        //   ここに保存されている Xi / ROnB ではなく、<b>評価時に渡された値</b>を使う。
        //   理由は GroupPileEffect の説明を参照。保存側 (SetParameters が計算する Kh0 / Py*) は
        //   「群杭の影響なし」の基準値で、杭を特定できない画面 (杭要素分割ウィンドウ等) の表示に使う。

        /// <summary>
        /// この要素の塑性水平地盤反力度 py (kN/m²)。杭間隔比 R/B は評価時に渡されたものを使う。
        /// 保存されている <c>Py*</c> は R/B 未設定 (群杭の影響なし) の基準値。
        /// </summary>
        private double PyAt(bool isTop, bool isFront, double rOnB)
            => GetPy(SoilType, isFront, B, isTop ? ZTop : ZBtm, rOnB, Phi, Cu,
                isTop ? SigmaZPrimeTop : SigmaZPrimeBtm);

        // 反力を返すメソッド (kN)
        public double GetSoilReaction(double y, bool isTop, bool isFront,
            in GroupPileEffect effect,
            SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            double py = PyAt(isTop, isFront, effect.ROnB);
            return GetP(y, py, mode, Kh0 * effect.Xi) * B * (ZTop - ZBtm) * 0.5;
        }

        //  接線剛性を返すメソッド (kN/m)
        public double GetSoilTangentReactionCoefficient(double y, bool isTop, bool isFront,
            in GroupPileEffect effect,
            SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            double py = PyAt(isTop, isFront, effect.ROnB);
            return GetkhTan(Kh0 * effect.Xi, y, py, mode) * B * (ZTop - ZBtm) * 0.5;
        }

        public double GetSoilSecantReactionCoefficient(double y, bool isTop, bool isFront,
            in GroupPileEffect effect,
            SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            double py = PyAt(isTop, isFront, effect.ROnB);
            return GetKh(Kh0 * effect.Xi, y, py, mode) * B * (ZTop - ZBtm) * 0.5;
        }

        /// <summary>
        /// v28 問題 A 診断: この変位 y が降伏状態かどうかを返す (p-y 曲線の弾性 sqrt 領域を越えたか)。
        /// 降伏判定式: kh0 × √y0 / √|y| × |y| ≥ py   (弾性 sqrt 領域で p が py に到達)
        /// = kh0 × √(y0 × |y|) ≥ py
        /// = |y| ≥ (py / kh0)² / y0  (= yy)
        /// py 頭打ちを行わないモード (Linear / KhReduction) では常に false。
        /// </summary>
        public bool IsYieldedAtY(double y, bool isTop, bool isFront,
            in GroupPileEffect effect,
            SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            if (mode != SoilNonlinearityMode.KhReductionWithPy) return false;
            double py = PyAt(isTop, isFront, effect.ROnB);
            double kh0 = Kh0 * effect.Xi;
            if (py <= 0 || kh0 <= 0) return false;
            double yy = GetYieldDisplacement(kh0, py);
            return Math.Abs(y) >= yy;
        }

        /// <summary>基準変位 y0 = 1cm。kh0 は「y = y0 における水平地盤反力係数」として定義される。</summary>
        public const double Y0 = 0.01; // m

        /// <summary>弾性域 (|y| ≤ 0.1·y0 = 1mm) の kh 倍率。= (0.1)^(-1/2) を丸めた基準指針の値。</summary>
        private const double ElasticKhFactor = 3.16;

        // ── 降伏後接線剛性の比率 ─────────────────────────────────────────────
        // 「p は py で頭打ち」が本来の意図だが、K_tan を厳密に 0 にすると降伏したばねが
        // K 行列に何も寄与せず特異化しうるため、微小な正勾配を残している。
        //
        // 経緯:
        //   v22 以前: 0.002 (= 降伏境界接線の 1/500)。降伏 ↔ 弾性を往復するばねがあると
        //             K_tan が毎反復 500× 変動し、ラインサーチが α=0.5 に張り付いて収束停滞。
        //   v22     : 0.02 (2%) へ引上げて安定化。ただし |y| = 2·yy で p が py を 1% 超過。
        //   現行    : 0.002 (0.2%) へ戻し、代わりに降伏境界での K_tan 不連続を
        //             YieldTangentBlendEnd の smoothstep で解消して v22 の停滞要因を除去。
        //             p の py 超過量は Δp/py = ratio × (|y|/yy − 1)/2 なので、
        //             |y| = 2·yy で 0.1%、|y| = 10·yy でも 0.9% に収まる。
        private const double PostYieldTangentRatio = 0.002;

        // 降伏境界 |y| = yy における K_tan の落差 (1 → PostYieldTangentRatio, 500×) を
        // smoothstep でならす区間の終端 (|y|/yy)。
        // 区間内では K_tan が d(K_sec×|y|)/d|y| より「硬め」にずれるが、Newton 方向が
        // 過小ステップ側 (安全側) に振れるだけで、内力 F_int = K_sec × |y| の厳密性は保たれる。
        // これは弾性↔sqrt 境界の ElasticSqrtBlend* (v23 A-2) と同じ考え方。
        private const double YieldTangentBlendEnd = 1.5; // |y|/yy

        // 降伏境界 |y|=yy における解析接線（pre-yield 式の d(p)/d|y|）
        // p_pre = kh0 × √(y0 × |y|), dp/d|y| = kh0 × √y0 / (2 × √|y|)
        // |y|=yy のとき √yy = py/(kh0×√y0) なので、
        // dp/d|y|(yy) = kh0 × √y0 × kh0 × √y0 / (2 × py) = kh0² × y0 / (2 × py)
        private static double YieldBoundaryTangent(double kh0, double py)
            => kh0 * kh0 * Y0 / (2.0 * py);

        /// <summary>降伏変位 yy: sqrt 域で p が py に達する変位。yy = (py/kh0)² / y0</summary>
        private static double GetYieldDisplacement(double kh0, double py)
            => Math.Pow(py / kh0, 2) / Y0;

        // 水平地盤反力係数khを返すメソッド (kN/m3) — 割線剛性 (p = kh × |y|)
        //
        // py ≤ 0 (砂質土で有効上載圧 σz' = 0 となる地表付近など) は全モード共通で反力なしとする。
        // 「その深さには水平抵抗が存在しない」というモデル上の判定であり、モードを変えても
        // 抵抗が現れないようにするため Linear / KhReduction でも同じ扱いにしている。
        private static double GetKh(double kh0, double y, double py, SoilNonlinearityMode mode)
        {
            if (py <= 0) return 0;
            if (mode == SoilNonlinearityMode.Linear) return kh0;

            double absY = Math.Abs(y);
            if (absY / Y0 <= ElasticSqrtBlendStart)
            {
                return ElasticKhFactor * kh0;
            }

            double khSqrt = kh0 / Math.Sqrt(absY / Y0);
            if (mode == SoilNonlinearityMode.KhReduction) return khSqrt;

            if (khSqrt * absY < py)
            {
                return khSqrt;
            }
            else
            {
                double yy = GetYieldDisplacement(kh0, py);
                double gradient = PostYieldTangentRatio * YieldBoundaryTangent(kh0, py);
                double p = gradient * (absY - yy) + py;
                return p / absY;
            }
        }

        // v23 (A-2) 弾性・sqrt 領域境界のスムージング幅
        // |y|/y0 = 0.1 (= 1mm) 付近で弾性 (3.16×kh0) → sqrt (1.58×kh0) へ 2× ジャンプが発生。
        // この狭い区間で接線を Hermite ブレンドして不連続を解消する。
        // ブレンド区間内では K_tan が d(K_sec × |y|)/d|y| と僅かにずれるが、
        // 幅が 0.1y0 と狭く影響は限定的。多数の節点が同時に境界を跨ぐチャタリングを防ぐ。
        private const double ElasticSqrtBlendStart = 0.10; // |y|/y0
        private const double ElasticSqrtBlendEnd = 0.20;   // |y|/y0

        // 水平地盤反力の接線剛性を返すメソッド (kN/m3)
        public static double GetkhTan(double kh0, double y, double py,
            SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            if (py <= 0) return 0;
            if (mode == SoilNonlinearityMode.Linear) return kh0;

            double absY = Math.Abs(y);
            double yRatio = absY / Y0;

            if (yRatio <= ElasticSqrtBlendStart)
            {
                return ElasticKhFactor * kh0;
            }

            // sqrt 領域の解析接線
            double sqrtTangent = Math.Sqrt(Y0) / 2.0 * kh0 / Math.Sqrt(absY);

            // v23 (A-2) 境界ブレンド: |y|/y0 ∈ [0.10, 0.20] で弾性 → sqrt に滑らかに遷移
            if (yRatio < ElasticSqrtBlendEnd)
            {
                double t = (yRatio - ElasticSqrtBlendStart) / (ElasticSqrtBlendEnd - ElasticSqrtBlendStart);
                // smoothstep: 3t² − 2t³ (両端で微分係数ゼロ、C¹ 連続)
                double s = t * t * (3.0 - 2.0 * t);
                double elasticTangent = ElasticKhFactor * kh0;
                return (1.0 - s) * elasticTangent + s * sqrtTangent;
            }

            // py 頭打ちを行わないモードは sqrt 領域の接線をそのまま使う
            if (mode == SoilNonlinearityMode.KhReduction) return sqrtTangent;

            // 通常 sqrt 領域（降伏未到達）
            if (kh0 / Math.Sqrt(yRatio) * absY < py)
            {
                return sqrtTangent;
            }

            // 降伏後は降伏境界接線の PostYieldTangentRatio 倍。
            // ただし降伏直後 (|y|/yy ∈ [1, YieldTangentBlendEnd]) は smoothstep で
            // 落差をならし、降伏境界を跨ぐばねの K_tan チャタリングを防ぐ。
            double yieldTangent = YieldBoundaryTangent(kh0, py);
            double postYieldTangent = PostYieldTangentRatio * yieldTangent;
            double yyTan = GetYieldDisplacement(kh0, py);
            if (absY < YieldTangentBlendEnd * yyTan)
            {
                double t = (absY / yyTan - 1.0) / (YieldTangentBlendEnd - 1.0);
                double s = t * t * (3.0 - 2.0 * t);
                return (1.0 - s) * yieldTangent + s * postYieldTangent;
            }
            return postYieldTangent;
        }

        // 反力pを返すメソッド (kN/m2)
        //
        // 表示 (グラフ / MGT エクスポート / 杭要素分割ダイアログ) と FEM 本体が
        // 厳密に同じ曲線になるよう、GetKh をそのまま用いる。
        // 旧実装は `sign(y) × min(Kh(|y|)×|y|, py)` とハードクランプしていたため、
        // 降伏後に表示 (完全に平坦) と FEM (PostYieldTangentRatio の微小勾配) が食い違っていた。
        // 現在は両者とも「py + gradient×(|y|−yy)」で一致する。
        /// <param name="kh0">
        /// 群杭係数 ξ を掛けた kh0。<b>0 を渡すとこの要素の基準値 <c>Kh0</c> (ξ 未適用) を使う。</b>
        /// 杭を特定できる呼び出し側 (解析・グラフ) は必ず ξ 適用後の値を渡すこと。
        /// </param>
        public double GetP(double y, double py, SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy,
            double kh0 = 0.0)
        {
            if (y == 0 || py <= 0) return 0;
            double sign = y > 0 ? 1.0 : -1.0;
            double absY = Math.Abs(y);
            return sign * GetKh(kh0 > 0.0 ? kh0 : Kh0, absY, py, mode) * absY;
        }

        /// <summary>
        /// この杭 (群杭の影響 <paramref name="effect"/>) における塑性水平地盤反力度 py (kN/m²)。
        /// グラフ・表示が解析と同じ値を出すための公開口。
        /// </summary>
        public double GetPyFor(bool isTop, bool isFront, in GroupPileEffect effect)
            => PyAt(isTop, isFront, effect.ROnB);

        /// <summary>この杭における基準水平地盤反力係数 kh0 (kN/m³)。手入力の上書きにも ξ が掛かる。</summary>
        public double GetKh0For(in GroupPileEffect effect) => Kh0 * effect.Xi;

        // 基準水平地盤反力係数kh0を返すメソッド (kN/m3)
        private static double GetKh0(double alpha, double xi, double e0, double b)
        {
            double b0 = 0.01;
            return alpha * xi * e0 * Math.Pow(b / b0, -3.0 / 4.0);
        }

        // 塑性地盤反力pyを返すメソッド (kN/m2)
        public static double GetPy(string soilType, bool isFront, double b, double z, double rOnB, double phi, double cu, double sigmaZPrime)
        {

            if (soilType == "砂質土" || soilType == "礫質土")
            {
                double kappa = GetKappa(isFront, rOnB, phi);
                double Kp = (1 + Math.Sin(phi * Math.PI / 180)) / (1 - Math.Sin(phi * Math.PI / 180));

                return kappa * Kp * sigmaZPrime;
            }

            else /*(soilType == "粘性土")*/
            {
                (double mu, double lambda) = GetMuLambda(isFront, rOnB);

                if (Math.Abs(z) / b <= 2.5)
                {
                    return 2 * (1 + mu * Math.Abs(z) / b) * cu;
                }
                else
                {
                    return lambda * cu;
                }
            }
        }


        /// <summary>
        /// 杭間隔比 R/B が未設定 (0 以下) か。未設定の杭は<b>群杭の影響を考えない</b>
        /// (= 後方杭でも前方杭と同じ κ・µ・λ を使う) 扱いとする。
        ///
        /// <para>R/B は杭配置の入力で、既定は 0 (未入力)。0 を式に入れると後方杭の κ が
        /// 負になり、py が符号ごと壊れる。解析に R/B を通す前は全杭に R/B = 10,000 を
        /// 渡していた (κ は 3 で頭打ち、µ・λ は R/B ≧ 3 の枝) ので、未設定はその挙動に揃える。</para>
        /// </summary>
        private static bool IsSpacingUnset(double rOnB) => !(rOnB > 0.0);

        // κを返すメソッド
        private static double GetKappa(bool isFront, double rOnB, double phi)
        {
            if (isFront || IsSpacingUnset(rOnB)) // 前方杭 / 杭間隔比 未設定
            {
                return 3.0;
            }
            else // 後方杭
            {
                return Math.Min((0.55 - 0.007 * phi) * (rOnB - 1.0) + 0.4, 3);
            }
        }

        // µ、λを返すメソッド
        private static (double, double) GetMuLambda(bool isFront, double rOnB)
        {
            if (isFront || IsSpacingUnset(rOnB)) // 前方杭 / 杭間隔比 未設定
            {
                return (1.4, 9.0);
            }
            else // 後方杭
            {
                if (rOnB >= 3.0)
                {
                    return (1.4, 9.0);
                }
                else
                {
                    // 表6.6.4: λ = 3.0(R/B)。z/B = 2.5 で 2(1 + μz/B) = 3.0(R/B) となり py が連続する
                    // (λ = 3.0 になっていたのを 2026-09-11 に修正。SoilReactionContinuityTests)
                    return (0.6 * rOnB - 0.4, 3.0 * rOnB);
                }
            }
        }
    }

    /// <summary>
    /// 水平地盤ばねに効く<b>杭 1 本ごとの群杭の影響</b>。群杭係数 ξ と杭間隔比 R/B を運ぶ。
    ///
    /// <para><b>なぜ土層-杭セット (SoilPile) に保存しないか。</b> 1 つの SoilPile は
    /// (地盤, 杭体, 杭頭高さ) が同じ杭で共有されるのに、ξ と R/B は<b>杭配置ごとの入力</b>
    /// なので、同じ SoilPile を使う杭の間で値が違いうる。保存してしまうと、どれか 1 本の値が
    /// 他の杭にも効く。前後方杭の判定 (<c>isFront</c>) が荷重ケースごとに変わるので
    /// 保存せず評価時に渡しているのと同じ理由で、これも評価時に渡す。
    /// 共有インスタンスを書き換えないので、ケース並列でも競合しない。</para>
    ///
    /// <para>2026-09-12 まで、解析は ξ = 1・R/B = 10,000 の決め打ちで、入力された
    /// 群杭係数・杭間隔比が<b>どこにも効いていなかった</b> (計算書と画面には表示されていた)。</para>
    /// </summary>
    public readonly struct GroupPileEffect
    {
        /// <summary>
        /// 群杭係数 ξ。kh0 に掛ける (基礎指針'19 (6.6.12) の ξ)。1 = 低減なし。
        /// 手入力で上書きした kh0 にも掛かる (ξ は地盤の硬さではなく杭の並びで決まるため)。
        /// </summary>
        public double Xi { get; init; }

        /// <summary>
        /// 杭間隔比 R/B。後方杭の py (κ・µ・λ) に入る。<b>0 以下 = 未入力 = 群杭の影響なし。</b>
        /// </summary>
        public double ROnB { get; init; }

        /// <summary>群杭の影響を考えない (単杭と同じ) 補正。杭を特定できない表示用。</summary>
        public static GroupPileEffect None => new() { Xi = 1.0, ROnB = 0.0 };

        /// <summary>この杭配置の入力から作る。</summary>
        public static GroupPileEffect For(PileLayoutDataItem pile)
            => pile == null
                ? None
                : new GroupPileEffect { Xi = pile.GroupPileFactor, ROnB = pile.PileSpacingFactor };
    }

    /// <summary>
    /// 基準水平地盤反力係数 kh0 の土層ごとの手入力オーバーライド。
    /// SoilPile（土層-杭セット）に保持され、指定土層内の全要素の kh0 を手入力値で固定する。
    /// 未登録の土層は自動計算値を用いる。
    /// </summary>
    public class Kh0LayerOverride
    {
        public string LayerName { get; set; }
        public double Kh0 { get; set; }

        public Kh0LayerOverride DeepCopy()
            => new() { LayerName = this.LayerName, Kh0 = this.Kh0 };
    }
}
