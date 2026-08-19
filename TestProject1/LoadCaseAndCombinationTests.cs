using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Media3D;

namespace TestProject1
{
    /// <summary>
    /// LoadCase / LoadCombination / LoadCasesInput / ElementDivision のレグレッションテスト群。
    /// 解析対象判定 (IsApplicable / IsAnalysisTarget / IsAnalyzed) と、Docx 出力時の二重ガード
    /// (IsApplicableForDocxDisplay) の挙動が UI と解析ロジックの双方で前提となっているため、
    /// ここで属性レベルの不変条件を固定化する。
    /// </summary>
    [TestClass]
    public class LoadCaseAndCombinationTests
    {
        // ========================================================================
        // LoadCase: IsApplicable / IsAnalyzed / IsApplicableForDocxDisplay
        //
        // ルール:
        //   - IsApplicableForDocxDisplay = IsApplicable && IsAnalyzed (getter)
        //   - setter は IsAnalyzed=true の時だけ IsApplicable に反映
        //   - IsApplicable / IsAnalyzed の変更時は IsApplicableForDocxDisplay も
        //     PropertyChanged 通知を発行 (XAML バインディングで CheckBox が追従するため)
        // ========================================================================

        [TestMethod]
        public void IsApplicableForDocxDisplay_BothTrue_ReturnsTrue()
        {
            var lc = new LoadCase { IsApplicable = true, IsAnalyzed = true };
            Assert.IsTrue(lc.IsApplicableForDocxDisplay);
        }

        [TestMethod]
        public void IsApplicableForDocxDisplay_ApplicableButNotAnalyzed_ReturnsFalse()
        {
            // 解析未実行時は IsApplicable=true でもチェックボックス未チェック表示にする UX
            var lc = new LoadCase { IsApplicable = true, IsAnalyzed = false };
            Assert.IsFalse(lc.IsApplicableForDocxDisplay);
        }

        [TestMethod]
        public void IsApplicableForDocxDisplay_AnalyzedButNotApplicable_ReturnsFalse()
        {
            var lc = new LoadCase { IsApplicable = false, IsAnalyzed = true };
            Assert.IsFalse(lc.IsApplicableForDocxDisplay);
        }

        [TestMethod]
        public void IsApplicableForDocxDisplay_Setter_OnlyTakesEffectWhenAnalyzed()
        {
            // IsAnalyzed=false の時に Docx 表示用 setter で true を試みても、IsApplicable は変化しない
            var lc = new LoadCase { IsApplicable = false, IsAnalyzed = false };
            lc.IsApplicableForDocxDisplay = true;
            Assert.IsFalse(lc.IsApplicable, "IsAnalyzed=false 時は IsApplicable を変えてはいけない");
        }

        [TestMethod]
        public void IsApplicableForDocxDisplay_Setter_UpdatesIsApplicableWhenAnalyzed()
        {
            var lc = new LoadCase { IsApplicable = false, IsAnalyzed = true };
            lc.IsApplicableForDocxDisplay = true;
            Assert.IsTrue(lc.IsApplicable);
        }

        [TestMethod]
        public void IsApplicable_Setter_FiresPropertyChangedForBothApplicableAndDocxDisplay()
        {
            // CheckBox バインディング: IsApplicable 変更時に Docx 表示用も追従通知が必要
            var lc = new LoadCase { IsAnalyzed = true };
            var changed = new List<string>();
            lc.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            lc.IsApplicable = true;

            CollectionAssert.Contains(changed, nameof(LoadCase.IsApplicable));
            CollectionAssert.Contains(changed, nameof(LoadCase.IsApplicableForDocxDisplay));
        }

        [TestMethod]
        public void IsAnalyzed_Setter_FiresPropertyChangedForBothAnalyzedAndDocxDisplay()
        {
            var lc = new LoadCase { IsAnalyzed = false };
            var changed = new List<string>();
            lc.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            lc.IsAnalyzed = true;

            CollectionAssert.Contains(changed, nameof(LoadCase.IsAnalyzed));
            CollectionAssert.Contains(changed, nameof(LoadCase.IsApplicableForDocxDisplay));
        }

        [TestMethod]
        public void IsApplicable_SetSameValue_DoesNotFirePropertyChanged()
        {
            // 等価判定で no-op になるべき (CheckBox の不要な再描画を抑止)
            var lc = new LoadCase { IsApplicable = true };
            int fireCount = 0;
            lc.PropertyChanged += (_, _) => fireCount++;

            lc.IsApplicable = true;
            Assert.AreEqual(0, fireCount);
        }

