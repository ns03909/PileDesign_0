using PileDesignCore.PileLibrary;
using PileDesignCore.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Microsoft.Win32;
using System.Windows;

namespace PileDesignCore
{
    [Serializable]
    public class PileTopViewModel : BaseViewModel
    {
        private string _selectedPileTopType;
        public string SelectedPileTopType
        {
            get => _selectedPileTopType;
            set => SetProperty(ref _selectedPileTopType, value);
        }

        // パイルキャップ
        private double _pileCapFc = 24.0;
        public double PileCapFc
        {
            get => _pileCapFc;
            set
            {
                if (SetProperty(ref _pileCapFc, value))
                {
                    SetEc();
                }
            }
        }

        private double _pileCapGamma = 23.0;
        public double PileCapGamma
        {
            get => _pileCapGamma;
            set
            {
                if (SetProperty(ref _pileCapGamma, value))
                {
                    SetEc();
                }
            }
        }

        private double _pileCapEc = 3.35 * Math.Pow(10, 4) * Math.Pow(23.0 / 24.0, 2.0) * Math.Pow(24.0 / 60.0, 1.0 / 3.0);
        public double PileCapEc
        {
            get => _pileCapEc;
            set => SetProperty(ref _pileCapEc, value);
        }

        internal void SetEc()
        {
            PileCapEc = 3.35 * Math.Pow(10, 4) * Math.Pow(PileCapGamma / 24.0, 2.0) * Math.Pow(PileCapFc / 60.0, 1.0 / 3.0);
        }

        private ObservableCollection<Spec> _selectedPileTopSpecification;
        public ObservableCollection<Spec> SelectedPileTopSpecification
        {
            get => _selectedPileTopSpecification;
            set => SetProperty(ref _selectedPileTopSpecification, value);
        }
        
        // PCリングオプション
        public ObservableCollection<string> PCRingOption { get; } = new ObservableCollection<string>();
        //public ObservableCollection<string> PCD_squareOption { get; set; } = new ObservableCollection<string>();
        //public ObservableCollection<string> PCD_circleOption { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> FTCapOption { get; set; } = new ObservableCollection<string>();

        private readonly PCRingLoader _pcRingLoader = new PCRingLoader();
        //private readonly CaptainPileTensionBarPCDLoader _captainPileTensionBarPCDLoader = new CaptainPileTensionBarPCDLoader();
        private readonly FTCapLoader _ftCapLoader = new FTCapLoader();
        
        public List<PCRing> PCRings { get; set; } = new List<PCRing>();
        //public List<CaptainPileTensionBarPCD> CaptainPileTensionBarPCDsSquare { get; set; } = new List<CaptainPileTensionBarPCD>();
        //public List<CaptainPileTensionBarPCD> CaptainPileTensionBarPCDsCircle { get; set; } = new List<CaptainPileTensionBarPCD>();
        public List<(int, string, string)> Description { get; set; } = new List<(int, string, string)>();

        // FTパイルオプション
        
        public List<FTCap> FTCaps { get; set; } = new List<FTCap>();

        public int SelectedFTCap { get; set; }

        //チャート関連
        //[NonSerialized]
        //public Chart ChartMN = new Chart();
        //ChartArea Chartarea1 = new ChartArea();
        //Series SeriesDamage = new Series();
        //Series SeriesUltimate = new Series();

        //// M thetaチャート関連
        //[NonSerialized]
        //public Chart ChartThetaM = new Chart();
        //ChartArea Chartarea2 = new ChartArea();
        //List<Series> Series2s = new List<Series>();

        public FTPile FTPile { get; set; } = new FTPile();
        public CaptainPile CaptainPile { get; set; } = new CaptainPile();

        // コンストラクタ
        public PileTopViewModel()
        {
            InitializeOptions();
            //InitializePCRingOptions();
            //InitializeCaptainPileTensionBarPCDs();
            //InitializeFTCaps();
            // チャート初期化
            InitializeCharts();
        }

        private void InitializeOptions()
        {
            LoadPCRingOptions();
            //LoadCaptainPileTensionBarPCDs();
            LoadFTCaps();
        }

        private void LoadPCRingOptions()
        {
            PCRings = _pcRingLoader.LoadFromCsv(@"..\..\PileLibrary\PCRing.csv");
            foreach (var pcRing in PCRings)
            {
                PCRingOption.Add($"{pcRing.Name}");
            }
        }

