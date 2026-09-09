using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 材料のモデル化オプションが、どれも <c>ConcreteModelOptions.Signature()</c> に
    /// 現れること。
    ///
    /// M-φ はプロセス全体で共有する静的キャッシュに溜まり、その鍵に署名が入ります。
    /// 署名に入っていないオプションを変えると<b>鍵が変わらないまま曲線の中身だけが
    /// 変わるべき</b>状況になり、キャッシュが古いオプションで計算した曲線を返します。
    /// 例外にならず、解析はそのまま完走します。
    ///
    /// オプションは 16 個あり、追加のたびに署名へ足すのを覚えていなければなりません。
    /// 個別のテスト (<c>FiberOption_IncludedInSignature</c> など) はありましたが、
    /// <b>足し忘れを機械的に捕まえる網はありませんでした。</b>
    ///
    /// 同じ系統の不具合が <c>CaptainPile</c> にもありました。あちらは鍵 (ε₀, φ) が
    /// パイルキャップの Fc を含まないのにキャッシュを捨てておらず、Fc を変えても
    /// 安全限界 N-M 曲線が変わりませんでした。
    /// </summary>
    [TestClass]
    public class ConcreteOptionSignatureTests
    {
        [TestMethod]
        public void EveryOption_ShowsUpInTheSignature()
        {
            var missing = new List<string>();
            int checkedOptions = 0;

            var options = TestStateScope.SettableStaticOptions().ToList();

            foreach (var p in options)
            {
                // 既定の状態と、真偽値を全部 ON にした状態の 2 つで試す。
                // 告示 1113 の区分のように「別のオプションが ON のときだけ効く」
                // ものがあり、効かない状態では署名に出ないのが正しい。
                // 「効く状態が 1 つでもあれば署名に現れる」ことを見る。
                bool showedUp = false;

                foreach (bool allOn in new[] { false, true })
                {
                    using var scope = TestStateScope.Enter();
                    if (allOn)
                    {
                        foreach (var q in options.Where(q => q.PropertyType == typeof(bool)))
                            q.SetValue(null, true);
                    }

                    object? original = p.GetValue(null);
                    string before = ConcreteModelOptions.Signature();

                    foreach (object candidate in CandidatesFor(p.PropertyType, original))
                    {
                        p.SetValue(null, candidate);
                        checkedOptions++;
                        // 「まとめ役」が他も書き換えて元に戻ることがあるので、
                        // 実際に値が変わったときだけ見る
                        if (!Equals(p.GetValue(null), original)
                            && ConcreteModelOptions.Signature() != before)
                        {
                            showedUp = true;
                        }
                    }
                }

                if (!showedUp) missing.Add(p.Name);
            }

            TestSource.AssertScanned(checkedOptions, 16, "材料のモデル化オプション");

            Assert.AreEqual(0, missing.Count,
                "値を変えても Signature() が変わらないオプションがあります。"
                + "M-φ の静的キャッシュが古いオプションで計算した曲線を返します。"
                + "ConcreteModelOptions.Signature() に足してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", missing));
        }

        /// <summary>そのオプションで「今と違う値」の候補。</summary>
        private static IEnumerable<object> CandidatesFor(Type t, object? current)
        {
            if (t == typeof(bool)) return [!(bool)(current ?? false)];
            if (t == typeof(int)) return [(int)(current ?? 0) + 1];
            if (t == typeof(double))
            {
                // β は 0.01〜1.00 に丸められるので、その範囲で別の値を選ぶ
                double now = (double)(current ?? 0.0);
                return [Math.Abs(now - 0.5) < 1e-9 ? 0.75 : 0.5];
            }
            return [];
        }
    }
}
