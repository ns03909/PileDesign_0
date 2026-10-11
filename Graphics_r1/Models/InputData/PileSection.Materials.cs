using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.Models.InputData
{
    // PileSection のうち、材料と配筋の入力 (コンクリート・PC 鋼材・主筋・鋼管・ヤング係数)。
    public partial class PileSection
    {
        // コンクリート基準強度
        private double _concreteFc = 27.0;
        public double ConcreteFc
        {
            get => _concreteFc;
            set
            {
                // 物理的に不正な値を防ぐ
                double safeValue = value < 12 ? 12 : value;
                if (SetProperty(ref _concreteFc, safeValue))
                {
                    RecalculateConcreteE();
                    InvalidateAllCaches();
                }
            }
        }

        // コンクリートプレストレス
        //
        // 有効プレストレス σe はひび割れ・降伏・N-M・N-Q の曲線に効く。以前はセッターで
        // キャッシュを捨てておらず、σe だけが違う製品 (PHC の B 種 → C 種など) に
        // 切り替えても前の製品の曲線が残っていた。M-φ はキーに σe を含むので解析値は
        // 正しく、検定と図だけが古いまま出ていた。
        private double _prestress;
        public double Prestress
        {
            get => _prestress;
            set
            {
                if (SetProperty(ref _prestress, value))
                    InvalidateAllCaches();
            }
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
                    InvalidateAllCaches();
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
                    InvalidateAllCaches();
                }
            }
        }

        // コンクリート縦弾性係数 N/mm2
        //
        // γ・Gsi・Fc から導く派生値なので<b>書き出さない</b>。
        // 書き出すと、ファイル内の値の並び順で「Ec が先か、その材料が先か」が変わり、
        // 読み込み結果が変わっていた (揺らした並び順の網が全例題で検出)。
        // 読み込みの仕上げ (OnDeserialized) で導き直す。
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        //
        // 製品の転記 (RecalculateSelectedPrecastPile) はカタログの Ec を直に入れるので、
        // RecalculateConcreteE を通らない。セッター側でも曲線を捨てないと、
        // Ec の違う製品へ切り替えても前の Ec の曲線が残る。
        public double ConcreteE
        {
            get => _concreteE;
            set
            {
                if (SetProperty(ref _concreteE, value))
                    InvalidateAllCaches();
            }
        }
        private double _concreteE;

        // コンクリートのヤング係数の計算メソッド (いまの材料モデル化オプションで)
        public void RecalculateConcreteE() => RecalculateConcreteE(ConcreteModelOptions.UseUnitGsiForConcreteE);

        /// <summary>
        /// コンクリートのヤング係数を、指定した「Ec の算定に ξ = 1.0 を使うか」で求め直す。
        /// オプションは静的 (<see cref="ConcreteModelOptions"/>) で今の入力の設定を持つので、別の設定を持つ入力
        /// (読み込んだ解析結果の控え) の断面は、その入力の基本設定の値を渡して求め直す
        /// (<see cref="InputModel.RecalculateConcreteEWithOwnOptions"/>)。
        /// </summary>
        public void RecalculateConcreteE(bool useUnitGsiForConcreteE)
        {
            // Ec 算定用 ξ: オプション時は 1.0（強度側 Gsi·Fc 等は実 Gsi のまま）
            double gsiForEc = useUnitGsiForConcreteE ? 1.0 : ConcreteGsi;
            double e = 3.35 * Math.Pow(10, 4) * Math.Pow(ConcreteGamma / 24.0, 2.0) * Math.Pow(gsiForEc * ConcreteFc / 60.0, 1.0 / 3.0);
            // 既製杭の製品を選んでいれば、カタログの Ec (例 40,000) を使う。
            // 以前は製品を選んだときだけカタログの値が入り、保存して開き直すと (読込の仕上げでここを通るので)
            // 式の値 (Fc85・γ26 で約 44,150) に変わって、EA・EI が 8〜10% 動いていた。計算例の読込も式の値だった
            if (CatalogConcreteE is double catalogEc) e = catalogEc;
            if (ConcreteE == e) return;

            ConcreteE = e;

            // Ec はひび割れ・降伏の曲線に効くので、変わったらこの断面のキャッシュを捨てる。
            // オプション変更による破棄 (版数の判定) とは別で、あちらは Ec を書き換える前に
            // 済んでいることがある。ここで捨てないと、新しい Ec が曲線に出ない。
            InvalidateComputedCaches();
        }

        /// <summary>
        /// 選んだ既製杭の製品のカタログの Ec。既製杭で製品を選んでいて、カタログに Ec があるときだけ値を返す。
        /// </summary>
        private double? CatalogConcreteE
            => PileBodyType == PileTypeNames.PrecastConcrete
               && !string.IsNullOrEmpty(SelectedPrecastPile?.Name)
               && SelectedPrecastPile.Ec is double ec && ec > 0 && double.IsFinite(ec)
                ? ec : null;

        // コンクリートプレストレス N/mm2
        private double _concreteSigmaE;
        public double ConcreteSigmaE
        {
            get => _concreteSigmaE;
            set => SetProperty(ref _concreteSigmaE, value);
        }

        // PC 鋼材の諸元 (Ap・Dp・降伏/引張強さ・Ep) はどれも曲線に効く。
        // 以前はセッターで曲線を捨てておらず、製品を切り替えても前の製品の
        // N-M・N-Q・ひび割れ・降伏の曲線が残っていた (Prestress と同じ)。

        // PC鋼線断面積
        private double _tendonAp;
        public double TendonAp
        {
            get => _tendonAp;
            set
            {
                if (SetProperty(ref _tendonAp, value))
                    InvalidateAllCaches();
            }
        }

        // PC鋼線配置直径断面積
        private double _tendonDp;
        public double TendonDp
        {
            get => _tendonDp;
            set
            {
                if (SetProperty(ref _tendonDp, value))
                    InvalidateAllCaches();
            }
        }

        // PC鋼線降伏強さ
        private double _tendonSigmaPy;
        public double TendonSigmaPy
        {
            get => _tendonSigmaPy;
            set
            {
                if (SetProperty(ref _tendonSigmaPy, value))
                    InvalidateAllCaches();
            }
        }

        // PC鋼線引張強さ
        private double _tendonSigmaPu;
        public double TendonSigmaPu
        {
            get => _tendonSigmaPu;
            set
            {
                if (SetProperty(ref _tendonSigmaPu, value))
                    InvalidateAllCaches();
            }
        }

        // PC鋼線縦弾性係数
        private double _tendonEp;
        public double TendonEp
        {
            get => _tendonEp;
            set
            {
                if (SetProperty(ref _tendonEp, value))
                    InvalidateAllCaches();
            }
        }

        // 鉄筋径
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string[] MainBarSizeOption { get; } =
        [
            "D10","D13","D16","D19","D22","D25","D29","D32","D35","D38","D41"
        ];

        // 鉄筋規格
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string[] MainBarSpecOption { get; } =
        [
            "SD295", "SD345", "SD390", "SD490"
        ];


        // 鋼管規格オプション
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string[] PipeGradeOption { get; } =
        [
            "SKK400",
            "SKK490"
        ];

        // 鉄筋径
        private string _mainBarSize = "D29";
        public string MainBarSize
        {
            get => _mainBarSize;
            set
            {
                if (SetProperty(ref _mainBarSize, value))
                {
                    // 読み込み中は再計算しない。ファイルに書かれた MainBarAg は
                    // 製品カタログの値 (SetSelectedPrecastPileByName で pipe.Ag) の場合があり、
                    // 公称値 (本数 × 呼び径の断面積) とわずかに違う。
                    // 再計算すると、キーの並び順によってどちらが残るかが変わる
                    // (設計例集3.1 の PRC杭 で 4592 ⇄ 4584 と揺れていた)。
                    if (!_isDeserializing) MainBarAg = MainBarNum * GetBarArea(MainBarSize);
                    InvalidateAllCaches();
                }
            }
        }

        // 鉄筋本数
        private int _mainBarNum = 16;
        public int MainBarNum
        {
            get => _mainBarNum;
            set
            {
                int safeValue = value < 0 ? 0 : value;
                if (SetProperty(ref _mainBarNum, safeValue))
                {
                    // 読み込み中は再計算しない (理由は MainBarSize と同じ)
                    if (!_isDeserializing) MainBarAg = MainBarNum * GetBarArea(MainBarSize);
                    InvalidateAllCaches();
                }
            }
        }

        private static readonly Dictionary<string, double> BarAreas = new()
        {
            ["D10"] = 71.3,
            ["D13"] = 127.0,
            ["D16"] = 199.0,
            ["D19"] = 287.0,
            ["D22"] = 387.0,
            ["D25"] = 507.0,
            ["D29"] = 642.0,
            ["D32"] = 794.0,
            ["D35"] = 957.0,
            ["D38"] = 1140.0,
            ["D41"] = 1340.0,
            ["D51"] = 2027.0
        };

        internal static double GetBarArea(string barSize)
            => BarAreas.TryGetValue(barSize, out var area) ? area : 0.0;


        // 鉄筋断面積 mm2
        private double _mainBarAg;
        public double MainBarAg
        {
            get => _mainBarAg;
            set
            {
                if (SetProperty(ref _mainBarAg, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // 鉄筋規格
        private string _mainBarSpec = "SD390";
        public string MainBarSpec
        {
            get => _mainBarSpec;
            set
            {
                if (SetProperty(ref _mainBarSpec, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        /// <summary>
        /// 重心かぶり厚を、いまの配置直径に合わせる。
        ///
        /// 逆向きのセッター (<see cref="MainBarCenterCover"/>) は配置直径を書き戻すので、
        /// ここから呼ばずに控えの値を直に書く。互いを呼び合う形を避ける。
        /// </summary>
        private void SyncCoverFromPlacementDiameter()
        {
            if (!(ConcreteOutDia > 0) || !(_mainBarDr > 0)) return;

            double cover = (ConcreteOutDia - _mainBarDr) * 0.5;
            if (_mainbarCenterCover == cover) return;

            _mainbarCenterCover = cover;
            OnPropertyChanged(nameof(MainBarCenterCover));
        }

        // 鉄筋配置直径
        private double _mainBarDr;
        public double MainBarDr
        {
            get => _mainBarDr;
            set
            {
                if (SetProperty(ref _mainBarDr, value))
                {
                    // 重心かぶり厚を配置直径に合わせる。
                    //
                    // 画面の入力欄は<b>かぶり厚</b>で、配置直径は
                    // <see cref="RecalculatePileDia"/> が ConcreteOutDia - 2·かぶり厚 で導く。
                    // ここで揃えておかないと、配置直径を<b>直接与えた</b>状態
                    // (例題データ・保存ファイル) が、読み込みの仕上げ (OnDeserialized →
                    // RecalculatePileDia) で既定のかぶり厚 200mm から導いた値に塗り潰される。
                    //
                    // 実際に例題 5 件 (基礎指針 計算例9 は φ1000 で 700 → 600) がそうなっており、
                    // <b>保存して開き直すと主筋の配置直径が縮んで曲げ耐力が下がっていた。</b>
                    // 製品ライブラリを当てる経路では以前からこの向きで揃えている
                    // (MainBarCenterCover = (ConcreteOutDia - MainBarDr)/2)。
                    //
                    // 読み込み中は揃えない。外径がまだ既定のまま配置直径が先に来ると、
                    // 誤ったかぶり厚を導いてしまう (キーの並び順で結果が変わる)。
                    // 値が揃った仕上げ (OnDeserialized) で一度だけ揃える。
                    if (!_isDeserializing) SyncCoverFromPlacementDiameter();
                    InvalidateAllCaches();
                }
            }
        }

        // 鉄筋規格降伏点
        // 鉄筋の規格降伏点は材料 (Rebar) が持つ。断面側の写しは読み手がいなかったので
        // 2026-09-19 に撤去した (諸元表の行も以前からコメントアウトされていた)。

        // 鉄筋重心かぶり厚 mm
        private double _mainbarCenterCover = 200.0;
        public double MainBarCenterCover
        {
            get => _mainbarCenterCover;
            set
            {
                if (SetProperty(ref _mainbarCenterCover, value))
                {
                    MainBarDr = ConcreteOutDia - 2.0 * MainBarCenterCover;
                    InvalidateAllCaches();
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
                    InvalidateAllCaches();
                }
            }
        }

        // 鋼管径
        private double _pipeDia = 0.0;
        public double PipeDia
        {
            get => _pipeDia;
            set
            {
                if (_pipeDia != value)
                {
                    _pipeDia = value;
                    OnPropertyChanged(nameof(PipeDia));
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        // 鋼管厚
        private double _pipeTs = 0.0;
        public double PipeTs
        {
            get => _pipeTs;
            //set => SetProperty(ref _pipeTs, value);
            set
            {
                if (_pipeTs != value)
                {
                    _pipeTs = value;
                    OnPropertyChanged(nameof(PipeTs));
                    RecalculatePileDia();
                    InvalidateAllCaches();
                }
            }
        }

        // 鋼管断面積
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public double PipeAs => PipeTs * (PipeDia - PipeTs) * Math.PI;

        // 鋼管降伏点

        private double _pipeFts;
        public double PipeFts
        {
            get => _pipeFts;
            set
            {
                if (SetProperty(ref _pipeFts, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // 鋼管規格
        private string _pipeGrade = "SKK490";
        public string PipeGrade
        {
            get => _pipeGrade;
            set
            {
                if (SetProperty(ref _pipeGrade, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        // 鋼管縦弾性係数 (N/mm2)
        private double _pipeEs = 205000.0;
        public double PipeEs
        {
            get => _pipeEs;
            set
            {
                if (SetProperty(ref _pipeEs, value))
                {
                    InvalidateAllCaches();
                }
            }
        }

        /// <summary>
        /// 「基礎部材の強度と変形性能」のヤング係数を使う設定のとき、
        /// 製品カタログから読み込んだ鋼材のヤング係数を指針の値に差し替える。
        ///
        /// カタログ値はメーカーで食い違う（異形棒鋼 200,000 / 205,000、鋼管 200,000 / 205,000）。
        /// 差し替えは<b>製品を断面へ反映した直後の 1 箇所</b>で行う。こうすると
        /// EI・EA だけでなく N-M 曲線・M-φ まで同じ E で一貫する。
        ///
        /// 既製杭は指針が E ではなく<b>ヤング係数比 n = 5</b> を規定しているため、
        /// E = n·Ec として与える。Ec が 40,000 以外（Fc 105 級など）でも n が 5 に保たれる。
        /// 鋼管は E = 205,000 の直接指定。
        /// </summary>
        /// <summary>
        /// 既製杭の製品を選んでいれば、鋼材のヤング係数 (PC 鋼材 Ep・鉄筋 Er・鋼管 Es) をカタログの値へ戻し、
        /// 基本設定の「鋼材のヤング係数を指針値に」が有効なら指針値を当て直す。
        ///
        /// 以前は製品を選んだときにしか当てておらず、あとで設定を切り替えても入力済みの断面は変わらなかった
        /// (どちらの設定のときに選んだかで値が決まり、指針値を外してもカタログの値へ戻る経路が無かった)。
        /// 材料モデルの設定を当てるとき (<c>ApplyConcreteModelOptions</c>) に呼ぶ。
        /// </summary>
        internal void ReapplyPrecastSteelYoungsModulus()
        {
            if (CatalogConcreteE == null) return;
            var product = SelectedPrecastPile;
            if (product.Ep > 0 && TendonAp > 0) TendonEp = product.Ep;
            if (product.Er > 0 && MainBarAg > 0) MainBarEr = product.Er;
            if (product.Es > 0 && PileSectionType == PileTypeNames.Sc) PipeEs = product.Es;
            ApplyGuideYoungsModulusIfEnabled();
        }

        private void ApplyGuideYoungsModulusIfEnabled()
        {
            if (!ConcreteModelOptions.UseGuideYoungsModulus) return;
            if (ConcreteE <= 0) return;

            if (PileTypeNames.IsPhcLikeSection(PileSectionType) || PileTypeNames.IsPrcLikeSection(PileSectionType))
            {
                // PHC杭・PRC杭 (節杭を含む): PC鋼材・鉄筋とも n = 5 固定
                double e = ConcreteModelOptions.GuideModularRatioForPrecast * ConcreteE;
                if (TendonAp > 0) TendonEp = e;
                if (MainBarAg > 0) MainBarEr = e;
            }
            else if (PileSectionType == PileTypeNames.Sc)
            {
                // SC杭の鋼管
                PipeEs = ConcreteModelOptions.GuideSteelYoungsModulus;
            }
        }

    }
}
