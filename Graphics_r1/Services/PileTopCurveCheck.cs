using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PileDesign.Services
{
    /// <summary>
    /// 水平解析に使った杭頭の M-θ 曲線 (回転ばねのケースごとの控え。保存ファイルに入る) と、
    /// いまの計算方法で同じ軸力から作り直した曲線を比べる。
    ///
    /// <para>結果付きのファイルを再解析せずに計算書にすると、応力は解析したときの M-θ で解かれているのに、
    /// 諸元と検定はいまのコードで求める。表示用の諸元の文字 (PileTopSpecsBuilder) だけを比べると、諸元に出ない
    /// 算定 (Ke・軸力ごとの M-θ) の方法が変わったときに見逃す (2026-11-28 のレビューの指摘)。
    /// 解析がばねに入れた曲線そのものと比べれば、どこを変えても曲線が変われば分かる。</para>
    /// </summary>
    internal static class PileTopCurveCheck
    {
        /// <summary>曲線が食い違った 1 件 (杭・荷重ケースごと)。</summary>
        internal sealed record Difference(int PileBodyNo, int PileNo, string CaseKey, double AxialN, string Detail);

        /// <summary>
        /// 比べた結果。<see cref="Compared"/> は比べた控えの数、<see cref="ComparedBodies"/> は控えを比べた杭体番号、
        /// <see cref="UncheckedBodies"/> は照合できない控え (軸力を読めない・杭体を決められない) があった杭体番号 (決められなければ 0)。
        /// </summary>
        internal sealed record Result(int Compared, IReadOnlyList<Difference> Differences, IReadOnlySet<int> ComparedBodies, bool HasSnapshots = false,
            int Unchecked = 0, IReadOnlySet<int>? UncheckedBodies = null)
        {
            /// <summary>
            /// この杭体の対象の控えを<b>すべて</b>照合し、どれも同じだったか。照合できない控えが 1 件でもあれば false
            /// (以前は 1 件でも比べられれば照合済みとし、軸力を読めない控えがあっても旧版の注記を省いていた)。
            /// </summary>
            internal bool Verified(int pileBodyNo) => ComparedBodies.Contains(pileBodyNo)
                && Differences.All(d => d.PileBodyNo != pileBodyNo)
                && UncheckedBodies?.Contains(pileBodyNo) != true && UncheckedBodies?.Contains(0) != true;
        }

        /// <summary>控えの理由に書いた軸力 (解析は 1 kN に丸めた値を使い、整数で書く)。</summary>
        private static readonly Regex AxialNInReason = new(@"axialN=(-?\d+(?:\.\d+)?)kN", RegexOptions.CultureInvariant);

        /// <summary>控えの軸力 (kN)。記録が無ければ理由の文字から読む (以前の控えは AxialN を入れていなかった)。</summary>
        internal static double? AxialNOf(MThetaCaseSnapshot snapshot)
        {
            if (snapshot.AxialN != 0.0 && double.IsFinite(snapshot.AxialN)) return snapshot.AxialN;
            var m = AxialNInReason.Match(snapshot.SetupReason ?? "");
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : null;
        }

        /// <summary>
        /// 杭頭の M-θ を使ったケースの控えについて、いまの計算方法の曲線と比べる。
        /// 設定で剛にしたケース (杭頭回転角を考慮しない・杭を線形で解く) は計算方法と関係ないので比べない。
        /// </summary>
        internal static Result Compare(InputModel? input, AnaModel? model)
        {
            var differences = new List<Difference>();
            var bodies = new HashSet<int>();
            var uncheckedBodies = new HashSet<int>();
            int compared = 0, uncheckedCount = 0;
            bool hasSnapshots = false;
            if (input?.PileLayoutItems == null || model?.RotationalSprings == null) return new(0, differences, bodies);
            var cache = new Dictionary<(int Body, double N), PileHeadRotationDef?>();
            foreach (var spring in model.RotationalSprings)
            {
                if (spring == null || spring.CaseMThetaSnapshots.IsEmpty) continue;
                hasSnapshots = true;
                var pile = spring.FindPileLayout(input.PileLayoutItems);
                int bodyNo = pile?.PileBodyNo ?? spring.PileBodyNo ?? 0;
                var body = input.PileBodyAt(bodyNo);
                foreach (var (key, snap) in spring.CaseMThetaSnapshots.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    string reason = snap.SetupReason ?? "";
                    if (reason.StartsWith("Rigid(基本設定", StringComparison.Ordinal) || reason.StartsWith("Rigid(IsPileNonLinear", StringComparison.Ordinal))
                        continue;
                    // 照合できない控え (杭体を決められない・軸力を読めない) は数えて残す。照合済みにしない
                    if (body == null || AxialNOf(snap) is not double n)
                    {
                        uncheckedCount++;
                        uncheckedBodies.Add(body == null ? 0 : bodyNo);
                        continue;
                    }
                    if (!cache.TryGetValue((bodyNo, n), out var def))
                    {
                        try { def = body.GetMThetaRelationship(n); }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException) { def = null; }
                        cache[(bodyNo, n)] = def;
                    }
                    compared++;
                    bodies.Add(bodyNo);
                    string? detail = Describe(snap, spring, def);
                    if (detail != null) differences.Add(new(bodyNo, pile?.No ?? 0, key, n, detail));
                }
            }
            return new(compared, differences, bodies, hasSnapshots, uncheckedCount, uncheckedBodies);
        }

        /// <summary>控えの曲線といまの曲線の違い。同じなら null。</summary>
        private static string? Describe(MThetaCaseSnapshot snap, RotationalSpring spring, PileHeadRotationDef? now)
        {
            if (now == null) return "いまの計算方法で M-θ 関係を作れない";
            bool wasRigid = (snap.SetupReason ?? "").StartsWith("Rigid(def.Mode=Rigid", StringComparison.Ordinal);
            bool isRigid = now.Mode == PileHeadRotationMode.Rigid;
            if (wasRigid || isRigid)
                return wasRigid == isRigid ? null : (wasRigid ? "解析時は剛、いまは M-θ 関係がある" : "解析時は M-θ 関係があり、いまは剛");
            MomentRotationCurve? before = snap.Mode == RotationalSpringMode.SingleDof ? snap.Curve : snap.CurveXY;
            MomentRotationCurve? after = snap.Mode == RotationalSpringMode.SingleDof
                ? (spring.Dof == RotationalDof.Ry ? now.CurveY : now.CurveX)
                : now.CurveXY;
            if ((before == null) != (after == null)) return "M-θ 曲線の有無が違う";
            if (before != null && after != null && !SameCurve(before.Points, after.Points))
                return CurveDetail(before, after);

            // 曲線の無い線形ばね (鋼管杭の鉄筋定着工法の Kθ など) と、定義が剛性・ひび割れモーメントを与えるものは値で比べる。
            // 以前は曲線がどちらも無ければ一致とし、線形ばねの剛性が変わっても見逃した (2026-11-28 の指摘)。
            // 解析は定義の剛性があればそれをばねに入れる (無ければ曲線の初期接線などから決めるので、曲線の比較で足りる)
            double? kNow = snap.Mode == RotationalSpringMode.SingleDof
                ? (spring.Dof == RotationalDof.Ry ? now.Ky : now.Kx)
                : now.KthetaXY;
            double? kBefore = snap.Mode == RotationalSpringMode.SingleDof ? snap.Ktheta : snap.KthetaXY;
            if (kNow is double k && k > 0.0 && !SameValue(kBefore, k))
                return $"回転剛性 解析時 {Format(kBefore)} → 現在 {k:N0} kN·m/rad";
            if (snap.Mode != RotationalSpringMode.SingleDof && !SameValue(snap.McrXY, now.McrXY))
                return $"ひび割れモーメント 解析時 {Format(snap.McrXY)} → 現在 {Format(now.McrXY)} kN·m";
            return null;
        }

        private static string Format(double? v) => v is double d ? d.ToString("N0", CultureInfo.InvariantCulture) : "なし";

        /// <summary>2 つの値が同じか (どちらも無いか、相対 1e-9 で一致)。</summary>
        internal static bool SameValue(double? a, double? b)
        {
            if (a is not double x || b is not double y) return a == null && b == null;
            return Math.Abs(x - y) <= 1e-9 * Math.Max(1e-12, Math.Max(Math.Abs(x), Math.Abs(y)));
        }

        private static string CurveDetail(MomentRotationCurve before, MomentRotationCurve after)
        {
            double mBefore = before.Points.Count == 0 ? 0 : before.Points.Max(p => Math.Abs(p.Moment));
            double mAfter = after.Points.Count == 0 ? 0 : after.Points.Max(p => Math.Abs(p.Moment));
            double kBefore = InitialSlope(before.Points), kAfter = InitialSlope(after.Points);
            return $"最大の曲げモーメント 解析時 {mBefore:N1} → 現在 {mAfter:N1} kN·m、初期勾配 解析時 {kBefore:N0} → 現在 {kAfter:N0} kN·m/rad";
        }

        private static double InitialSlope(IReadOnlyList<(double Theta, double Moment)> pts)
        {
            var p = pts.FirstOrDefault(q => q.Theta > 0);
            return p.Theta > 0 ? p.Moment / p.Theta : 0.0;
        }

        /// <summary>点の数と各点の θ・M が一致するか (相対 1e-9。同じ計算なら同じ値になる)。</summary>
        internal static bool SameCurve(IReadOnlyList<(double Theta, double Moment)> a, IReadOnlyList<(double Theta, double Moment)> b)
        {
            if (a.Count != b.Count) return false;
            double scaleM = Math.Max(1e-12, a.Concat(b).Max(p => Math.Abs(p.Moment)));
            double scaleT = Math.Max(1e-15, a.Concat(b).Max(p => Math.Abs(p.Theta)));
            for (int i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i].Theta - b[i].Theta) > 1e-9 * scaleT) return false;
                if (Math.Abs(a[i].Moment - b[i].Moment) > 1e-9 * scaleM) return false;
            }
            return true;
        }

        /// <summary>
        /// 計算書・警告に書く文。比べた控えが無い (解析時の曲線の記録が無い) ときと、違いがあるときだけ。
        /// </summary>
        internal static string? Note(Result result, bool usesPileHeadRotation)
        {
            if (result.Differences.Count > 0)
            {
                var shown = result.Differences
                    .GroupBy(d => d.PileBodyNo)
                    .Select(g => g.First())
                    .Select(d => $"杭体 {d.PileBodyNo} (杭 No.{d.PileNo}、{d.CaseKey}、N = {d.AxialN:N0} kN): {d.Detail}");
                return "※ 水平解析に使った杭頭の M-θ 関係が、いまの計算方法で同じ軸力から求めたものと異なる (計算方法の変更など。"
                       + $"{result.Differences.Count} 件)。" + string.Join("。", shown)
                       + "。応力解析は解析時の M-θ 関係で、杭頭の諸元と検定はいまの計算方法で求めている。"
                       + (result.Unchecked > 0 ? $"ほかに照合できない控えが {result.Unchecked} 件ある。" : "")
                       + "一致させるには水平解析をやり直す。";
            }
            if (result.Unchecked > 0)
                return $"※ 水平解析に使った杭頭の M-θ 関係のうち {result.Unchecked} 件は、軸力などを読めず、いまの計算方法と同じかを確かめられない"
                       + (result.Compared > 0 ? $" (照合できた {result.Compared} 件は同じ)" : "")
                       + "。杭頭の諸元と検定はいまの計算方法で求めている。一致させるには水平解析をやり直す。";
            // 控えはあるが比べたものが無い = どのケースも設定で剛にした (M-θ を使っていない)。断ることはない。
            // 控えそのものが無いのは、ケースごとの M-θ を保存していない旧版の結果
            if (!result.HasSnapshots && usesPileHeadRotation)
                return "※ この水平解析の結果には、解析に使った杭頭の M-θ 関係の記録が無いため、いまの計算方法と同じかを確かめられない。"
                       + "杭頭の諸元と検定はいまの計算方法で求めている。一致させるには水平解析をやり直す。";
            return null;
        }
    }
}
