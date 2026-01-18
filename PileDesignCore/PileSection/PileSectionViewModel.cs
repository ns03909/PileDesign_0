using PileDesignCore.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Microsoft.Win32;
using System.Windows;
using PileDesignCore.Shared;
using PileDesignCore.InsituPileSection;




namespace PileDesignCore
{
    [Serializable]
    public class PileSectionViewModel : BaseViewModel

    {
        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        private string _selectedPileBodyType = "場所打ち鉄筋コンクリート杭";
        public string SelectedPileBodyType
        {
            get => _selectedPileBodyType;
            set
            {
                if (SetProperty(ref _selectedPileBodyType, value))
                {
                    ResetSectionProperties();
                }
            }
        }

        private string _selectedPileSectionType;
        public string SelectedPileSectionType
        {
            get => _selectedPileSectionType;
            set
            {
                if (SetProperty(ref _selectedPileSectionType, value))
                {
                    RecalculatePileDia();
                }
            }
        }

        private string _selectedSteelPipe;
        public string SelectedSteelPipe
        {
            get => _selectedSteelPipe;
            set => SetProperty(ref _selectedSteelPipe, value);
        }
        
        public ObservableCollection<string> InsituPileSectionTypesOption { get; } = new ObservableCollection<string>()
        {
            "場所打ち鉄筋コンクリート杭",
            "場所打ち鋼管コンクリート杭",
        };

        // 場所打ち鋼管コンクリート杭の部位 
        public ObservableCollection<string> InsituSteelPileSectionTypeOption { get; } = new ObservableCollection<string>()
        {
            "鋼管コンクリート部",
            "鉄筋コンクリート部",
        };

        public ObservableCollection<string> PreCastConcretePileSectionTypeOption { get; } = new ObservableCollection<string>()
        {
            "PHC杭",
            "PRC杭",
            "SC杭"
        };

        public ObservableCollection<string> SteelPipePileSectionTypeOption { get; } = new ObservableCollection<string>()
        {
            "鋼管杭",
        };

        // 杭径
        private double _pileDiameter = 1200.0;
        public double PileDiameter
        {
            get => _pileDiameter;
            set
            {
                if (SetProperty(ref _pileDiameter, value))
                {
                }
            }
        }

        // コンクリート外径
        private double _concreteOutDia = 1200.0;
        public double ConcreteOutDia
        {
            get => _concreteOutDia;
            set
            {
                if (SetProperty(ref _concreteOutDia, value))
                {
                    RecalculatePileDia();
                }
            }
        }

        public void RecalculatePileDia()
        {
            if (SelectedPileBodyType == "場所打ち鉄筋コンクリート杭")
            {
                PileDiameter = ConcreteOutDia;
                ConcreteThickness = ConcreteOutDia / 2;
                MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;

            }

            else if (SelectedPileBodyType == "場所打ち鋼管コンクリート杭")
            {
                if (SelectedPileSectionType == "鉄筋コンクリート部")
                {
                    PileDiameter = ConcreteOutDia;
                    ConcreteThickness = ConcreteOutDia / 2;
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                }
                else if (SelectedPileSectionType == "鋼管コンクリート部")
                {
                    PileDiameter = PipeDia;
                    ConcreteOutDia = PipeDia - PipeTs * 2.0;
                    ConcreteThickness = ConcreteOutDia / 2.0;
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                }
            }
            else if (SelectedPileBodyType == "鋼管杭")
            {
                PileDiameter = PipeDia;
            }
        }

        //杭断面変更時
        private void ResetSectionProperties()
        {
            if (SelectedPileBodyType == "場所打ち鉄筋コンクリート杭")
            {
                ConcreteOutDia = 1200.0;
                MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                SelectedPileSectionType = "鉄筋コンクリート";

            }
            else if (SelectedPileBodyType == "場所打ち鋼管コンクリート杭")
            {
                ConcreteOutDia = 0.0;
                PipeDia = 1200.0;
                PipeTs = 16.0;

            }
            else if (SelectedPileBodyType == "既製コンクリート杭")
            {
                ConcreteOutDia = 0.0;
                MainBarNum = 0;
                PipeDia = 0.0;
                PipeTs = 0.0;
            }
            else if (SelectedPileBodyType == "鋼管杭")
            {
                ConcreteOutDia = 0.0;
                MainBarNum = 0;
                PipeDia = 0.0;
                PipeTs = 0.0;
            }
        }

        // コンクリート肉厚
        private double _concreteThickness;
        public double ConcreteThickness
        {
            get => _concreteThickness;
            set => SetProperty(ref _concreteThickness, value);
        }