        // ========================================================================
        // LoadCase: ForceActionPoint Point3D 合成
        //   getter: (X, Y, Z) → Point3D
        //   setter: Point3D → 各成分にバラす
        // ========================================================================

        [TestMethod]
        public void ForceActionPoint_Getter_ComposesXYZ()
        {
            var lc = new LoadCase
            {
                ForceActionPointX = 1.0,
                ForceActionPointY = 2.0,
                ForceActionPointAltitude = 3.0
            };
            var p = lc.ForceActionPoint;
            Assert.AreEqual(1.0, p.X);
            Assert.AreEqual(2.0, p.Y);
            Assert.AreEqual(3.0, p.Z);  // Altitude → Z
        }

        [TestMethod]
        public void ForceActionPoint_Setter_DistributesToXYZ()
        {
            var lc = new LoadCase();
            lc.ForceActionPoint = new Point3D(4.0, 5.0, 6.0);
            Assert.AreEqual(4.0, lc.ForceActionPointX);
            Assert.AreEqual(5.0, lc.ForceActionPointY);
            Assert.AreEqual(6.0, lc.ForceActionPointAltitude);
        }

        // ========================================================================
        // LoadCase: SumH / SumHOverSumVText
        //   SumH = UpperMassForce + FoundationMassForce
        //   SumHOverSumVText = "-" if |SumV|<1e-6 else (SumH/|SumV|).ToString("F3")
        //   (SumV は InputModel back-reference 必須なので個別テストは Skip)
        // ========================================================================

        [TestMethod]
        public void SumH_AddsUpperAndFoundationMass()
        {
            var lc = new LoadCase { UpperMassForce = 1500, FoundationMassForce = 700 };
            Assert.AreEqual(2200.0, lc.SumH);
        }

        [TestMethod]
        public void SumH_ZeroByDefault()
        {
            var lc = new LoadCase();
            Assert.AreEqual(0.0, lc.SumH);
        }

        [TestMethod]
        public void SumHOverSumVText_ZeroSumV_ReturnsHyphen()
        {
            // InputModel が null だと SumV = 0 になる → 'ハイフン表示'
            var lc = new LoadCase { UpperMassForce = 100, FoundationMassForce = 200 };
            Assert.AreEqual("-", lc.SumHOverSumVText);
        }

        // ========================================================================
        // LoadCase: 構築時パラメータ
        // ========================================================================

        [TestMethod]
        public void Constructor_WithViewModel_AssignsAllFields()
        {
            var lc = new LoadCase(null!, isApplicable: true, level: 2, no: 5,
                loadName: "U3", loadAngle: 45.0,
                soilNonlinearityMode: SoilNonlinearityMode.KhReduction, isPileNonLinear: false,
                upperMassForce: 1200, foundationMassForce: 800,
                forceActionPointX: 0.5, forceActionPointY: 1.5, forceActionPointAltitude: 2.5);

            Assert.IsTrue(lc.IsApplicable);
            Assert.AreEqual(2, lc.Level);
            Assert.AreEqual(5, lc.No);
            Assert.AreEqual("U3", lc.LoadName);
            Assert.AreEqual(45.0, lc.LoadAngle);
            Assert.AreEqual(SoilNonlinearityMode.KhReduction, lc.SoilNonlinearityMode);
            Assert.IsTrue(lc.IsSoilNonLinear); // 旧 API: Linear 以外なら true
            Assert.IsFalse(lc.IsPileNonLinear);
            Assert.AreEqual(1200, lc.UpperMassForce);
            Assert.AreEqual(800, lc.FoundationMassForce);
            Assert.AreEqual(0.5, lc.ForceActionPointX);
            Assert.AreEqual(1.5, lc.ForceActionPointY);
            Assert.AreEqual(2.5, lc.ForceActionPointAltitude);
        }

