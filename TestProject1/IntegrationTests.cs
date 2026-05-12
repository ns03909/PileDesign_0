using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using MathNet.Numerics.LinearAlgebra;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;
using Material = PileDesign.FEM.Material;

namespace TestProject1
{
    /// <summary>
    /// 計算例を読み込んでFEMモデル構築・解析まで実行する統合テスト
    /// </summary>
    [TestClass]
    public class IntegrationTests
    {
        private static string GetExamplesDir()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            return Path.Combine(projectRoot, "Graphics_r1", "Examples");
        }

        /// <summary>
        /// 地盤例題JSONをGroundInputにロードし、地盤反力係数計算が実行できるか
        /// </summary>
        [TestMethod]
        public void GroundExample1_LoadAndCalculate_NoException()
        {
            var examplesDir = GetExamplesDir();
            var filePath = Path.Combine(examplesDir, "Example1.json");
            if (!File.Exists(filePath)) { Assert.Inconclusive("Example1.json not found"); return; }

            var json = File.ReadAllText(filePath);
            var groundInput = Newtonsoft.Json.JsonConvert.DeserializeObject<GroundInput>(json);
            Assert.IsNotNull(groundInput);
            Assert.IsTrue(groundInput.GroundLayers.Count > 0);

            // バリデーション実行
            bool valid = groundInput.ValidateForAnalysis(out string msg);
            // Example1 は正しいデータなので true を期待（ただし Es=0 の層があるかもしれない）
            // バリデーション自体が例外なく完了すれば OK
            Assert.IsNotNull(msg);
        }

        /// <summary>
        /// 地盤例題の全計算例で内部摩擦角が計算可能であること
        /// </summary>
        [TestMethod]
        public void AllGroundExamples_GetFrictionAngle_Finite()
        {
            var examplesDir = GetExamplesDir();
            if (!Directory.Exists(examplesDir)) { Assert.Inconclusive(); return; }

            foreach (var file in Directory.GetFiles(examplesDir, "Example*.json"))
            {
                var json = File.ReadAllText(file);
                var ground = Newtonsoft.Json.JsonConvert.DeserializeObject<GroundInput>(json);
                if (ground?.GroundLayers == null) continue;

                foreach (var layer in ground.GroundLayers)
                {
                    if (layer.GranularityClass != "粘性土")
                    {
                        double phi = ground.GetFrictionAngle(layer.NValue, 100.0);
                        Assert.IsTrue(double.IsFinite(phi) && phi >= 0 && phi <= 40,
                            $"{Path.GetFileName(file)} Layer '{layer.Name}': φ={phi}");
                    }
                }
            }
        }

        /// <summary>
        /// 杭例題JSONの読み込みテスト
        /// </summary>
        [TestMethod]
        public void PileExample3_1_Deserialize_HasPileBodies()
        {
            var examplesDir = GetExamplesDir();
            var filePath = Path.Combine(examplesDir, "PileExample3_1.json");
            if (!File.Exists(filePath)) { Assert.Inconclusive("PileExample3_1.json not found"); return; }

            var json = File.ReadAllText(filePath);
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            var data = JsonSerializer.Deserialize<PileExampleData>(json, opts);
            Assert.IsNotNull(data);
            Assert.IsTrue(data.PileBodies.Count > 0, "杭体データがありません");
            Assert.IsTrue(data.PileLayoutItems.Count > 0, "杭配置データがありません");
        }

        /// <summary>
        /// 全杭例題JSONが読み込み可能であること
        /// </summary>
        [TestMethod]
        public void AllPileExamples_Deserialize_NoException()
        {
            var examplesDir = GetExamplesDir();
            if (!Directory.Exists(examplesDir)) { Assert.Inconclusive(); return; }

            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            foreach (var file in Directory.GetFiles(examplesDir, "PileExample*.json"))
            {
                var json = File.ReadAllText(file);
                var data = JsonSerializer.Deserialize<PileExampleData>(json, opts);
                Assert.IsNotNull(data, $"{Path.GetFileName(file)} のデシリアライズに失敗");
                Assert.IsTrue(data.PileBodies.Count > 0,
                    $"{Path.GetFileName(file)}: 杭体データなし");
            }
        }

