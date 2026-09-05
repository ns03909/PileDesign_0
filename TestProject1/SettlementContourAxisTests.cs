using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;

namespace TestProject1
{
    /// <summary>
    /// コンタの格子 (X・Y 座標) は<b>表示中のケースの沈下値から引く</b>こと。
    ///
    /// 以前は入力モデルの <c>PileGroupSettlement.SettlementGridX/Y</c> を読んでいた。
    /// あれは解析が<b>現在の入力</b>に書くもので、解析時のスナップショットには移らない。
    /// 表示・計算書はスナップショットを読むため、
    /// 「水平解析 → 入力を編集 → 沈下だけ再実行」の順で
    /// <b>軸が空のまま → コンタが出ない</b>という状態になっていた
    /// (描画側は軸が空だと既存のコンタを消して戻る)。
    ///
    /// 軸は沈下値そのものから決まるので、持たずに引き出す。
    /// </summary>
    [TestClass]
    public class SettlementContourAxisTests
    {
        private static GroupSettlementCaseRecord MakeRecord()
        {
            var rec = new GroupSettlementCaseRecord { LoadCaseName = "VL", LoadingType = "任意矩形" };
            // 3 × 2 の格子 (順序はわざとばらばらに入れる)
            foreach (var (x, y, s) in new[]
                     {
                         (2.0, 1.0, 3.0), (0.0, 0.0, 1.0), (1.0, 1.0, 2.0),
                         (0.0, 1.0, 1.5), (2.0, 0.0, 2.5), (1.0, 0.0, 1.2),
                     })
            {
                rec.SettlementGridData.Add(new SettlementGridDataItem { X = x, Y = y, Settlement = s });
            }
            return rec;
        }

        [TestMethod]
        public void TheAxesComeFromTheRecordItself()
        {
            var result = new GroupSettlementResult();
            result.CaseRecords.Add(MakeRecord());
            result.ActiveCaseIndex = 0;

            CollectionAssert.AreEqual(new List<double> { 0.0, 1.0, 2.0 }, result.ActiveGridX,
                "X 軸が沈下値から作られていません");
            CollectionAssert.AreEqual(new List<double> { 0.0, 1.0 }, result.ActiveGridY,
                "Y 軸が沈下値から作られていません");
        }

        /// <summary>
        /// 入力側の <c>SettlementGridX/Y</c> が空でも、コンタの軸が引けること。
        /// スナップショット (解析時の入力) はまさにこの状態になる。
        /// </summary>
        [TestMethod]
        public void EmptyInputSideGrid_DoesNotHideTheContour()
        {
            var pgs = new PileGroupSettlement
            {
                SettlementGridX = [],   // スナップショットに残っている古い (空の) 軸
                SettlementGridY = [],
            };
            pgs.CaseRecords.Add(MakeRecord());
            pgs.ActiveCaseIndex = 0;

            Assert.AreEqual(3, pgs.ActiveGridX.Count,
                "入力側の軸が空だとコンタが描けない状態に戻っています");
            Assert.AreEqual(2, pgs.ActiveGridY.Count);
        }

        /// <summary>
        /// ケースを切り替えたら軸も切り替わること。格子の大きさが違うケースが混ざっても
        /// 点が落ちない (軸と沈下値が必ず同じケースのものになる)。
        /// </summary>
        [TestMethod]
        public void SwitchingTheCase_SwitchesTheAxes()
        {
            var result = new GroupSettlementResult();
            result.CaseRecords.Add(MakeRecord());

            var coarse = new GroupSettlementCaseRecord { LoadCaseName = "L2", LoadingType = "任意矩形" };
            coarse.SettlementGridData.Add(new SettlementGridDataItem { X = 0.0, Y = 0.0, Settlement = 5.0 });
            coarse.SettlementGridData.Add(new SettlementGridDataItem { X = 5.0, Y = 0.0, Settlement = 6.0 });
            result.CaseRecords.Add(coarse);

            result.ActiveCaseIndex = 0;
            Assert.AreEqual(3, result.ActiveGridX.Count);

            result.ActiveCaseIndex = 1;
            CollectionAssert.AreEqual(new List<double> { 0.0, 5.0 }, result.ActiveGridX,
                "ケースを切り替えても前のケースの軸が残っています");

            // 軸の要素数 × = 沈下値の点がすべて格子に載ること
            var xs = result.ActiveGridX;
            var ys = result.ActiveGridY;
            foreach (var item in result.ActiveSettlementGridData)
            {
                Assert.IsTrue(xs.IndexOf(item.X) >= 0 && ys.IndexOf(item.Y) >= 0,
                    $"格子に載らない点があります ({item.X}, {item.Y})");
            }
        }

        /// <summary>沈下値を入れ替えたら軸も作り直されること (キャッシュの取り残し防止)。</summary>
        [TestMethod]
        public void ReplacingTheData_RebuildsTheAxes()
        {
            var rec = MakeRecord();
            Assert.AreEqual(3, rec.GridX.Count);

            rec.SettlementGridData =
            [
                new SettlementGridDataItem { X = 10.0, Y = 0.0, Settlement = 1.0 },
                new SettlementGridDataItem { X = 20.0, Y = 0.0, Settlement = 2.0 },
            ];

            CollectionAssert.AreEqual(new List<double> { 10.0, 20.0 }, rec.GridX,
                "沈下値を入れ替えたのに前の軸が返っています");
        }
    }
}