        [TestMethod]
        public void DeepCopy_ProducesIndependentInstance_PrimitivesEqual()
        {
            var src = new LoadCase
            {
                IsApplicable = true,
                Level = 2,
                No = 7,
                LoadName = "VL+E1",
                LoadAngle = 30.0,
                UpperMassForce = 999,
                FoundationMassForce = 777
            };
            var dst = src.DeepCopy();

            Assert.AreNotSame(src, dst);
            Assert.AreEqual(src.LoadName, dst.LoadName);
            Assert.AreEqual(src.Level, dst.Level);
            Assert.AreEqual(src.No, dst.No);
            Assert.AreEqual(src.LoadAngle, dst.LoadAngle);
            Assert.AreEqual(src.UpperMassForce, dst.UpperMassForce);
            Assert.AreEqual(src.FoundationMassForce, dst.FoundationMassForce);
            Assert.AreEqual(src.IsApplicable, dst.IsApplicable);
        }

        // ========================================================================
        // LoadCases.GetLoadCase: 静的検索ヘルパー
        // ========================================================================

        [TestMethod]
        public void GetLoadCase_FindsByLoadName()
        {
            var col = new ObservableCollection<LoadCase>
            {
                new() { LoadName = "VL+E1" },
                new() { LoadName = "VL+E2" },
                new() { LoadName = "U3" }
            };
            var found = LoadCases.GetLoadCase(col, "VL+E2");
            Assert.IsNotNull(found);
            Assert.AreEqual("VL+E2", found!.LoadName);
        }

        [TestMethod]
        public void GetLoadCase_NotFound_ReturnsNull()
        {
            var col = new ObservableCollection<LoadCase>
            {
                new() { LoadName = "VL+E1" }
            };
            Assert.IsNull(LoadCases.GetLoadCase(col, "missing"));
        }

        [TestMethod]
        public void GetLoadCase_EmptyCollection_ReturnsNull()
        {
            Assert.IsNull(LoadCases.GetLoadCase([], "anything"));
        }

        // ========================================================================
        // LoadCombination: Name / GetName のフォーマット
        //   Name    : "αₗ:1.00/βᵤ:0.50/βₗ:-0.50" (Unicode 添字付き、ログ表示用)
        //   GetName : "1.00/0.50/-0.50" (プレーン、シリアライズ用)
        // ========================================================================

        [TestMethod]
        public void LoadCombination_Name_FormatsWithGreekSubscripts()
        {
            var c = new LoadCombination(no: 1, alpha1: 1.0, beta1: 0.5, beta2: -0.5);
            string name = c.Name;
            // 形式: "α<u2097>:1.00/β<u1d64>:0.50/β<u2097>:-0.50"
            StringAssert.Contains(name, "1.00");
            StringAssert.Contains(name, "0.50");
            StringAssert.Contains(name, "-0.50");
            StringAssert.Contains(name, "α");
            StringAssert.Contains(name, "β");
        }

        [TestMethod]
        public void LoadCombination_GetName_ReturnsPlainSlashFormat()
        {
            var c = new LoadCombination(no: 1, alpha1: 1.0, beta1: 0.5, beta2: -0.5);
            Assert.AreEqual("1.00/0.50/-0.50", c.GetName());
        }

        [TestMethod]
        public void LoadCombination_GetName_TwoDecimalPlaces()
        {
            var c = new LoadCombination(no: 1, alpha1: 0.123, beta1: 1.999, beta2: -0.001);
            // F2 で四捨五入 → "0.12/2.00/-0.00"
            Assert.AreEqual("0.12/2.00/-0.00", c.GetName());
        }

        // ========================================================================
        // LoadCombination: PropertyChanged
        // ========================================================================

        [TestMethod]
        public void LoadCombination_Beta1_Setter_FiresPropertyChanged()
        {
            var c = new LoadCombination(no: 1, alpha1: 1.0, beta1: 1.0, beta2: 1.0);
            string? lastName = null;
            c.PropertyChanged += (_, e) => lastName = e.PropertyName;

            c.Beta1 = 0.5;
            Assert.AreEqual(nameof(LoadCombination.Beta1), lastName);
        }

        [TestMethod]
        public void LoadCombination_IsApplicable_FiresBothApplicableAndDocxDisplay()
        {
            var c = new LoadCombination(no: 1, alpha1: 1.0, beta1: 1.0, beta2: 1.0)
            {
                IsAnalyzed = true,
                IsApplicable = false  // 初期化
            };
            var changed = new List<string>();
            c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            c.IsApplicable = true;
            CollectionAssert.Contains(changed, nameof(LoadCombination.IsApplicable));
            CollectionAssert.Contains(changed, nameof(LoadCombination.IsApplicableForDocxDisplay));
        }

