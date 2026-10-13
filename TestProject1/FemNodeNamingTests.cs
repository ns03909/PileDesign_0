using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 解析モデルの節点の名前 (<see cref="FemNodeNaming"/>) と、杭番号 → 接合節点の索引
    /// (<see cref="AnaModel.PileJointNodeOf"/>) の検査。
    ///
    /// <para>以前は <c>"FoundationNode-P{No}"</c> / <c>"CapNode-"</c> / <c>"InputNode-"</c> / <c>"杭節点-{no}-0"</c>
    /// などを 20 か所あまりで手で組み立て・読み分けていた。画面には名前を作る関数の行単位の写しがあり、
    /// 変形図には自前の接頭辞の読み分けがあった。1 か所の書き方が変わると「節点が見つからない」が
    /// 例外にならずに起き、荷重が載らない・線が描かれない形で静かに壊れる。</para>
    ///
    /// <para>また、杭の接合節点は荷重ステップごとに杭ごと最大 5 回、名前を組み立てて節点の一覧を
    /// 先頭から探していた (杭数² × 節点数 の文字列比較と割り当て)。</para>
    /// </summary>
    [TestClass]
    public class FemNodeNamingTests
    {
        // ---- (a) 付け方と読み方の往復 ----

        /// <summary>名前の文字列は以前と 1 文字も変わらないこと (保存された結果・ログ・自由度キーと同じ)。</summary>
        [TestMethod]
        public void Builders_ProduceTheSameStringsAsBefore()
        {
            Assert.AreEqual("CapNode-3", FemNodeNaming.Cap(3));
            Assert.AreEqual("FoundationNode-P12", FemNodeNaming.PileJoint(12));
            Assert.AreEqual("FoundationNode-7", FemNodeNaming.FoundationBeam(7));
            Assert.AreEqual("InputNode-6", FemNodeNaming.Input(6));
            Assert.AreEqual("GroundNode-P4", FemNodeNaming.Ground(4));
            Assert.AreEqual("杭節点-12-3", FemNodeNaming.Pile(12, 3));
            Assert.AreEqual("杭節点-5-0", FemNodeNaming.PileHead(5));
            Assert.AreEqual("杭地盤節点-2-9", FemNodeNaming.PileSoil(2, 9));
            Assert.AreEqual("ActionPoint", FemNodeNaming.ActionPoint);
            Assert.AreEqual("根入部節点", FemNodeNaming.Embedment);
            Assert.AreEqual("根入部地盤節点", FemNodeNaming.EmbedmentSoil);
            Assert.AreEqual("ActionPoint:Uz", FemNodeNaming.DofKey(FemNodeNaming.ActionPoint, "Uz"));
        }

        /// <summary>すべての種類で、付けた名前を読むと元の種類・番号・節点順に戻ること (「名前:自由度」でも)。</summary>
        [TestMethod]
        public void EveryKind_RoundTrips()
        {
            int checkedCount = 0;
            foreach (int no in new[] { 0, 1, 7, 12, 123, 4096 })
            {
                foreach (int idx in new[] { 0, 1, 25 })
                {
                    var cases = new (string Name, FemNodeKind Kind, int Number, int Index)[]
                    {
                        (FemNodeNaming.Cap(no), FemNodeKind.Cap, no, -1),
                        (FemNodeNaming.PileJoint(no), FemNodeKind.PileJoint, no, -1),
                        (FemNodeNaming.FoundationBeam(no), FemNodeKind.FoundationBeam, no, -1),
                        (FemNodeNaming.Input(no), FemNodeKind.Input, no, -1),
                        (FemNodeNaming.Ground(no), FemNodeKind.Ground, no, -1),
                        (FemNodeNaming.Pile(no, idx), FemNodeKind.Pile, no, idx),
                        (FemNodeNaming.PileSoil(no, idx), FemNodeKind.PileSoil, no, idx),
                        (FemNodeNaming.ActionPoint, FemNodeKind.ActionPoint, -1, -1),
                        (FemNodeNaming.Embedment, FemNodeKind.Embedment, -1, -1),
                        (FemNodeNaming.EmbedmentSoil, FemNodeKind.EmbedmentSoil, -1, -1),
                    };
                    foreach (var c in cases)
                    {
                        foreach (var name in new[] { c.Name, FemNodeNaming.DofKey(c.Name, "Ry") })
                        {
                            var kind = FemNodeNaming.Parse(name, out int number, out int index);
                            Assert.AreEqual(c.Kind, kind, name);
                            Assert.AreEqual(c.Number, number, name);
                            Assert.AreEqual(c.Index, index, name);
                            checkedCount++;
                        }
                    }
                }
            }
            // 種類を足したら上の表にも足す
            int kinds = Enum.GetValues<FemNodeKind>().Length - 1;   // Unknown を除く
            Assert.AreEqual(10, kinds, "節点の種類が増えた/減った。往復の表を見直すこと");
            TestSource.AssertScanned(checkedCount, 6 * 3 * kinds * 2, "往復の検査");
        }

        /// <summary>接合節点と基礎梁の節点は接頭辞を共有する。取り違えないこと。</summary>
        [TestMethod]
        public void PileJointAndFoundationBeamNode_AreNotConfused()
        {
            Assert.AreEqual(FemNodeKind.PileJoint, FemNodeNaming.KindOf("FoundationNode-P3"));
            Assert.AreEqual(FemNodeKind.FoundationBeam, FemNodeNaming.KindOf("FoundationNode-3"));
            Assert.AreEqual(3, FemNodeNaming.PileJointNoOf("FoundationNode-P3"));
            Assert.IsNull(FemNodeNaming.PileJointNoOf("FoundationNode-3"));
            Assert.AreEqual(4, FemNodeNaming.CapPileNoOf("CapNode-4"));
            Assert.IsNull(FemNodeNaming.CapPileNoOf("杭節点-4-0"));
        }

        /// <summary>ここで付けない名前・番号が読めない名前は Unknown / null。</summary>
        [TestMethod]
        public void ForeignNames_AreUnknown()
        {
            foreach (var name in new[] { null, "", "dummy", "dummyI", "CapNode-", "CapNode-x", "FoundationNode-P",
                                         "杭節点-", "杭節点-12", "杭節点-x-0", "eq12", "RθXY-3" })
            {
                Assert.AreEqual(FemNodeKind.Unknown, FemNodeNaming.KindOf(name), name ?? "(null)");
                Assert.IsNull(FemNodeNaming.PileNoOf(name), name ?? "(null)");
            }
        }

        /// <summary>
        /// 種類で絞る判定 (基礎梁の端を座標で探す候補・剛床の slave にする節点) は、以前の
        /// <c>StartsWith</c> の組み合わせと同じ答えを返すこと。
        /// </summary>
        [TestMethod]
        public void PrefixPredicates_MatchTheOldStartsWithLogic()
        {
            var names = new[]
            {
                FemNodeNaming.ActionPoint, FemNodeNaming.Embedment, FemNodeNaming.EmbedmentSoil,
                FemNodeNaming.Cap(1), FemNodeNaming.PileJoint(1), FemNodeNaming.FoundationBeam(1),
                FemNodeNaming.Input(1), FemNodeNaming.Ground(1), FemNodeNaming.Pile(1, 0), FemNodeNaming.PileSoil(1, 0),
            };
            foreach (var n in names)
            {
                bool oldFoundationOrInput = n.StartsWith("FoundationNode-") || n.StartsWith("InputNode-");
                bool oldSlab = n.StartsWith("InputNode-") || (n.StartsWith("FoundationNode-") && !n.StartsWith("FoundationNode-P"));
                Assert.AreEqual(oldFoundationOrInput, FemNodeNaming.IsFoundationOrInput(n), n);
                Assert.AreEqual(oldSlab, FemNodeNaming.IsSlabNode(n), n);
                Assert.AreEqual(n.StartsWith("杭地盤節点-"), FemNodeNaming.IsPileSoil(n), n);
                Assert.AreEqual(n.StartsWith("GroundNode-"), FemNodeNaming.IsGround(n), n);
            }
        }

        /// <summary>残差の診断の呼び方は以前と同じ文になること。</summary>
        [TestMethod]
        public void ResidualLabels_AreUnchanged()
        {
            Assert.AreEqual("杭 No.3", ResidualHotspots.Label("杭節点-3-2"));
            Assert.AreEqual("荷重の作用点 (基礎)", ResidualHotspots.Label("ActionPoint"));
            Assert.AreEqual("杭 No.4 の杭頭 (基礎側)", ResidualHotspots.Label("CapNode-4"));
            Assert.AreEqual("杭 No.5 の接合節点", ResidualHotspots.Label("FoundationNode-P5"));
            Assert.AreEqual("基礎梁の節点 6", ResidualHotspots.Label("FoundationNode-6"));
            Assert.AreEqual("節点 7", ResidualHotspots.Label("InputNode-7"));
            Assert.AreEqual("根入部節点", ResidualHotspots.Label("根入部節点"));

            Assert.AreEqual(3, ResidualHotspots.PileNoOf("杭節点-3-2"));
            Assert.AreEqual(4, ResidualHotspots.PileNoOf("CapNode-4"));
            Assert.AreEqual(5, ResidualHotspots.PileNoOf("FoundationNode-P5"));
            Assert.IsNull(ResidualHotspots.PileNoOf("FoundationNode-6"));
            Assert.IsNull(ResidualHotspots.PileNoOf("ActionPoint"));
        }

        // ---- (b) 名前の文字列は FemNodeNaming の外に書かない ----

        // 文字列リテラルの先頭 (" の直後) に節点名の接頭辞・固定名があるもの。
        // 画面の説明文 (「…根入部節点 (NodeI, 杭側)…」) は文字列の途中なので当たらない。
        private static readonly Regex NodeNameLiteral = new(
            "\"(?:CapNode-|FoundationNode-|InputNode-|GroundNode-|杭節点-|杭地盤節点-)"
            + "|\"(?:ActionPoint|根入部節点|根入部地盤節点)[\":\\s{]",
            RegexOptions.Compiled);

        // 他の作業と同時に編集中のため、この変更では触らなかったファイル。
        // 直したら下の検査 (まだ違反を含むこと) が落ちるので、ここから消すこと。
        private static readonly HashSet<string> NotYetMigrated = new(StringComparer.Ordinal)
        {
            "Output/WordDocument.cs",
            "Output/WordDocument.SummaryTables.cs",
        };

        [TestMethod]
        public void NodeNameLiterals_LiveOnlyInFemNodeNaming()
        {
            // 捕まえる式が以前の書き方に当たること (空振りの防止)
            foreach (var old in new[]
            {
                "n.Name == $\"FoundationNode-P{pileNo}\"", "$\"CapNode-{pile.No}\"", "$\"InputNode-{n.No}\"",
                "$\"杭節点-{pile.No}-0\"", "StartsWith(\"杭地盤節点-\")", "StartsWith(\"GroundNode-\")",
                "n.Name == \"ActionPoint\"", "\"ActionPoint:Uz\"", "$\"ActionPoint: (", "s.NodeI?.Name == \"根入部節点\"",
                "FindNode(\"根入部地盤節点\",", "string prefixCap = \"CapNode-\";",
            })
                Assert.IsTrue(NodeNameLiteral.IsMatch(old), $"(前提) 式が以前の書き方に当たりません: {old}");
            // 画面の説明文には当たらないこと
            Assert.IsFalse(NodeNameLiteral.IsMatch("\"X 軸 (相対変位): 根入部節点 (NodeI, 杭側) の変位\""));

            string root = TestSource.Dir("Graphics_r1");
            var violations = new List<string>();
            var stillDirty = new HashSet<string>(StringComparer.Ordinal);
            int scanned = 0;
            int inNamingClass = 0;
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (rel.StartsWith("bin/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal)) continue;
                scanned++;
                string code = StripComments(File.ReadAllText(path));
                var hits = NodeNameLiteral.Matches(code);
                if (rel == "FEM/FemNodeNaming.cs") { inNamingClass = hits.Count; continue; }
                if (hits.Count == 0) continue;
                if (NotYetMigrated.Contains(rel)) { stillDirty.Add(rel); continue; }
                foreach (Match m in hits) violations.Add($"{rel}: {Line(code, m.Index)}");
            }

            TestSource.AssertScanned(scanned, 300, "Graphics_r1 の .cs");
            TestSource.AssertScanned(inNamingClass, 10, "FemNodeNaming の名前の文字列");
            Assert.AreEqual(0, violations.Count,
                "節点の名前を FemNodeNaming の外で組み立て・読み分けています。FemNodeNaming の付け方・読み方を使うこと:\n"
                + string.Join("\n", violations));
            foreach (var rel in NotYetMigrated)
                Assert.IsTrue(stillDirty.Contains(rel),
                    $"{rel} はもう節点名の文字列を含みません。NotYetMigrated から消すこと (除外が残ると次の違反を見逃す)");
        }

        /// <summary>画面の鉛直梁の結果は解析モデルの名前の付け方をそのまま使う (行単位の写しを持たない)。</summary>
        [TestMethod]
        public void VerticalResultCanvas_DelegatesToTheModelNaming()
        {
            string src = StripComments(TestSource.Read("Graphics_r1", "Views", "MainWindow.CanvasResultsVertical.cs"));
            string decl = TestSource.Region(src, "ResolveVBFemNodeName(InputModel", 200);
            StringAssert.Contains(decl, "AnalysisModelling.FemNodeNameOf(", "画面が名前を自前で作っています");
            Assert.IsFalse(src.Contains("case NodeReferenceType.", StringComparison.Ordinal),
                "名前を作る関数の写しが画面に戻っています");
        }

        // ---- (c) 杭番号 → 接合節点の索引 ----

        /// <summary>以前の探し方 (接合節点 → 杭頭、それぞれ一覧の先頭から)。比較の基準。</summary>
        private static Node? OldLinearSearch(AnaModel model, int pileNo)
        {
            var conn = model.Nodes.FirstOrDefault(n => n != null && n.Name == $"FoundationNode-P{pileNo}");
            if (conn != null) return conn;
            return model.Nodes.FirstOrDefault(n => n != null && n.Name == $"CapNode-{pileNo}");
        }

        private static AnaModel Build(InputModel input)
        {
            var m = new AnalysisModelling(input);
            return new AnaModel(input, m.Nodes, m.Beams, m.DummyBeams, m.RigidBodies, m.HorizontalSoilSprings, m.RotationalSprings);
        }

        private static IEnumerable<(string Label, InputModel Input, bool HasBeams)> Scenes()
        {
            foreach (var (ground, pile) in new[] { ("Example9", "PileExample9"), ("Example10", "PileExample10"), ("Example3_1", "PileExample3_1") })
            {
                var input = IntegrationTests.BuildExampleInputModel(ground, pile).Item1;
                if (input != null) yield return (pile, input, false);
            }
            foreach (var mode in new[] { FoundationBeamConnectionMode.RigidBody, FoundationBeamConnectionMode.RigidFloor })
            {
                var (input, _) = IterativeBeamSettlementInvariantTests.Scene();   // 計算例5 (基礎梁つき)
                if (input == null) continue;
                input.FoundationBeamInput.ConnectionMode = mode;
                yield return ($"計算例5 基礎梁 {mode}", input, true);
            }
        }

        /// <summary>
        /// 例題の全杭で、索引が以前の探し方と同じ節点を返すこと。基礎梁あり (接合節点) ・なし (杭頭) の両方、
        /// およびケースごとの複製 (DeepCopy) で。複製では<b>複製の節点</b>が返ること (元のモデルの節点を返すと、
        /// 並列に解くケースが互いの荷重を書き換える)。
        /// </summary>
        [TestMethod]
        public void PileJointIndex_ReturnsTheSameNodeAsTheOldLinearSearch()
        {
            int piles = 0, withBeams = 0, withoutBeams = 0;
            foreach (var (label, input, hasBeams) in Scenes())
            {
                var main = Build(input);
                var copy = main.DeepCopy();
                foreach (var pile in input.PileLayoutItems)
                {
                    var expectedMain = OldLinearSearch(main, pile.No);
                    var expectedCopy = OldLinearSearch(copy, pile.No);
                    Assert.IsNotNull(expectedMain, $"{label} 杭 {pile.No}: (前提) 接合節点が無い");
                    Assert.AreEqual(hasBeams ? FemNodeKind.PileJoint : FemNodeKind.Cap, FemNodeNaming.KindOf(expectedMain.Name),
                        $"{label} 杭 {pile.No}: (前提) 基礎梁の有無と接合節点の種類が合わない");

                    Assert.AreSame(expectedMain, main.PileJointNodeOf(pile.No), $"{label} 杭 {pile.No}: 元のモデル");
                    Assert.AreSame(expectedCopy, copy.PileJointNodeOf(pile.No), $"{label} 杭 {pile.No}: ケースの複製");
                    Assert.AreSame(expectedCopy, HorizontalCalculationViewModel.ResolvePileJointNodeInModel(copy, pile.No),
                        $"{label} 杭 {pile.No}: 解析が使う入口");
                    Assert.AreNotSame(expectedMain, copy.PileJointNodeOf(pile.No),
                        $"{label} 杭 {pile.No}: 複製が元のモデルの節点を返しています");
                    piles++;
                    if (hasBeams) withBeams++; else withoutBeams++;
                }
                Assert.IsNull(main.PileJointNodeOf(-12345), "無い杭番号は null");
            }
            TestSource.AssertScanned(withoutBeams, 10, "基礎梁なしの杭");
            TestSource.AssertScanned(withBeams, 4, "基礎梁ありの杭");
        }

        /// <summary>
        /// 索引を作ったあとで節点の一覧が変わったら作り直すこと (古い節点を返さない)。
        /// 追加 (数が変わる)・同じ位置の差し替え (数は同じ)・一覧そのものの差し替え、のどれでも。
        /// </summary>
        [TestMethod]
        public void PileJointIndex_FollowsChangesToTheNodeList()
        {
            var input = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9").Item1;
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var model = Build(input);
            int no = input.PileLayoutItems[0].No;
            var before = model.PileJointNodeOf(no);
            Assert.IsNotNull(before);

            // 1) 先頭に同じ名前の節点を足す → 以前の探し方は先頭のものを返す
            var inserted = new Node();
            inserted.SetNodeInfo(before.Name, 0, 0, 0);
            model.Nodes.Insert(0, inserted);
            Assert.AreSame(OldLinearSearch(model, no), model.PileJointNodeOf(no), "追加に追従していません");
            Assert.AreSame(inserted, model.PileJointNodeOf(no));

            // 2) 同じ位置で差し替える (数は変わらない)
            var replaced = new Node();
            replaced.SetNodeInfo(before.Name, 0, 0, 0);
            model.Nodes[0] = replaced;
            Assert.AreSame(replaced, model.PileJointNodeOf(no), "差し替えに追従していません");

            // 3) 接合節点 (基礎梁あり) を後から足す → 杭頭より優先
            var joint = new Node();
            joint.SetNodeInfo(FemNodeNaming.PileJoint(no), 0, 0, 0);
            model.Nodes.Add(joint);
            Assert.AreSame(joint, model.PileJointNodeOf(no), "接合節点を優先していません");

            // 4) 一覧そのものを差し替える
            model.Nodes = new List<Node>(model.Nodes.Where(n => !ReferenceEquals(n, joint)));
            Assert.AreSame(OldLinearSearch(model, no), model.PileJointNodeOf(no), "一覧の差し替えに追従していません");
        }

        /// <summary>
        /// 索引を作ったあとは、引いても割り当てが起きないこと (荷重ステップごとに杭数 × 最大 5 回引くため)。
        /// 以前は呼ぶたびに名前の文字列・ラムダ・列挙子を割り当て、節点の一覧を先頭から比べていた。
        /// </summary>
        [TestMethod]
        public void PileJointIndex_LookupDoesNotAllocate()
        {
            var input = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10").Item1;
            if (input == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var copy = Build(input).DeepCopy();
            var nos = input.PileLayoutItems.Select(p => p.No).ToArray();
            foreach (var no in nos) HorizontalCalculationViewModel.ResolvePileJointNodeInModel(copy, no);   // 索引を作る・JIT

            const int rounds = 200;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int r = 0; r < rounds; r++)
                foreach (var no in nos)
                    HorizontalCalculationViewModel.ResolvePileJointNodeInModel(copy, no);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            TestSource.AssertScanned(nos.Length, 2, "Example10 の杭");
            Assert.IsTrue(allocated < 1024,
                $"接合節点を引くたびに割り当てています ({allocated} バイト / {rounds * nos.Length} 回)。索引を使っていない");
        }

        // ---- 道具 ----

        /// <summary>コメント (// と /* */) を取り除く。コメントに残した以前の書き方を違反と数えないため。</summary>
        private static string StripComments(string code)
        {
            code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return Regex.Replace(code, @"//[^\r\n]*", "");
        }

        private static string Line(string code, int index)
        {
            int s = code.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            int e = code.IndexOf('\n', index);
            return code[s..(e < 0 ? code.Length : e)].Trim();
        }
    }
}