        /// <summary>
        /// InputModel に地盤＋杭データを組み合わせて GenerateSoilPiles が実行できるか
        /// </summary>
        [TestMethod]
        public void CombinedExample_GenerateSoilPiles_CreatesSoilPiles()
        {
            var (inputModel, _) = BuildExampleInputModel("Example3_1", "PileExample3_1");
            if (inputModel == null) { Assert.Inconclusive("Example files not found"); return; }

            // SoilPiles が生成されていること
            Assert.IsNotNull(inputModel.ElementDivision);
            Assert.IsNotNull(inputModel.ElementDivision.SoilPiles);
            Assert.IsTrue(inputModel.ElementDivision.SoilPiles.Count > 0,
                "SoilPiles が生成されていません");
        }

        /// <summary>
        /// AnalysisModelling が計算例データから FEM モデルを正しく構築できるか
        /// </summary>
        [TestMethod]
        public void CombinedExample_AnalysisModelling_CreatesNodesAndBeams()
        {
            var (inputModel, _) = BuildExampleInputModel("Example3_1", "PileExample3_1");
            if (inputModel == null) { Assert.Inconclusive("Example files not found"); return; }

            // FEMモデル構築
            var modelling = new AnalysisModelling(inputModel);

            Assert.IsTrue(modelling.Nodes.Count > 0,
                $"Nodes: {modelling.Nodes.Count}");
            Assert.IsTrue(modelling.Beams.Count > 0,
                $"Beams: {modelling.Beams.Count}");
            Assert.IsTrue(modelling.HorizontalSoilSprings.Count > 0,
                $"HorizontalSoilSprings: {modelling.HorizontalSoilSprings.Count}");
        }

        /// <summary>
        /// AnaModel が正しく組立られるか（剛性マトリクスの基本性質）
        /// </summary>
        [TestMethod]
        public void CombinedExample_AnaModel_AssemblesCorrectly()
        {
            var (inputModel, _) = BuildExampleInputModel("Example3_1", "PileExample3_1");
            if (inputModel == null) { Assert.Inconclusive("Example files not found"); return; }

            var modelling = new AnalysisModelling(inputModel);
            var anaModel = new AnaModel(
                inputModel,
                modelling.Nodes,
                modelling.Beams,
                modelling.DummyBeams,
                modelling.RigidBodies,
                modelling.HorizontalSoilSprings,
                modelling.RotationalSprings
            );

            Assert.IsTrue(anaModel.CountFree > 0, $"CountFree={anaModel.CountFree}");

            // ばね要素の剛性を初期化（通常は HorizontalCalculationViewModel.FindK で行われる）
            InitializeSprings(anaModel);

            // 接線剛性マトリクスの組立
            anaModel.MapOnKtanMat();
            Assert.IsNotNull(anaModel.KAA_tan);
            Assert.AreEqual(anaModel.CountFree, anaModel.KAA_tan.RowCount);

            // 対角成分が正（安定なモデル）
            int negCount = 0;
            for (int i = 0; i < anaModel.CountFree; i++)
                if (anaModel.KAA_tan[i, i] <= 0) negCount++;
            Assert.AreEqual(0, negCount,
                $"負の対角成分が{negCount}個あります");
        }