        [TestMethod]
        public void LoadCombination_IsApplicableForDocxDisplay_GuardedByIsAnalyzed()
        {
            // LoadCase と同じ UX ガード: 未解析時は Docx 表示用 setter は no-op
            var c = new LoadCombination(no: 1, alpha1: 1.0, beta1: 1.0, beta2: 1.0)
            {
                IsAnalyzed = false,
                IsApplicable = false
            };
            c.IsApplicableForDocxDisplay = true;
            Assert.IsFalse(c.IsApplicable);
            Assert.IsFalse(c.IsApplicableForDocxDisplay);
        }

        // ========================================================================
        // LoadCombination: DeepCopy
        // ========================================================================

        [TestMethod]
        public void LoadCombination_DeepCopy_PreservesValuesAndIsIndependent()
        {
            var src = new LoadCombination(no: 3, alpha1: 1.5, beta1: 0.8, beta2: -0.4)
            {
                IsApplicable = true
            };
            var dst = src.DeepCopy();

            Assert.AreNotSame(src, dst);
            Assert.AreEqual(src.No, dst.No);
            Assert.AreEqual(src.Alpha1, dst.Alpha1);
            Assert.AreEqual(src.Beta1, dst.Beta1);
            Assert.AreEqual(src.Beta2, dst.Beta2);
            Assert.AreEqual(src.IsApplicable, dst.IsApplicable);
        }

        // ========================================================================
        // LoadCombinations.GetLoadCombination: 静的検索ヘルパー (GetName 形式)
        // ========================================================================

        [TestMethod]
        public void GetLoadCombination_FindsByGetNameFormat()
        {
            var col = new ObservableCollection<LoadCombination>
            {
                new(1, 1.0, 1.0, 1.0),
                new(2, 1.0, 0.5, -0.5),
                new(3, 1.5, 0.0, 0.0)
            };
            var found = LoadCombinations.GetLoadCombination(col, "1.00/0.50/-0.50");
            Assert.IsNotNull(found);
            Assert.AreEqual(2, found!.No);
        }

        [TestMethod]
        public void GetLoadCombination_NotFound_ReturnsNull()
        {
            var col = new ObservableCollection<LoadCombination>
            {
                new(1, 1.0, 1.0, 1.0)
            };
            Assert.IsNull(LoadCombinations.GetLoadCombination(col, "0.00/0.00/0.00"));
        }
    }

    /// <summary>
    /// LoadCasesInput: AllSeismicLoadCases / AllLoadCases / AnalysisTargetSeismicLoadCases /
    /// AllLoadCombinations のフィルタリング論理を検証。
    ///
    /// SetMainWindowViewModel(MainWindowViewModel) は MainWindowViewModel.CurrentInputModel
    /// バックリファレンスを必要とし MSTest 単体では構築困難なため、ここでは個々のコレクションを
    /// 手動で組み立てて挙動を検証する。
    /// </summary>
    [TestClass]
    public class LoadCasesInputTests
    {
        [TestMethod]
        public void AllSeismicLoadCases_OnlyIncludesIsApplicable()
        {
            var lci = new LoadCasesInput
            {
                LoadCasesLevel1 =
                [
                    new() { LoadName = "L1-A", IsApplicable = true },
                    new() { LoadName = "L1-B", IsApplicable = false },
                ],
                LoadCasesLevel2 =
                [
                    new() { LoadName = "L2-A", IsApplicable = true },
                ]
            };

            var seismic = lci.AllSeismicLoadCases;
            Assert.AreEqual(2, seismic.Count);
            CollectionAssert.AreEquivalent(
                new[] { "L1-A", "L2-A" },
                new[] { seismic[0].LoadName, seismic[1].LoadName });
        }

        [TestMethod]
        public void AllSeismicLoadCases_NullCollections_ReturnsEmpty()
        {
            var lci = new LoadCasesInput();
            Assert.AreEqual(0, lci.AllSeismicLoadCases.Count);
        }