        // コンクリート基準強度
        private double _concreteFc = 27.0;
        public double ConcreteFc
        {
            get => _concreteFc;
            set
            {
                if (SetProperty(ref _concreteFc, value))
                {
                    RecalculateConcreteE();
                }
            }
        }

        // コンクリートプレストレス
        private double _prestress;
        public double Prestress
        {
            get => _prestress;
            set => SetProperty(ref _prestress, value);
        }


        // コンクリート単位体積重量
        private double _concreteGamma = 23.0;
        public double ConcreteGamma
        {
            get => _concreteGamma;
            set
            {
                if (SetProperty(ref _concreteGamma, value))
                {
                    RecalculateConcreteE();
                }
            }
        }

        // コンクリート現場管理係数
        private double _concreteGsi = 1.00;
        public double ConcreteGsi
        {
            get => _concreteGsi;
            set
            {
                if (SetProperty(ref _concreteGsi, value))
                {
                    RecalculateConcreteE();
                };
            }
        }

        // コンクリート縦弾性係数
        private double _concreteE;
        public double ConcreteE
        {
            get => _concreteE;
            set => SetProperty(ref _concreteE, value);
        }

        public void RecalculateConcreteE()
        {
            ConcreteE = 3.35 * Math.Pow(10, 4) * Math.Pow(ConcreteGamma / 24.0, 2.0) * Math.Pow(ConcreteGsi * ConcreteFc / 60.0, 1.0 / 3.0);
        }

        // コンクリートプレストレス
        private double _concreteSigmaE;
        public double ConcreteSigmaE
        {
            get => _concreteSigmaE;
            set => SetProperty(ref _concreteSigmaE, value);
        }

        // 選択したPrecast杭
        private string _selectedPrecastPileName;
        public string SelectedPrecastPileName
        {
            get => _selectedPrecastPileName;
            set
            {
                if (SetProperty(ref _selectedPrecastPileName, value))
                {
                    RecalculateSelectedPrecastPile();
                }
            }
        }

        PrecastPile SelectedPrecastPile = new PrecastPile();
        SteelPipePile SelectedSteelPipePile = new SteelPipePile();

        readonly SteelPipePileLoader steelPipeLoader = new SteelPipePileLoader();
        readonly List<SteelPipePile> steelPipePiles;
        readonly PrecastPileLoader precastPileLoader = new PrecastPileLoader();
        //List<PrecastPile> precastPiles;

        private void RecalculateSelectedPrecastPile()
        {
            List<PrecastPile> precastPiles =new List<PrecastPile>();
            if (SelectedPileSectionType == "PHC杭") { precastPiles = PHCs; }
            else if (SelectedPileSectionType == "PRC杭") { precastPiles = PRCs; }
            else if (SelectedPileSectionType == "SC杭") { precastPiles = SCs; }

            foreach (PrecastPile pipe in precastPiles)
            {
                if (SelectedPrecastPileName == pipe.Name)
                {
                    SelectedPrecastPile = pipe;
                    PileDiameter = pipe.PileDiameter;
                    ConcreteOutDia = pipe.PileDiameter - pipe.Ts * 2;
                    ConcreteThickness = pipe.PileThickness - pipe.Ts;
                    TendonDp = pipe.Dp;
                    MainBarNum = pipe.Nr;
                    MainBarSize = pipe.RDesignation;
                    MainBarDr = pipe.Dr;
                    MainBarCenterCover = (ConcreteOutDia - MainBarDr) / 2.0;
                    PipeDia = pipe.PileDiameter;
                    PipeTs = pipe.Ts;
                    ConcreteFc = pipe.Fc;
                    Prestress = pipe.SigmaE;
                    ConcreteE = pipe.Ec;
                    TendonAp = pipe.Ap;
                    TendonEp = pipe.Ep;
                    TendonSigmaPy = pipe.Ftp;
                    TendonSigmaPu = pipe.SigmaPu;
                    MainBarAg = pipe.Ag;
                    MainBarEr = pipe.Er;
                    PipeEs = pipe.Es;
                    HoopPsSigmay=pipe.PsSigmaY;

                    break;
                }
            }
        }

        // 選択したS杭
        private string _selectedSteelPipePileName;
        public string SelectedSteelPipePileName
        {
            get => _selectedSteelPipePileName;
            set
            {
                if (SetProperty(ref _selectedSteelPipePileName, value))
                {
                    RecalculateSelectedSteelPipePipe();
                }
            }
        }

