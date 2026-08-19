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

        // パラメータセット
        public void SetParameters(
            string name, string soilType, double gamma, double b, double e0,
            double zTop, double zBtm,
            double xi, double rOnB, double nValue, double phi, double cu,
            double sigmaZPrimeTop, double sigmaZPrimeBtm, double alpha = 60)
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

            //double alpha = 80;
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

        // 反力を返すメソッド (kN)
        public double GetSoilReaction(double y, bool isTop, bool isFront,
            SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            return GetP(y, py, mode) * B * (ZTop - ZBtm) * 0.5;
        }

        //  接線剛性を返すメソッド (kN/m)
        public double GetSoilTangentReactionCoefficient(double y, bool isTop, bool isFront,
            SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            return GetkhTan(Kh0, y, py, mode) * B * (ZTop - ZBtm) * 0.5;
        }

        public double GetSoilSecantReactionCoefficient(double y, bool isTop, bool isFront,
            SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            return GetKh(Kh0, y, py, mode) * B * (ZTop - ZBtm) * 0.5;
        }

        /// <summary>
        /// v28 問題 A 診断: この変位 y が降伏状態かどうかを返す (p-y 曲線の弾性 sqrt 領域を越えたか)。
        /// 降伏判定式: kh0 × √y0 / √|y| × |y| ≥ py   (弾性 sqrt 領域で p が py に到達)
        /// = kh0 × √(y0 × |y|) ≥ py
        /// = |y| ≥ (py / kh0)² / y0  (= yy)
        /// py 頭打ちを行わないモード (Linear / KhReduction) では常に false。
        /// </summary>
        public bool IsYieldedAtY(double y, bool isTop, bool isFront,
            SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            if (mode != SoilNonlinearityMode.KhReductionWithPy) return false;
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            if (py <= 0 || Kh0 <= 0) return false;
            double yy = GetYieldDisplacement(Kh0, py);
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
        public double GetP(double y, double py, SoilNonlinearityMode mode = SoilNonlinearityMode.KhReductionWithPy)
        {
            if (y == 0 || py <= 0) return 0;
            double sign = y > 0 ? 1.0 : -1.0;
            double absY = Math.Abs(y);
            return sign * GetKh(Kh0, absY, py, mode) * absY;
        }

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


        // κを返すメソッド
        private static double GetKappa(bool isFront, double rOnB, double phi)
        {
            if (isFront) // 前方杭
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
            if (isFront) // 前方杭
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
                    return (0.6 * rOnB - 0.4, 3.0);
                }
            }
        }
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