        [TestMethod]
        public void AllLoadCases_IncludesVL0VLaddVL_AndApplicableSeismic()
        {
            var lci = new LoadCasesInput
            {
                LoadCaseVL0 = new LoadCase { LoadName = "VL0" },
                LoadCaseVLadd = new LoadCase { LoadName = "VLadd" },
                LoadCaseVL = new LoadCase { LoadName = "VL" },
                LoadCasesLevel1 =
                [
                    new() { LoadName = "L1-A", IsApplicable = true },
                    new() { LoadName = "L1-B", IsApplicable = false },  // 除外
                ]
            };

            var all = lci.AllLoadCases;
            Assert.AreEqual(4, all.Count);  // VL0 + VLadd + VL + L1-A
            // VL 系は IsApplicable に関係なく含まれる
            CollectionAssert.Contains(
                new[] { all[0].LoadName, all[1].LoadName, all[2].LoadName, all[3].LoadName },
                "VL0");
        }

        [TestMethod]
        public void AnalysisTargetSeismicLoadCases_FiltersByIsAnalysisTarget()
        {
            var lci = new LoadCasesInput
            {
                LoadCasesLevel1 =
                [
                    new() { LoadName = "L1-A", IsAnalysisTarget = true },
                    new() { LoadName = "L1-B", IsAnalysisTarget = false },
                ],
                LoadCasesLevel2 =
                [
                    new() { LoadName = "L2-A", IsAnalysisTarget = true },
                ]
            };

            var targets = lci.AnalysisTargetSeismicLoadCases;
            Assert.AreEqual(2, targets.Count);
            // IsApplicable は無視 — IsAnalysisTarget だけが効く
        }

        [TestMethod]
        public void AnalysisTargetSeismicLoadCases_NullCollections_ReturnsEmpty()
        {
            var lci = new LoadCasesInput();
            Assert.AreEqual(0, lci.AnalysisTargetSeismicLoadCases.Count);
        }

        [TestMethod]
        public void AllLoadCombinations_FiltersByIsApplicable()
        {
            var lci = new LoadCasesInput
            {
                LoadCombinations =
                [
                    new(1, 1.0, 1.0, 1.0) { IsApplicable = true },
                    new(2, 1.0, 0.5, -0.5) { IsApplicable = false },
                    new(3, 1.5, 0.0, 0.0) { IsApplicable = true },
                ]
            };

            var apps = lci.AllLoadCombinations;
            Assert.AreEqual(2, apps.Count);
            Assert.AreEqual(1, apps[0].No);
            Assert.AreEqual(3, apps[1].No);
        }

        [TestMethod]
        public void IsAnalysisTarget_Setter_FiresAnalysisTargetSeismicCasesNotification()
        {
            // LoadCase の IsAnalysisTarget を変更すると LoadCasesInput.AnalysisTargetSeismicLoadCases に
            // OnPropertyChanged 通知が伝搬する (Subscribe 経由)
            var lci = new LoadCasesInput
            {
                LoadCasesLevel1 = [new LoadCase { LoadName = "L1-A" }]
            };
            string? lastProp = null;
            ((INotifyPropertyChanged)lci).PropertyChanged += (_, e) => lastProp = e.PropertyName;

            lci.LoadCasesLevel1[0].IsAnalysisTarget = true;

            Assert.AreEqual(nameof(LoadCasesInput.AnalysisTargetSeismicLoadCases), lastProp);
        }

        [TestMethod]
        public void DeepCopy_PreservesAllSubcollections()
        {
            // SetMainWindowViewModel を呼ばずに最小限のデータで DeepCopy を検証
            var lci = new LoadCasesInput
            {
                LoadCombinationFactor = 1.5,
                LoadCombinations = [new(1, 1.0, 0.5, -0.5)],
                LoadCombinationsPlus =
                [
                    new(1, 1.0, 0.0, 0.0),
                    new(2, 1.0, 0.0, 0.0),
                    new(3, 1.0, 0.0, 0.0),
                    new(4, 1.0, 0.0, 0.0)
                ],
                LoadCaseLevel1Common = new LoadCaseCommon(SoilNonlinearityMode.KhReductionWithPy, false, 1000, 800, 0, 0, 1),
                LoadCaseLevel2Common = new LoadCaseCommon(SoilNonlinearityMode.KhReductionWithPy, true, 2000, 1600, 0, 0, 1),
                LoadCasesLevel1 = [new LoadCase { LoadName = "L1-A", Level = 1 }],
                LoadCasesLevel2 = [new LoadCase { LoadName = "L2-A", Level = 2 }],
            };

            var copy = lci.DeepCopy();

            Assert.AreNotSame(lci, copy);
            Assert.AreEqual(1.5, copy.LoadCombinationFactor);
            Assert.AreEqual(1, copy.LoadCombinations.Count);
            Assert.AreEqual(0.5, copy.LoadCombinations[0].Beta1);
            Assert.AreEqual(4, copy.LoadCombinationsPlus.Count);
            Assert.AreNotSame(lci.LoadCasesLevel1[0], copy.LoadCasesLevel1[0]);
            Assert.AreEqual("L1-A", copy.LoadCasesLevel1[0].LoadName);
            Assert.AreEqual("L2-A", copy.LoadCasesLevel2[0].LoadName);
        }
    }

