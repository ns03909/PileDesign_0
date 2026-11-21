using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 鋼管杭の杭頭部 (l1・l2) を杭体ウィンドウで入れ、杭体の上の 2 区間を作る (2026-11-21 利用者の要望)。
    ///
    /// <para>l1 (コンクリート充填部) と l2 (充填部下端〜鋼管の断面変化部) は杭体の上の 2 区間そのもので、
    /// 以前は杭断面に別の入力として持ち、区間長と二重になっていた。利用者の決めたこと:
    /// 杭の全長は保つ (下の区間で吸収)・上 2 区間の長さは表で編集しない・l1 + l2 &gt; 4D は受け付けない・
    /// l1 = 0 は充填部なし。</para>
    /// </summary>
    [TestClass]
    public class HeadZoneSegmentsTests
    {
        private static PileSection Pipe(double ts, string type = PileTypeNames.SteelPipeSection)
            => new() { PileBodyType = PileTypeNames.SteelPipe, PileSectionType = type, PipeDia = 800, PipeTs = ts, ConcreteFc = 24 };

        /// <summary>
        /// 鋼管杭の杭体。区間の一覧を入れると断面の諸元が初期化されることがあるので、一覧を入れてから断面を入れる。
        /// </summary>
        private static PileBodyInput Make(params (double Length, PileSection Section)[] segments)
        {
            var body = new PileBodyInput { PileBodyType = PileTypeNames.SteelPipe };
            body.PileBodySegments = new System.Collections.ObjectModel.ObservableCollection<PileBodySegment>(
                segments.Select((s, i) => new PileBodySegment { No = i + 1, SegmentLength = s.Length }));
            for (int i = 0; i < segments.Length; i++) body.PileBodySegments[i].PileSection = segments[i].Section;
            return body;
        }

        /// <summary>鋼管杭 D = 800、1 区間 20 m (薄い鋼管 t = 9)。</summary>
        private static PileBodyInput Body() => Make((20.0, Pipe(9)));

        private static double Total(PileBodyInput b) => b.PileBodySegments.Sum(s => s.SegmentLength);

        [TestMethod]
        public void SettingL1AndL2BuildsTheTopSegments_AndKeepsTheTotalLength()
        {
            var body = Body();
            // 充填部の鋼管は 1 区間目を写す。厚い鋼管にしてから l2 を入れる流れを模す
            string? problem = body.SetHeadZone(1200, 800);
            Assert.IsNull(problem, problem);

            var segs = body.PileBodySegments;
            Assert.AreEqual(3, segs.Count, "上 2 区間 + 下の区間になっていない");
            Assert.AreEqual(20.0, Total(body), 1e-9, "杭の全長が変わった");
            Assert.AreEqual((HeadZoneRole.Fill, 1.2, PileTypeNames.CftSection), (segs[0].HeadZoneRole, segs[0].SegmentLength, segs[0].PileSection.PileSectionType));
            Assert.AreEqual((HeadZoneRole.ThickPart, 0.8, PileTypeNames.SteelPipeSection), (segs[1].HeadZoneRole, segs[1].SegmentLength, segs[1].PileSection.PileSectionType));
            Assert.AreEqual(HeadZoneRole.None, segs[2].HeadZoneRole);
            Assert.AreEqual(18.0, segs[2].SegmentLength, 1e-9, "下の区間で差を吸収していない");
            Assert.AreEqual((1200.0, 800.0), (body.HeadFillLength, body.HeadThickPartLength));

            // 杭断面へ配る値 (合成剛性 EIeq と節点に使う)
            Assert.AreEqual((1200.0, 800.0), (segs[0].PileSection.CftFillLength, segs[0].PileSection.CftThickPartLength));

            // 充填部の鋼管を変えると l2 の区間が追随する
            segs[0].PileSection.PipeTs = 19;
            body.SyncHeadZoneSections();
            Assert.AreEqual(19.0, body.PileBodySegments[1].PileSection.PipeTs, "l2 の区間の鋼管が充填部に追随していない");
            Assert.AreEqual(9.0, body.PileBodySegments[2].PileSection.PipeTs, "下の区間の鋼管まで変えた");

            // 変え直しても全長は保つ。l2 = 0 で厚い区間は消える
            Assert.IsNull(body.SetHeadZone(1600, 0));
            Assert.AreEqual(2, body.PileBodySegments.Count);
            Assert.AreEqual(20.0, Total(body), 1e-9);
            Assert.AreEqual(18.4, body.PileBodySegments[1].SegmentLength, 1e-9);

            // l1 = 0 は充填部なし。区間は元の 1 つに戻る
            Assert.IsNull(body.SetHeadZone(0, 0));
            Assert.AreEqual(1, body.PileBodySegments.Count);
            Assert.AreEqual(20.0, body.PileBodySegments[0].SegmentLength, 1e-9);
            Assert.AreEqual(HeadZoneRole.None, body.PileBodySegments[0].HeadZoneRole);
        }

        [TestMethod]
        public void InvalidValuesAreRejected_AndNothingChanges()
        {
            var body = Body();
            Assert.IsNull(body.SetHeadZone(800, 400));
            var before = body.PileBodySegments.Select(s => (s.HeadZoneRole, s.SegmentLength)).ToList();

            // 4D = 3,200 mm を超える
            StringAssert.Contains(body.SetHeadZone(2000, 1300), "4D");
            // 充填部が無いのに l2
            StringAssert.Contains(body.SetHeadZone(0, 400), "l2 は 0");
            // 負
            Assert.IsNotNull(body.SetHeadZone(-1, 0));
            CollectionAssert.AreEqual(before, body.PileBodySegments.Select(s => (s.HeadZoneRole, s.SegmentLength)).ToList(), "受け付けない値で区間を変えた");

            // 下に 1 m 以上残らない (全長 3 m の杭で l1 + l2 = 2.5 m)
            var shortBody = Make((3.0, Pipe(9)));
            StringAssert.Contains(shortBody.SetHeadZone(2000, 500), "1,000 mm 以上");

            // 鋼管杭以外は入れられない
            var rc = new PileBodyInput { PileBodyType = PileTypeNames.InsituRc };
            Assert.IsNotNull(rc.ValidateHeadZone(800, 0));
        }

        /// <summary>
        /// この仕組みより前のファイル (印が無い) は、1 区間目が充填鋼管部なら充填部、そのすぐ下が同じ鋼管の鋼管部なら
        /// l2 の区間とする。設計例集3.5 の形 (充填 0.8 m = D、下は肉厚の違う鋼管) は l1 = 800・l2 = 0 で、
        /// 以前の計算 (l1 未入力は D、l2 = 0) と同じ値になる。
        /// </summary>
        [TestMethod]
        public void OldFilesGetTheirRolesFromTheSegments()
        {
            var example = Make((0.8, Pipe(17, PileTypeNames.CftSection)), (21.7, Pipe(11)));
            example.EnsureHeadZoneRoles();
            Assert.AreEqual((HeadZoneRole.Fill, HeadZoneRole.None), (example.PileBodySegments[0].HeadZoneRole, example.PileBodySegments[1].HeadZoneRole));
            Assert.AreEqual((800.0, 0.0), (example.HeadFillLength, example.HeadThickPartLength));
            Assert.AreEqual(800.0, example.PileBodySegments[0].PileSection.CftFillLength, "杭断面へ l1 を配っていない");

            var withThick = Make((1.0, Pipe(17, PileTypeNames.CftSection)), (0.6, Pipe(17)), (20.0, Pipe(11)));
            withThick.EnsureHeadZoneRoles();
            Assert.AreEqual((1000.0, 600.0), (withThick.HeadFillLength, withThick.HeadThickPartLength), "同じ鋼管の鋼管部を l2 にしていない");

            // 充填鋼管部が 1 区間目に無ければ印を付けない
            var plain = Body();
            plain.EnsureHeadZoneRoles();
            Assert.AreEqual(0.0, plain.HeadFillLength);
        }

        /// <summary>杭体ウィンドウ: l1・l2 の欄と図があり、杭頭の区間の長さは表で編集しない・削除しない。杭断面ウィンドウには入力欄が無い。</summary>
        [TestMethod]
        public void TheInputsLiveInThePileBodyWindow()
        {
            string body = TestSource.Read("Graphics_r1", "Views", "PileBodyWindow.xaml");
            StringAssert.Contains(body, "Binding HeadFillLength, UpdateSourceTrigger=LostFocus, ValidatesOnExceptions=True");
            StringAssert.Contains(body, "Binding HeadThickPartLength, UpdateSourceTrigger=LostFocus, ValidatesOnExceptions=True");
            StringAssert.Contains(body, "<ctrl:SteelPipeHeadZoneDiagram");

            string code = TestSource.Read("Graphics_r1", "Views", "PileBodyWindow.xaml.cs");
            StringAssert.Contains(code, "PileBodySegment { HeadZoneRole: not HeadZoneRole.None }", "杭頭の区間の長さを表で編集できる");
            string vm = TestSource.Read("Graphics_r1", "ViewModels", "PileBodyViewModel.cs");
            StringAssert.Contains(vm, "selectedItem.HeadZoneRole != HeadZoneRole.None", "杭頭の区間を削除できる");

            string section = TestSource.Read("Graphics_r1", "Views", "PileSectionWindow.xaml");
            Assert.IsFalse(section.Contains("<TextBox Text=\"{Binding PileSection.CftFillLength"), "杭断面ウィンドウに l1 の入力欄が残っている");
            Assert.IsFalse(section.Contains("<TextBox Text=\"{Binding PileSection.CftThickPartLength"), "杭断面ウィンドウに l2 の入力欄が残っている");
        }
    }
}
