using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    // 円形断面クラス
    internal class CircularSolidSection
    {
        private double Dia { get; }

        // コンストラクタ
        internal CircularSolidSection(double diameter)
        {
            Dia = diameter;
        }

        // 軸力、曲げモーメント取得メソッド
        //
        // 各短冊の幾何量（面積・断面一次モーメント）を解析的に厳密計算する補正版。
        // 弦長 w(z)=2√(R²-z²) は縁 z=±R で接線が垂直（√特異性）となるため、
        // 「幅(中点)×dz」の中点則では分割を増やしても面積が真円 πR² に収束しにくい（誤差 O(n^-1.5)、端の短冊が支配的）。
        // そこで短冊 [z1,z2] ごとに
        //   面積          A = ∫ 2√(R²-z²) dz
        //   断面一次モーメント Q = ∫ 2z√(R²-z²) dz
        // を閉形式で評価する。これにより ΣA は任意の分割数で厳密に πR² となり、
        // モーメントの幾何重み付けも厳密になる。応力は各短冊の図心 z̄ = Q/A で評価し、
        // 残る近似は「短冊内で応力一定」のみ（縁の特異性を持たないため速やかに収束する）。
        internal (double, double) GetForceAndMoment(MaterialLaw type, Material material, double epsilon0, double curvature, int division = 200)
        {
            try
            {
                double r = Dia * 0.5;
                double dz = Dia / division;

                double axialForce = 0.0;
                double bendingMoment = 0.0;

                // 望遠鏡和: 各短冊の境界での不定積分値を 1 回ずつ評価して差分をとる。
                double prevArea = AreaAntiderivative(-r, r);
                double prevMoment = FirstMomentAntiderivative(-r, r);

                for (int i = 0; i < division; i++)
                {
                    // 端の丸め誤差を避けるため最終短冊上端は厳密に +r とする。
                    double z2 = (i == division - 1) ? r : -r + (i + 1) * dz;
                    double curArea = AreaAntiderivative(z2, r);
                    double curMoment = FirstMomentAntiderivative(z2, r);

                    double area = curArea - prevArea;            // 短冊の面積 (>0)
                    double firstMoment = curMoment - prevMoment; // 短冊の断面一次モーメント Q

                    prevArea = curArea;
                    prevMoment = curMoment;

                    if (area <= 0.0) continue; // 退化短冊の保護

                    double zBar = firstMoment / area;            // 図心
                    double epsilon = epsilon0 - curvature * zBar;
                    double sigma = material.GetStress(type, epsilon);

                    axialForce += sigma * area;
                    bendingMoment += -sigma * firstMoment;       // M = ∫ -z·σ·w dz = -σ·Q
                }
                return (axialForce, bendingMoment);
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("断面積分（N・M→0）", ex, $"実心円 D={Dia}");
                return (0.0, 0.0);
            }
        }

        // 面積の不定積分: ∫ 2√(r²-z²) dz = z√(r²-z²) + r²·asin(z/r)
        private static double AreaAntiderivative(double z, double r)
        {
            double t = Math.Max(0.0, r * r - z * z);
            double ratio = Math.Clamp(z / r, -1.0, 1.0);
            return z * Math.Sqrt(t) + r * r * Math.Asin(ratio);
        }

        // 断面一次モーメントの不定積分: ∫ 2z√(r²-z²) dz = -(2/3)(r²-z²)^{3/2}
        private static double FirstMomentAntiderivative(double z, double r)
        {
            double t = Math.Max(0.0, r * r - z * z);
            return -(2.0 / 3.0) * Math.Pow(t, 1.5);
        }
    }

    // 円環断面クラス
    internal class CircularPipeSection(double diameter, double t)
    {
        private double Dia { get; } = diameter;
        private double T { get; } = t;

        // 軸力、曲げモーメント取得メソッド
        internal (double, double) GetForceAndMoment(MaterialLaw type, Material material, double epsilon0, double curvature, int division = 200)
        {
            try
            {
                double z;
                double dCirc = Math.PI * Dia / division;
                double epsilon;
                double sigma;

                double axialForce = 0.0;
                double bendingMoment = 0.0;

                for (int i = 0; i < division; i++)
                {
                    z = Dia * 0.5 * Math.Cos(2.0 * Math.PI * i / division);
                    epsilon = epsilon0 - curvature * z;
                    sigma = material.GetStress(type, epsilon);
                    axialForce += T * sigma * dCirc;
                    bendingMoment += T * sigma * dCirc * -z;
                }
                return (axialForce, bendingMoment);
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("断面積分（N・M→0）", ex, $"円環 D={Dia}, t={T}");
                return (0.0, 0.0);
            }
        }
    }
}
