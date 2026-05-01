using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PileDesign.Models.InputData
{
    public class PileTop : BaseModel
    {
        // フィールド
        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        private string _pileBodyType;
        public string PileBodyType
        {
            get => _pileBodyType;
            set => SetProperty(ref _pileBodyType, value);
        }

        private string _pileTopType;
        public string PileTopType
        {
            get => _pileTopType;
            set => SetProperty(ref _pileTopType, value);
        }

        // パイルキャップ
        private double _pileCapFc = 24.0;
        public double PileCapFc
        {
            get => _pileCapFc;
            set => SetProperty(ref _pileCapFc, value);
        }

        private double _pileCapGamma = 23.0;
        public double PileCapGamma
        {
            get => _pileCapGamma;
            set => SetProperty(ref _pileCapGamma, value);
        }

        private double _pileCapEc;
        public double PileCapEc
        {
            get => 3.35 * Math.Pow(10, 4) * Math.Pow(PileCapGamma / 24.0, 2.0) * Math.Pow(PileCapFc / 60.0, 1.0 / 3.0);
            set
            {
                if (SetProperty(ref _pileCapEc, value))
                {
                    _pileCapEc = 3.35 * Math.Pow(10, 4) * Math.Pow(PileCapGamma / 24.0, 2.0) * Math.Pow(PileCapFc / 60.0, 1.0 / 3.0);
                }
            }
        }

        private ObservableCollection<Spec> _selectedPileTopSpecification;
        public ObservableCollection<Spec> SelectedPileTopSpecification
        {
            get => _selectedPileTopSpecification;
            set => SetProperty(ref _selectedPileTopSpecification, value);
        }


        // PCリングオプション
        //public ObservableCollection<string> PCRingOption { get; }
        public ObservableCollection<string> FTCapOption { get; set; }

        //public List<PCRing> PCRings { get; set; }
        public ObservableCollection<(int, string, string)> Description { get; set; }

        // FTパイルオプション
        public int SelectedFTCap { get; set; }

        private FTPile _FTPile;
        public FTPile FTPile
        {
            get => _FTPile;
            set => SetProperty(ref _FTPile, value);
        }

        private CaptainPile _captainPile;
        public CaptainPile CaptainPile
        {
            get => _captainPile;
            set => SetProperty(ref _captainPile, value);
        }


        // コンクリート外径
        private double _concreteOutDia;
        public double ConcreteOutDia
        {
            get => _concreteOutDia;
            set => SetProperty(ref _concreteOutDia, value);
        }

        // コンクリート肉厚
        private double _concreteThickness;
        public double ConcreteThickness
        {
            get => _concreteThickness;
            set => SetProperty(ref _concreteThickness, value);
        }

        // 鉄筋径
        public string[] MainBarSizeOption { get; } =
        [
            "D10","D13","D16","D19","D22","D25","D29","D32","D35","D38","D41"
        ];

        // 鉄筋規格
        public string[] MainBarSpecOption { get; } =
        [
            "SD295", "SD345", "SD390", "SD490"
        ];

        internal static double GetBarArea(string barSize)
        {
            return barSize switch
            {
                "D10" => 71.3,
                "D13" => 127.0,
                "D16" => 199.0,
                "D19" => 287.0,
                "D22" => 387.0,
                "D25" => 507.0,
                "D29" => 642.0,
                "D32" => 794.0,
                "D35" => 957.0,
                "D38" => 1140.0,
                "D41" => 1340.0,
                "D51" => 2027.0,
                _ => 0.0
            };
        }

        // 鉄筋径
        private string _mainBarSize1 = "D41";
        public string MainBarSize1
        {
            get => _mainBarSize1;
            set
            {
                if (SetProperty(ref _mainBarSize1, value))
                {
                    MainBarAg1 = MainBarNum1 * GetBarArea(MainBarSize1);
                }
            }
        }

        // 鉄筋本数
        private int _mainBarNum1 = 16;
        public int MainBarNum1
        {
            get => _mainBarNum1;
            set
            {
                if (SetProperty(ref _mainBarNum1, value))
                {
                    MainBarAg1 = MainBarNum1 * GetBarArea(MainBarSize1);
                }
            }
        }

        // 鉄筋断面積 mm2
        private double _mainBarAg1;
        public double MainBarAg1
        {
            get => _mainBarAg1;
            set
            {
                if (SetProperty(ref _mainBarAg1, value))
                {
                }
            }
        }

        // 鉄筋規格
        private string _mainBarSpec1 = "SD390";
        public string MainBarSpec1
        {
            get => _mainBarSpec1;
            set
            {
                if (SetProperty(ref _mainBarSpec1, value))
                {
                }
            }
        }

        // 鉄筋配置直径
        private double _mainBarDr1;
        public double MainBarDr1
        {
            get => _mainBarDr1;
            set
            {
                if (SetProperty(ref _mainBarDr1, value))
                {
                }
            }
        }

        // 鉄筋規格降伏点
        private double _mainbarFtr1;
        public double MainBarFtr1
        {
            get => _mainbarFtr1;
            set => SetProperty(ref _mainbarFtr1, value);
        }

        // 鉄筋重心かぶり厚 mm
        private double _mainbarCenterCover1 = 100;
        public double MainBarCenterCover1
        {
            get => _mainbarCenterCover1;
            set
            {
                if (SetProperty(ref _mainbarCenterCover1, value))
                {
                    MainBarDr1 = ConcreteOutDia - 2.0 * MainBarCenterCover1;
                }
            }
        }


        // 鉄筋径
        private string _mainBarSize2 = "D29";
        public string MainBarSize2
        {
            get => _mainBarSize2;
            set
            {
                if (SetProperty(ref _mainBarSize2, value))
                {
                    MainBarAg2 = MainBarNum1 * GetBarArea(MainBarSize2);
                }
            }
        }

        // 鉄筋本数
        private int _mainBarNum2;
        public int MainBarNum2
        {
            get => _mainBarNum2;
            set
            {
                if (SetProperty(ref _mainBarNum2, value))
                {
                    MainBarAg2 = MainBarNum2 * GetBarArea(MainBarSize2);
                }
            }
        }

        // 鉄筋断面積 mm2
        private double _mainBarAg2;
        public double MainBarAg2
        {
            get => _mainBarAg2;
            set
            {
                if (SetProperty(ref _mainBarAg2, value))
                {
                }
            }
        }

        // 鉄筋規格
        private string _mainBarSpec2 = "SD390";
        public string MainBarSpec2
        {
            get => _mainBarSpec2;
            set
            {
                if (SetProperty(ref _mainBarSpec2, value))
                {
                }
            }
        }

        // 鉄筋配置直径
        private double _mainBarDr2;
        public double MainBarDr2
        {
            get => _mainBarDr2;
            set
            {
                if (SetProperty(ref _mainBarDr2, value))
                {
                }
            }
        }

        // 鉄筋規格降伏点
        private double _mainbarFtr2;
        public double MainBarFtr2
        {
            get => _mainbarFtr2;
            set => SetProperty(ref _mainbarFtr2, value);
        }

        // 鉄筋重心かぶり厚 mm
        private double _mainbarCenterCover2;
        public double MainBarCenterCover2
        {
            get => _mainbarCenterCover2;
            set
            {
                if (SetProperty(ref _mainbarCenterCover2, value))
                {
                    MainBarDr2 = ConcreteOutDia - 2.0 * MainBarCenterCover2;
                }
            }
        }

        // 鉄筋縦弾性係数
        private double _mainbarEr = 205_000;
        public double MainBarEr
        {
            get => _mainbarEr;
            set
            {
                if (SetProperty(ref _mainbarEr, value))
                {
                }
            }
        }

        // NMキャッシュは使用しない（パラメータ変更の即時反映のため）
        public (List<double> N, List<double> M) UnfactoredServiceNMRaw => GetNMRaw(nameof(UnfactoredServiceNM));
        public (List<double> N, List<double> M) UnfactoredDamageNMRaw => GetNMRaw(nameof(UnfactoredDamageNM));
        public (List<double> N, List<double> M) UnfactoredUltimateNMRaw => GetNMRaw(nameof(UnfactoredUltimateNM));

        public (List<double> N, List<double> M) UnfactoredServiceNM =>
            (GetMultipliedListValues(UnfactoredServiceNMRaw.N, 1e-3),
             GetMultipliedListValues(UnfactoredServiceNMRaw.M, 1e-6));

        public (List<double> N, List<double> M) UnfactoredDamageNM =>
            (GetMultipliedListValues(UnfactoredDamageNMRaw.N, 1e-3),
             GetMultipliedListValues(UnfactoredDamageNMRaw.M, 1e-6));

        public (List<double> N, List<double> M) UnfactoredUltimateNM =>
            (GetMultipliedListValues(UnfactoredUltimateNMRaw.N, 1e-3),
             GetMultipliedListValues(UnfactoredUltimateNMRaw.M, 1e-6));

        // コンストラクタ
        public PileTop()
        {
            CaptainPile = new CaptainPile(PileCapFc, PileCapEc);
        }

        // List<double>型データの構成要素すべてに係数を乗ずるメソッド
        internal static List<double> GetMultipliedListValues(List<double> originalList, double multiplier)
        {
            if (originalList == null)
                return [];
            List<double> result = [];
            for (int i = 0; i < originalList.Count; i++)
            {
                result.Add(originalList[i] * multiplier);
            }
            return result;
        }

        private (List<double> N, List<double> M) GetNMRaw(string propertyName)
        {
            // 2 段鉄筋 (杭体主筋 MainBars2 + 定着筋 MainBars1) を持つ円形 RC 断面で評価する。
            // 旧実装は判定が "場所打ち鉄筋コンクリート杭" になっていたが、これは 1 段断面で
            // 定着筋入力 UI も持たない (PileTopWindow.xaml も MainBars2 入力を表示しない)。
            // InsituSteelPipeReinforcedConcreteTopSection は本来「場所打ち鋼管コンクリート杭 +
            // 鉄筋定着工法」の杭頭部 (主筋 + 定着筋 2 段) 用のクラスなので判定をそちらへ修正。
            if (PileBodyType == "場所打ち鋼管コンクリート杭")
            {
                double pileCapGsi = 1.0;
                var insituConcrete = new InsituConcrete(ConcreteOutDia, pileCapGsi, PileCapFc);
                var mainBars1 = new MainBars(MainBarDr1, MainBarNum1, MainBarSpec1, MainBarSize1);
                var mainBars2 = new MainBars(MainBarDr2, MainBarNum2, MainBarSpec2, MainBarSize2);
                var pileTop = new InsituSteelPipeReinforcedConcreteTopSection(insituConcrete, mainBars1, mainBars2);

                //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
                //UltimateLimitAxialForceThresholds = [.. pileTop.UltimateLimitAxialForceThresholds];


                (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
                {
                    "UnfactoredServiceNM" => pileTop.UnfactoredServiceNM,
                    "UnfactoredDamageNM" => pileTop.UnfactoredDamageNM,
                    "UnfactoredUltimateNM" => pileTop.UnfactoredUltimateNM,
                    "FactoredServiceNM" => pileTop.FactoredServiceNM,
                    "FactoredDamageNM" => pileTop.FactoredDamageNM,
                    "FactoredUltimateNM" => pileTop.FactoredUltimateNM,
                    _ => ([], [], [], [])
                };

                return (n, m);
            }
            else // (PileBodyType == "既製コンクリート杭" || || )
            {
                double pileCapGsi = 1.0;
                var insituConcrete = new InsituConcrete(ConcreteOutDia, pileCapGsi, PileCapFc);
                insituConcrete.ApplyBearingFactor(2.0); // 支圧係数: 許容ひずみ・降伏応力を2倍（Ecは不変）
                var mainBars = new MainBars(MainBarDr1, MainBarNum1, MainBarSpec1, MainBarSize1);
                var pileTop = new PrecastPileTopSection(insituConcrete, mainBars);

                //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
                //UltimateLimitAxialForceThresholds = [.. pileTop.UltimateLimitAxialForceThresholds];


                (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
                {
                    "UnfactoredServiceNM" => pileTop.UnfactoredServiceNM,
                    "UnfactoredDamageNM" => pileTop.UnfactoredDamageNM,
                    "UnfactoredUltimateNM" => pileTop.UnfactoredUltimateNM,
                    "FactoredServiceNM" => pileTop.FactoredServiceNM,
                    "FactoredDamageNM" => pileTop.FactoredDamageNM,
                    "FactoredUltimateNM" => pileTop.FactoredUltimateNM,
                    _ => ([], [], [], [])
                };

                return (n, m);
            }


        }


        // 浅いコピーを作成するメソッド
        public PileTop ShallowCopy()
        {
            return (PileTop)this.MemberwiseClone();
        }

        // 深いコピーを作成するメソッド
        public PileTop DeepCopy()
        {
            return (PileTop)this.MemberwiseClone();
        }
    }
}

