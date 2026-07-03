using PileDesign.Models.InputData;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Fonts = ScottPlot.Fonts;
using WpfPlot = ScottPlot.WPF.WpfPlot;

namespace PileDesign.Common
{
    /// <summary>
    /// N-M 曲線上のクリック点に対応する断面のひずみ度・応力度プロファイルを、
    /// 共有ポップアップウィンドウ(StrainStressProfileWindow)に表示する共有サービス。
    /// 杭断面ウィンドウ・解析結果NMINTグラフの双方から利用する。全杭種対応。
    /// </summary>
    internal static class StrainStressProfileService
    {
        private static StrainStressProfileWindow? _window;
        private static string? _sig;
        private static AbstractPileSection? _section;
        private static List<(string Name, double[] Nkn, double[] Mknm, double[] Eps, double[] Phi, bool Ultimate)>? _curves;

        /// <summary>
        /// クリック点 (N kN, M kNm) に最も近い曲線上の点を全曲線から探し、その (εc, φ) で
        /// 断面プロファイルを描画してポップアップ表示する。対応断面でなければ何もしない。
        /// </summary>
        public static void ShowAtClick(Window owner, PileSection pileSection, double clickN_kN, double clickM_kNm)
        {
            if (pileSection == null) return;
            if (!EnsureCurves(pileSection)) return;
            var section = _section!;

            double best = double.MaxValue, bestEps = 0, bestPhi = 0, bestN = 0, bestM = 0;
            bool bestUlt = false;
            string bestName = "";
            foreach (var c in _curves!)
            {
                if (c.Nkn.Length == 0) continue;
                double nScale = Math.Max(1.0, c.Nkn.Max(Math.Abs));
                double mScale = Math.Max(1.0, c.Mknm.Max(Math.Abs));
                for (int i = 0; i < c.Nkn.Length; i++)
                {
                    double dx = (c.Nkn[i] - clickN_kN) / nScale;
                    double dy = (c.Mknm[i] - clickM_kNm) / mScale;
                    double d = dx * dx + dy * dy;
                    if (d < best)
                    {
                        best = d; bestEps = c.Eps[i]; bestPhi = c.Phi[i];
                        bestUlt = c.Ultimate; bestName = c.Name; bestN = c.Nkn[i]; bestM = c.Mknm[i];
                    }
                }
            }
            if (best == double.MaxValue) return;

            // 既製杭(PHC/PRC/SC)の使用・損傷限界は許容応力度式ベースで εc/φ を持たない (=0)。
            // その場合はクリック点 (N,M) から弾性換算断面で線形の (εc,φ) を復元する。
            if (Math.Abs(bestPhi) < 1e-12 && Math.Abs(bestM) > 1e-9)
            {
                var (ec, ie, ae, rOuter) = section.GetElasticSectionProps();
                if (ec > 0 && ie > 0 && ae > 0 && rOuter > 0)
                {
                    double nN = bestN * 1e3;     // kN → N
                    double mNmm = bestM * 1e6;   // kNm → N·mm
                    bestPhi = mNmm / (ec * ie);
                    double epsAxial = nN / (ec * ae);
                    bestEps = epsAxial + bestPhi * rOuter;
                    bestUlt = false;             // 線形(弾性)で表示
                }
            }

            var profile = section.GetStrainStressProfile(bestEps, bestPhi, bestUlt);
            if (profile.Materials.Count == 0) return;

            string header = $"{bestName}  N={bestN:N0} kN  M={bestM:N0} kNm  εc={bestEps:0.000E+0}  φ={bestPhi:0.000E+0} /mm";

            EnsureWindow(owner);
            _window!.HeaderText.Text = header;
            DrawProfile(_window.wpfPlotStrain, profile, stress: false, "ひずみ度 ε");
            DrawProfile(_window.wpfPlotStress, profile, stress: true, "応力度 σ (N/mm²)");
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        private static bool EnsureCurves(PileSection pileSection)
        {
            string sig = string.Join("|",
                pileSection.PileBodyType, pileSection.PileSectionType,
                pileSection.ConcreteOutDia, pileSection.ConcreteFc, pileSection.ConcreteGsi, pileSection.ConcreteThickness,
                pileSection.MainBarDr, pileSection.MainBarNum, pileSection.MainBarSpec, pileSection.MainBarSize,
                pileSection.PipeDia, pileSection.PipeTs, pileSection.PipeGrade, pileSection.CorrosionDepth,
                pileSection.TendonDp, pileSection.TendonAp, pileSection.Prestress, pileSection.PileDiameter,
                PileDesign.Models.InputData.ConcreteModelOptions.Signature());

            if (_sig == sig && _section != null && _curves != null) return true;

            try
            {
                if (pileSection.CreateSectionCalculator() is not AbstractPileSection section) return false;

                var curves = new List<(string, double[], double[], double[], double[], bool)>();
                foreach (var (name, t, ult) in section.GetProfileSourceCurves())
                {
                    var (Ns, Ms, Eps, Phi) = t;
                    if (Ns == null || Ms == null || Eps == null || Phi == null) continue;
                    int n = Ns.Count;
                    if (n == 0 || Ms.Count != n || Eps.Count != n || Phi.Count != n) continue;
                    var nkn = new double[n];
                    var mknm = new double[n];
                    var eps = new double[n];
                    var phi = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        nkn[i] = Ns[i] * 1e-3;
                        mknm[i] = Ms[i] * 1e-6;
                        eps[i] = Eps[i];
                        phi[i] = Phi[i];
                    }
                    curves.Add((name, nkn, mknm, eps, phi, ult));
                }
                if (curves.Count == 0) return false;

                _section = section;
                _curves = curves;
                _sig = sig;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureWindow(Window owner)
        {
            if (_window == null)
            {
                _window = new StrainStressProfileWindow { Owner = owner };
                _window.Closed += (s, e) => _window = null;
            }
        }

        private static ScottPlot.Color ColorFor(SectionMaterialKind kind) => kind switch
        {
            SectionMaterialKind.Concrete => new ScottPlot.Color((byte)31, (byte)119, (byte)180),  // 青
            SectionMaterialKind.MainBar => new ScottPlot.Color((byte)214, (byte)39, (byte)40),     // 赤
            SectionMaterialKind.Tendon => new ScottPlot.Color((byte)44, (byte)160, (byte)44),      // 緑
            SectionMaterialKind.SteelPipe => new ScottPlot.Color((byte)90, (byte)90, (byte)90),    // 灰
            _ => new ScottPlot.Color((byte)0, (byte)0, (byte)0),
        };

        private static void DrawProfile(WpfPlot wpf, SectionStrainStressProfile p, bool stress, string xLabel)
        {
            if (wpf == null) return;

            wpf.Plot.Clear();
            string title = stress ? "応力度分布" : "ひずみ度分布";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);
            wpf.Plot.Axes.Left.Label.Text = "断面高さ z (mm)  上=圧縮側";
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect("断面高さ");
            wpf.Plot.Legend.FontName = Fonts.Detect("コンクリート");

            // 杭断面範囲を薄いグレーで塗る
            var band = wpf.Plot.Add.VerticalSpan(-p.Radius, p.Radius);
            band.FillStyle.Color = new ScottPlot.Color((byte)170, (byte)170, (byte)170, (byte)45);
            band.LineStyle.Width = 0;
            band.LegendText = "杭断面";

            foreach (var m in p.Materials)
            {
                double[] xs = (stress ? m.Stress : m.Strain).ToArray();
                double[] ys = m.Z.ToArray();
                var sc = wpf.Plot.Add.Scatter(xs, ys);
                sc.LegendText = m.Name;
                sc.Color = ColorFor(m.Kind);
                sc.LineWidth = 2;
                sc.MarkerSize = 0;
            }

            wpf.Plot.Add.VerticalLine(0, 1, new ScottPlot.Color((byte)0, (byte)0, (byte)0));
            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.InvertY();   // 圧縮縁を上に
            PlotHelper.InitCrosshair(wpf, new ScottPlot.Color((byte)98, (byte)176, (byte)226));
            wpf.Refresh();
        }
    }
}
