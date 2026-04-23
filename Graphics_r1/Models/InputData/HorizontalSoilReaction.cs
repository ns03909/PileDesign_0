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
        public double Kh0 { get; set; } // 基準水平地盤反力係数
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
                Kh0 = this.Kh0
            };
        }

        // 反力を返すメソッド (kN)
        public double GetSoilReaction(double y, bool isTop, bool isFront)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            return GetP(y, py) * B * (ZTop - ZBtm) * 0.5;
        }

        //  接線剛性を返すメソッド (kN/m)
        public double GetSoilTangentReactionCoefficient(double y, bool isTop, bool isFront)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            return GetkhTan(Kh0, y, py) * B * (ZTop - ZBtm) * 0.5;
        }

        public double GetSoilSecantReactionCoefficient(double y, bool isTop, bool isFront)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            return GetKh(Kh0, y, py) * B * (ZTop - ZBtm) * 0.5;
        }

        /// <summary>
        /// v28 問題 A 診断: この変位 y が降伏状態かどうかを返す (p-y 曲線の弾性 sqrt 領域を越えたか)。
        /// 降伏判定式: kh0 × √y0 / √|y| × |y| ≥ py   (弾性 sqrt 領域で p が py に到達)
        /// = kh0 × √(y0 × |y|) ≥ py
        /// = |y| ≥ (py / kh0)² / y0  (= yy)
        /// </summary>
        public bool IsYieldedAtY(double y, bool isTop, bool isFront)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            if (py <= 0 || Kh0 <= 0) return false;
            const double y0 = 0.01;
            double yy = Math.Pow(py / Kh0, 2) / y0;
            return Math.Abs(y) >= yy;
        }

        // 降伏後接線剛性の比率（v22 修正）
        // 旧値 `0.001 × py / yy` は降伏境界の解析接線に対し 約 1/500 と極端に小さく、
        // NR 反復で「降伏 ↔ 弾性」を行き来する springs があると K 行列の値が毎反復で
        // 500× も変動し、ラインサーチが α=0.5 に張り付いて収束停滞の主因になっていた。
        // 新値は「降伏境界の解析接線の 2%」。GetKh/GetkhTan の両方で同じ gradient を使うので
        // 力 F_int = K_sec × |y| と接線 K_tan の整合性（K_tan = dF/d|y|）は完全に保たれる。
        // 降伏後の p は平坦ではなく僅かに増加する形となるが、|y|=2×yy で p は 1% 程度しか
        // 増えず物理的に妥当な範囲に収まる。
        private const double PostYieldTangentRatio = 0.02;

        // 降伏境界 |y|=yy における解析接線（pre-yield 式の d(p)/d|y|）
        // p_pre = kh0 × √(y0 × |y|), dp/d|y| = kh0 × √y0 / (2 × √|y|)
        // |y|=yy のとき √yy = py/(kh0×√y0) なので、
        // dp/d|y|(yy) = kh0 × √y0 × kh0 × √y0 / (2 × py) = kh0² × y0 / (2 × py)
        private static double YieldBoundaryTangent(double kh0, double py)
            => kh0 * kh0 * 0.01 / (2.0 * py); // y0 = 0.01 固定

        // 水平地盤反力係数khを返すメソッド (kN/m3)
        private static double GetKh(double kh0, double y, double py)
        {
            if (py == 0) return 0;

            double y0 = 0.01; // m, 1cm
            if (Math.Abs(y) / y0 <= 0.1)
            {
                return 3.16 * kh0;
            }
            else if (kh0 / Math.Sqrt(Math.Abs(y) / y0) * Math.Abs(y) < py)
            {
                return kh0 / Math.Sqrt(Math.Abs(y) / y0);
            }
            else
            {
                double yy = Math.Pow(py / kh0, 2) / y0;
                double gradient = PostYieldTangentRatio * YieldBoundaryTangent(kh0, py);
                double p = gradient * (Math.Abs(y) - yy) + py;
                return p / Math.Abs(y);
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
        public static double GetkhTan(double kh0, double y, double py)
        {
            double y0 = 0.01; // m, 1cm

            if (py == 0) return 0;
            double absY = Math.Abs(y);
            double yRatio = absY / y0;

            if (yRatio <= ElasticSqrtBlendStart)
            {
                return 3.16 * kh0;
            }

            // sqrt 領域の解析接線
            double sqrtTangent = Math.Sqrt(y0) / 2.0 * kh0 / Math.Sqrt(absY);

            // v23 (A-2) 境界ブレンド: |y|/y0 ∈ [0.10, 0.20] で弾性 → sqrt に滑らかに遷移
            if (yRatio < ElasticSqrtBlendEnd)
            {
                double t = (yRatio - ElasticSqrtBlendStart) / (ElasticSqrtBlendEnd - ElasticSqrtBlendStart);
                // smoothstep: 3t² − 2t³ (両端で微分係数ゼロ、C¹ 連続)
                double s = t * t * (3.0 - 2.0 * t);
                double elasticTangent = 3.16 * kh0;
                return (1.0 - s) * elasticTangent + s * sqrtTangent;
            }

            // 通常 sqrt 領域（降伏未到達）
            if (kh0 / Math.Sqrt(yRatio) * absY < py)
            {
                return sqrtTangent;
            }

            // 降伏後は降伏境界接線の PostYieldTangentRatio 倍を一定値として採用
            return PostYieldTangentRatio * YieldBoundaryTangent(kh0, py);
        }

        // 反力pを返すメソッド (kN/m2)
        // v25: 対称性バグ修正。旧実装 `Math.Min(Kh*y, py)` は y>0 側のみ py で clamp され、
        // y<0 側は clamp されず負方向に無制限に伸びていた（v22 で GetKh の plateau が
        // `py/|y|` → `(gradient*(|y|-yy)+py)/|y|` に変わり post-yield creep が生じた副作用）。
        // 現在は `sign(y) × min(Kh(|y|) × |y|, py)` で両側対称に clamp する。
        // ※ 表示専用（グラフ / MGT エクスポート / 要素分割ダイアログ）。
        //    FEM 本体は `GetSoilSecantReactionCoefficient` / `GetSoilTangentReactionCoefficient`
        //    経由で |y| 対称、post-yield creep を許容する（本メソッドと意図的に挙動が異なる）
        public double GetP(double y, double py)
        {
            if (y == 0 || py == 0) return 0;
            double sign = y > 0 ? 1.0 : -1.0;
            double absY = Math.Abs(y);
            return sign * Math.Min(GetKh(Kh0, absY, py) * absY, py);
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
}