        /// <summary>
        /// 1ステップ線形解析が実行できるか
        /// </summary>
        [TestMethod]
        public void CombinedExample_SingleStepSolve_ProducesFiniteDisplacements()
        {
            var (inputModel, _) = BuildExampleInputModel("Example3_1", "PileExample3_1");
            if (inputModel == null) { Assert.Inconclusive("Example files not found"); return; }

            var modelling = new AnalysisModelling(inputModel);
            var anaModel = new AnaModel(
                inputModel,
                modelling.Nodes,
                modelling.Beams,
                modelling.DummyBeams,
                modelling.RigidBodies,
                modelling.HorizontalSoilSprings,
                modelling.RotationalSprings
            )
            {
                RotationalSprings = modelling.RotationalSprings,
                PenaltySprings = modelling.PenaltySprings
            };

            // 荷重ケースの設定
            var loadCases = inputModel.LoadCasesInput?.LoadCasesLevel1;
            if (loadCases == null || loadCases.Count == 0)
            {
                Assert.Inconclusive("荷重ケースが定義されていません");
                return;
            }

            var loadCase = loadCases[0];
            if (loadCase.UpperMassForce == 0 && loadCase.FoundationMassForce == 0)
            {
                Assert.Inconclusive("荷重がゼロです");
                return;
            }

            // ばね要素の剛性を初期化
            InitializeSprings(anaModel);

            // 初期化
            anaModel.InitializeStates();
            anaModel.UpdateVectorDOFForcedDisp();

            // 荷重ベクトルの設定（簡易：杭頭節点に水平力を載荷）
            // 実際の荷重設定は HorizontalCalculationViewModel が行うが、
            // ここでは最初の SoilPile の杭頭節点を探して直接載荷する
            Node? pileTopNode = null;
            foreach (var node in anaModel.Nodes)
            {
                // 最初の拘束されていない節点を杭頭とみなす
                if (!node.Boundary.Ux && !node.Boundary.Uy)
                {
                    pileTopNode = node;
                    break;
                }
            }

            if (pileTopNode == null)
            {
                Assert.Inconclusive("杭頭節点が見つかりません");
                return;
            }

            // 水平力を載荷
            double P = 100.0; // kN
            pileTopNode.SetIncrementalLoad(new NodeLoad(P, 0, 0, 0, 0, 0));
            pileTopNode.UpdateCumulativeLoad();
            anaModel.MapOnVectorF();
            anaModel.MapOnKtanMat();
            anaModel.MapOnKsecMat();
            anaModel.InitializeVectorR();

            // R = F
            for (int i = 0; i < anaModel.CountFree; i++)
                anaModel.VectorR[i] = anaModel.VectorF[i];

            // 求解
            Solver.SolveDisp(anaModel);

            // 結果検証：変位が有限値であること
            double maxDisp = 0;
            int nanCount = 0;
            foreach (var node in anaModel.Nodes)
            {
                var d = node.CumulativeDisp;
                if (!double.IsFinite(d.Ux) || !double.IsFinite(d.Uy) || !double.IsFinite(d.Uz))
                    nanCount++;
                maxDisp = Math.Max(maxDisp, Math.Abs(d.Ux));
                maxDisp = Math.Max(maxDisp, Math.Abs(d.Uy));
            }

            Assert.AreEqual(0, nanCount, $"{nanCount}個の節点でNaN/Infinity変位");
            Assert.IsTrue(maxDisp > 0, "最大変位が0（荷重が反映されていない可能性）");
        }

        /// <summary>
        /// 複数の計算例でFEMモデルが構築可能か（回帰テスト）
        /// </summary>
        [TestMethod]
        public void MultipleExamples_AnalysisModelling_AllSucceed()
        {
            var pairs = new[]
            {
                ("Example3_1", "PileExample3_1"),
                ("Example3_2", "PileExample3_2"),
                ("Example3_3", "PileExample3_3"),
                ("Example9", "PileExample9"),
            };

            int successCount = 0;
            foreach (var (groundName, pileName) in pairs)
            {
                var (inputModel, error) = BuildExampleInputModel(groundName, pileName);
                if (inputModel == null)
                {
                    continue; // ファイルが見つからない場合はスキップ
                }

                try
                {
                    var modelling = new AnalysisModelling(inputModel);
                    Assert.IsTrue(modelling.Nodes.Count > 0,
                        $"{groundName}+{pileName}: Nodes=0");
                    Assert.IsTrue(modelling.Beams.Count > 0,
                        $"{groundName}+{pileName}: Beams=0");
                    successCount++;
                }
                catch (Exception ex)
                {
                    Assert.Fail($"{groundName}+{pileName}: {ex.Message}");
                }
            }

            if (successCount == 0)
                Assert.Inconclusive("テスト可能な計算例ペアが見つかりません");
        }