    /// <summary>
    /// ElementDivision: SoilPiles 連動の番号オプション生成と、コンストラクタのデフォルト値。
    /// </summary>
    [TestClass]
    public class ElementDivisionTests
    {
        [TestMethod]
        public void Constructor_AssignsExpectedDefaults()
        {
            var ed = new ElementDivision();
            Assert.AreEqual(1, ed.PileGroundNo);
            Assert.AreEqual(1, ed.PileBodyNo);
            Assert.AreEqual(0.1, ed.FirstDistance);
            Assert.AreEqual(1.0, ed.MaxPileSpacing);
            Assert.AreEqual(5.0, ed.MaxEmbedmentSpacing);
            Assert.IsNotNull(ed.SoilPiles);
            Assert.AreEqual(0, ed.SoilPiles.Count);
        }

        [TestMethod]
        public void UpdateSoilPileNumberOption_GeneratesOneBasedRange()
        {
            var ed = new ElementDivision();
            ed.SoilPiles = [new SoilPile(), new SoilPile(), new SoilPile()];

            ed.UpdateSoilPileNumberOption();

            Assert.IsNotNull(ed.SoilPileNumberOption);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ed.SoilPileNumberOption);
        }

        [TestMethod]
        public void UpdateSoilPileNumberOption_EmptyPiles_GeneratesEmptyRange()
        {
            var ed = new ElementDivision();
            ed.UpdateSoilPileNumberOption();
            Assert.IsNotNull(ed.SoilPileNumberOption);
            Assert.AreEqual(0, ed.SoilPileNumberOption.Count);
        }

        [TestMethod]
        public void SoilPiles_Setter_FiresPropertyChanged()
        {
            var ed = new ElementDivision();
            string? lastProp = null;
            ((INotifyPropertyChanged)ed).PropertyChanged += (_, e) => lastProp = e.PropertyName;

            ed.SoilPiles = [new SoilPile()];

            Assert.AreEqual(nameof(ElementDivision.SoilPiles), lastProp);
        }

        [TestMethod]
        public void SetSoilPilesSilently_DoesNotFirePropertyChanged()
        {
            // Undo 退避用: DataGrid バインディング再構築を防ぐため通知抑止
            var ed = new ElementDivision();
            int fireCount = 0;
            ((INotifyPropertyChanged)ed).PropertyChanged += (_, _) => fireCount++;

            ed.SetSoilPilesSilently([new SoilPile()]);

            Assert.AreEqual(0, fireCount, "SetSoilPilesSilently は PropertyChanged を発火してはいけない");
        }

        [TestMethod]
        public void NumericProperties_Setters_FirePropertyChanged()
        {
            var ed = new ElementDivision();
            var changed = new List<string>();
            ((INotifyPropertyChanged)ed).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            ed.FirstDistance = 0.2;
            ed.MaxPileSpacing = 2.0;
            ed.MaxEmbedmentSpacing = 7.0;
            ed.PileGroundNo = 3;
            ed.PileBodyNo = 4;
            ed.Z = 1.5;
            ed.PileBottomAltitude = -10.0;

            CollectionAssert.Contains(changed, nameof(ElementDivision.FirstDistance));
            CollectionAssert.Contains(changed, nameof(ElementDivision.MaxPileSpacing));
            CollectionAssert.Contains(changed, nameof(ElementDivision.MaxEmbedmentSpacing));
            CollectionAssert.Contains(changed, nameof(ElementDivision.PileGroundNo));
            CollectionAssert.Contains(changed, nameof(ElementDivision.PileBodyNo));
            CollectionAssert.Contains(changed, nameof(ElementDivision.Z));
            CollectionAssert.Contains(changed, nameof(ElementDivision.PileBottomAltitude));
        }
    }
}
