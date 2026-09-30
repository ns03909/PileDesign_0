using PileDesign.Common;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>番号の参照の検査の結果。</summary>
    /// <param name="NeedsReview">手で選び直す必要があるもの (杭が指す杭体・地盤が無いなど)。</param>
    /// <param name="Repairable">作り直せば直るもの (土層-杭セット・土質点の層番号など、入力から作る派生データ)。</param>
    public sealed record ReferenceIntegrityReport(IReadOnlyList<Diagnostic> NeedsReview, IReadOnlyList<Diagnostic> Repairable)
    {
        public bool IsClean => NeedsReview.Count == 0 && Repairable.Count == 0;

        public IEnumerable<Diagnostic> All => NeedsReview.Concat(Repairable);

        /// <summary>知らせる文 (問題が無ければ null)。直し方の違う 2 つを分けて書く。</summary>
        public string? Describe()
        {
            if (IsClean) return null;
            var lines = new List<string> { "入力の番号の参照に食い違いがあります。グラフ・計算書・解析で、その杭を扱えません。" };
            if (NeedsReview.Count > 0)
            {
                lines.Add("");
                lines.Add("■ 選び直しが必要なもの (杭配置の杭体・地盤の欄で選び直してください):");
                lines.AddRange(NeedsReview.Take(10).Select(d => "・" + d.Message));
                if (NeedsReview.Count > 10) lines.Add($"…ほか {NeedsReview.Count - 10} 件");
            }
            if (Repairable.Count > 0)
            {
                lines.Add("");
                lines.Add("■ 作り直せば直るもの (入力から作る派生データです。要素分割のやり直し・地盤の画面の確定で作り直されます):");
                lines.AddRange(Repairable.Take(10).Select(d => "・" + d.Message));
                if (Repairable.Count > 10) lines.Add($"…ほか {Repairable.Count - 10} 件");
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// 杭配置・土層-杭セット・杭体・地盤のあいだの番号の参照を確かめる。ファイルを開いた直後に行う。
    ///
    /// <para>以前は解析の前にしか確かめておらず、保存データの食い違いや削除したあとの古い番号は、
    /// グラフや計算書を開いたときに初めて「インデックスが範囲外です」で落ちた。読み込んだ時点で、
    /// どの杭がどの番号を指しているかを知らせる。データは書き換えない (直し方は利用者が選ぶ)。</para>
    /// </summary>
    public static class ReferenceIntegrity
    {
        /// <summary>選び直す候補 (いまある番号と名前。多ければ先頭の 5 つ)。候補が無ければ空。</summary>
        internal static string DescribeCandidates(IEnumerable<string?>? names, string kind)
        {
            var list = (names ?? []).Select((n, i) => string.IsNullOrWhiteSpace(n) ? $"{kind}{i + 1}" : $"{kind}{i + 1} {n}").ToList();
            if (list.Count == 0) return "";
            return " 選び直す候補: " + string.Join("・", list.Take(5)) + (list.Count > 5 ? $" ほか {list.Count - 5} 個" : "") + "。";
        }

        public static ReferenceIntegrityReport Check(InputModel? input)
        {
            var needsReview = new List<Diagnostic>();
            var repairable = new List<Diagnostic>();
            if (input == null) return new(needsReview, repairable);

            var soilPiles = input.ElementDivision?.SoilPiles;
            bool hasSoilPiles = soilPiles != null && soilPiles.Count > 0;

            foreach (var pile in input.PileLayoutItems ?? [])
            {
                if (pile == null) continue;
                bool bodyOk = input.PileBodyAt(pile.PileBodyNo) != null;
                bool groundOk = input.GroundAt(pile.GroundNo) != null;
                // 自動では付け替えない (誤った杭体・地盤に結び付ける恐れがある)。選び直す候補を示し、利用者が選ぶ
                if (!bodyOk)
                    needsReview.Add(Diagnostic.Input(DiagnosticTarget.Pile(pile.No, pile.PileBodyNo),
                        $"杭 No.{pile.No}: 杭体番号 {pile.PileBodyNo} の杭体がありません (杭体は {input.PileBodies?.Count ?? 0} 個)。"
                        + DescribeCandidates(input.PileBodies?.Select(b => b?.PileBodyRef), "杭体")));
                if (!groundOk)
                    needsReview.Add(Diagnostic.Input(DiagnosticTarget.Pile(pile.No),
                        $"杭 No.{pile.No}: 地盤番号 {pile.GroundNo} の地盤がありません (地盤は {input.GroundsInput?.Count ?? 0} 個)。"
                        + DescribeCandidates(input.GroundsInput?.Select(g => g?.GroundRef), "地盤")));

                // 土層-杭セットとの対応は、セットを作ったあとだけ見る (作る前は対応が無いのが普通)
                if (!hasSoilPiles || !bodyOk || !groundOk) continue;
                var soilPile = pile.SoilPileAt(input);
                if (soilPile == null)
                    repairable.Add(Diagnostic.Input(DiagnosticTarget.Pile(pile.No),
                        $"杭 No.{pile.No}: 土層-杭セットとの対応がありません (番号 {pile.SoilPileAltNo}、セットは {soilPiles!.Count} 個)。"));
                else if (soilPile.PileBodyNo != pile.PileBodyNo || soilPile.GroundNo != pile.GroundNo)
                    repairable.Add(Diagnostic.Input(DiagnosticTarget.Pile(pile.No),
                        $"杭 No.{pile.No}: 土層-杭セットが別の杭体・地盤を指しています "
                        + $"(杭は杭体{pile.PileBodyNo}・地盤{pile.GroundNo}、セットは杭体{soilPile.PileBodyNo}・地盤{soilPile.GroundNo})。"));
            }

            if (hasSoilPiles)
            {
                for (int i = 0; i < soilPiles!.Count; i++)
                {
                    var sp = soilPiles[i];
                    if (sp == null) continue;
                    if (input.PileBodyAt(sp.PileBodyNo) == null)
                        repairable.Add(Diagnostic.Input(DiagnosticTarget.PileBody(sp.PileBodyNo),
                            $"土層-杭セット {i + 1}: 杭体番号 {sp.PileBodyNo} の杭体がありません。"));
                    if (input.GroundAt(sp.GroundNo) == null)
                        repairable.Add(Diagnostic.Input(DiagnosticTarget.Ground(sp.GroundNo),
                            $"土層-杭セット {i + 1}: 地盤番号 {sp.GroundNo} の地盤がありません。"));
                }
            }

            // 杭体の区間。杭が使う杭体に区間が 1 つも無いと、杭の長さも断面も決まらない
            var usedBodies = (input.PileLayoutItems ?? []).Where(p => p != null).Select(p => p.PileBodyNo).Distinct().OrderBy(n => n);
            foreach (int bodyNo in usedBodies)
            {
                if (input.PileBodyAt(bodyNo) is { } body && (body.PileBodySegments?.Count ?? 0) == 0)
                    needsReview.Add(Diagnostic.Input(DiagnosticTarget.PileBody(bodyNo), $"杭体{bodyNo}: 区間が 1 つもありません。杭体の入力画面で区間を足してください。"));
            }

            if (hasSoilPiles)
            {
                for (int i = 0; i < soilPiles!.Count; i++)
                {
                    var sp = soilPiles[i];
                    if (sp == null) continue;
                    // 土層-杭セットの節点が指す杭体の区間番号 (要素分割で振る派生データ)
                    int segments = input.PileBodyAt(sp.PileBodyNo)?.PileBodySegments?.Count ?? -1;
                    if (segments >= 0 && sp.ZDataItems?.Any(z => z?.SegmentNo is int s && (s < 1 || s > segments)) == true)
                        repairable.Add(Diagnostic.Input(DiagnosticTarget.PileBody(sp.PileBodyNo),
                            $"土層-杭セット {i + 1}: 杭体{sp.PileBodyNo} に無い区間番号を指す節点があります (区間は {segments} 個)。"));

                    // kh0 の手入力は土層名で引く。地盤に同じ名前の土層が無いと、手入力が黙って効かない
                    var layerNames = input.GroundAt(sp.GroundNo)?.GroundLayers?.Where(l => l != null).Select(l => l.Name).ToHashSet();
                    if (layerNames == null) continue;
                    foreach (var o in sp.Kh0LayerOverrides ?? [])
                    {
                        if (o != null && !string.IsNullOrEmpty(o.LayerName) && !layerNames.Contains(o.LayerName))
                            needsReview.Add(Diagnostic.Input(DiagnosticTarget.Ground(sp.GroundNo),
                                $"土層-杭セット {i + 1}: kh0 の手入力「{o.LayerName}」に当たる土層が地盤{sp.GroundNo}にないため、この手入力は効いていません。"
                                + "要素分割の画面で入れ直すか、土層名を確かめてください。"));
                    }
                }
            }

            // 土質点が属する土層の番号 (地盤の画面の更新で付け直す派生データ)
            for (int g = 0; g < (input.GroundsInput?.Count ?? 0); g++)
            {
                var ground = input.GroundsInput![g];
                if (ground?.GroundMassesData == null) continue;
                int layers = ground.GroundLayers?.Count ?? 0;
                int bad = ground.GroundMassesData.Count(m => m?.LayerNo is int l && (l < 1 || l > layers));
                if (bad > 0)
                    repairable.Add(Diagnostic.Input(DiagnosticTarget.Ground(g + 1),
                        $"地盤{g + 1}: 土質点 {bad} 点が、無い土層の番号を指しています (土層は {layers} 層)。"
                        + "地盤の入力画面を開いて OK で閉じると付け直されます。"));
            }

            var embedment = input.EmbedmentInput;
            if (embedment != null && embedment.EmbedmentLayersCount > 0 && input.GroundAt(embedment.GroundNo) == null)
                needsReview.Add(Diagnostic.Input(DiagnosticTarget.Embedment(),
                    $"根入部: 地盤番号 {embedment.GroundNo} の地盤がありません (地盤は {input.GroundsInput?.Count ?? 0} 個)。根入部の地盤を選び直してください。"));

            return new(needsReview, repairable);
        }
    }
}
