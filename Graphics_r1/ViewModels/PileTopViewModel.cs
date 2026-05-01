using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Views;
using ScottPlot.Plottables;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Fonts = ScottPlot.Fonts;

using Serilog;
namespace PileDesign.ViewModels
{
    public partial class PileTopViewModel : ObservableObject
    {
        public PileTopWindow PileTopWindowInstance { get; set; }

        public readonly UndoManager _undoManager = new();

        public string PileTopType { get; set; }

        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        [ObservableProperty]
        private int _pileBodyNo;

        [ObservableProperty]
        private string _pileBodyType;

        // PileTop は INotifyPropertyChanged の subscribe/unsubscribe を伴う複雑な
        // setter のため手書き維持
        private PileTop _pileTop;
        public PileTop PileTop
        {
            get => _pileTop;
            set
            {
                if (_pileTop == value) return;

                // 既存の購読を解除
                if (_pileTop is System.ComponentModel.INotifyPropertyChanged oldNotify)
                    oldNotify.PropertyChanged -= PileTop_PropertyChanged;

                SetProperty(ref _pileTop, value);

                // 新しい PileTop の変更通知を購読
                if (_pileTop is System.ComponentModel.INotifyPropertyChanged newNotify)
                    newNotify.PropertyChanged += PileTop_PropertyChanged;

                // 初期表示用更新
                RefreshTopSectionDisplayFromProvidedSection();

                // Canvas が既にセットされていれば描画
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try { RedrawShapes(); } catch (Exception ex) { Log.Warning(ex, "[PileTopVM] RedrawShapes"); }
                }), System.Windows.Threading.DispatcherPriority.Loaded);

