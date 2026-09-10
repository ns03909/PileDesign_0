using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 保存中に表を編集しても壊れないこと。
    ///
    /// 保存は別スレッドで直列化する一方、利用者は表を編集し続けられる。生きたモデルを
    /// そのまま辿ると、行の足し引きで列挙が壊れて <see cref="InvalidOperationException"/>
    /// になる。守り方は「編集できる入れ物だけを新しくし、要素は同じ実体を指したまま渡す」
    /// (<c>InputModel.SnapshotForSaving</c>)。要素が同じなので保存ファイルは 1 バイトも
    /// 変わらない。
    ///
    /// 実機で競合を狙うのは現実的でない (手動保存は 116ms で終わる)。守り方が
    /// 並行性ではなく<b>入れ物の差し替え</b>なので、「写したあとに元を編集して写した器が
    /// 動かないこと」を確かめれば足りる。ここではそれを<b>全数</b>で見る。
    ///
    /// 以前は次の 2 つが抜けていた。
    /// <list type="number">
    /// <item>守っている入れ物のうち 1 本しか編集して確かめていなかった
    ///   (6 本あるので、7 本目を足して写し忘れても通る)</item>
    /// <item>自動保存が写しを<b>バックグラウンドスレッドで</b>取り、しかもその手前で
    ///   生きたモデルを 6 秒かけて全走査していた (NaN 検査)。守る仕組みを入れたのに、
    ///   その手前でむき出しにしていた</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class SaveDuringEditTests
    {
        private static JsonSerializerOptions SaveOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        private static readonly Regex _idLineRegex = new("\"Id\": \\d+", RegexOptions.Compiled);
        private static string NormalizeRuntimeIds(string json) => _idLineRegex.Replace(json, "\"Id\": *");

        /// <summary>
        /// 画面から編集できる <see cref="InputModel"/> 直下の入れ物。
        /// ここに載っているものは、写した器で<b>別インスタンス</b>になっていなければならない。
        /// </summary>
        private static readonly string[] EditableCollections =
        [
            nameof(InputModel.PileLayoutItems),   // メイン画面 杭配置
            nameof(InputModel.GridXItems),        // メイン画面 通り芯 X
            nameof(InputModel.GridYItems),        // メイン画面 通り芯 Y
            nameof(InputModel.InputNodes),        // メイン画面 一般節点
            nameof(InputModel.GroundsInput),      // 地盤の一覧
            nameof(InputModel.PileBodies),        // 杭体の一覧
        ];

        /// <summary>
        /// 例題を読み、<b>守る対象の表すべてに行があること</b>を保証して返す。
        ///
        /// 空 (または null) の表は「元と写しがどちらも null」で一致してしまい、
        /// 写し忘れを見逃す。実際に例題 9 は一般節点を持たないため、
        /// <c>Shallow(_inputNodes)</c> を消しても最初の版のテストは合格していた。
        /// 行を用意したうえで、<see cref="AllTablesHaveRows"/> が空振りも検出する。
        /// </summary>
        private static InputModel Example()
        {
            var (input, _) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            Assert.IsNotNull(input, "例題を読み込めない");

            // 例題が持たない表は、ここで 1 行入れてから写す
            (input!.GridXItems ??= []).Add(new GridDataItem());
            (input.GridYItems ??= []).Add(new GridDataItem());
            (input.InputNodes ??= []).Add(new InputNode());

            return input;
        }

        /// <summary>
        /// 守る対象の表すべてに行があること。空だと写し忘れを見逃すので、
        /// <b>網が空振りしていないこと自体</b>を確かめる。
        /// </summary>
        [TestMethod]
        public void AllTablesHaveRows()
        {
            var input = Example();

            var empty = EditableCollections
                .Where(n => Count(input, n) <= 0)
                .ToList();

            Assert.AreEqual(0, empty.Count,
                "行の無い表があります。元と写しがどちらも空で一致してしまい、"
                + "写し忘れを見逃します: " + string.Join(", ", empty));
        }

        /// <summary>
        /// 編集できる入れ物が<b>全数</b>、写した器では別インスタンスになっていること。
        ///
        /// 1 本でも共有していると、その表を保存中に編集したときだけ列挙が壊れる。
        /// 入れ物ごとに確かめないと、他の 5 本が守られていることで隠れる。
        /// </summary>
        [TestMethod]
        public void EveryEditableCollection_IsSwappedInTheSnapshot()
        {
            var input = Example();
            var snapshot = input.SnapshotForSaving();

            var shared = new List<string>();
            foreach (var name in EditableCollections)
            {
                var pi = typeof(InputModel).GetProperty(name);
                Assert.IsNotNull(pi, $"{name} が見つかりません (名前が変わった?)");

                var live = pi!.GetValue(input);
                var copied = pi.GetValue(snapshot);

                // null をスキップしないこと。スキップすると、例題が持たない表の
                // 写し忘れを見逃す (AllTablesHaveRows が行の存在を保証している)
                Assert.IsNotNull(live, $"{name} が空です。写し忘れを見逃します");

                if (ReferenceEquals(live, copied)) shared.Add(name);
            }

            TestSource.AssertScanned(EditableCollections.Length, 6, "保存で守る入れ物");
            Assert.AreEqual(0, shared.Count,
                "写した器が元と同じ入れ物を指しています。保存中にこの表の行を足し引きすると"
                + "列挙が壊れます: " + string.Join(", ", shared));
        }

        /// <summary>
        /// 写した器を辿っているあいだに<b>どの表を編集しても</b>、写した器が動かないこと。
        /// 実際に行を足して、写した器の件数と直列化の両方を確かめる。
        /// </summary>
        [TestMethod]
        public void EditingAnyTableDuringSerialization_DoesNotBreakTheSnapshot()
        {
            var input = Example();
            var snapshot = input.SnapshotForSaving();

            var counts = EditableCollections
                .ToDictionary(n => n, n => Count(snapshot, n));

            // 直列化の最中に利用者が各表を編集した状況を作る。
            //
            // 行がある表は<b>削除</b>で確かめる。杭配置に行を足すと
            // CollectionChanged が画面のビューモデルを要求するので、画面の無い
            // ここでは足せない (元のテストが通り芯 1 本しか見ていなかった理由)。
            // 削除は購読を外すだけなので画面が要らず、しかも列挙を壊すという点では
            // 追加と同じ。行が無い表だけ追加で確かめる。
            Edit(input.PileLayoutItems, () => new PileLayoutDataItem());
            Edit(input.GridXItems, () => new GridDataItem());
            Edit(input.GridYItems, () => new GridDataItem());
            Edit(input.InputNodes, () => new InputNode());
            Edit(input.GroundsInput, () => new GroundInput());
            Edit(input.PileBodies, () => new PileBodyInput());

            var moved = EditableCollections
                .Where(n => Count(snapshot, n) != counts[n])
                .ToList();

            Assert.AreEqual(0, moved.Count,
                "元を編集すると写した器まで動きます。列挙が壊れる余地が残っています: "
                + string.Join(", ", moved));

            // 写した器はそのまま書き出せること (例外が出ないこと)
            string json = JsonSerializer.Serialize(snapshot, SaveOptions());
            Assert.IsTrue(json.Length > 0, "写した器を直列化できない");
        }

        private static int Count(InputModel model, string name)
            => typeof(InputModel).GetProperty(name)!.GetValue(model) is ICollection c ? c.Count : -1;

        /// <summary>行があれば末尾を削り、無ければ 1 行足す。どちらも列挙を壊す操作。</summary>
        private static void Edit<T>(ObservableCollection<T>? c, Func<T> make)
        {
            if (c == null) return;
            if (c.Count > 0) c.RemoveAt(c.Count - 1);
            else c.Add(make());
        }

        /// <summary>
        /// 写した器をもう一度写しても、書き出す結果が変わらないこと。
        ///
        /// 自動保存は画面のスレッドで写してからバックグラウンドへ渡すので、
        /// <c>SaveProjectData</c> の中の写しは<b>二重になる</b>。要素は同じ実体を
        /// 指したままなので参照の畳まれ方 ($id/$ref) は変わらないはずだが、
        /// ここが崩れると保存ファイルの中身が黙って変わる。
        /// </summary>
        [TestMethod]
        public void SnapshottingTwice_SerializesIdentically()
        {
            var input = Example();

            string once = NormalizeRuntimeIds(
                JsonSerializer.Serialize(input.SnapshotForSaving(), SaveOptions()));
            string twice = NormalizeRuntimeIds(
                JsonSerializer.Serialize(input.SnapshotForSaving().SnapshotForSaving(), SaveOptions()));

            Assert.AreEqual(once, twice,
                "写しを二重に取ると書き出す結果が変わります。"
                + "自動保存は画面のスレッドで写してから渡すので、二重になっても同じでなければなりません");
        }

        /// <summary>
        /// NaN 検査が<b>写した器</b>にかかること。
        ///
        /// 検査 (<c>FindNonFiniteDouble</c>) は反射で IEnumerable を全部辿り、
        /// 6 秒以上かかる。生きたモデルにかけると、そのあいだずっと生きたコレクションを
        /// 列挙することになり、自動保存はバックグラウンドで走るので行の足し引きで
        /// 列挙が壊れる。<b>写しより手前に検査を置くと、守る仕組みが無意味になる。</b>
        /// </summary>
        [TestMethod]
        public void TheNaNCheck_RunsOnTheSnapshotNotTheLiveModel()
        {
            string src = TestSource.Read("Graphics_r1", "Services", "FileOperationService.cs");

            int snapshotAt = src.IndexOf("var inputToSave = SnapshotForSaving(", StringComparison.Ordinal);
            int validateAt = src.IndexOf("ValidateFinite(", StringComparison.Ordinal);

            Assert.IsTrue(snapshotAt > 0, "写しを取る処理が見つかりません");
            Assert.IsTrue(validateAt > 0, "NaN 検査が見つかりません");
            Assert.IsTrue(snapshotAt < validateAt,
                "NaN 検査が写しより手前にあります。6 秒のあいだ生きたコレクションを"
                + "列挙するので、保存中の編集で列挙が壊れます");

            StringAssert.Contains(src, "ValidateFinite(inputToSave",
                "NaN 検査が生きたモデルにかかっています。書き出すもの (写した器) を検査すること");
        }

        /// <summary>
        /// 自動保存が写しを<b>画面のスレッドで</b>取ること。
        ///
        /// 写す処理そのものが元のコレクションを列挙するので、<c>Task.Run</c> の中で
        /// 写すと、写している最中の編集で列挙が壊れる。守る仕組みの意味が無くなる。
        /// </summary>
        [TestMethod]
        public void AutoSave_TakesTheSnapshotOnTheUiThread()
        {
            string src = TestSource.Read("Graphics_r1", "Services", "AutoSaveService.cs");

            int prepareAt = src.IndexOf("var p = PrepareState();", StringComparison.Ordinal);
            int taskRunAt = src.IndexOf("await Task.Run(", StringComparison.Ordinal);

            Assert.IsTrue(prepareAt > 0,
                "自動保存が写しを取る処理が見つかりません (PrepareState)");
            Assert.IsTrue(taskRunAt > 0, "バックグラウンドへ逃がす処理が見つかりません");
            Assert.IsTrue(prepareAt < taskRunAt,
                "自動保存が Task.Run の中で写しています。"
                + "写す処理自体が元のコレクションを列挙するので、そのあいだの編集で壊れます");

            // 写すかどうかの判断は共通の場所に任せること。
            // 自前で InputModel.SnapshotForSaving() を呼ぶと、解析結果と同じ実体を
            // 指している場面でも写してしまい、保存ファイルの $ref の畳まれ方が変わる
            StringAssert.Contains(src, "FileOperationService.SnapshotForSaving(",
                "自動保存が写しの判断を自前で持っています。共通の判断を通すこと");
        }

        /// <summary>
        /// モーダルのウィンドウが開いているあいだは自動保存を見送ること。
        ///
        /// 写しが守るのは <see cref="InputModel"/> 直下の入れ物だけで、土層表や区間表の
        /// ような<b>入れ子の表は生きたまま</b>辿られる (要素を複製すると実体が変わり、
        /// 保存ファイルの $ref の畳まれ方が変わるので守れない)。入れ子の表を編集できるのは
        /// モーダルのウィンドウだけなので、そのあいだ見送れば露出が無くなる。
        ///
        /// <c>DispatcherTimer.Tick</c> は <c>ShowDialog</c> の入れ子ディスパッチャでも
        /// 発火するので、「モーダルだからぶつからない」は自動保存には効かない。
        /// </summary>
        [TestMethod]
        public void AutoSave_IsSkippedWhileAModalWindowIsOpen()
        {
            string src = TestSource.Read("Graphics_r1", "Services", "AutoSaveService.cs");

            StringAssert.Contains(src, "ComponentDispatcher.IsThreadModal",
                "モーダル中に自動保存を見送る判定がありません。"
                + "入れ子の表 (土層・区間) を編集中に走ると列挙が壊れます");

            int modalAt = src.IndexOf("ComponentDispatcher.IsThreadModal", StringComparison.Ordinal);
            int prepareAt = src.IndexOf("var p = PrepareState();", StringComparison.Ordinal);
            Assert.IsTrue(modalAt > 0 && modalAt < prepareAt,
                "モーダルの判定が写しより後にあります。写す前に見送ること");
        }
    }
}