        //private void LoadCaptainPileTensionBarPCDs()
        //{
        //    CaptainPileTensionBarPCDsSquare = _captainPileTensionBarPCDLoader.LoadFromCsv(@"..\..\PileLibrary\CaptainPileTensionBarPCD_square.csv");
        //    foreach (var pcdSquare in CaptainPileTensionBarPCDsSquare)
        //    {
        //        PCD_squareOption.Add($"{pcdSquare.D}-{pcdSquare.Nu}");
        //    }

        //    CaptainPileTensionBarPCDsCircle = _captainPileTensionBarPCDLoader.LoadFromCsv(@"..\..\PileLibrary\CaptainPileTensionBarPCD_circle.csv");
        //    foreach (var pcdCircle in CaptainPileTensionBarPCDsCircle)
        //    {
        //        PCD_circleOption.Add($"{pcdCircle.D}-{pcdCircle.Nu}");
        //    }
        //}

        private void LoadFTCaps()
        {
            FTCaps = _ftCapLoader.LoadFromCsv(@"..\..\PileLibrary\FTCap.csv");
            foreach (var ftCap in FTCaps)
            {
                FTCapOption.Add($"{ftCap.Phi}");
            }
        }
        // キャプテンパイルPCリングライブラリ読み込みメソッド
        //private void InitializePCRingOptions()
        //{
        //    PCRingLoader _PCRingLoader = new PCRingLoader();

        //    PCRings = _PCRingLoader.LoadFromCsv(@"..\..\PileLibrary\PCRing.csv");
        //    foreach (var _PCRing in PCRings)
        //    {
        //        PCRingOption.Add($"{_PCRing.Name}");
        //    }
        //}

        // キャプテンパイル引張鉄筋ライブラリ読み込みメソッド
        //private void InitializeCaptainPileTensionBarPCDs()
        //{
        //    CaptainPileTensionBarPCDLoader _CaptainPileTensionBarPCDLoader = new CaptainPileTensionBarPCDLoader();

        //    CaptainPileTensionBarPCDs_square = _CaptainPileTensionBarPCDLoader.LoadFromCsv(@"..\..\PileLibrary\CaptainPileTensionBarPCD_square.csv");
        //    foreach (var PCD_square in CaptainPileTensionBarPCDs_square)
        //    {
        //        PCD_squareOption.Add($"{PCD_square.D}-{PCD_square.Nu}");
        //    }

        //    CaptainPileTensionBarPCDs_circle = _CaptainPileTensionBarPCDLoader.LoadFromCsv(@"..\..\PileLibrary\CaptainPileTensionBarPCD_circle.csv");
        //    foreach (var PCD_circle in CaptainPileTensionBarPCDs_circle)
        //    {
        //        PCD_circleOption.Add($"{PCD_circle.D}-{PCD_circle.Nu}");
        //    }
        //}

        // FTキャップライブラリ読み込みメソッド
        //private void InitializeFTCaps()
        //{
        //    FTCapLoader _FTCapLoader = new FTCapLoader();
        //    FTCaps = _FTCapLoader.LoadFromCsv(@"..\..\PileLibrary\FTCap.csv");
        //    foreach (var _FTCap in FTCaps)
        //    {
        //        FTCapOption.Add($"{_FTCap.Phi}");
        //    }
        //}

        public void InitializeCharts()
        {
            //InitializeChart(ChartMN);
            //InitializeChart(ChartThetaM);
        }

        // チャート初期化
        //public void InitializeChart(Chart chart)
        //{
        //    chart.Legends.Add(new Legend());
        //    var chartArea = chart.ChartAreas.Add("Area");
        //    chartArea.AxisX.Title = "X Axis Title";
        //    chartArea.AxisY.Title = "Y Axis Title";
            // Add other chart initialization logic here
            ////チャート関連
            //ChartMN = new Chart();
            ////Chartarea1 = new ChartArea();
            //ChartMN.Legends.Add(new Legend("MNLegend"));
            //ChartMN.Legends[0].Docking = Docking.Right;
            ////ChartMN.Legends[0].Alignment = StringAlignment.Center;
            ////ChartMN.Legends[0].BackColor = Color.Yellow;
            //Chartarea1 = ChartMN.ChartAreas.Add("Area1");

            //SeriesDamage = new Series();
            //SeriesUltimate = new Series();

            ////ChartAreaの設定(グラフタイトル、軸ラベル)
            //Chartarea1.AxisX.Title = "N (kN)";
            //Chartarea1.AxisY.Title = "M (kNm)";

