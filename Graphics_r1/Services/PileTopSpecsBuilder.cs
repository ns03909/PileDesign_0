using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.PileLibrary;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 杭頭の諸元 (計算書の杭頭諸元の表・解析の記録)。いまの入力と、解析と同じ派生値から組む。
    ///
    /// <para>計算書は、杭頭部ウィンドウで作って保存された諸元 (<see cref="PileTop.SelectedPileTopSpecification"/>) を使わない。
    /// ウィンドウを開いた時点の値で、入力や計算方法がそのあと変わると、解析・検定と違う値が出た (2026-11-28)。</para>
    ///
    /// <para>水平解析を終えたときにも同じものを組んで解析の記録 (<see cref="AnalysisRunMetadata.PileTopSpecs"/>) に残す。
    /// 結果付きのファイルを再解析せずに計算書にすると、応力は解析したときの値 (Ke・Mu など) で解かれているのに、
    /// 諸元はいまの計算方法で組み直した値になる。記録と比べて、違えば計算書に注記する。</para>
    /// </summary>
    internal static class PileTopSpecsBuilder
    {
        /// <summary>
        /// 杭体 1 本の杭頭諸元。杭頭モデルへ杭体とパイルキャップの値を配るのは解析の入口と同じ処理
        /// (<see cref="PileBodyInput.ApplyTopSectionToPileTop"/>。変わっていなければ何もしない)。
        /// ここで組まない杭頭タイプは保存された諸元を返す。
        /// </summary>
        internal static IReadOnlyList<Spec>? Build(PileBodyInput pileBody)
        {
            var pileTop = pileBody.PileTop;
            if (pileTop == null) return null;
            var topSection = pileBody.PileBodySegments?.FirstOrDefault()?.PileSection;
            string type = pileBody.PileTopType ?? "";
            if (type == NewJBar.PileTopTypeName)
            {
                // 杭とパイルキャップから受け取る値は保存しないので、検定と同じ値にそろえた写しから作る
                var jbar = pileTop.NewJBarFor(type, topSection);
                return jbar == null ? null : [.. pileTop.GetPileCapConcreteSpecs(), .. jbar.GetSpecs()];
            }
            if (type is "キャプテンパイル工法" or "FT-Pile構法" or "キャプリングパイル工法")
            {
                pileBody.ApplyTopSectionToPileTop();
                // パイルキャップの Fc・γ・Ec は杭頭 M-θ を動かす (FT-Pile は曲げ耐力 3/5·φc·Ap·Fc と K0、
                // キャプテンは CTPConcrete、キャプリングは Ec/Eb) ので、検算できるよう先頭に置く
                IEnumerable<Spec>? method = type switch
                {
                    "キャプテンパイル工法" when pileTop.CaptainPile?.PCRing != null => pileTop.CaptainPile.GetCombinedSpecs(),
                    "キャプリングパイル工法" when pileTop.CapringPile?.PCRing != null => pileTop.CapringPile.GetCombinedSpecs(),
                    "FT-Pile構法" when pileTop.FTPile?.FTCap != null => pileTop.FTPile.GetCombinedSpecs(),
                    _ => null,
                };
                return method == null ? null : [.. pileTop.GetPileCapConcreteSpecs(), .. method];
            }
            if (type.Contains("鉄筋定着工法"))
                return pileTop.BuildRebarAnchorageSpecs(pileBody.PileBodyType, topSection);
            return pileTop.SelectedPileTopSpecification;
        }

        /// <summary>解析の記録に残す形 (名前 = 項目と記号、値 = 値と単位)。</summary>
        internal static List<AnalysisConditionEntry> ToEntries(IEnumerable<Spec> specs)
            => specs.Select(s => new AnalysisConditionEntry(
                    string.IsNullOrEmpty(s.Mark) ? s.Item ?? "" : $"{s.Item} {s.Mark}",
                    string.IsNullOrEmpty(s.Unit) ? s.Value ?? "" : $"{s.Value} {s.Unit}"))
                .ToList();

        /// <summary>水平解析を終えたときの、杭体ごとの杭頭諸元 (解析の記録に残す)。</summary>
        internal static List<AnalysisPileTopSpecs> Record(InputModel? input)
        {
            var records = new List<AnalysisPileTopSpecs>();
            var bodies = input?.PileBodies;
            if (bodies == null) return records;
            for (int i = 0; i < bodies.Count; i++)
            {
                var pb = bodies[i];
                if (pb == null) continue;
                IReadOnlyList<Spec>? specs;
                try { specs = Build(pb); }
                catch (System.Exception ex) when (ex is System.ArgumentException or System.InvalidOperationException or System.ArithmeticException)
                {
                    Serilog.Log.Warning(ex, "[解析の記録] 杭体 {No} の杭頭諸元を組めませんでした", i + 1);
                    continue;
                }
                if (specs == null || specs.Count == 0) continue;
                records.Add(new AnalysisPileTopSpecs { PileBodyNo = i + 1, PileTopType = pb.PileTopType ?? "", Specs = ToEntries(specs) });
            }
            return records;
        }

        /// <summary>
        /// 計算書の杭頭諸元の表の下に添える注記。水平解析の結果が無ければ null。
        /// <list type="bullet">
        /// <item>解析の記録に杭頭諸元がある: いまの値と違う項目があれば、解析したときの値を挙げる。同じなら null。</item>
        /// <item>記録が無い (杭頭諸元を記録していない版の結果): 解析に使った値と違うことがあると断る。</item>
        /// </list>
        /// </summary>
        /// <param name="curveVerified">この杭体の M-θ を、解析に使った曲線と照合して同じだったか (PileTopCurveCheck)。
        /// 同じなら、記録が無くても応力解析の前提は変わっていないので断らず、違う項目は検定などの値の違いとして書く。</param>
        internal static string? ChangeNote(int pileBodyNo, string? pileTopType, IReadOnlyList<Spec> current,
            bool hasHorizontalResult, AnalysisRunMetadata? horizontalRecord, bool curveVerified = false)
        {
            if (!hasHorizontalResult) return null;
            var recorded = horizontalRecord?.PileTopSpecs?
                .FirstOrDefault(r => r.PileBodyNo == pileBodyNo && r.PileTopType == (pileTopType ?? ""));
            if (recorded == null)
            {
                if (curveVerified) return null;
                string version = string.IsNullOrEmpty(horizontalRecord?.ApplicationVersion) ? "不明" : horizontalRecord!.ApplicationVersion;
                bool recordsSpecs = horizontalRecord?.PileTopSpecs is { Count: > 0 };
                return recordsSpecs
                    ? "※ 水平解析のときのこの杭体の杭頭諸元は記録されていない (解析のあとに杭頭タイプを変えた等)。表はいまの入力と計算方法による値であり、" +
                      "応力解析 (杭頭の M-θ) に使った値と異なる場合がある。一致させるには水平解析をやり直す。"
                    : $"※ この水平解析の結果は、杭頭諸元を記録していない版 (プログラムの版 {version}) で求めたものである。表はいまの計算方法による値であり、" +
                      "応力解析 (杭頭の M-θ) に使った値と異なる場合がある。一致させるには水平解析をやり直す。";
            }
            var now = ToEntries(current).GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First().Value);
            var changed = recorded.Specs
                .Where(e => now.TryGetValue(e.Name, out var v) && v != e.Value)
                .Select(e => $"{e.Name} (解析時 {e.Value} → 現在 {now[e.Name]})")
                .ToList();
            if (changed.Count == 0) return null;
            if (curveVerified)
                return "※ 次の項目は、水平解析のときの値と異なる (計算方法の変更による): " + string.Join("、", changed) +
                       "。杭頭の M-θ 関係は解析に使った曲線と照合して同じであり、検定はこの表の値で行っている。";
            return "※ 次の項目は、水平解析のときの値と異なる (計算方法の変更による): " + string.Join("、", changed) +
                   "。応力解析 (杭頭の M-θ) は解析時の値で、検定はこの表の値で行っている。一致させるには水平解析をやり直す。";
        }
    }
}
