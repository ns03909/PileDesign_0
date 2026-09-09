using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 土質杭 (SoilPile) の引き当てキャッシュが、入力モデルごとに独立していること。
    ///
    /// 以前は <c>static readonly</c> な辞書 1 つを<b>すべての入力モデルが共有</b>して
    /// いました (IDE の「フィールドの導入」が既定名 <c>value</c> のまま残った形)。
    /// 有効フラグはインスタンスごとなので、次の順で別のモデルの値が返ります。
    ///
    /// <list type="number">
    /// <item>A が引く → 共有辞書を Clear して A で埋め、A のフラグを立てる</item>
    /// <item>B が引く → 同じ辞書を Clear して B で埋め、B のフラグを立てる</item>
    /// <item>A が引く → A のフラグは立ったままなので再構築せず、<b>B の中身を返す</b></item>
    /// </list>
    ///
    /// 解析結果のスナップショットと編集中の入力は同時に生きているので、
    /// <b>解析したあとに入力を変えると</b>起きます。値は多くの場合たまたま
    /// 一致するので、気づきにくい形です。
    ///
    /// ロックもインスタンスごとで、共有辞書は守れていませんでした
    /// (「複数スレッドからの並行呼出で Dictionary が破損するのを防ぐ」と
    /// 書かれていたのに、防げていませんでした)。
    /// </summary>
    [TestClass]
    public class SoilPileCacheIsolationTests
    {
        private static InputModel WithSoilPile(double toeDia)
        {
            var model = new InputModel();
            var sp = new SoilPile
            {
                GroundNo = 1,
                PileBodyNo = 1,
                Z = 0.0,
                Dp = toeDia,           // どちらのモデルか見分けるための目印
            };
            model.ElementDivision ??= new ElementDivision();
            model.ElementDivision.SetSoilPilesSilently(new ObservableCollection<SoilPile> { sp });
            return model;
        }

        [TestMethod]
        public void TwoModels_DoNotShareTheLookupTable()
        {
            var a = WithSoilPile(1000.0);
            var b = WithSoilPile(2000.0);

            // A → B → A の順に引く。3 回目で A 自身の値が返ること
            Assert.AreEqual(1000.0, a.LookupSoilPile(1, 1, 0.0)?.Dp,
                "1 回目から A の値が返っていません");
            Assert.AreEqual(2000.0, b.LookupSoilPile(1, 1, 0.0)?.Dp,
                "B の値が返っていません");
            Assert.AreEqual(1000.0, a.LookupSoilPile(1, 1, 0.0)?.Dp,
                "B が引いたあとに A が B の土質杭を返しています。"
                + "引き当ての辞書を入力モデルどうしで共有しています");
        }

        /// <summary>
        /// 引き当ての辞書が <c>static</c> でないこと。
        /// 上のテストは呼ぶ順に依存するので、作りのほうも直接見ておく。
        /// </summary>
        [TestMethod]
        public void TheLookupTable_IsNotShared()
        {
            var shared = typeof(InputModel)
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => typeof(System.Collections.IDictionary).IsAssignableFrom(f.FieldType))
                .Select(f => f.Name)
                .ToList();

            Assert.AreEqual(0, shared.Count,
                "InputModel に static な辞書があります。入力モデルごとの状態を"
                + "プロセスで 1 つしか持てず、別のモデルの値が混ざります: "
                + string.Join(", ", shared));
        }
    }
}
