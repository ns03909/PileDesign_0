using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 保存グラフの全スカラーを既定値から動かす道具。
    ///
    /// <b>なぜ要るのか。</b> 不変条件の網 (保存の往復・キーの並び順・DeepCopy) は
    /// どれも「同じ入力から 2 通りに作ったものが一致すること」を見る。この判定は
    /// 値が物理的に妥当かどうかに依存しないので、<b>値をでたらめに動かしてよい</b>。
    /// 動かす目的はただ 1 つで、<b>既定値のままのプロパティを無くすこと</b>。
    ///
    /// 既定値のままだと、網は「何も持っていないもの」を比べることになる。
    /// 2026-09-10 に、テストのビルダーが例題の断面を読んでいなかったあいだ
    /// 往復の網が既定値だけのモデルを往復させていたことが分かり、実機に合わせた
    /// 瞬間に本体の不具合 2 件が落ちた。例題に頼ると「例題が持っていない設定」は
    /// 永久に既定値のままなので、型から辿って機械的に動かす。
    /// </summary>
    internal static class ModelPerturbation
    {
        /// <summary>
        /// 保存グラフの全スカラーを既定値から動かす。動かせた個数を返す。
        ///
        /// 動かすのは double / int / bool だけ。<b>文字列と列挙は動かさない</b> —
        /// 断面タイプや工法の名前を壊すと、そもそも別のモデルの話になってしまう。
        ///
        /// 決まった順で決まった値を入れるので、実行ごとに同じ結果になる
        /// (乱数を使うと、落ちたときに再現できない)。
        /// </summary>
        internal static int PerturbAll(object root)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            int seq = 0;
            int moved = 0;
            Recurse(root);
            return moved;

            void Recurse(object? obj)
            {
                if (obj == null || obj is string) return;
                if (!visited.Add(obj)) return;

                var t = obj.GetType();
                if (t.IsPrimitive || t.IsEnum || t == typeof(decimal)
                    || t == typeof(DateTime) || t == typeof(Guid)) return;

                if (obj is System.Collections.IEnumerable e && obj is not System.Collections.IDictionary)
                {
                    foreach (var item in e) Recurse(item);
                    return;
                }

                if (!(t.Namespace ?? "").StartsWith("PileDesign", StringComparison.Ordinal)) return;

                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;

                    // 書き出されないものは動かしても意味がない
                    if (p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) continue;

                    var pt = p.PropertyType;

                    if (p.CanWrite && p.SetMethod?.IsPublic == true)
                    {
                        object? newValue = null;
                        if (pt == typeof(double) || pt == typeof(double?))
                        {
                            // 正の実数。0 や負を弾くセッターが多いので避ける。
                            // 端数を付けて、丸めで既定値へ戻らないようにする
                            newValue = 10.0 + (++seq % 97) + 0.125;
                        }
                        else if (pt == typeof(int) || pt == typeof(int?))
                        {
                            newValue = 1 + (++seq % 7);
                        }
                        else if (pt == typeof(bool) || pt == typeof(bool?))
                        {
                            object? cur;
                            try { cur = p.GetValue(obj); } catch { continue; }
                            newValue = !(cur is bool b && b);
                        }

                        if (newValue != null)
                        {
                            object? before, after;
                            try { before = p.GetValue(obj); } catch { before = null; }
                            try { p.SetValue(obj, newValue); } catch { continue; }
                            try { after = p.GetValue(obj); } catch { after = null; }

                            // クランプや無視で動かなかったものは数えない
                            if (!Equals(before, after)) moved++;
                            continue;
                        }
                    }

                    // 子へ潜る (文字列・列挙は触らない)
                    if (!pt.IsValueType && pt != typeof(string) && p.CanRead)
                    {
                        object? child;
                        try { child = p.GetValue(obj); } catch { continue; }
                        Recurse(child);
                    }
                }
            }
        }

        /// <summary>
        /// 2 つのグラフを<b>並行して辿り</b>、スカラーの値だけを突き合わせる。
        /// 食い違いを <paramref name="diffs"/> に積み、見比べた個数を返す。
        ///
        /// 直列化した文字列で比べないのは、<c>DeepCopy</c> が共有していた参照を 2 つに
        /// 分けるため。保存の形 ($id/$ref) は変わるのが正しく、文字列比較だとその
        /// 正しい違いに本当の写し漏らしが埋もれる。
        ///
        /// 構造 (件数) が違っていればその場で記録して打ち切る。件数が違うのは
        /// 値の写し漏らしより先に直すべき問題だから。
        /// </summary>
        internal static int CompareScalars(object a, object b, List<string> diffs, int maxDiffs = 200)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            int compared = 0;
            Walk(a, b, "InputModel");
            return compared;

            void Walk(object? x, object? y, string path)
            {
                if (diffs.Count >= maxDiffs) return;

                if (x == null || y == null)
                {
                    if (!ReferenceEquals(x, y)) diffs.Add($"{path}: 片方だけ null (元={x == null} 複製={y == null})");
                    return;
                }
                if (!visited.Add(x)) return;

                var t = x.GetType();
                if (t != y.GetType())
                {
                    diffs.Add($"{path}: 型が違います (元={t.Name} 複製={y.GetType().Name})");
                    return;
                }

                if (x is System.Collections.IEnumerable ex && x is not System.Collections.IDictionary)
                {
                    var xs = ex.Cast<object?>().ToList();
                    var ys = ((System.Collections.IEnumerable)y).Cast<object?>().ToList();
                    if (xs.Count != ys.Count)
                    {
                        diffs.Add($"{path}: 件数が違います (元={xs.Count} 複製={ys.Count})");
                        return;
                    }
                    for (int i = 0; i < xs.Count; i++) Walk(xs[i], ys[i], $"{path}[{i}]");
                    return;
                }

                if (!(t.Namespace ?? "").StartsWith("PileDesign", StringComparison.Ordinal)) return;

                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (p.GetMethod == null || !p.GetMethod.IsPublic) continue;

                    // 書き出さない派生値は比べない。計算し直せるので写す必要がない
                    if (p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) continue;

                    // Id は読み込み・複製のたびに static カウンタで振り直される runtime-only な番号。
                    // 永続識別子は UniqueId のほうで、そちらは比べる。
                    // (保存の網も "Id" を正規化してから比べている)
                    if (p.Name == "Id" && p.PropertyType == typeof(int)) continue;

                    var pt = p.PropertyType;
                    object? xv, yv;
                    try { xv = p.GetValue(x); yv = p.GetValue(y); } catch { continue; }

                    if (pt == typeof(double) || pt == typeof(double?)
                        || pt == typeof(int) || pt == typeof(int?)
                        || pt == typeof(bool) || pt == typeof(bool?)
                        || pt == typeof(string) || pt.IsEnum)
                    {
                        compared++;
                        if (!Equals(xv, yv))
                            diffs.Add($"{path}.{p.Name}: 元={xv} 複製={yv}");
                        continue;
                    }

                    if (!pt.IsValueType) Walk(xv, yv, $"{path}.{p.Name}");
                }
            }
        }

        /// <summary>
        /// 食い違ったプロパティ名を<b>全部</b>挙げる。1 件ずつ直すと、同じ原因の
        /// ものを何周も追うことになる。
        /// </summary>
        internal static string DifferingProperties(string a, string b)
        {
            var la = a.Split('\n');
            var lb = b.Split('\n');
            int max = Math.Min(la.Length, lb.Length);
            var key = new System.Text.RegularExpressions.Regex(@"""([^""]+)""\s*:");

            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < max; i++)
            {
                if (la[i] == lb[i]) continue;
                var m = key.Match(la[i]);
                string name = m.Success ? m.Groups[1].Value : $"(行 {i})";
                if (seen.Add(name)) names.Add(name);
            }

            string tail = la.Length != lb.Length ? $" / 行数が違います: {la.Length} と {lb.Length}" : "";
            return $"食い違ったプロパティ {names.Count} 種: "
                + string.Join(", ", names.GetRange(0, Math.Min(40, names.Count))) + tail;
        }

        /// <summary>最初に食い違った行を、前後 3 行の文脈付きで返す。</summary>
        internal static string FirstDiff(string a, string b, string labelA = "A", string labelB = "B")
        {
            var la = a.Split('\n');
            var lb = b.Split('\n');
            int max = Math.Min(la.Length, lb.Length);
            for (int i = 0; i < max; i++)
            {
                if (la[i] == lb[i]) continue;
                var ctx = "";
                for (int k = Math.Max(0, i - 3); k < Math.Min(max, i + 4); k++)
                {
                    var marker = k == i ? ">>" : "  ";
                    ctx += $"\n{marker} {k}: {labelA}={la[k].TrimEnd('\r')}\n   {k}: {labelB}={lb[k].TrimEnd('\r')}";
                }
                return $"最初の差異 行 {i} ({labelA} {la.Length} 行 / {labelB} {lb.Length} 行):{ctx}";
            }
            return la.Length == lb.Length ? "(差異なし)" : $"行数が違います: {la.Length} / {lb.Length}";
        }
    }
}
