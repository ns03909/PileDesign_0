using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.FEM;
using ScottPlot;
using ScottPlot.WPF;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 単杭沈下の骨格曲線 (荷重-沈下関係) に Masing 則を適用して、
    /// 一定の変動軸力を受ける地震時の荷重-沈下履歴を可視化する。
    ///
    /// 履歴は以下の 5 段階:
    ///   ① 0 → 常時荷重 (骨格曲線)
    ///   ② 常時荷重 → 常時荷重 - 変動軸力 (Masing 除荷)
    ///   ③ 常時荷重 - 変動軸力 → 常時荷重 + 変動軸力 (Masing 載荷)
    ///   ④ 常時荷重 + 変動軸力 → 常時荷重 - 変動軸力 (Masing 除荷)
    ///   ⑤ 常時荷重 - 変動軸力 → 常時荷重 + 変動軸力 (Masing 載荷)
    /// </summary>
    public partial class MasingHysteresisViewModel : ObservableObject
    {
        private readonly VerticalLoadTransferMethod _vtm;

        public MasingHysteresisViewModel(VerticalLoadTransferMethod vtm,
            double initialConstantLoad = 0, double initialVariableLoad = 0)
        {
            _vtm = vtm ?? throw new ArgumentNullException(nameof(vtm));
            _constantLoad = initialConstantLoad;
            _variableLoad = initialVariableLoad;
        }

        public event EventHandler RequestClose;

        public WpfPlot WpfPlotHead { get; set; }
        public WpfPlot WpfPlotToe { get; set; }

        [ObservableProperty]
        private double _constantLoad;

        [ObservableProperty]
        private double _variableLoad;

        [ObservableProperty]
        private bool _isLegendVisible = true;

        /// <summary>
        /// Pyke 修正則を適用するかどうか (true: 過去最大/最小 P を超えた瞬間に骨格曲線へ復帰)。
        /// false にすると純 Masing (2× スケールのみ、メモリなし) になる。
        /// </summary>
        [ObservableProperty]
        private bool _isPykeRuleEnabled = true;

        partial void OnConstantLoadChanged(double value) => UpdatePlots();
        partial void OnVariableLoadChanged(double value) => UpdatePlots();
        partial void OnIsLegendVisibleChanged(bool value) => UpdatePlots();
        partial void OnIsPykeRuleEnabledChanged(bool value) => UpdatePlots();

        [RelayCommand]
        private void OnClose() => RequestClose?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// 骨格曲線 f(P) を奇関数として拡張した値を返す (杭頭沈下、m)。
        /// 引抜側 (P&lt;0) の解析結果がない場合でも対称形として推定する。
        /// </summary>
        private double SkeletonHead(double pileTopForce)
        {
            int sign = Math.Sign(pileTopForce);
            if (sign == 0) return 0;
            var v = _vtm.GetDisplacementForGivenLoad(Math.Abs(pileTopForce));
            return sign * (v?[0] ?? 0);
        }

        /// <summary>骨格曲線 f(P) (杭先端沈下、m)、奇関数拡張。</summary>
        private double SkeletonToe(double pileTopForce)
        {
            int sign = Math.Sign(pileTopForce);
            if (sign == 0) return 0;
            var v = _vtm.GetDisplacementForGivenLoad(Math.Abs(pileTopForce));
            int n = v?.Count ?? 0;
            return sign * (n >= 2 ? v[n - 2] : 0);
        }

        /// <summary>
        /// (P_r, s_r) を反転点とする Masing 則: s = s_r + 2·f̃((P-P_r)/2)
        /// の path を点列で返す (始点・終点の長さは samples)。
        /// </summary>
        private static (List<double> P, List<double> S) BuildMasingBranch(
            double pStart, double pEnd, double sStart,
            Func<double, double> skeleton, int samples = 60)
        {
            // 反転点は始点 (pStart, sStart)
            var pList = new List<double>(samples);
            var sList = new List<double>(samples);
            double delta = pEnd - pStart;
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / (samples - 1);
                double p = pStart + t * delta;
                double s = sStart + 2.0 * skeleton(t * delta / 2.0);
                pList.Add(p);
                sList.Add(s);
            }
            return (pList, sList);
        }

        /// <summary>骨格曲線 (initial loading): origin → (pEnd, f̃(pEnd))。</summary>
        private static (List<double> P, List<double> S) BuildSkeletonBranch(
            double pEnd, Func<double, double> skeleton, int samples = 60)
        {
            var pList = new List<double>(samples);
            var sList = new List<double>(samples);
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / (samples - 1);
                double p = t * pEnd;
                pList.Add(p);
                sList.Add(skeleton(p));
            }
            return (pList, sList);
        }

        /// <summary>
        /// 任意区間の骨格曲線: (pStart, f̃(pStart)) → (pEnd, f̃(pEnd))。
        /// </summary>
        private static (List<double> P, List<double> S) BuildBackbonePathBranch(
            double pStart, double pEnd, Func<double, double> skeleton, int samples = 60)
        {
            var pList = new List<double>(samples);
            var sList = new List<double>(samples);
            double delta = pEnd - pStart;
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / (samples - 1);
                double p = pStart + t * delta;
                pList.Add(p);
                sList.Add(skeleton(p));
            }
            return (pList, sList);
        }

        /// <summary>
        /// Pyke 修正則による分岐生成。
        /// 載荷で過去最大 P を超えるとき、または除荷で過去最小 P を割るときに、
        /// その境界 P で Masing 分岐から骨格曲線へ切替える (Masing-Pyke の規則)。
        /// </summary>
        private static (List<double> P, List<double> S) BuildPykeMasingBranch(
            double pStart, double pEnd, double sStart,
            double priorPmax, double priorPmin,
            Func<double, double> skeleton, int samples = 60)
        {
            double delta = pEnd - pStart;
            bool exceedsMax = delta > 0 && pEnd > priorPmax && pStart < priorPmax;
            bool fallsBelowMin = delta < 0 && pEnd < priorPmin && pStart > priorPmin;

            if (!exceedsMax && !fallsBelowMin)
            {
                // メモリ規則発動なし → 純 Masing
                return BuildMasingBranch(pStart, pEnd, sStart, skeleton, samples);
            }

            double pivotP = exceedsMax ? priorPmax : priorPmin;

            // フェーズ 1: Masing 分岐 (pStart → pivotP)
            // (Masing 性質より終点 s は f̃(pivotP) と一致するはず)
            int n1 = Math.Max(samples / 2, 8);
            int n2 = Math.Max(samples - n1 + 1, 8); // +1: pivot 重複分
            var (p1, s1) = BuildMasingBranch(pStart, pivotP, sStart, skeleton, n1);

            // フェーズ 2: 骨格曲線 (pivotP → pEnd)
            var (p2, s2) = BuildBackbonePathBranch(pivotP, pEnd, skeleton, n2);

            // 連結 (pivotP は片側で省略)
            var pCombined = new List<double>(p1.Count + p2.Count - 1);
            var sCombined = new List<double>(s1.Count + s2.Count - 1);
            pCombined.AddRange(p1);
            sCombined.AddRange(s1);
            for (int i = 1; i < p2.Count; i++)
            {
                pCombined.Add(p2[i]);
                sCombined.Add(s2[i]);
            }
            return (pCombined, sCombined);
        }

        /// <summary>
        /// 5 段階の履歴を生成し、各反転点の (P, s_head, s_toe) を含めて返す。
        /// 戻り値の最初の要素は origin (P=0, s=0)。
        /// </summary>
        public (List<List<double>> Ps, List<List<double>> Sheads, List<List<double>> Stoes,
                List<double> ReversalP, List<double> ReversalSHead, List<double> ReversalSToe)
            BuildHistory(int samplesPerSegment = 60)
        {
            double pConst = ConstantLoad;
            double pVar = VariableLoad;

            // 5 終点の荷重
            double p1 = pConst;
            double p2 = pConst - pVar;
            double p3 = pConst + pVar;
            double p4 = pConst - pVar;
            double p5 = pConst + pVar;

            var psList = new List<List<double>>();
            var sHeadsList = new List<List<double>>();
            var sToesList = new List<List<double>>();
            var revP = new List<double> { 0 };
            var revSHead = new List<double> { 0 };
            var revSToe = new List<double> { 0 };

            // ① 0 → p1: 骨格曲線
            var (pSeg, sHeadSeg) = BuildSkeletonBranch(p1, SkeletonHead, samplesPerSegment);
            var (_, sToeSeg) = BuildSkeletonBranch(p1, SkeletonToe, samplesPerSegment);
            psList.Add(pSeg); sHeadsList.Add(sHeadSeg); sToesList.Add(sToeSeg);
            double curP = p1;
            double curSHead = sHeadSeg[^1];
            double curSToe = sToeSeg[^1];
            revP.Add(curP); revSHead.Add(curSHead); revSToe.Add(curSToe);

            // メモリ (これまで訪れた P の最大/最小)
            double priorPmax = Math.Max(0, p1);
            double priorPmin = Math.Min(0, p1);

            // ② → ⑤: Pyke 修正 (有効時) または純 Masing 分岐
            double[] targets = [p2, p3, p4, p5];
            foreach (double pNext in targets)
            {
                List<double> pH, sH, sT;
                if (IsPykeRuleEnabled)
                {
                    var rH = BuildPykeMasingBranch(curP, pNext, curSHead, priorPmax, priorPmin, SkeletonHead, samplesPerSegment);
                    var rT = BuildPykeMasingBranch(curP, pNext, curSToe, priorPmax, priorPmin, SkeletonToe, samplesPerSegment);
                    pH = rH.P; sH = rH.S; sT = rT.S;
                }
                else
                {
                    var rH = BuildMasingBranch(curP, pNext, curSHead, SkeletonHead, samplesPerSegment);
                    var rT = BuildMasingBranch(curP, pNext, curSToe, SkeletonToe, samplesPerSegment);
                    pH = rH.P; sH = rH.S; sT = rT.S;
                }
                psList.Add(pH); sHeadsList.Add(sH); sToesList.Add(sT);
                curP = pNext;
                curSHead = sH[^1];
                curSToe = sT[^1];
                revP.Add(curP); revSHead.Add(curSHead); revSToe.Add(curSToe);
                if (curP > priorPmax) priorPmax = curP;
                if (curP < priorPmin) priorPmin = curP;
            }

            return (psList, sHeadsList, sToesList, revP, revSHead, revSToe);
        }

        public void UpdatePlots()
        {
            if (WpfPlotHead == null || WpfPlotToe == null) return;

            WpfPlotHead.Plot.Clear();
            WpfPlotToe.Plot.Clear();

            var (psList, sHeadsList, sToesList, revP, revSHead, revSToe) = BuildHistory();

            // 骨格曲線 (参考線): 0 から履歴の最大 |P| まで描画
            DrawBackboneReference(WpfPlotHead, WpfPlotToe, revP);

            // 段階別の色 (① 骨格 / ②④ 除荷 / ③⑤ 載荷)
            SKColor[] segmentColors =
            [
                NikkenSKColor.SkyBlue,    // ①
                NikkenSKColor.LineOrange, // ②
                NikkenSKColor.Green,      // ③
                NikkenSKColor.LineSlate,  // ④
                NikkenSKColor.PaleRed,    // ⑤
            ];
            string[] segmentLabels =
            [
                "① 0 → 常時",
                "② 常時 → 常時-変動",
                "③ 常時-変動 → 常時+変動",
                "④ 常時+変動 → 常時-変動",
                "⑤ 常時-変動 → 常時+変動",
            ];

            for (int i = 0; i < psList.Count; i++)
            {
                // mm 単位で表示
                double[] xHead = sHeadsList[i].Select(s => s * 1000.0).ToArray();
                double[] xToe = sToesList[i].Select(s => s * 1000.0).ToArray();
                double[] yLoad = [.. psList[i]];
                AddLineScatter(WpfPlotHead, xHead, yLoad, segmentColors[i], segmentLabels[i]);
                AddLineScatter(WpfPlotToe, xToe, yLoad, segmentColors[i], segmentLabels[i]);
            }

            // 反転点マーカー
            double[] xRevHead = revSHead.Select(s => s * 1000.0).ToArray();
            double[] xRevToe = revSToe.Select(s => s * 1000.0).ToArray();
            double[] yRev = [.. revP];
            AddReversalMarkers(WpfPlotHead, xRevHead, yRev);
            AddReversalMarkers(WpfPlotToe, xRevToe, yRev);

            ConfigurePlot(WpfPlotHead, "Masing 履歴: 荷重-杭頭沈下", "杭頭沈下量(mm)", "荷重 (kN)");
            ConfigurePlot(WpfPlotToe, "Masing 履歴: 荷重-杭先端沈下", "杭先端沈下量(mm)", "荷重 (kN)");

            WpfPlotHead.Plot.Legend.IsVisible = IsLegendVisible;
            WpfPlotToe.Plot.Legend.IsVisible = IsLegendVisible;

            WpfPlotHead.Refresh();
            WpfPlotToe.Refresh();
        }

        private void DrawBackboneReference(WpfPlot wpfHead, WpfPlot wpfToe, List<double> revP)
        {
            // 履歴の最大/最小 P を範囲とし、骨格曲線を半透明グレーで全範囲描画
            double pMax = revP.Max();
            double pMin = revP.Min();
            // 0 を含めるよう拡張
            pMax = Math.Max(pMax, 0);
            pMin = Math.Min(pMin, 0);
            if (Math.Abs(pMax - pMin) < 1e-6) return;

            int n = 120;
            var pSamples = new double[n];
            var sHeadSamples = new double[n];
            var sToeSamples = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / (n - 1);
                double p = pMin + t * (pMax - pMin);
                pSamples[i] = p;
                sHeadSamples[i] = SkeletonHead(p) * 1000.0;
                sToeSamples[i] = SkeletonToe(p) * 1000.0;
            }
            AddBackboneScatter(wpfHead, sHeadSamples, pSamples);
            AddBackboneScatter(wpfToe, sToeSamples, pSamples);
        }

        private static void AddBackboneScatter(WpfPlot wpf, double[] x, double[] y)
        {
            var sc = wpf.Plot.Add.Scatter(x, y);
            sc.Color = new Color(128, 128, 128, 100); // 半透明グレー
            sc.LineWidth = 2;
            sc.LinePattern = LinePattern.Dashed;
            sc.MarkerShape = MarkerShape.None;
            sc.LegendText = "骨格曲線 (参考)";
        }

        private static void AddLineScatter(WpfPlot wpf, double[] x, double[] y, SKColor color, string legend)
        {
            var sc = wpf.Plot.Add.Scatter(x, y);
            sc.Color = Color.FromSKColor(color);
            sc.LineWidth = 2;
            sc.MarkerShape = MarkerShape.None;
            sc.LegendText = legend;
        }

        private static void AddReversalMarkers(WpfPlot wpf, double[] x, double[] y)
        {
            var sc = wpf.Plot.Add.Scatter(x, y);
            sc.Color = Color.FromSKColor(NikkenSKColor.Red);
            sc.LineWidth = 0;
            sc.MarkerSize = 10;
            sc.MarkerShape = MarkerShape.OpenCircle;
            sc.LegendText = "反転点";

            // ラベル ①〜⑤ (origin は除く)
            string[] marks = ["", "①", "②", "③", "④", "⑤"];
            for (int i = 1; i < x.Length && i < marks.Length; i++)
            {
                var t = wpf.Plot.Add.Text(marks[i], new Coordinates(x[i], y[i]));
                t.LabelFontColor = Color.FromSKColor(NikkenSKColor.Red);
                t.LabelFontSize = 14;
                t.LabelBold = true;
                t.LabelFontName = Fonts.Detect(marks[i]);
                t.LabelAlignment = Alignment.LowerLeft;
            }
        }

        private static void ConfigurePlot(WpfPlot wpf, string title, string xLabel, string yLabel)
        {
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);
            wpf.Plot.Legend.FontName = Fonts.Detect(yLabel);

            Color grayColor = new(128, 128, 128, 255);
            wpf.Plot.Add.VerticalLine(0, 1, grayColor);
            wpf.Plot.Add.HorizontalLine(0, 1, grayColor);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();
        }
    }
}
