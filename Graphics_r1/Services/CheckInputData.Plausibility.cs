using PileDesign.Common;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 値としては受け付けられるが、単位の取り違え・名前の重なり・範囲の外などで
    /// <b>もっともらしい誤った結果</b>を黙って出す入力の検査。
    ///
    /// <para>解析の数値は変えない (検査は入力を書き換えない)。止めるもの (<see cref="CollectPlausibilityBlockers"/>) は
    /// 解析の前に止め、残りは解析前の確認と計算書の「入力についての注意」に出す (<see cref="CollectPlausibilityNotices"/>)。</para>
    /// </summary>
    partial class CheckInputData
    {
        /// <summary>地表面加速度としてありそうな範囲 [m/s²]。外なら単位の取り違えを疑う。</summary>
        internal const double MinPlausibleGroundAcceleration = 0.5, MaxPlausibleGroundAcceleration = 10.0;

        /// <summary>土の単位体積重量 (全重量) としてありそうな範囲 [kN/m³]。外なら t/m³ や水中重量の入力を疑う。</summary>
        internal const double MinPlausibleSoilDensity = 10.0, MaxPlausibleSoilDensity = 25.0;

        /// <summary>変形係数 Es が N 値のこの倍数 [kN/m²] を下回れば、N/mm² (MN/m²) での入力を疑う (目安の Es = 700N の 1/7)。</summary>
        internal const double MinPlausibleEsPerN = 100.0;

        /// <summary>杭間隔比 R/B と、杭配置から求めた中心間隔 ÷ 杭径 がこの割合を超えて違えば知らせる。</summary>
        internal const double SpacingRatioTolerance = 0.25;

        /// <summary>解析を止める、もっともらしい誤りの入力 (kh0 の手入力が当たる土層が決まらない・杭間隔比が 1 未満)。</summary>
        internal static List<Diagnostic> CollectPlausibilityBlockers(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            if (inputModel == null) return problems;
            problems.AddRange(CollectKh0OverrideProblems(inputModel));
            problems.AddRange(CollectPileSpacingProblems(inputModel));
            return problems;
        }

        /// <summary>解析は止めないが、もっともらしい誤りの結果になりうる入力の注意 (結果に影響)。</summary>
        internal static List<Diagnostic> CollectPlausibilityNotices(InputModel inputModel)
        {
            var notices = new List<Diagnostic>();
            if (inputModel == null) return notices;
            notices.AddRange(CollectKh0OverrideNotices(inputModel));
            notices.AddRange(CollectPileToeNValueNotices(inputModel));
            notices.AddRange(CollectNegativeStressSpringNotices(inputModel));
            notices.AddRange(CollectGroundAccelerationNotices(inputModel));
            notices.AddRange(CollectSoilMagnitudeNotices(inputModel));
            notices.AddRange(CollectPileSpacingNotices(inputModel));
            return notices;
        }

        // ── kh0 の手入力 ──

        /// <summary>土層-杭セットの地盤 (入力の地盤を優先し、無ければセットが持つ地盤)。</summary>
        private static GroundInput? GroundOf(InputModel inputModel, SoilPile soilPile)
            => inputModel.GroundAt(soilPile.GroundNo) ?? soilPile.GroundInput;

        /// <summary>
        /// kh0 の手入力は土層名で引く (<see cref="SoilPile.GetKh0Override"/>)。同じ名前の土層が 2 つ以上あると、
        /// 手入力の値がその名前の層<b>すべて</b>に効き、どの層のつもりの値かが決まらない。新しい土層は既定で
        /// 同じ名前 (「(AS#)」) になるので、名前を付けないまま手入力すると通常の操作で起きる。
        /// </summary>
        internal static List<Diagnostic> CollectKh0OverrideProblems(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            foreach (var soilPile in inputModel?.ElementDivision?.SoilPiles ?? [])
            {
                if (soilPile?.Kh0LayerOverrides is not { Count: > 0 } overrides) continue;
                var layers = GroundOf(inputModel!, soilPile)?.GroundLayers;
                if (layers == null) continue;
                foreach (var name in overrides.Select(o => o?.LayerName).Where(n => !string.IsNullOrEmpty(n)).Distinct())
                {
                    int count = layers.Count(l => l?.Name == name);
                    if (count <= 1) continue;
                    problems.Add(Diagnostic.Input(DiagnosticTarget.Ground(soilPile.GroundNo),
                        $"杭体{soilPile.PileBodyNo}・地盤{soilPile.GroundNo}: 土層名「{name}」の土層が {count} 層あり、kh0 の手入力をどの層に使うか決まりません。"
                        + "土層名を重ならないように付けてください。") with
                    {
                        Remedy = "地盤の画面で土層名を付け直し、杭要素分割の画面で kh0 の手入力を確かめる",
                    });
                }
            }
            return problems;
        }

        /// <summary>
        /// kh0 の手入力のうち、地盤にその名前の土層が無いもの。土層の名前を変えると手入力が取り残され、
        /// 解析では使われない (自動算定に戻る) のに、計算書の仮定一覧には手入力として書かれていた。
        /// </summary>
        internal static List<Diagnostic> CollectKh0OverrideNotices(InputModel inputModel)
        {
            var notices = new List<Diagnostic>();
            foreach (var soilPile in inputModel?.ElementDivision?.SoilPiles ?? [])
            {
                if (soilPile?.Kh0LayerOverrides is not { Count: > 0 } overrides) continue;
                var layers = GroundOf(inputModel!, soilPile)?.GroundLayers;
                if (layers == null) continue;
                foreach (var o in overrides)
                {
                    if (o == null || layers.Any(l => l?.Name == o.LayerName)) continue;
                    notices.Add(Diagnostic.Notice(DiagnosticSeverity.Warning, DiagnosticTarget.Ground(soilPile.GroundNo),
                        $"杭体{soilPile.PileBodyNo}・地盤{soilPile.GroundNo}: kh0 の手入力 (土層名「{o.LayerName}」、{o.Kh0:N0} kN/m³) に当たる土層がありません。"
                        + "この手入力は使われず、kh0 は自動算定の値で計算します (土層名を変えると手入力が取り残されます)。",
                        "杭要素分割の画面で kh0 の手入力を付け直すか、解除する"));
                }
            }
            return notices;
        }

        // ── 杭先端の平均 N 値 ──

        /// <summary>
        /// 一般式の杭先端平均 N 値の範囲 (杭先端 ±D) に土質点が無い土層-杭セット。平均 N 値は 0 になり
        /// (<see cref="SoilPile.PileToeNValue"/>)、N 値から求める極限先端支持力度が 0 のまま計算が進む。
        /// 以前は単杭沈下の画面を開いたときにしか知らせていなかった。
        /// </summary>
        internal static List<Diagnostic> CollectPileToeNValueNotices(InputModel inputModel)
        {
            var notices = new List<Diagnostic>();
            foreach (var soilPile in inputModel?.ElementDivision?.SoilPiles ?? [])
            {
                if (soilPile?.GroundInput == null || soilPile.IsSmartMagnum || soilPile.IsHybridKneading) continue;
                if (!UsesToeNValue(soilPile.PileConstructionType, soilPile.PileToeGranularityClass)) continue;
                if (soilPile.PileToeNValuePoints().Count > 0) continue;
                double lower = soilPile.PileToeNValueAverageRangeLowerAltitude, upper = soilPile.PileToeNValueAverageRangeUpperAltitude;
                notices.Add(Diagnostic.Notice(DiagnosticSeverity.Warning, DiagnosticTarget.PileBody(soilPile.PileBodyNo),
                    $"杭体{soilPile.PileBodyNo}・地盤{soilPile.GroundNo} (杭先端 標高 {soilPile.PileBottomAltitude:F2} m): "
                    + $"杭先端の平均 N 値を求める範囲 (標高 {lower:F2}〜{upper:F2} m、杭先端から上下に杭先端径) に土質点がありません。"
                    + "平均 N 値を 0 とし、先端支持力を 0 として計算します。",
                    "地盤の画面で、杭先端付近の土質点 (N 値) を入力する"));
            }
            return notices;
        }

        /// <summary>その工法・先端の土質で、極限先端支持力度に平均 N 値を使うか (<c>SoilPile.UpdatePileToeProperties</c> の式)。</summary>
        private static bool UsesToeNValue(string? constructionType, string? toeSoil)
        {
            bool granular = toeSoil is "砂質土" or "礫質土";
            if (constructionType is PileConstructionTypeNames.Insitu or PileConstructionTypeNames.Chubori) return granular;
            if (PileConstructionTypeNames.IsPreboring(constructionType)
                || constructionType is PileConstructionTypeNames.Rotary or PileConstructionTypeNames.Driven)
                return granular || toeSoil == "粘性土";
            return false;
        }

        // ── 有効上載圧が負になって水平地盤ばねが消える区間 ──

        /// <summary>
        /// 砂質土・礫質土の塑性水平地盤反力 py は有効上載圧に比例する。単位体積重量に水中重量を入れたなど
        /// (地下水位より下で γ &lt; 10 kN/m³) で有効上載圧が負になると、py が 0 以下 (内部摩擦角を畑中式で求めると
        /// 数値でない値) になり、その区間の水平地盤ばねが無いまま計算が進む。
        /// </summary>
        internal static List<Diagnostic> CollectNegativeStressSpringNotices(InputModel inputModel)
        {
            var notices = new List<Diagnostic>();
            foreach (var soilPile in inputModel?.ElementDivision?.SoilPiles ?? [])
            {
                if (soilPile?.HorizontalSoilReactions == null) continue;
                var bad = soilPile.HorizontalSoilReactions
                    .Where(r => r != null && r.SoilType is "砂質土" or "礫質土"
                                && (!(r.SigmaZPrimeTop >= 0) || !(r.SigmaZPrimeBtm >= 0)))
                    .ToList();
                if (bad.Count == 0) continue;
                double top = bad.Max(r => r.ZTop), bottom = bad.Min(r => r.ZBtm);
                notices.Add(Diagnostic.Notice(DiagnosticSeverity.Warning, DiagnosticTarget.Ground(soilPile.GroundNo),
                    $"杭体{soilPile.PileBodyNo}・地盤{soilPile.GroundNo}: 標高 {top:F2}〜{bottom:F2} m の砂質土・礫質土で有効上載圧が負になり、"
                    + "その区間の水平地盤ばねが 0 になります (塑性水平地盤反力 py が 0 以下になるため)。",
                    "地盤の画面で、単位体積重量 (水中重量ではなく全重量) と地下水位を確かめる"));
            }
            return notices;
        }

        // ── 単位の取り違え (地表面加速度・単位体積重量・変形係数) ──

        /// <summary>
        /// 地表面加速度 (液状化の判定の τd/σz' = rn·αmax/g·σz/σz'·rd に入る) が m/s² としてありそうにない値。
        /// gal (350 のつもり) や g (0.35 のつもり) で入れると、液状化の判定が桁で外れたまま計算が進む。
        /// 入力欄は 0〜100 m/s² に丸めて受け付けるので、gal で入れた値は 100 になって残る。
        /// </summary>
        internal static List<Diagnostic> CollectGroundAccelerationNotices(InputModel inputModel)
        {
            var notices = new List<Diagnostic>();
            var grounds = inputModel?.GroundsInput;
            if (grounds == null) return notices;
            for (int g = 0; g < grounds.Count; g++)
            {
                var ground = grounds[g];
                if (ground == null) continue;
                foreach (var (level, value, property) in new[]
                {
                    (1, ground.GroundAcceleration1, nameof(GroundInput.GroundAcceleration1)),
                    (2, ground.GroundAcceleration2, nameof(GroundInput.GroundAcceleration2)),
                })
                {
                    if (value >= MinPlausibleGroundAcceleration && value <= MaxPlausibleGroundAcceleration) continue;
                    bool clamped = ground.GetErrors(property)?.Cast<object>().Any() == true;
                    notices.Add(Diagnostic.Notice(DiagnosticSeverity.Warning, DiagnosticTarget.Ground(g + 1),
                        $"地盤 {g + 1}: レベル{level} の地表面加速度 {value:0.###} m/s² は単位が違う可能性があります "
                        + "(m/s² で入力します。350 gal = 3.5 m/s²、0.35 g ≒ 3.4 m/s²)。"
                        + (clamped ? "入力した値は範囲 (0〜100 m/s²) の外だったため、丸めた値になっています。" : "")
                        + "液状化の判定に使います。",
                        "地盤の画面で地表面加速度を m/s² で入れ直す"));
                }
            }
            return notices;
        }

        /// <summary>
        /// 土層の単位体積重量・変形係数の桁がありそうにないもの。負は <see cref="CollectSoilPropertyProblems"/> が止める。
        /// <list type="bullet">
        /// <item>γ が 10〜25 kN/m³ の外: t/m³ (1.8 のつもり) や水中重量で入れた疑い。有効応力が変わり、水平地盤ばね・周面摩擦力・液状化の判定に効く。</item>
        /// <item>Es が 100·N kN/m² 未満: N/mm² (MN/m²) で入れた疑い (目安の 700N の 1/7 未満)。kh0 は Es に比例する。</item>
        /// </list>
        /// </summary>
        internal static List<Diagnostic> CollectSoilMagnitudeNotices(InputModel inputModel)
        {
            var notices = new List<Diagnostic>();
            var grounds = inputModel?.GroundsInput;
            if (grounds == null) return notices;
            for (int g = 0; g < grounds.Count; g++)
            {
                var layers = grounds[g]?.GroundLayers;
                if (layers == null) continue;
                for (int li = 0; li < layers.Count; li++)
                {
                    var layer = layers[li];
                    if (layer == null) continue;
                    var at = DiagnosticTarget.GroundLayer(g + 1, li + 1);
                    // 負と数値でない値は解析を止める側 (CollectSoilPropertyProblems) に任せる
                    if (layer.Density >= 0 && double.IsFinite(layer.Density)
                        && !(layer.Density >= MinPlausibleSoilDensity && layer.Density <= MaxPlausibleSoilDensity))
                        notices.Add(Diagnostic.Notice(DiagnosticSeverity.Warning, at,
                            $"{at.Label}: 単位体積重量 γ = {layer.Density:0.###} kN/m³ は土としては"
                            + (layer.Density < MinPlausibleSoilDensity ? "小さすぎます" : "大きすぎます")
                            + $" (通常 {MinPlausibleSoilDensity:0}〜{MaxPlausibleSoilDensity:0} kN/m³)。"
                            + "t/m³ や水中重量で入れていないか確かめてください (全重量を kN/m³ で入力します)。"
                            + "有効上載圧が変わり、水平地盤ばね・周面摩擦力・液状化の判定に効きます。",
                            "地盤の画面で単位体積重量を kN/m³ の全重量で入れ直す"));
                    if (layer.NValue > 0 && layer.Es > 0 && double.IsFinite(layer.Es) && layer.Es < MinPlausibleEsPerN * layer.NValue)
                        notices.Add(Diagnostic.Notice(DiagnosticSeverity.Warning, at,
                            $"{at.Label}: 変形係数 Es = {layer.Es:0.###} kN/m² が N 値 ({layer.NValue:0.#}) に比べて小さすぎます "
                            + $"(Es = 700N なら {700 * layer.NValue:N0} kN/m²)。N/mm² (MN/m²) で入れていないか確かめてください (kN/m² で入力します)。"
                            + "基準水平地盤反力係数 kh0 は Es に比例します。",
                            "地盤の画面で変形係数を kN/m² で入れ直す"));
                }
            }
            return notices;
        }

        // ── 杭間隔比 R/B ──

        /// <summary>
        /// 杭間隔比 R/B (中心間隔 ÷ 杭径) が 1 未満の杭。杭どうしが重なる間隔で、後方杭の py の式
        /// (基礎指針'19 表6.6.3・表6.6.4 の κ・µ・λ) の範囲の外になる (µ は R/B &lt; 2/3 で負)。
        /// 0 以下は「未入力 (群杭の影響なし)」として警告 (<see cref="CollectInputWarningDiagnostics"/>) にとどめる。
        /// </summary>
        internal static List<Diagnostic> CollectPileSpacingProblems(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            foreach (var p in inputModel?.PileLayoutItems ?? [])
            {
                if (p == null) continue;
                double r = p.PileSpacingFactor;
                if (!(r > 0 && r < 1)) continue;
                problems.Add(Diagnostic.InputAt(DiagnosticTarget.Pile(p.No),
                    $"杭間隔比 R/B = {r:0.###} が 1 未満です (杭どうしが重なる間隔です)。"
                    + "R/B は杭の中心間隔 ÷ 杭径で、1 以上で入力してください (群杭の影響を考えないなら 0)。"
                    + "1 未満では後方杭の塑性水平地盤反力 py の係数が式の範囲の外になり、負にもなります。"));
            }
            return problems;
        }

        /// <summary>
        /// 杭間隔比 R/B が、杭配置から求めた中心間隔 ÷ 杭径 のどれとも大きく違う杭 (同じ R/B の杭をまとめて 1 行)。
        ///
        /// <para>R/B は荷重方向の杭の並びで決まるが、荷重方向は解析のケースで変わるので、ここでは方向を決めない。
        /// 中心間隔の候補は、いちばん近い杭・X 方向に並ぶ杭 (Y が同じ)・Y 方向に並ぶ杭 (X が同じ) の 3 つで、
        /// どれとも 25% を超えて違うときだけ知らせる。R/B を大きく入れると (既定の 10 など) 後方杭の py は単杭と同じになり、
        /// 実際の間隔が狭ければ水平抵抗を過大に見る。</para>
        /// </summary>
        internal static List<Diagnostic> CollectPileSpacingNotices(InputModel inputModel)
        {
            var notices = new List<Diagnostic>();
            var piles = (inputModel?.PileLayoutItems ?? []).Where(p => p != null).ToList();
            if (piles.Count < 2) return notices;
            const double tol = 1e-3; // m。同じ位置・同じ列とみなす

            var mismatched = new List<(PileLayoutDataItem Pile, double[] Candidates)>();
            foreach (var p in piles)
            {
                double r = p.PileSpacingFactor;
                if (!(r >= 1) || !double.IsFinite(r)) continue; // 未入力 (0 以下) と 1 未満は別に知らせる
                double b = (inputModel!.PileBodyAt(p.PileBodyNo)?.PileBodySegments?.FirstOrDefault()?.PileSection?.PileDiameter ?? 0) / 1000.0;
                if (!(b > 0) || !double.IsFinite(b)) continue;

                double nearest = double.PositiveInfinity, alongX = double.PositiveInfinity, alongY = double.PositiveInfinity;
                foreach (var q in piles)
                {
                    if (ReferenceEquals(p, q)) continue;
                    double dx = Math.Abs(q.X - p.X), dy = Math.Abs(q.Y - p.Y);
                    double d = StableNumerics.Norm(dx, dy);
                    if (!(d > tol)) continue; // 同じ位置の杭は別の警告 (ModelConnectivityCheck) で知らせる
                    nearest = Math.Min(nearest, d);
                    if (dy <= tol) alongX = Math.Min(alongX, dx);
                    if (dx <= tol) alongY = Math.Min(alongY, dy);
                }
                var candidates = new[] { nearest, alongX, alongY }.Where(double.IsFinite).Select(d => d / b).Distinct().ToArray();
                if (candidates.Length == 0) continue;
                if (candidates.Any(c => Math.Abs(r - c) <= SpacingRatioTolerance * c)) continue;
                mismatched.Add((p, candidates));
            }

            foreach (var group in mismatched.GroupBy(m => m.Pile.PileSpacingFactor))
            {
                var list = group.ToList();
                double lo = list.Min(m => m.Candidates.Min()), hi = list.Max(m => m.Candidates.Max());
                string who = "杭 No." + string.Join("・", list.Take(10).Select(m => m.Pile.No))
                           + (list.Count > 10 ? $" ほか {list.Count - 10} 本" : "");
                string geometric = Math.Abs(hi - lo) < 0.05 ? $"{lo:0.##}" : $"{lo:0.##}〜{hi:0.##}";
                notices.Add(Diagnostic.Notice(DiagnosticSeverity.Warning, DiagnosticTarget.Pile(list[0].Pile.No),
                    $"{who}: 杭間隔比 R/B = {group.Key:0.###} が、杭配置から求めた中心間隔 ÷ 杭径 ({geometric}) と "
                    + $"{SpacingRatioTolerance:P0} を超えて違います。R/B は後方杭の塑性水平地盤反力 py に効き、大きく入れると群杭の影響を小さく見ます。",
                    "杭配置の杭間隔比 R/B を、荷重方向の杭の中心間隔 ÷ 杭径 にする") with
                {
                    MoreTargets = list.Skip(1).Select(m => DiagnosticTarget.Pile(m.Pile.No)).ToList(),
                });
            }
            return notices;
        }
    }
}
