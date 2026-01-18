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
        public ObservableCollection<string> FTCapOption { get; set; } = new ObservableCollection<string>();

        private readonly PCRingLoader _pcRingLoader = new PCRingLoader();
        private readonly FTCapLoader _ftCapLoader = new FTCapLoader();

        public List<PCRing> PCRings { get; set; } = new List<PCRing>();
        public List<(int, string, string)> Description { get; set; } = new List<(int, string, string)>();

        // FTパイルオプション
        public List<FTCap> FTCaps { get; set; } = new List<FTCap>();

        public int SelectedFTCap { get; set; }

        public FTPile FTPile { get; set; } = new FTPile();
        public CaptainPile CaptainPile { get; set; } = new CaptainPile();

        // コンストラクタ
        public PileTopViewModel()
        {
            InitializeOptions();
            InitializeCharts();
        }

        private void InitializeOptions()
        {
            LoadPCRingOptions();
            LoadFTCaps();
        }

        private void LoadPCRingOptions()
        {
            PCRings = _pcRingLoader.LoadFromCsv(@"..\..\PileLibrary\PCRing.csv");
            PCRingOption = PCRings.ToObservableStringCollection(p => p.Name);
        }

        private void LoadFTCaps()
        {
            FTCaps = _ftCapLoader.LoadFromCsv(@"..\..\PileLibrary\FTCap.csv");
            FTCapOption = FTCaps.ToObservableStringCollection(f => $"{f.Phi}");
        }

        public void InitializeCharts()
        {
        }

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
        }

        public void UpdateSeries2()
        {
        }

        public void ReplaceSeries2(List<double> phis, List<double> moments)
        {
        }

        public void ReplaceSeries(
            List<double> damageN, List<double> damageM,
            List<double> ultimateN, List<double> ultimateM
            )
        {
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
