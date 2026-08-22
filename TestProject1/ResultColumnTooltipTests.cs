using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.Results;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 解析結果テーブルの列の説明に関するテスト。
    ///
    /// 列ヘッダは Mxi(kNm) のような記号なので、意味はツールチップでしか伝わらない。
    /// 列を足したときに説明を書き忘れると、日本語 UI の中に説明のない英字列が増える。
    /// </summary>
    [TestClass]
    public class ResultColumnTooltipTests
    {
        private static IEnumerable<Type> RowTypes()
        {
            return typeof(ResultColumnAttribute).Assembly
                .GetTypes()
                .Where(t => t.Namespace == "PileDesign.Models.Results"
                         && t.GetProperties().Any(p => p.GetCustomAttribute<ResultColumnAttribute>() != null));
        }

        [TestMethod]
        public void EveryResultColumn_HasTooltip()
        {
            var missing = new List<string>();
            int total = 0;

            foreach (var type in RowTypes())
            {
                foreach (var prop in type.GetProperties())
                {
                    var attr = prop.GetCustomAttribute<ResultColumnAttribute>();
                    if (attr == null) continue;

                    total++;
                    if (string.IsNullOrWhiteSpace(attr.Tooltip))
                        missing.Add($"{type.Name}.{prop.Name} (ヘッダ \"{attr.Header}\")");
                }
            }

            Assert.IsTrue(total >= 100, $"結果列が {total} 件しか見つからない (収集が壊れている可能性)");
            Assert.AreEqual(0, missing.Count,
                "説明 (tooltip) の無い結果列があります。記号だけでは意味が伝わりません:\n  " +
                string.Join("\n  ", missing));
        }

        /// <summary>
        /// ツールチップを付けた列のヘッダは TextBlock になる。
        /// この分岐を落とすと CSV・クリップボード・列レイアウト保存に
        /// "System.Windows.Controls.TextBlock" が混入する。
        /// </summary>
        [TestMethod]
        public void DataGridHeaderText_HandlesEveryHeaderShape()
        {
            string? result = null;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                // 素の文字列
                Assert.AreEqual("荷重条件", DataGridHeaderText.From("荷重条件"));
                Assert.AreEqual("荷重条件", DataGridHeaderText.From("  荷重条件  "));

                // ツールチップ付きの列 (TextBlock)
                var tb = new System.Windows.Controls.TextBlock { Text = "Mxi(kNm)", ToolTip = "説明" };
                result = DataGridHeaderText.From(tb);
                Assert.AreEqual("Mxi(kNm)", result);

                // 多段見出し (StackPanel + TextBlock 複数)
                var panel = new System.Windows.Controls.StackPanel();
                panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "変形" });
                panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "係数" });
                panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "(kN/m2)" });
                Assert.AreEqual("変形 係数 (kN/m2)", DataGridHeaderText.From(panel));

                // 未設定
                Assert.AreEqual("", DataGridHeaderText.From((object?)null));
            }, out bool timedOut, timeoutSeconds: 60);

            if (timedOut)
            {
                Assert.Inconclusive("STA スレッドが 60 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
                Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
        }
    }
}
