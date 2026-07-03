using PileDesign.Common;
using System.Windows;

namespace PileDesign.Views
{
    /// <summary>
    /// 杭断面のひずみ度・応力度分布をポップアップ表示するウィンドウ。
    /// N-M グラフ上のクリックに応じて PileSectionViewModel が内容を更新する。
    /// プロット上をホバーすると標準のヘアカーソル＋座標ポップアップ (PlotHelper) で値を表示する。
    /// </summary>
    public partial class StrainStressProfileWindow : Window
    {
        public StrainStressProfileWindow()
        {
            InitializeComponent();

            // 標準の座標ポップアップ (PlotHelper) を取り付ける。
            // クロスヘア自体は再描画 (Plot.Clear) のたびに ViewModel 側で再登録される。
            wpfPlotStrain.MouseMove += (s, e) =>
                PlotHelper.WpfPlot_MouseMove(s, e, "_", "ε", "z(mm)", 5, 0);
            wpfPlotStress.MouseMove += (s, e) =>
                PlotHelper.WpfPlot_MouseMove(s, e, "_", "σ(N/mm²)", "z(mm)", 1, 0);
        }
    }
}
