using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System.Collections.Generic;

namespace TestProject1
{
    /// <summary>
    /// 入力の隣に出ている計算値が、入力を変えたときに一緒に更新されること。
    ///
    /// 値そのものは毎回計算し直すので<b>常に正しい</b>。ただし変更通知が飛ばないと、
    /// 画面だけが古い値のまま残ります。<b>計算は合っているのに表示だけ古い</b>ので、
    /// 数字を見比べても気づけません。
    ///
    /// 実際に 3 件ありました。
    /// <list type="bullet">
    /// <item>杭頭部の Ec (Fc を 24 から変えても 22,669 のまま)</item>
    /// <item>根入れの DX / DY (X1 を編集しても隣の列が動かない)</item>
    /// <item>荷重ケースの ΣH (上部構造慣性力を変えても合計が動かない)</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class DerivedValueNotificationTests
    {
        private static List<string?> Watch(System.ComponentModel.INotifyPropertyChanged m)
        {
            var seen = new List<string?>();
            m.PropertyChanged += (_, e) => seen.Add(e.PropertyName);
            return seen;
        }

        [TestMethod]
        public void Embedment_DXFollowsX1AndX2()
        {
            var item = new EmbedmentDataItem();
            var seen = Watch(item);

            item.X1 = 1.0;
            CollectionAssert.Contains(seen, nameof(EmbedmentDataItem.DX),
                "X1 の隣に DX 列が出ています。知らせないと古い幅のまま残ります");

            seen.Clear();
            item.X2 = 5.0;
            CollectionAssert.Contains(seen, nameof(EmbedmentDataItem.DX));
            Assert.AreEqual(4.0, item.DX, 1e-12);
        }

        [TestMethod]
        public void Embedment_DYFollowsY1AndY2()
        {
            var item = new EmbedmentDataItem();
            var seen = Watch(item);

            item.Y1 = 2.0;
            CollectionAssert.Contains(seen, nameof(EmbedmentDataItem.DY));

            seen.Clear();
            item.Y2 = 7.0;
            CollectionAssert.Contains(seen, nameof(EmbedmentDataItem.DY));
            Assert.AreEqual(5.0, item.DY, 1e-12);
        }

        [TestMethod]
        public void LoadCase_SumHFollowsTheTwoForces()
        {
            var lc = new LoadCase();
            var seen = Watch(lc);

            lc.UpperMassForce = 100.0;
            CollectionAssert.Contains(seen, nameof(LoadCase.SumH),
                "ΣH = 上部構造慣性力 + 基礎構造慣性力。"
                + "水平解析ウィンドウの ΣH 列が古いまま残ります");
            CollectionAssert.Contains(seen, nameof(LoadCase.SumHOverSumVText),
                "ΣH/ΣV も一緒に動きます");

            seen.Clear();
            lc.FoundationMassForce = 50.0;
            CollectionAssert.Contains(seen, nameof(LoadCase.SumH));
            Assert.AreEqual(150.0, lc.SumH, 1e-12);
        }

        [TestMethod]
        public void PileTop_EcFollowsFcAndGamma()
        {
            var pileTop = new PileTop();
            var seen = Watch(pileTop);

            pileTop.PileCapFc = 36.0;
            CollectionAssert.Contains(seen, nameof(PileTop.PileCapEc),
                "Fc を変えたのに Ec 欄が既定 (24 のときの 22,669) のまま残ります");

            seen.Clear();
            pileTop.PileCapGamma = 24.5;
            CollectionAssert.Contains(seen, nameof(PileTop.PileCapEc),
                "Ec は γ² に比例します");
        }
    }
}