                ChartUpdate();
            }
        }

        // ハンドラ：PileTop の個別プロパティ変更時の処理
        private void PileTop_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e == null) return;

            // 上面図を更新したいプロパティ名のリスト
            var watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "MainBarNum1", "MainBarSize1", "MainBarSpec1", "MainBarCenterCover1",
                "MainBarNum2", "MainBarSize2", "MainBarSpec2", "MainBarCenterCover2",
                "ConcreteOutDia", "PipeDia", "PipeTs"
            };

            if (watched.Contains(e.PropertyName))
            {
                // 少し遅延して UI スレッドで安全に再描画
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // 必要であれば表示情報を更新
                        RefreshTopSectionDisplayFromProvidedSection();
                        RedrawShapes();
                        ChartUpdate();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "PileTop_PropertyChanged redraw failed");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        [ObservableProperty]
        private string _pileConstructionType;

        [ObservableProperty]
        private PileSection _pileSection;

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;

        private readonly PileTop PrevPileTop;

        public Canvas Canvas { get; set; }
        private PathGeometry DrawingGeometry = new();
        private PathGeometry TopSectionDrawingGeometry = new();
        private double Scale;


        public static Crosshair MyCrosshair_MN { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_MN;

        public static Crosshair MyCrosshair_ThetaM { get; private set; }

        [ObservableProperty]
        private string _crosshairPositionText_ThetaM;

        // 追加: 最上段断面表示用プロパティ群
        //private double _displayTopSectionDiameter;
        //public double DisplayTopSectionDiameter
        //{
        //    get => _displayTopSectionDiameter;
        //    set => SetProperty(ref _displayTopSectionDiameter, value);
        //}

        //private string _displayTopSectionMainBarSize;
        //public string DisplayTopSectionMainBarSize
        //{
        //    get => _displayTopSectionMainBarSize;
        //    set => SetProperty(ref _displayTopSectionMainBarSize, value);
        //}

        //private int _displayTopSectionMainBarNum;
        //public int DisplayTopSectionMainBarNum
        //{
        //    get => _displayTopSectionMainBarNum;
        //    set => SetProperty(ref _displayTopSectionMainBarNum, value);
        //}

        [ObservableProperty]
        private double _displayTopSectionPipeDia;

        [ObservableProperty]
        private double _displayTopSectionPipeTs;

        // コンストラクタ
        public PileTopViewModel(
            MainWindowViewModel mainWindowViewModel,
            PileTop pileTop,
            int pileBodyNo,
            string pileBodyType,
            string pileTopType,
            string pileConstructionType,
            PileSection pileSection
            )
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            PileTop = pileTop;
            PileBodyNo = pileBodyNo;
            PileBodyType = pileBodyType;
            PileTopType = pileTopType;
            PileConstructionType = pileConstructionType;
            PileSection = pileSection;

            // ShallowCopyメソッドを使用して値渡し
            PrevPileTop = PileTop.ShallowCopy();

            PileTop.SelectedPileTopSpecification = [];

            // 初期表示用：場所打ち鋼管コンクリート杭 の場合は最上段 PileSection 情報を表示
            RefreshTopSectionDisplayFromProvidedSection();
        }

        // 再利用可能な更新メソッド（コンストラクタおよび外部から呼べる）
        private void RefreshTopSectionDisplayFromProvidedSection()
        {
            try
            {
                // 対象施工タイプであり、PileSection が渡されている場合のみ処理
                if (string.Equals(PileBodyType, "場所打ち鋼管コンクリート杭", StringComparison.Ordinal) && PileSection != null)
                {
                    // 杭径（表示は最上段の杭径 + 200mm）
                    double baseDia = 0.0;
                    try { baseDia = PileSection.PileDiameter; } catch { baseDia = 0.0; }

                    PileTop.ConcreteOutDia = Math.Round(baseDia + 200.0, 3);
                    PileTop.MainBarCenterCover2 = PileSection.MainBarCenterCover + 100;

                    // 主筋情報（存在するフィールドを安全に参照）
                    try { PileTop.MainBarSize2 = PileSection.MainBarSize ?? string.Empty; } catch { PileTop.MainBarSize2 = string.Empty; }
                    try { PileTop.MainBarNum2 = PileSection.MainBarNum; } catch { PileTop.MainBarNum2 = 0; }
                    try { PileTop.MainBarFtr2 = PileSection.MainBarFtr; } catch { PileTop.MainBarFtr2 = 0; }
                    try { PileTop.MainBarSpec2 = PileSection.MainBarSpec; } catch { PileTop.MainBarSpec2 = "SD390"; }

                    // 鋼管情報（場所打ち鋼管コンクリート杭向け）
                    try { DisplayTopSectionPipeDia = PileSection.PipeDia; } catch { DisplayTopSectionPipeDia = 0.0; }
                    try { DisplayTopSectionPipeTs = PileSection.PipeTs; } catch { DisplayTopSectionPipeTs = 0.0; }
                }
                else if (string.Equals(PileBodyType, "既製コンクリート杭", StringComparison.Ordinal) && PileSection != null)
                {
                    // 杭径（表示は最上段の杭径 + 200mm）
                    double baseDia = 0.0;
                    try { baseDia = PileSection.PileDiameter; } catch { baseDia = 0.0; }
                    PileTop.ConcreteOutDia = Math.Round(baseDia, 3);
                    double thickness = 0.0;
                    try { thickness = PileSection.ConcreteThickness; } catch { thickness = 0.0; }

                    PileTop.ConcreteThickness = Math.Round(thickness, 3);
                    PileTop.MainBarCenterCover2 = PileSection.MainBarCenterCover;

                    // 主筋情報（存在するフィールドを安全に参照）
                    PileTop.MainBarSize2 = string.Empty;
                    PileTop.MainBarNum2 = 0;
                    PileTop.MainBarFtr2 = 0;
                    PileTop.MainBarSpec2 = "SD390";

                    //// 鋼管情報（場所打ち鋼管コンクリート杭向け）
                    //try { DisplayTopSectionPipeDia = PileSection.PipeDia; } catch { DisplayTopSectionPipeDia = 0.0; }
                    //try { DisplayTopSectionPipeTs = PileSection.PipeTs; } catch { DisplayTopSectionPipeTs = 0.0; }
                }
                else if (string.Equals(PileBodyType, "鋼管杭", StringComparison.Ordinal) && PileSection != null)
                {
                    // 鋼管杭 + 鉄筋定着工法
                    // 杭径 (= 下層 RC 径) は SteelPipePileLowerRcDiameter 公式で自動算定
                    // (PileSection.PileDiameter は腐食代考慮済みの有効径)
                    double baseDia = 0.0;
                    try { baseDia = PileSection.PileDiameter; } catch { baseDia = 0.0; }
                    PileTop.ConcreteOutDia = Math.Round(PileTop.SteelPipePileLowerRcDiameter(baseDia), 3);

                    // 鋼管情報を表示
                    try { DisplayTopSectionPipeDia = PileSection.PipeDia; } catch { DisplayTopSectionPipeDia = 0.0; }
                    try { DisplayTopSectionPipeTs = PileSection.PipeTs; } catch { DisplayTopSectionPipeTs = 0.0; }

                    // 主筋 (MainBars2) は editable のため初期値のみセット (既存値があれば残す)。
                    // 鋼管杭の杭体には鉄筋情報は持たないため PileSection からは引かない。
                    if (PileTop.MainBarNum2 == 0 && string.IsNullOrEmpty(PileTop.MainBarSize2))
                    {
                        PileTop.MainBarSize2 = "D29";
                        PileTop.MainBarSpec2 = "SD390";
                        PileTop.MainBarCenterCover2 = 300;
                    }
                }
                else
                {
                    // 非該当時は初期化（表示を消したいとき）
                    PileTop.ConcreteOutDia = 0.0;
                    PileTop.MainBarSize2 = string.Empty;
                    PileTop.MainBarNum2 = 0;
                    DisplayTopSectionPipeDia = 0.0;
                    DisplayTopSectionPipeTs = 0.0;
                }
            }
            catch (Exception ex)
            {
                // 安全のため例外を握りつぶすがログに出す
                Log.Warning(ex, "RefreshTopSectionDisplayFromProvidedSection failed");
            }
        }

        [RelayCommand]
        public void Undo()
        {
            _undoManager.Undo();
            if (_undoManager.CurrentState is PileTop state)
            {
                PileTop = state.DeepCopy();
            }
        }

        [RelayCommand]
        public void Redo()
        {
            _undoManager.Redo();
            if (_undoManager.CurrentState is PileTop state)
            {
                PileTop = state.DeepCopy();
            }
        }


        [RelayCommand]
        private void OnOk()
        {
            if (!_mainWindowViewModel.CheckAndResetElementSplit("杭頭部"))
                return;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void OnCancel()
        {
            // プロパティを前回の保存時の値に戻す
            PileTop = PrevPileTop.ShallowCopy();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        // 図の更新
        public void ChartUpdate()
        {
            if (PileTopType == "キャプテンパイル工法")
            {
                UpdateChartMNSeries(PileTop.CaptainPile.YieldNs, PileTop.CaptainPile.YieldMs, PileTop.CaptainPile.UltimateNs, PileTop.CaptainPile.UltimateMs);
                UpdateChartThetaMSeries(PileTop.CaptainPile.Ns, PileTop.CaptainPile.ThetasMs);
            }
            else if (PileTopType == "FT-Pile構法" && PileTop.FTPile.Ns != null && PileTop.FTPile.ThetasMs != null)
            {
                UpdateChartThetaMSeries(PileTop.FTPile.Ns, PileTop.FTPile.ThetasMs);
            }
            else if (PileTopType == "鉄筋定着工法")
            {
                // 鉄筋定着工法用の MN 曲線描画（場所打ち鋼管コンクリート杭 を想定）
                try
                {
                    DrawRebarAnchorage_MN();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ChartUpdate: DrawRebarAnchorage_MN failed");
                }
            }
        }


        private void DrawRebarAnchorage_MN()
        {
            if (PileTop == null || PileTopWindowInstance == null) return;
            var wpf = PileTopWindowInstance.wpfPlotMN;
            if (wpf == null) return;

            // PileTop の NM データを安全に取得（Null 許容を考慮）
            var svc = PileTop.UnfactoredServiceNM;
            List<double> serviceN = svc.N ?? [];
            List<double> serviceM = svc.M ?? [];

            var dmg = PileTop.UnfactoredDamageNM;
            List<double> damageN = dmg.N ?? [];
            List<double> damageM = dmg.M ?? [];

            var ult = PileTop.UnfactoredUltimateNM;
            List<double> ultimateN = ult.N ?? [];
            List<double> ultimateM = ult.M ?? [];

            wpf.Plot.Clear();

            string title = "軸力と曲げモーメントの耐力曲線（定着工法）";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            wpf.Plot.Axes.Bottom.Label.Text = "N (kN)";
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect("N (kN)");
            wpf.Plot.Axes.Left.Label.Text = "M (kNm)";
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect("M (kNm)");

            // ローカルヘルパ：リストを kN / kNm に変換してプロット
            void TryPlot(IList<double> nList, IList<double> mList, string legend, bool dashed = false, double lineWidth = 1.5)
            {
                if (nList == null || mList == null) return;
                if (nList.Count == 0 || nList.Count != mList.Count) return;

                double[] xs = [.. nList.Select(x => x /** 1e-3*/)];      // N -> kN
                double[] ys = [.. mList.Select(m => m /** 1e-6*/)];      // Nmm/N*m -> kNm（既存実装に合わせる）
                var scatter = wpf.Plot.Add.Scatter(xs, ys);
                scatter.LegendText = legend;
                scatter.LineWidth = (float)lineWidth;
                scatter.MarkerSize = 0;
                if (dashed)
                {
                    try { scatter.LineStyle.Pattern = ScottPlot.LinePattern.Dashed; } catch (Exception ex) { Log.Warning(ex, "[PileTopVM] LinePattern"); }
                }
                wpf.Plot.Legend.FontName = Fonts.Detect(legend ?? "メイリオ");
            }

            // 描画順：使用限界・損傷限界・安全限界
            TryPlot(serviceN, serviceM, "(低減前) 使用限界", true, 2);
            TryPlot(damageN, damageM, "(低減前) 損傷限界", true, 1.5);
            TryPlot(ultimateN, ultimateM, "(低減前) 安全限界", true, 1.5);

            var black = new ScottPlot.Color(0, 0, 0);
            wpf.Plot.Add.VerticalLine(0, 1, black);
            wpf.Plot.Add.HorizontalLine(0, 1, black);

            wpf.Plot.Legend.IsVisible = true;
            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();

            wpf.Refresh();

            // クロスヘア初期化（多重登録に注意）
            MyCrosshair_MN = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            // イベントを二重登録しないため、追加のみ（既存で二重登録が問題ならデリゲートを保持して解除する実装に変更してください）
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_MN", "N(kN)", "M(kNm)", 1, 1);
        }


        public void UpdateChartMNSeries(
            ObservableCollection<double> yieldNs,
            ObservableCollection<double> yieldMs,
            ObservableCollection<double> ultimateNs,
            ObservableCollection<double> ultimateMs)
        {
            var wpf = PileTopWindowInstance.wpfPlotMN;
            wpf.Plot.Clear();

            string title = "軸力と曲げモーメントの耐力曲線";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "N (kN)";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "M (kNm)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpf.Plot.Legend.FontName = Fonts.Detect(yLabel);

            if (yieldMs != null && yieldMs.Count > 0)
            {
                List<double> xs = [];
                List<double> ys = [];
                for (int j = 0; j < yieldNs.Count; j++)
                {
                    xs.Add(yieldNs[j] / Math.Pow(10, 3));
                    ys.Add(yieldMs[j] / Math.Pow(10, 6));
                }

                var scatter = wpf.Plot.Add.Scatter(xs, ys);
                scatter.LineWidth = 1;
                scatter.MarkerSize = 0;
                scatter.MarkerShape = ScottPlot.MarkerShape.None;
                string legend = "yieldMN";
                scatter.LegendText = legend;
                wpf.Plot.Legend.FontName = Fonts.Detect(legend);
            }
            if (ultimateMs != null && ultimateMs.Count > 0)
            {
                List<double> xs = [];
                List<double> ys = [];
                for (int j = 0; j < ultimateNs.Count; j++)
                {
                    xs.Add(ultimateNs[j] / Math.Pow(10, 3));
                    ys.Add(ultimateMs[j] / Math.Pow(10, 6));
                }

                var scatter = wpf.Plot.Add.Scatter(xs, ys);
                scatter.LineWidth = 1;
                scatter.MarkerSize = 0;
                scatter.MarkerShape = ScottPlot.MarkerShape.None;
                string legend = "ultimateMN";
                scatter.LegendText = legend;
                wpf.Plot.Legend.FontName = Fonts.Detect(legend);
            }

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();

            wpf.Plot.Axes.Left.Min = 0.0;

            wpf.Refresh();

            // クロスヘアの初期化
            MyCrosshair_MN = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_MN", "N(kN)", "M(kNm)", 1, 1);
        }

        public void UpdateChartThetaMSeries(
            ObservableCollection<double> axialForces,
            ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> thetasMs)
        {
            var wpf = PileTopWindowInstance.wpfPlotThetaM;
            wpf.Plot.Clear();

            string title = "回転角と曲げモーメント関係";
            wpf.Plot.Axes.Title.Label.Text = title;
            wpf.Plot.Axes.Title.Label.FontName = Fonts.Detect(title);

            string xLabel = "θ (rad)";
            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
            wpf.Plot.Axes.Bottom.Label.FontName = Fonts.Detect(xLabel);

            string yLabel = "M (kNm)";
            wpf.Plot.Axes.Left.Label.Text = yLabel;
            wpf.Plot.Axes.Left.Label.FontName = Fonts.Detect(yLabel);

            wpf.Plot.Legend.FontName = Fonts.Detect(yLabel);

            if (axialForces != null && axialForces.Count > 0)
            {
                for (int i = axialForces.Count - 1; i >= 0; i--)
                {
                    ObservableCollection<double> thetas = thetasMs[i].Item1;
                    ObservableCollection<double> moments = thetasMs[i].Item2;
                    List<double> xs = [];
                    List<double> ys = [];
                    for (int j = 0; j < thetas.Count; j++)
                    {
                        xs.Add(thetas[j]);
                        ys.Add(moments[j] / Math.Pow(10, 6));
                    }
                    var scatter = wpf.Plot.Add.Scatter(xs, ys);
                    scatter.LineWidth = 1;
                    scatter.MarkerSize = 0;
                    scatter.MarkerShape = ScottPlot.MarkerShape.None;
                    string legend = $"N = {axialForces[i] / Math.Pow(10, 3):N0}kN";
                    scatter.LegendText = legend;
                    wpf.Plot.Legend.FontName = Fonts.Detect(legend);
                }
            }

            var blackTM = new ScottPlot.Color(0, 0, 0);
            wpf.Plot.Add.VerticalLine(0, 1, blackTM);
            wpf.Plot.Add.HorizontalLine(0, 1, blackTM);

            wpf.Plot.Axes.AutoScale();
            wpf.Plot.Axes.AutoScaleExpandX();
            wpf.Plot.Axes.AutoScaleExpandY();

            wpf.Plot.Axes.Left.Min = 0.0;

            wpf.Refresh();

            // クロスヘアの初期化
            MyCrosshair_ThetaM = PlotHelper.InitCrosshair(wpf, ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue));
            wpf.MouseMove += (s, e) => PlotHelper.WpfPlot_MouseMove(s, e, "CrosshairPositionText_ThetaM", "θ(rad)", "M(kNm)", 1, 1);
        }

        public static void ReplaceSeries(List<double> damageN, List<double> ultimateN)
        {
            double maxN = double.MinValue;
            double minN = double.MaxValue;

            for (int i = 0; i < damageN.Count; i++)
            {
                maxN = Math.Max(maxN, damageN[i]);
                minN = Math.Min(minN, damageN[i]);
            }

            for (int i = 0; i < ultimateN.Count; i++)
            {
                maxN = Math.Max(maxN, ultimateN[i]);
                minN = Math.Min(minN, ultimateN[i]);
            }

            bool shouldBreak = false;
            for (int i = 0; i <= 5; i++)
            {
                for (int j = 0; j <= 5; j++)
                {
                    double minScaleValue = -j * 2 * Math.Pow(10, i);
                    if (minScaleValue < minN)
                    {
                        shouldBreak = true;
                        break;
                    }
                }
                if (shouldBreak)
                {
                    break;
                }
            }
        }

        // List<double>型データの構成要素すべてに係数を乗ずるメソッド
        internal static List<double> GetMultipliedListValues(List<double> originalList, double multiplier)
        {
            List<double> result = [];
            for (int i = 0; i < originalList.Count; i++)
            {
                result.Add(originalList[i] * multiplier);
            }
            return result;
        }

        // Canvas画像を保存するメソッド
        public static void SaveImage(Canvas canvas)
        {
            // ファイル保存ダイアログを作成し、デフォルトの保存場所をデスクトップに設定します
            SaveFileDialog saveFileDialog = new()
            {
                Filter = "PNGファイル (*.png)|*.png|すべてのファイル (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            // ユーザーがダイアログでOKを選択した場合の処理を定義します
            if (saveFileDialog.ShowDialog() == true)
            {
                // RenderTargetBitmapを作成し、Canvasを描画します
                RenderTargetBitmap renderBitmap = new((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96d, 96d, PixelFormats.Default);
                renderBitmap.Render(canvas);

                // BitmapEncoderを使用してRenderTargetBitmapを画像ファイルに書き込みます
                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                // ファイルに書き込みます
                using System.IO.FileStream fileStream = new(saveFileDialog.FileName, System.IO.FileMode.Create);
                encoder.Save(fileStream);
            }
        }

        // Canvasの内容を画像としてクリップボードにコピーするメソッド
        public static void CopyCanvasToClipboard(Canvas canvas)
        {
            // RenderTargetBitmapを作成し、Canvasを描画します
            var renderBitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96d, 96d, PixelFormats.Default);
            renderBitmap.Render(canvas);

            // Clipboard.SetImage()はStringMetadata非対応で例外になる環境があるため
            // BitmapSourceを一切渡さず、生バイトストリームのみでクリップボードに設定
            var pngEnc = new PngBitmapEncoder();
            pngEnc.Frames.Add(BitmapFrame.Create(renderBitmap));
            var pngStream = new System.IO.MemoryStream();
            pngEnc.Save(pngStream);

            var bmpEnc = new BmpBitmapEncoder();
            bmpEnc.Frames.Add(BitmapFrame.Create(renderBitmap));
            var bmpStream = new System.IO.MemoryStream();
            bmpEnc.Save(bmpStream);
            bmpStream.Position = 14;
            var dibBytes = new byte[bmpStream.Length - 14];
            bmpStream.Read(dibBytes, 0, dibBytes.Length);

            var dataObject = new DataObject();
            dataObject.SetData("PNG", pngStream, false);
            dataObject.SetData(DataFormats.Dib, new System.IO.MemoryStream(dibBytes), false);
            Clipboard.SetDataObject(dataObject, true);
        }

        // スケール取得メソッド
        private double GetScale()
        {
            double canvasWidth = Canvas.ActualWidth;
            double canvasHeight = Canvas.ActualHeight;
            canvasHeight = Math.Max(canvasHeight, 100.0); /// 仮
            double baseDimension = 1200.0;
            if (PileTopType == "キャプテンパイル工法" && PileTop.CaptainPile.PCRing != null)
            {
                baseDimension = Math.Max(PileTop.CaptainPile.PCRing.RD2 + 150.0, 1200.0);
            }
            else if (PileTopType == "FT-Pile構法" && PileTop.FTPile.FTCap != null)
            {
                baseDimension = Math.Max(PileTop.FTPile.FTCap.D2 + 150.0, 1200.0);
            }
            else if (PileTopType == "鉄筋定着工法")
            {
                baseDimension = Math.Max(PileTop.ConcreteOutDia, 1200.0);
            }

            return Math.Min(canvasWidth, canvasHeight) / baseDimension;
        }


        public void RedrawShapes()
        {
            // Canvas が未セットまたはサイズが未確定の場合は遅延再描画をスケジュールして終了
            if (Canvas == null || Canvas.ActualWidth <= 1.0 || Canvas.ActualHeight <= 1.0)
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    if (Canvas != null && Canvas.ActualWidth > 1.0 && Canvas.ActualHeight > 1.0)
                        RedrawShapes();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            // 初期化
            DrawingGeometry = new PathGeometry();
            TopSectionDrawingGeometry = new PathGeometry();

            Scale = GetScale();

            Canvas.Children.Clear();

            DrawGauge();

            // 主描画ロジック（既存ケースを保持しつつ null 安全に）
            if (PileTopType == "キャプテンパイル工法" && PileTop.CaptainPile.PCRing != null)
            {
                // 杭
                double pileconcreteOutDia = PileTop.CaptainPile.PCRing.D;
                DrawDonut("concrete", pileconcreteOutDia, 0.0);

                // PCリング
                double concreteOutDia = PileTop.CaptainPile.PCRing.RD2;
                double concreteInDia = PileTop.CaptainPile.PCRing.RD1;
                DrawDonut("concrete", concreteOutDia, concreteInDia);

                // 鋼リング
                double steelRingDia = PileTop.CaptainPile.PCRing.RD1 + 2 * PileTop.CaptainPile.PCRing.RingSteelTs;
                DrawDonut("steelPipe", steelRingDia, concreteInDia);

                // PCリング引張定着筋
                int number = PileTop.CaptainPile.PCRing.BarNum;
                double dia = ExtractNumber(PileTop.CaptainPile.PCRing.BarSize);
                double pcd = concreteOutDia - 2 * (40.0 + ExtractNumber(PileTop.CaptainPile.PCRing.SpiralDia)) - ExtractNumber(PileTop.CaptainPile.PCRing.BarSize);
                DrawMainBars(number, dia, pcd);

                if (PileTop.CaptainPile.CTPTensionRebars != null)
                {
                    if (PileTop.CaptainPile.CTPTensionRebars.HasTensionRebars && PileTop.CaptainPile.CTPTensionRebars.IsCircleArrangement)
                    {
                        int number1 = PileTop.CaptainPile.CTPTensionRebars.SelectedBarNumberCircle;
                        double dia1 = ExtractNumber(PileTop.CaptainPile.CTPTensionRebars.SelectedTensionAnchorDia);
                        double pcd1 = PileTop.CaptainPile.CTPTensionRebars.TDorTB;
                        DrawMainBars(number1, dia1, pcd1);
                    }
                    if (PileTop.CaptainPile.CTPTensionRebars.HasTensionRebars && PileTop.CaptainPile.CTPTensionRebars.IsSquareArrangement)
                    {
                        int number1 = PileTop.CaptainPile.CTPTensionRebars.SelectedBarNumberSquare;
                        double dia1 = ExtractNumber(PileTop.CaptainPile.CTPTensionRebars.SelectedTensionAnchorDia);
                        double pcd1 = PileTop.CaptainPile.CTPTensionRebars.TDorTB;
                        DrawSquareArrangedBars(number1, dia1, pcd1);
                    }
                }

                // 上面緩衝材
                if (PileTop.CaptainPile.Nu != 1.00)
                {
                    double nuDia = PileTop.CaptainPile.PCRing.D * PileTop.CaptainPile.Nu;
                    DrawDonut("concrete", nuDia, 0.0);
                }
            }

            else if (PileTopType == "FT-Pile構法" && PileTop.FTPile.FTCap != null)
            {
                // キャップ外径
                double capOutDia = PileTop.FTPile.FTCap.D2;
                DrawDonut("steelPipe", capOutDia, 0.0);

                // 杭径
                double piledia = PileTop.FTPile.FTCap.D1;
                DrawDonut("dashLine", piledia, 0.0);

                if (PileTop.FTPile.FTPileTensionBars.IsTensionType == true)
                {
                    int number1 = PileTop.FTPile.FTPileTensionBars.TensionAnchorNum;
                    double dia1 = 11;
                    double pcd1 = PileTop.FTPile.FTCap.D1 - 150; // need to change
                    DrawMainBars(number1, dia1, pcd1);
                }
            }

            else if (PileTopType == "鉄筋定着工法")
            {
                if (string.Equals(PileBodyType, "場所打ち鋼管コンクリート杭", StringComparison.Ordinal))
                {
                    if (PileSection != null)
                    {
                        double concreteOutDia = PileTop.ConcreteOutDia;
                        // 杭径（青）
                        DrawDonut("concrete", concreteOutDia, 0.0, NikkenBrush.SkyBlue);

                        // 鋼管（灰）
                        double outdia = PileSection.PipeDia;
                        double india = Math.Max(0.0, PileSection.PipeDia - 2 * PileSection.PipeTs);
                        DrawDonut("steelPipe", outdia, india, Brushes.Gray);

                        // 主筋（青）
                        int number2 = Math.Max(0, PileTop.MainBarNum2);
                        double dia2 = ExtractNumber(PileTop.MainBarSize2);
                        double pcd2 = Math.Max(0.0, concreteOutDia - PileTop.MainBarCenterCover2 * 2.0);
                        if (number2 > 0 && dia2 > 0 && pcd2 > 0)
                            DrawMainBars(number2, dia2, pcd2, Brushes.SkyBlue);

                        // 主筋（青）
                        int number1 = Math.Max(0, PileTop.MainBarNum1);
                        double dia1 = ExtractNumber(PileTop.MainBarSize1);
                        double pcd1 = Math.Max(0.0, concreteOutDia - PileTop.MainBarCenterCover1 * 2.0);
                        if (number1 > 0 && dia1 > 0 && pcd1 > 0)
                            DrawMainBars(number1, dia1, pcd1, Brushes.SkyBlue);

                    }
                }

                else if (string.Equals(PileBodyType, "既製コンクリート杭", StringComparison.Ordinal))
                {
                    if (PileSection != null)
                    {
                        double concreteOutDia = PileTop.ConcreteOutDia;
                        double concreteInDia = PileTop.ConcreteOutDia - 2 * PileTop.ConcreteThickness;
                        // 杭径（青）
                        DrawDonut("concrete", concreteOutDia, concreteInDia, NikkenBrush.SkyBlue);

                        // 鋼管（灰）
                        //double outdia = PileSection.PipeDia;
                        double india = Math.Max(0.0, PileSection.PipeDia - 2 * PileSection.PipeTs);
                        DrawDonut("steelPipe", india, 0, Brushes.Gray);

                        // 主筋（青）
                        int number1 = Math.Max(0, PileTop.MainBarNum1);
                        double dia1 = ExtractNumber(PileTop.MainBarSize1);
                        double pcd1 = Math.Max(0.0, concreteOutDia - PileTop.MainBarCenterCover1 * 2.0);
                        if (number1 > 0 && dia1 > 0 && pcd1 > 0)
                            DrawMainBars(number1, dia1, pcd1, Brushes.SkyBlue);
                    }
                }
            }

            // Canvas に Path を追加（TopSection を先に灰色で重ねる）
            if (TopSectionDrawingGeometry != null && TopSectionDrawingGeometry.Figures.Count > 0)
            {
                var topSectionPath = new Path()
                {
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1,
                    Data = TopSectionDrawingGeometry
                };
                Canvas.Children.Add(topSectionPath);
            }

            var path = new Path()
            {
                Stroke = NikkenBrush.SkyBlue,
                StrokeThickness = 1,
                Data = DrawingGeometry
            };
            Canvas.Children.Add(path);
        }


        private void DrawGauge()
        {
            // 目盛
            int minor = 100;
            int major = 500;

            LineGeometry line0 = new()
            {
                StartPoint = new Point(0.0, Canvas.ActualHeight / 2.0),
                EndPoint = new Point(50 * Scale, Canvas.ActualHeight / 2.0),
            };
            DrawingGeometry.AddGeometry(line0);
            int i = 1;

            while (i * minor * Scale < Canvas.ActualHeight / 2.0)
            {
                double linelength = i % (major / minor) == 0 ? 50 : 25;
                LineGeometry line1 = new()
                {
                    StartPoint = new Point(0.0, Canvas.ActualHeight / 2.0 + minor * i * Scale),
                    EndPoint = new Point(linelength * Scale, Canvas.ActualHeight / 2.0 + minor * i * Scale),
                };
                DrawingGeometry.AddGeometry(line1);

                LineGeometry line2 = new()
                {
                    StartPoint = new Point(0.0, Canvas.ActualHeight / 2.0 - minor * i * Scale),
                    EndPoint = new Point(linelength * Scale, Canvas.ActualHeight / 2.0 - minor * i * Scale),
                };
                DrawingGeometry.AddGeometry(line2);
                i += 1;
            }
        }

        // ドーナツ描画メソッド
        private void DrawDonut(string type, double outdia, double india, Brush? stroke = null)
        {
            PathGeometry geometry = new();
            EllipseGeometry outerCircle = new(new Point(Canvas.ActualWidth * 0.5, Canvas.ActualHeight * 0.5), outdia * 0.5 * Scale, outdia * 0.5 * Scale);
            EllipseGeometry innerCircle = new(new Point(Canvas.ActualWidth * 0.5, Canvas.ActualHeight * 0.5), india * 0.5 * Scale, india * 0.5 * Scale);
            geometry.AddGeometry(outerCircle);
            geometry.AddGeometry(innerCircle);
            Path donutPath = new();
            // 指定がなければ従来色（青）を使う
            Brush useStroke = stroke ?? NikkenBrush.SkyBlue;

            if (type == "concrete")
            {
                donutPath.Stroke = useStroke; // 線の色
                                              //donutPath.Fill = Brushes.NavajoWhite;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }
            else if (type == "steelPipe")
            {
                donutPath.Stroke = useStroke; // 線の色
                                              //donutPath.Fill = Brushes.WhiteSmoke;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }
            else if (type == "hoop")
            {
                donutPath.Stroke = useStroke;
                //donutPath.Fill = Brushes.AntiqueWhite;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }
            else if (type == "dashLine")
            {
                donutPath.StrokeDashArray = new DoubleCollection([4, 2]); // 4ピクセルの破線と2ピクセルの間隔
                donutPath.Stroke = useStroke;
                //donutPath.Fill = Brushes.AntiqueWhite;
                donutPath.StrokeThickness = 1;
                donutPath.Data = geometry;
            }
            Canvas.Children.Add(donutPath); // Canvasに追加
        }

        // PC鋼材描画メソッド
        private void DrawTendons(double dia)
        {
            EllipseGeometry circleGeometry = new(new Point(Canvas.ActualWidth * 0.5, Canvas.ActualHeight * 0.5), dia * 0.5 * Scale, dia * 0.5 * Scale);

            Path dashedCirclePath = new()
            {
                Stroke = NikkenBrush.SkyBlue, // 線の色
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection([4, 2]), // 破線パターン
                Data = circleGeometry
            };
            Canvas.Children.Add(dashedCirclePath); // Canvasに追加
        }

        // 主筋描画メソッド
        private void DrawMainBars(double number, double dia, double pcd, Brush? stroke = null)
        {
            PathGeometry geo = new();

            for (int i = 0; i < number; i++)
            {
                double X = pcd / 2.0 * Math.Cos(i / number * 2 * Math.PI) * Scale;
                double Y = pcd / 2.0 * Math.Sin(i / number * 2 * Math.PI) * Scale;
                EllipseGeometry ellipse = new(new Point(Canvas.ActualWidth * 0.5 + X, Canvas.ActualHeight * 0.5 + Y), dia * 0.5 * Scale, dia * 0.5 * Scale);
                geo.AddGeometry(ellipse);
            }
            Path barPath = new()
            {
                Stroke = stroke ?? NikkenBrush.SkyBlue,
                //Fill = Brushes.DarkBlue,
                StrokeThickness = 1,
                //StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }), // 破線パターン
                Data = geo
            };
            Canvas.Children.Add(barPath); // Canvasに追加
        }

        private void DrawSquareArrangedBars(int number, double dia, double tB)
        {
            PathGeometry geo = new();
            double X;
            double Y;
            EllipseGeometry ellipse;
            for (int i = 0; i < number / 4 + 1; i++)
            {
                for (int j = -1; j < 2; j += 2)
                {
                    X = (-tB * 0.5 + tB * i / (number / 4)) * Scale;
                    Y = -tB * 0.5 * j * Scale;
                    ellipse = new EllipseGeometry(new Point(Canvas.ActualWidth * 0.5 + X, Canvas.ActualHeight * 0.5 + Y), dia * 0.5 * Scale, dia * 0.5 * Scale);
                    geo.AddGeometry(ellipse);

                    if (i == 0 || i == number / 4)
                    {
                        for (int k = 1; k < number / 4; k++)
                        {
                            X = -tB * 0.5 * j * Scale;
                            Y = (-tB * 0.5 + tB * k / (number / 4)) * Scale;
                            ellipse = new EllipseGeometry(new Point(Canvas.ActualWidth * 0.5 + X, Canvas.ActualHeight * 0.5 + Y), dia * 0.5 * Scale, dia * 0.5 * Scale);
                            geo.AddGeometry(ellipse);
                        }
                    }
                }
            }
            Path barPath = new()
            {
                Stroke = NikkenBrush.SkyBlue,
                //Fill = Brushes.DarkBlue,
                StrokeThickness = 1,
                //StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }), // 破線パターン
                Data = geo
            };
            Canvas.Children.Add(barPath); // Canvasに追加
        }


        private static double ExtractNumber(string input)
        {
            if (input == null)
            {
                return 0;
            }

            // 数字以外の文字を削除してから double に変換
            string numericPart = Regex.Replace(input, "[^0-9.]", "");
            if (double.TryParse(numericPart, out double result))
            {
                return result;
            }
            // 変換できない場合は例外をスローするか、適切な処理を行う
            throw new ArgumentException("入力文字列に数字が含まれていません。");
        }
    }
}