        // S杭選択時
        private void RecalculateSelectedSteelPipePipe()
        {
            foreach (var pipe in steelPipePiles)
            {
                if (SelectedSteelPipePileName == $"{pipe.Diameter}x{pipe.Thickness}")
                {
                    SelectedSteelPipePile = pipe;
                    PipeDia = pipe.Diameter;
                    PipeTs = pipe.Thickness;

                    break;
                }
            }
        }

        // PC鋼線断面積
        private double _tendonAp;
        public double TendonAp
        {
            get => _tendonAp;
            set => SetProperty(ref _tendonAp, value);
        }

        // PC鋼線配置直径断面積
        private double _tendonDp;
        public double TendonDp
        {
            get => _tendonDp;
            set => SetProperty(ref _tendonDp, value);
        }

        // PC鋼線降伏強さ
        private double _tendonSigmaPy;
        public double TendonSigmaPy
        {
            get => _tendonSigmaPy;
            set => SetProperty(ref _tendonSigmaPy, value);
        }

        // PC鋼線引張強さ
        private double _tendonSigmaPu;
        public double TendonSigmaPu
        {
            get => _tendonSigmaPu;
            set => SetProperty(ref _tendonSigmaPu, value);
        }

        // PC鋼線縦弾性係数
        private double _tendonEp;
        public double TendonEp
        {
            get => _tendonEp;
            set => SetProperty(ref _tendonEp, value);
        }

        public ObservableCollection<string> MainBarSizeOption { get; } = new ObservableCollection<string>()
        {
            "D10","D13","D16","D19","D22","D25","D29","D32","D35","D38","D41"
        };

        public ObservableCollection<string> MainBarSpecOption { get; } = new ObservableCollection<string>()
        {
            "SD295", "SD345", "SD390", "SD490"
        };

        // PHC
        public ObservableCollection<string> PHCOption { get; } = new ObservableCollection<string>();
        public List<PrecastPile> PHCs { get; } = new List<PrecastPile>();

        // PRC
        public ObservableCollection<string> PRCOption { get; } = new ObservableCollection<string>();
        public List<PrecastPile> PRCs { get; } = new List<PrecastPile>();

        // SC
        public ObservableCollection<string> SCOption { get; } = new ObservableCollection<string>();
        public List<PrecastPile> SCs { get; } = new List<PrecastPile>();

        // 鋼管
        public ObservableCollection<string> SteelPipeOption { get; } = new ObservableCollection<string>();


        // 鉄筋径
        private string _mainBarSize = "D29";
        public string MainBarSize
        {
            get => _mainBarSize;
            set => SetProperty(ref _mainBarSize, value);
        }

        // 鉄筋本数
        private int _mainBarNum = 16;
        public int MainBarNum
        {
            get => _mainBarNum;
            set => SetProperty(ref _mainBarNum, value);
        }

        // 鉄筋断面積
        private double _mainBarAg;
        public double MainBarAg
        {
            get => _mainBarAg;
            set => SetProperty(ref _mainBarAg, value);
        }

        // 鉄筋規格
        private string _mainBarSpec = "SD390";
        public string MainBarSpec
        {
            get => _mainBarSpec;
            set => SetProperty(ref _mainBarSpec, value);
        }

        // 鉄筋配置直径
        private double _mainBarDr;
        public double MainBarDr
        {
            get => _mainBarDr;
            set => SetProperty(ref _mainBarDr, value);
        }

        // 鉄筋規格降伏点
        private double _mainbarFtr;
        public double MainBarFtr
        {
            get => _mainbarFtr;
            set => SetProperty(ref _mainbarFtr, value);
        }

        // 鉄筋重心かぶり厚
        private double _mainbarCenterCover = 200.0;
        public double MainBarCenterCover
        {
            get => _mainbarCenterCover;
            //set => SetProperty(ref _mainbarCenterCover, value);
            set
            {
                if (SetProperty(ref _mainbarCenterCover, value))
                {
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                }
            }
        }

        // 鉄筋縦弾性係数
        private double _mainbarEr;
        public double MainBarEr
        {
            get => _mainbarEr;
            set => SetProperty(ref _mainbarEr, value);
        }

        // せん断補強筋径
        private string _hoopSize = "D13";
        public string HoopSize
        {
            get => _hoopSize;
            set => SetProperty(ref _hoopSize, value);
        }

        // せん断補強筋間隔
        private double _hoopSpacing = 150.0;
        public double HoopSpacing
        {
            get => _hoopSpacing;
            set => SetProperty(ref _hoopSpacing, value);
        }

        // せん断補強筋規格
        private string _hoopSpec = "SD295";
        public string HoopSpec
        {
            get => _hoopSpec;
            set => SetProperty(ref _hoopSpec, value);
        }