            //// Y軸の目盛りを設定する
            //Chartarea1.AxisY.Minimum = 0; // Y軸の最小値を0に設定

            ////Seriesの初期設定(グラフの種類、線の太さ、凡例)

            //SeriesDamage.ChartType = SeriesChartType.Line;
            //SeriesDamage.BorderWidth = 1;
            //SeriesDamage.BorderDashStyle = ChartDashStyle.Dash;
            //SeriesDamage.Color = NikkenDrawingColors.Green;
            //SeriesDamage.LegendText = "(低減前)損傷限界";

            //SeriesUltimate.ChartType = SeriesChartType.Line;
            //SeriesUltimate.BorderWidth = 1;
            //SeriesUltimate.BorderDashStyle = ChartDashStyle.Dash;
            //SeriesUltimate.Color = NikkenDrawingColors.PaleRed;
            //SeriesUltimate.LegendText = "(低減前)安全限界";

            ////ChartにTitle,Seriesを追加
            //ChartMN.Series.Add(SeriesDamage);
            //ChartMN.Series.Add(SeriesUltimate);

            //SeriesDamage.Points.Add(0.0, 0.0); // tentative
            //                                    //ChartMN.Show();
        //}

        // チャート初期化
        //public void InitializeChart2()
        //{
        //    //チャート関連
        //    //ChartThetaM = new Chart();
        //    //Chartarea1 = new ChartArea();
        //    ChartThetaM.Legends.Add(new Legend("MThetaLegend"));
        //    ChartThetaM.Legends[0].Docking = Docking.Right;
        //    //ChartThetaM.Legends[0].Alignment = StringAlignment.Center;
        //    //ChartThetaM.Legends[0].BackColor = Color.Yellow;
        //    Chartarea2 = ChartThetaM.ChartAreas.Add("Area2");

        //    //ChartAreaの設定(グラフタイトル、軸ラベル)
        //    Chartarea2.AxisX.Title = "theta (rad)";
        //    Chartarea2.AxisY.Title = "M (kNm)";

        //    // Y軸の目盛りを設定する
        //    Chartarea2.AxisY.Minimum = 0; // Y軸の最小値を0に設定

        //}

        // Series2 seriesのアップデートメソッド

        public void ChartUpdate()
        {
            List<double> axialForces;
            List<(List<double>, List<double>)> thetasMs;

            if (SelectedPileTopType == "キャプテンパイル工法" && CaptainPile.Ns != null && CaptainPile.ThetasMs != null)
            {
                axialForces = CaptainPile.Ns;
                thetasMs = CaptainPile.ThetasMs;
                UpdateChartThetaMSeries(axialForces, thetasMs);
            }
            else if (SelectedPileTopType == "FT-Pile構法" && FTPile.Ns != null && FTPile.ThetasMs != null)
            {
                axialForces = FTPile.Ns;
                thetasMs = FTPile.ThetasMs;
                UpdateChartThetaMSeries(axialForces, thetasMs);
            }


        }

        public void UpdateChartThetaMSeries(List<double> axialForces, List<(List<double>, List<double>)> thetasMs)
        {
            //if (axialForces != null && axialForces.Count > 0)
            //{
            //    for (int i = 0; i < axialForces.Count; i++)
            //    {

            //        Series series = new Series();

            //        double axialForce = axialForces[i];
            //        List<double> thetas = thetasMs[i].Item1;
            //        List<double> moments = thetasMs[i].Item2;

            //        for (int j = 0; j < thetas.Count; j++)
            //            {
            //                series.Points.AddXY(thetas[j], moments[j]);
            //            }
            //        ChartThetaM.Series.Add(series);
            //    }
            //}
        }

        public void UpdateSeries2()
        {
            //foreach (Series series in ChartThetaM.Series)
            //{
            //    series.ChartType = SeriesChartType.Line;
            //    series.BorderWidth = 1;
            //    series.BorderDashStyle = ChartDashStyle.Dash;
            //    series.Color = NikkenDrawingColors.Green;
            //}
        }