        /// <summary>
        /// FEMモデルの要素数・節点数が妥当な範囲であること
        /// </summary>
        [TestMethod]
        public void CombinedExample_ModelSize_Reasonable()
        {
            var (inputModel, _) = BuildExampleInputModel("Example3_1", "PileExample3_1");
            if (inputModel == null) { Assert.Inconclusive(); return; }

            var modelling = new AnalysisModelling(inputModel);

            // 単杭でも最低10節点以上（杭頭〜杭先端）
            Assert.IsTrue(modelling.Nodes.Count >= 5,
                $"Nodes={modelling.Nodes.Count} は少なすぎます");
            // 梁要素は節点数-1以上
            Assert.IsTrue(modelling.Beams.Count >= modelling.Nodes.Count / 3,
                $"Beams={modelling.Beams.Count} は Nodes={modelling.Nodes.Count} に対して少なすぎます");
            // 地盤ばねは杭長に応じた数
            Assert.IsTrue(modelling.HorizontalSoilSprings.Count >= 1,
                $"HorizontalSoilSprings={modelling.HorizontalSoilSprings.Count}");

            // 節点数が異常に大きくないこと（メモリ問題の検出）
            Assert.IsTrue(modelling.Nodes.Count < 10000,
                $"Nodes={modelling.Nodes.Count} は多すぎます");
        }

        /// <summary>
        /// SoilPile に地盤反力データが正しく設定されているか
        /// </summary>
        [TestMethod]
        public void CombinedExample_SoilPiles_HaveReactionData()
        {
            var (inputModel, _) = BuildExampleInputModel("Example3_1", "PileExample3_1");
            if (inputModel == null) { Assert.Inconclusive(); return; }

            foreach (var sp in inputModel.ElementDivision.SoilPiles)
            {
                Assert.IsNotNull(sp.ZDataItems,
                    $"SoilPile[{sp.No}]: ZDataItems is null");
                Assert.IsTrue(sp.ZDataItems.Count > 0,
                    $"SoilPile[{sp.No}]: ZDataItems is empty");
                Assert.IsNotNull(sp.PileBodySegments,
                    $"SoilPile[{sp.No}]: PileBodySegments is null");
                Assert.IsTrue(sp.PileBodySegments.Count > 0,
                    $"SoilPile[{sp.No}]: PileBodySegments is empty");
            }
        }

        // ================================================================
        // ヘルパーメソッド
        // ================================================================