        // せん断補強筋重心かぶり
        private double _hoopCenterCover = 150.0;
        public double HoopCenterCover
        {
            get => _hoopCenterCover;
            set => SetProperty(ref _hoopCenterCover, value);
        }
        // せん断補強筋比
        private double _hoopPw;
        public double HoopPw
        {
            get => _hoopPw;
            set => SetProperty(ref _hoopPw, value);
        }

        // せん断補強筋降伏点
        private double _hoopSigmay;
        public double HoopSigmay
        {
            get => _hoopSigmay;
            set => SetProperty(ref _hoopSigmay, value);
        }

        // せん断補強筋降伏点
        private double _hoopPsSigmay;
        public double HoopPsSigmay
        {
            get => _hoopPsSigmay;
            set => SetProperty(ref _hoopPsSigmay, value);
        }

        // 鋼管径
        private double _pipeDia = 1200.0;
        public double PipeDia
        {
            get => _pipeDia;
            set
            {
                if (SetProperty(ref _pipeDia, value))
                {
                    RecalculatePileDia();
                }               
            }
        }

        // 鋼管厚
        private double _pipeTs = 16.0;
        public double PipeTs
        {
            get => _pipeTs;
            set
            {
                if (SetProperty(ref _pipeTs, value))
                {
                    RecalculatePileDia();
                }
            }
        }

        // 鋼管降伏点
        private double _pipeFts;
        public double PipeFts
        {
            get => _pipeFts;
            set => SetProperty(ref _pipeFts, value);
        }

        // 鋼管規格
        private string _pipeSpec = "SKK490";
        public string PipeSpec
        {
            get => _pipeSpec;
            set => SetProperty(ref _pipeSpec, value);
        }

        public ObservableCollection<string> PipeSpecOption { get; } = new ObservableCollection<string>()
        {
            "JIS A 5525:2019（鋼管ぐい）","JIS G 3444（一般構造用炭素鋼鋼管）"
        };

        public ObservableCollection<string> PipeGradeOption { get; } = new ObservableCollection<string>()
        {
            "SKK400","SKK490"
        };

        private string _pipeGrade="SKK490";
        public string PipeGrade
        {
            get => _pipeGrade;
            set => SetProperty(ref _pipeGrade, value);
        }

        // 鋼管縦弾性係数
        private double _pipeEs = 205000.0;
        public double PipeEs
        {
            get => _pipeEs;
            set => SetProperty(ref _pipeEs, value);
        }

        // 杭全断面積
        private double _a0;
        public double A0
        {
            get => _a0;
            set => SetProperty(ref _a0, value);
        }

        // 杭コンクリート断面積
        private double _ac;
        public double Ac
        {
            get => _ac;
            set => SetProperty(ref _ac, value);
        }

        // 杭等価コンクリート断面積
        private double _ae;
        public double Ae
        {
            get => _ae;
            set => SetProperty(ref _ae, value);
        }

        // 杭等価コンクリート断面二次モーメント
        private double _ie;
        public double Ie
        {
            get => _ie;
            set => SetProperty(ref _ie, value);
        }

        // 杭等価コンクリート断面係数
        private double _ze;
        public double Ze
        {
            get => _ze;
            set => SetProperty(ref _ze, value);
        }

        // コンストラクタ
        public PileSectionViewModel()
        {
            RecalculatePileDia();
            RecalculateConcreteE();

            steelPipePiles = steelPipeLoader.LoadFromCsv(@"..\..\PileLibrary\pile_library_SteelPile.csv");
            SteelPipeOption = steelPipePiles.ToObservableStringCollection(p => $"{p.Diameter}x{p.Thickness}");

            PHCs = precastPileLoader.LoadFromCsv(@"..\..\PileLibrary\pile_library_PHC.csv");
            PHCOption = PHCs.ToObservableStringCollection(p => p.Name);

            PRCs = precastPileLoader.LoadFromCsv(@"..\..\PileLibrary\pile_library_PRC.csv");
            PRCOption = PRCs.ToObservableStringCollection(p => p.Name);

            SCs = precastPileLoader.LoadFromCsv(@"..\..\PileLibrary\pile_library_SC.csv");
            SCOption = SCs.ToObservableStringCollection(p => p.Name);

            ChartInitialize();
        }

        public void ChartInitialize()
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

        public void ChartUpdate()
        {
        }

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
            RenderTargetBitmap renderBitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96d, 96d, PixelFormats.Default);
            renderBitmap.Render(canvas);

            // Clipboardに画像をコピーします
            Clipboard.SetImage(renderBitmap);
        }
    }
}