        public void ReplaceSeries2(List<double> phis, List<double> moments)
        {
            //double maxN = double.MinValue;
            //double minN = double.MaxValue;

            //// グラフのデータをクリア
            //Series2s = new List<Series>();
            //foreach (Series series in Series2s)
            //{
            //    series.Points.Clear();

            //    for (int i = 0; i < phis.Count; i++)
            //    {
            //        series.Points.AddXY(phis[i], moments[i]);
            //        maxN = Math.Max(maxN, phis[i]);
            //        minN = Math.Min(minN, phis[i]);
            //    }

            //    bool shouldBreak = false;
            //    for (int i = 0; i <= 5; i++)
            //    {
            //        for (int j = 0; j <= 5; j++)
            //        {
            //            double minScaleValue = -j * 2 * Math.Pow(10, i);
            //            if (minScaleValue < minN)
            //            {
            //                //Chartarea2.AxisX.Minimum = minScaleValue;
            //                //Chartarea2.AxisX.Interval = Math.Abs(minScaleValue);
            //                shouldBreak = true;
            //                break;
            //            }
            //        }
            //        if (shouldBreak)
            //        {
            //            break;
            //        }
            //    }
            //}
        }

        public void ReplaceSeries(
            List<double> damageN, List<double> damageM,
            List<double> ultimateN, List<double> ultimateM
            )
        {
            // グラフのデータをクリア
            //SeriesDamage.Points.Clear();
            //SeriesUltimate.Points.Clear();

            double maxN = double.MinValue;
            double minN = double.MaxValue;

            for (int i = 0; i < damageN.Count; i++)
            {
                //SeriesDamage.Points.AddXY(damageN[i], damageM[i]);
                maxN = Math.Max(maxN, damageN[i]);
                minN = Math.Min(minN, damageN[i]);
            }

            for (int i = 0; i < ultimateN.Count; i++)
            {
                //SeriesUltimate.Points.AddXY(ultimateN[i], ultimateM[i]);
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
                        //Chartarea1.AxisX.Minimum = minScaleValue;
                        //Chartarea1.AxisX.Interval = Math.Abs(minScaleValue);
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
        internal List<double> GetMultipliedListValues(List<double> originalList, double multiplier)
        {
            List<double> result = new List<double>();
            for (int i = 0; i < originalList.Count; i++)
            {
                result.Add(originalList[i] * multiplier);
            }
            return result;
        }

        //public void ChartUpdate()
        //{

        //    List<double> axialForces;
        //    List<(List<double>, List<double>)> thetasMs;


        //    if (SelectedPileTopType == "キャプテンパイル工法")
        //    {
        //        axialForces = CaptainPile.Ns;
        //        thetasMs = CaptainPile.ThetasMs;
        //    }

        //    else //if (SelectedPileTopType == "FT-Pile構法")
        //    {
        //        axialForces = FTPile.Ns;
        //        thetasMs = FTPile.ThetasMs;
        //    }

        //    if (axialForces != null && axialForces.Count > 0)
        //    {
        //        Series2s = new List<Series>();

        //        for (int i = 0; i < axialForces.Count; i++)
        //        {
        //            Series2s.Add(new Series());
        //            double axialForce = axialForces[i];

        //            List<double> thetas = thetasMs[i].Item1;
        //            List<double> moments = thetasMs[i].Item2;

        //            for (int j = 0; j < thetas.Count; j++)
        //            {
        //                Series2s[Series2s.Count - 1].Points.AddXY(thetas[j], moments[j]);
        //            }
        //            //ChartThetaM = new Chart();
        //            ChartThetaM.Series.Add(Series2s[Series2s.Count - 1]);

        //        }
        //    }
        //}

        // Canvas画像を保存するメソッド
        public void SaveImage(Canvas canvas)
        {
            // ファイル保存ダイアログを作成し、デフォルトの保存場所をデスクトップに設定します
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "PNGファイル (*.png)|*.png|すべてのファイル (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            // ユーザーがダイアログでOKを選択した場合の処理を定義します
            if (saveFileDialog.ShowDialog() == true)
            {
                // RenderTargetBitmapを作成し、Canvasを描画します
                RenderTargetBitmap renderBitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96d, 96d, PixelFormats.Default);
                renderBitmap.Render(canvas);

                // BitmapEncoderを使用してRenderTargetBitmapを画像ファイルに書き込みます
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                // ファイルに書き込みます
                using (FileStream fileStream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
        }

        // Canvasの内容を画像としてクリップボードにコピーするメソッド
        public void CopyCanvasToClipboard(Canvas canvas)
        {
            // RenderTargetBitmapを作成し、Canvasを描画します
            var renderBitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96d, 96d, PixelFormats.Default);
            renderBitmap.Render(canvas);

            // Clipboardに画像をコピーします
            Clipboard.SetImage(renderBitmap);
        }
    }
}
