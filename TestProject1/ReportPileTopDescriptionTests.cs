using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.PileLibrary;
using PileDesign.Output;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 計算書の杭頭接合部の記述 (2026-11-28 の点検)。
    /// <list type="bullet">
    /// <item>諸元の表は、杭頭部ウィンドウで作って保存された諸元ではなく、いまの入力と解析と同じ派生値から組む。
    /// 保存された諸元はウィンドウを開いた時点のもので、以前の版で保存したファイルではキャプリングの Ke などが
    /// 解析・検定と違う古い値のまま計算書に出ていた。</item>
    /// <item>工法の説明は、ヘルプと同じ文献 (表紙の書名・版・評定番号) と同じ検定 (Ma・PC リングのせん断) を書く。
    /// キャプテンパイルの評定番号が FD0230-01 のまま、Ma と PC リングのせん断の検定が無く、
    /// NewJ-BAR は説明の章が無かった。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class ReportPileTopDescriptionTests
    {
        [TestMethod]
        public void TheSpecsTable_IsBuiltFromTheCurrentModel_NotFromTheSavedSpecs()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("ExampleCAP3_7", "PileExampleCAP3_7");
            if (input == null) Assert.Inconclusive($"例題ロード失敗: {error}");
            var body = input!.PileBodies.First(pb => pb.PileTopType == "キャプリングパイル工法");
            var top = body.PileTop;

            // 保存ファイルに入っていた古い諸元 (ウィンドウを開いた時点の値)
            top.SelectedPileTopSpecification = [new Spec("初期回転剛性 (圧縮時)", "Ke", "1", "kN·m/rad")];

            var specs = WordDocument.CurrentPileTopSpecs(body, top)!;
            var ke = specs.Single(s => s.Mark == "Ke");
            Assert.AreEqual($"{top.CapringPile!.Ke * 1e-6:N0}", ke.Value, "計算書の Ke が、解析が使う値と違います");
            Assert.AreNotEqual("1", ke.Value, "保存された古い諸元を出しています");
            Assert.IsTrue(specs.Any(s => s.Mark == "Fc"), "パイルキャップのコンクリートの諸元がありません");
            Assert.AreEqual("1", top.SelectedPileTopSpecification![0].Value, "計算書が入力の諸元を書き換えています");

            // 鉄筋定着工法も、保存された諸元ではなく組み直したもの
            body.PileTopType = "鉄筋定着工法";
            top.SelectedPileTopSpecification = [new Spec("古い行", "", "", "")];
            var rebar = WordDocument.CurrentPileTopSpecs(body, top)!;
            Assert.IsFalse(rebar.Any(s => s.Item == "古い行"), "鉄筋定着工法で保存された古い諸元を出しています");
            Assert.IsTrue(rebar.Any(s => s.Mark == "Dc"), "杭頭部 RC 径がありません");
        }

        [TestMethod]
        public void TheMethodDescriptions_MatchTheHelp()
        {
            string src = TestSource.Read("Graphics_r1", "Output", "WordDocument.Descriptions.cs");
            string help = TestSource.Read("Graphics_r1", "Help", "help.html");

            string captain = TestSource.MethodBody(src, "private void AddDescriptionCaptainPile(Body body)");
            string capring = TestSource.MethodBody(src, "private void AddDescriptionCapringPile(Body body)");
            string newJBar = TestSource.MethodBody(src, "private void AddDescriptionNewJBar(Body body, bool hasInfill)");

            // 評定番号・書名は、ヘルプ (出典 PDF の表紙で確かめたもの) と同じ
            foreach (var (method, name, expected) in new[]
            {
                (captain, "キャプテンパイル工法", "FD0230-05"),
                (captain, "キャプテンパイル工法", "場所打ちコンクリート杭用杭頭半固定工法"),
                (capring, "キャプリングパイル工法", "FD0060-05"),
                (capring, "キャプリングパイル工法", "既製杭用杭頭半固定工法"),
                (newJBar, "NewJ-BAR", "開先付き異形棒鋼 NewJ-BAR WSD390 WSD490 設計マニュアル (杭頭補強編)"),
            })
            {
                StringAssert.Contains(method, expected, $"計算書の {name} の説明に「{expected}」がありません");
                StringAssert.Contains(help, expected, $"ヘルプに「{expected}」がありません (計算書と食い違います)");
            }
            Assert.IsFalse(src.Contains("FD0230-01", StringComparison.Ordinal), "キャプテンパイル工法の古い評定番号が残っています");

            // 検定の内容: キャプテンは Ma (レベル1)・PC リングのせん断、キャプリングは PC リングのせん断
            StringAssert.Contains(captain, "AddPCRingShearDescription(", "キャプテンパイル工法の PC リングのせん断の検定がありません");
            StringAssert.Contains(captain, "短期許容曲げモーメント\", Tex(@\"M_{a}\")", "キャプテンパイル工法の Ma の検定がありません");
            StringAssert.Contains(captain, "CaptainUltimateMomentText(", "キャプテンパイル工法の本計算の Mu の取り方がありません");
            Assert.IsFalse(captain.Contains(@"\varepsilon_{0}", StringComparison.Ordinal), "e 関数に定義の無い ε0 が残っています (εB)");
            StringAssert.Contains(capring, "AddPCRingShearDescription(", "キャプリングパイル工法の PC リングのせん断の検定がありません");
            StringAssert.Contains(capring, "ケース①′", "キャプリングパイル工法の引張定着筋なし・引張のケースがありません");

            // NewJ-BAR は剛接合で解く断り書き (回転剛性の規定が無い理由) を計算書に書かない (利用者の判断 2026-11-27)
            StringAssert.Contains(newJBar, "剛接合として解く");
            Assert.IsFalse(newJBar.Contains("回転剛性を定めていない", StringComparison.Ordinal) || newJBar.Contains("規定がない", StringComparison.Ordinal),
                "NewJ-BAR を剛接合で解く理由の断り書きは計算書に書かない");

            string report = TestSource.Read("Graphics_r1", "Output", "WordDocument.cs");
            StringAssert.Contains(report, "AddDescriptionNewJBar(", "NewJ-BAR の説明を計算書に出していません");
        }

        /// <summary>
        /// NewJ-BAR の説明が実装の分岐と食い違わないこと (2026-11-28 の指摘 2 件)。
        /// <list type="bullet">
        /// <item>中詰補強筋は本数によらず円環だが、NewJ-BAR は 8 本未満なら中詰補強筋があっても個々の位置で弱軸方向を求める。
        /// 以前は「2 段の円環」と書いていて、同じ章の前段 (8 本未満は弱軸方向) と矛盾した。</item>
        /// <item>軸力が範囲外で限界値 0 (NG) にするのは曲げだけで、せん断は軸力によらない支圧の耐力。
        /// 以前は曲げとせん断をまとめて「同じ軸力で求めた限界値」「範囲外なら限界値 0」と書いていた。</item>
        /// </list>
        /// </summary>
        [TestMethod]
        public void TheNewJBarDescription_FollowsTheBranchesOfTheImplementation()
        {
            // 実装の前提: 8 本未満は中詰補強筋があっても円環にしない。せん断の耐力は軸力を取らない
            var few = new PileDesign.Models.NewJBar { Grade = "WSD490", Size = "WD38J", Count = 6,
                InfillBarCount = 12, InfillBarSize = "D25", InfillBarGrade = "SD390", InfillBarCover = 100 };
            Assert.IsTrue(few.HasInfill);
            Assert.IsFalse(few.UsesRing, "(前提) 8 本未満の NewJ-BAR は中詰補強筋があっても円環にしない");
            Assert.IsTrue(new PileDesign.Models.NewJBar { Count = 8 }.UsesRing, "(前提) 8 本以上は円環");

            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.Descriptions.cs"),
                "private void AddDescriptionNewJBar(Body body, bool hasInfill)");
            int infillAt = body.IndexOf("\"中詰補強筋\", 2", StringComparison.Ordinal);
            int shearAt = body.IndexOf("\"せん断\", 2", StringComparison.Ordinal);
            Assert.IsTrue(infillAt > 0 && shearAt > infillAt, "(前提) 中詰補強筋とせん断の節");
            string infill = body[infillAt..shearAt];
            Assert.IsFalse(infill.Contains("2 段の円環", StringComparison.Ordinal), "中詰補強筋の節で、8 本未満の NewJ-BAR まで円環と書いています");
            StringAssert.Contains(infill, "8 本未満は個々の位置");
            StringAssert.Contains(infill, "中詰補強筋は本数によらず円環");

            int checkAt = body.IndexOf("\"杭頭接合部の検定 (NewJ-BAR)\"", StringComparison.Ordinal);
            string check = body[checkAt..];
            int shearPara = check.IndexOf("\"せん断は、", StringComparison.Ordinal);
            Assert.IsTrue(shearPara > 0, "検定の説明で、曲げとせん断を分けていません");
            string bending = check[..shearPara], shear = check[shearPara..];
            StringAssert.Contains(bending, "限界値を 0 (NG)", "軸力が範囲外の扱いは曲げの段落に書く");
            Assert.IsFalse(shear.Contains("限界値を 0", StringComparison.Ordinal), "せん断にも軸力範囲外で 0 にすると読めます");
            StringAssert.Contains(shear, "軸力によらず");
        }

        /// <summary>
        /// キャプテンパイル工法の σmax は、実装 (CTPConcrete) の上限 2Fc を含めて書く。以前の計算書は σmax = Fc/ν² とだけ書いていて、
        /// ν = 0.70 では式の 2.04Fc に対し実装は 2Fc で、計算書の式で検算した値が合わなかった (2026-11-28 の指摘。以前からの食い違い)。
        /// </summary>
        [TestMethod]
        public void TheCaptainSigmaMax_IncludesTheCapUsedByTheImplementation()
        {
            const double fc = 24.0;
            var narrow = new PileDesign.Models.CTPConcrete(fc, 0.70, 1200);
            Assert.AreEqual(2 * fc, narrow.SigmaMax, 1e-9, "(前提) ν = 0.70 では σmax = 2Fc");
            Assert.IsTrue(fc / (0.70 * 0.70) > 2 * fc, "(前提) 同書の式は 2Fc を超える");
            Assert.AreEqual(fc / (0.85 * 0.85), new PileDesign.Models.CTPConcrete(fc, 0.85, 1200).SigmaMax, 1e-9, "(前提) ν = 0.85 は上限にかからない");

            string captain = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.Descriptions.cs"),
                "private void AddDescriptionCaptainPile(Body body)");
            StringAssert.Contains(captain, @"\sigma_{max} = \min\left(\frac{F_{c}}{\nu^{2}},\ 2F_{c}\right)", "計算書の σmax に上限 2Fc がありません");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Help", "help.html"), @"\sigma_{max} = \min(F_c/\nu^2,\ 2F_c)", "ヘルプの σmax の定義に上限がありません");
        }

        /// <summary>
        /// キャプテンパイル工法の Mu の説明は、同書の規定 (最大曲げモーメント) と本計算の取り方を分け、
        /// 本計算の取り方はそのモデルの設定どおりに書く。以前は ⑥ で「Mu は最大曲げモーメントとする」と断定し、
        /// 直後に既定 (εcu = 0.005) を書いていたので、計算書だけを読むと採用した Mu を取り違えた (2026-11-28 の指摘)。
        /// </summary>
        [TestMethod]
        public void TheCaptainUltimateMoment_IsDescribedAsTheModelComputesIt()
        {
            var byStrain = new PileDesign.Models.CaptainPile();   // 既定: εcu = 0.005・強度倍率 1.1
            string text = WordDocument.CaptainUltimateMomentText([(1, byStrain)]);
            StringAssert.Contains(text, "εcu = 0.005 に達したとき");
            StringAssert.Contains(text, "強度倍率 1.1");
            StringAssert.Contains(text, "整合する設定");
            Assert.IsFalse(text.Contains("全塑性", StringComparison.Ordinal), "既定なのに全塑性と書いています: " + text);

            var plastic = new PileDesign.Models.CaptainPile { UseMaximumUltimateMoment = true, UltimateRebarStrengthFactor = 1.0 };
            text = WordDocument.CaptainUltimateMomentText([(1, plastic)]);
            StringAssert.Contains(text, "最大曲げモーメント (全塑性");
            StringAssert.Contains(text, "強度倍率 1 を");
            StringAssert.Contains(text, "とは異なる");
            Assert.IsFalse(text.Contains("εcu = 0.005 に達したとき", StringComparison.Ordinal));

            // 杭体ごとに違えば、杭体番号を添えて分けて書く
            text = WordDocument.CaptainUltimateMomentText([(1, byStrain), (2, plastic), (3, new PileDesign.Models.CaptainPile())]);
            StringAssert.Contains(text, "杭体 1・3 では圧縮縁のひずみ");
            StringAssert.Contains(text, "杭体 2 では最大曲げモーメント");

            // ⑥ は同書の規定として書き、本計算の仮定として断定しない
            string captain = TestSource.MethodBody(TestSource.Read("Graphics_r1", "Output", "WordDocument.Descriptions.cs"),
                "private void AddDescriptionCaptainPile(Body body)");
            Assert.IsFalse(captain.Contains("\"は、最大曲げモーメントとする。\"", StringComparison.Ordinal),
                "⑥ で Mu を最大曲げモーメントと断定しています");
            StringAssert.Contains(captain, "同書では最大曲げモーメントとしている");
            StringAssert.Contains(captain, "CaptainUltimateMomentText(", "本計算の Mu の取り方をモデルの設定から書いていません");
        }
    }
}