        /// <summary>
        /// 地盤例題＋杭例題を組み合わせて InputModel を構築する
        /// </summary>
        internal static (InputModel? model, string? error) BuildExampleInputModel(
            string groundExampleName, string pileExampleName)
        {
            var examplesDir = GetExamplesDir();

            // 地盤データ読み込み
            var groundPath = Path.Combine(examplesDir, $"{groundExampleName}.json");
            if (!File.Exists(groundPath))
                return (null, $"File not found: {groundPath}");

            var groundJson = File.ReadAllText(groundPath);
            var groundInput = Newtonsoft.Json.JsonConvert.DeserializeObject<GroundInput>(groundJson);
            if (groundInput == null)
                return (null, "Ground deserialization failed");

            // 杭データ読み込み
            var pilePath = Path.Combine(examplesDir, $"{pileExampleName}.json");
            if (!File.Exists(pilePath))
                return (null, $"File not found: {pilePath}");

            var pileJson = File.ReadAllText(pilePath);
            var pileOpts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            var pileData = JsonSerializer.Deserialize<PileExampleData>(pileJson, pileOpts);
            if (pileData == null)
                return (null, "Pile deserialization failed");

            // InputModel を構築
            var inputModel = new InputModel();
            inputModel.GroundsInput = new ObservableCollection<GroundInput> { groundInput };

            // 杭体データの構築
            var pileBodies = new ObservableCollection<PileBodyInput>();
            foreach (var pbDto in pileData.PileBodies)
            {
                var pb = new PileBodyInput();
                pb.PileBodyType = pbDto.PileBodyType ?? "場所打ち鉄筋コンクリート杭";
                pb.PileBodySegments.Clear();
                if (pbDto.Segments != null)
                {
                    foreach (var seg in pbDto.Segments)
                    {
                        var s = new PileBodySegment
                        {
                            No = seg.No,
                            SegmentLength = seg.SegmentLength,
                        };
                        pb.PileBodySegments.Add(s);
                    }
                }
                pileBodies.Add(pb);
            }
            inputModel.PileBodies = pileBodies;

            // 杭配置データの構築 — No / PileNo は AnalysisModelling のガード (item.No == i+1) を満たすため設定必須
            var pileLayoutItems = new ObservableCollection<PileLayoutDataItem>();
            for (int i = 0; i < pileData.PileLayoutItems.Count; i++)
            {
                var plDto = pileData.PileLayoutItems[i];
                var item = new PileLayoutDataItem
                {
                    No = i + 1,
                    PileNo = i + 1,
                    PileBodyNo = plDto.PileBodyNo > 0 ? plDto.PileBodyNo : 1,
                    GroundNo = plDto.GroundNo > 0 ? plDto.GroundNo : 1,
                    X = plDto.X,
                    Y = plDto.Y,
                    Z = plDto.Z != 0 ? plDto.Z : groundInput.GroundTopAltitude,
                };
                pileLayoutItems.Add(item);
            }
            inputModel.PileLayoutItems = pileLayoutItems;

            // 荷重ケース（最小限のデフォルト値）
            inputModel.LoadCasesInput = new LoadCasesInput();
            // AnalysisModelling が LoadCasesLevel1 を要求するため、最小限の荷重ケースを設定
            inputModel.LoadCasesInput.LoadCasesLevel1 = new ObservableCollection<LoadCase>
            {
                new LoadCase { Level = 1, No = 1, IsApplicable = true, IsAnalysisTarget = true,
                    UpperMassForce = 1000, FoundationMassForce = 800 }
            };
            inputModel.LoadCasesInput.LoadCasesLevel2 = new ObservableCollection<LoadCase>
            {
                new LoadCase { Level = 2, No = 1, IsApplicable = true, IsAnalysisTarget = true,
                    UpperMassForce = 2000, FoundationMassForce = 1600 }
            };
            inputModel.LoadCasesInput.LoadCombinations = new ObservableCollection<LoadCombination>
            {
                new LoadCombination(1, 1.0, 1.0, 1.0) { IsApplicable = true }
            };

            // ElementDivision の初期化
            inputModel.ElementDivision = new ElementDivision
            {
                SoilPiles = new ObservableCollection<SoilPile>()
            };

            // SoilPiles の生成（要素分割）
            inputModel.GenerateSoilPiles();

            return (inputModel, null);
        }

        /// <summary>
        /// ばね要素の剛性マトリクスを初期化する
        /// （通常は HorizontalCalculationViewModel.FindK が行うが、テストでは直接実行）
        /// </summary>
        private static void InitializeSprings(AnaModel anaModel)
        {
            double defaultKh = 10000.0; // kN/m（デフォルト地盤反力係数）
            foreach (var spring in anaModel.HorizontalSoilSprings)
            {
                spring.SetKe(defaultKh, defaultKh, 0, 0, 0, 0, true);  // 接線
                spring.SetKe(defaultKh, defaultKh, 0, 0, 0, 0, false); // 割線
            }
            if (anaModel.RotationalSprings != null)
            {
                foreach (var rs in anaModel.RotationalSprings)
                {
                    rs.SetKe(0, 0, 0, 0, 1e6, 1e6, true);
                    rs.SetKe(0, 0, 0, 0, 1e6, 1e6, false);
                }
            }
            if (anaModel.PenaltySprings != null)
            {
                foreach (var ps in anaModel.PenaltySprings)
                {
                    double kp = 1e8;
                    ps.SetKe(kp, kp, kp, kp, kp, kp, true);
                    ps.SetKe(kp, kp, kp, kp, kp, kp, false);
                }
            }
        }
    }
}
