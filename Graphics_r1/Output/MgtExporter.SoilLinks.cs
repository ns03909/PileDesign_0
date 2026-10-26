using System;
using System.Collections.Generic;
using System.Linq;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;

namespace PileDesign.Output
{
    // MGT に書くばね (弾性連結) を、ばねの種類ごとに作る partial。
    //
    // 解析モデルは水平地盤ばね・杭の鉛直ばね・土圧合力ばねを同じ一覧 (AnaModel.HorizontalSoilSprings) に入れている。
    // 以前の出力はこれを全部「X 方向の杭の水平地盤ばね」として書いていたので、
    //   - 杭の鉛直ばね (Z 方向の剛性だけを持つ) が X 方向の力 0 のばねになり、
    //   - 土圧合力ばねも、反力の情報が引けずに「その時点の剛性 (解析前は 0)」で書かれ、
    //   - 杭の水平地盤ばねは、節点の片側の要素の分しか反力に入っていなかった (解析は上下両側の要素を足す)。
    // いまはばねの持ち主 (杭・土圧合力の区間) から種類と方向を決め、力は解析 (HorizontalCalculationViewModel.PrepareKmat) と
    // 同じ式 (割線剛性 × 変位) で作る。どの種類にも当てはまらないばねがあれば、理由を示して出力を止める。
    public partial class MgtExporter
    {
        private enum LinkDirection { X, Y, Z }

        /// <summary>荷重-変形関係を持つ 1 本の弾性連結 (MULTI LINEAR)。</summary>
        private sealed class SoilLink
        {
            public required string Label { get; init; }
            public required Node PileSide { get; init; }
            /// <summary>地盤側の節点 (X 方向: 解析モデルの地盤節点。Y・Z 方向: 仮想節点)。</summary>
            public required int GroundNodeId { get; init; }
            public required LinkDirection Direction { get; init; }
            /// <summary>荷重-変形関係 (変形 m → 力 kN)。<see cref="Symmetric"/> なら正側だけ。</summary>
            public required (double Disp, double Force)[] Curve { get; init; }
            public required bool Symmetric { get; init; }
            /// <summary>地盤変位を与えるときの地盤の値 (杭周・根入部)。無ければ与えない (鉛直ばね)。</summary>
            public ZDataItem? GroundDisplacement { get; init; }
            public int FunctionId { get; set; }
        }

        /// <summary>ばねの地盤側に置く仮想節点 (6 自由度固定)。</summary>
        private readonly record struct VirtualNode(int Id, double X, double Y, double Z);

        /// <summary>杭頭の回転ばね (キャップ節点と杭頭節点の GEN 連結)。</summary>
        private sealed class PileHeadLink
        {
            public required RotationalSpring Spring { get; init; }
            public required double KTheta { get; init; }
        }

        // 杭の水平地盤ばね・土圧合力ばねの変形のサンプリング点 (m)。正側だけ (原点対称)
        private static readonly double[] MultiLinearDispSamples =
        [
            0.0, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0
        ];

        // 杭の鉛直ばねの沈下のサンプリング点 (m)。押込み (正) と引抜き (負) で違うので両側
        private static readonly double[] VerticalDispSamples =
        [
            0.0, 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2
        ];

        /// <summary>剛体相当の剛性 (杭頭の回転を剛とするとき。解析の SetupNonlinearMThetaForLoadCase の KBig と同じ)。</summary>
        private const double RigidRotation = 1e10;
        /// <summary>杭頭の回転ばねが並進・ねじりを拘束するペナルティ剛性 (解析の PrepareKmat と同じ)。</summary>
        private const double PileHeadTie = 1e8;

        /// <summary>
        /// 地盤ばね 1 組を作るときに使う荷重ケース。MGT は荷重ケースごとにばねを変えられないので、
        /// 作用点を置いている「レベル1 の最初の荷重ケース」(<see cref="AnalysisModelling"/> の代表点と同じ) の状態で作る。
        /// </summary>
        private LoadCase? RepresentativeCase => _anaModel.InputModel?.LoadCasesInput?.LoadCasesLevel1?.FirstOrDefault();

        private sealed class LinkPlan
        {
            public List<SoilLink> SoilLinks { get; } = [];
            public List<VirtualNode> VirtualNodes { get; } = [];
            public List<PileHeadLink> PileHeads { get; } = [];
            /// <summary>荷重-変形関数 (同じ曲線は 1 つにまとめる)。</summary>
            public List<((double Disp, double Force)[] Curve, bool Symmetric)> Functions { get; } = [];
            /// <summary>杭の鉛直ばねを持つ杭 (軸力の荷重ケースを書く)。</summary>
            public List<PileLayoutDataItem> PilesWithVerticalSprings { get; } = [];
            /// <summary>MGT のモデルが解析と違うところ (出力前の確認とファイルの先頭に書く)。</summary>
            public List<string> Approximations { get; } = [];
        }

        private LinkPlan BuildLinkPlan(Dictionary<Node, int> nodeIdMap, ref int nextNodeId)
        {
            var plan = new LinkPlan();
            var input = _anaModel.InputModel;
            var rep = RepresentativeCase;
            var mode = rep?.SoilNonlinearityMode ?? SoilNonlinearityMode.KhReductionWithPy;
            bool hasY = HasYDirectionAnalysis();
            var problems = new List<string>();
            var classified = new HashSet<HorizontalSoilSpring>(ReferenceEqualityComparer.Instance);
            int idCounter = nextNodeId;

            int VirtualAt(Node pileSide, double dx, double dy, double dz)
            {
                int id = idCounter++;
                plan.VirtualNodes.Add(new VirtualNode(id, pileSide.Coord.X + dx, pileSide.Coord.Y + dy, pileSide.Coord.Z + dz));
                return id;
            }

            string? NodeProblem(Node? n, string side)
                => n == null ? $"{side}の節点がありません"
                 : !nodeIdMap.ContainsKey(n) ? $"{side}の節点 ({NodeLabel(n)}) が解析モデルの節点にありません"
                 : null;

            void AddHorizontal(HorizontalSoilSpring s, string label, Func<double, double> forceX, Func<double, double> forceY, ZDataItem? ground)
            {
                classified.Add(s);
                var p = NodeProblem(s.NodeI, "杭側") ?? NodeProblem(s.NodeJ, "地盤側");
                if (p != null) { problems.Add($"・{label}: {p}"); return; }
                plan.SoilLinks.Add(new SoilLink
                {
                    Label = label, PileSide = s.NodeI, GroundNodeId = nodeIdMap[s.NodeJ], Direction = LinkDirection.X,
                    Curve = Sample(MultiLinearDispSamples, forceX), Symmetric = true, GroundDisplacement = ground,
                });
                // Y 方向を解析していなければ Y 方向のばねは書かない (地盤変位も Y 成分が 0)
                if (hasY)
                    plan.SoilLinks.Add(new SoilLink
                    {
                        Label = label, PileSide = s.NodeI, GroundNodeId = VirtualAt(s.NodeI, 0, SoilNodeOffsetY, 0),
                        Direction = LinkDirection.Y, Curve = Sample(MultiLinearDispSamples, forceY), Symmetric = true,
                        GroundDisplacement = ground,
                    });
            }

            // ── 杭の水平地盤ばね・鉛直ばね ──
            var frontDiffers = new List<int>();
            var headNotes = new List<int>();
            var classifiedHeads = new HashSet<RotationalSpring>(ReferenceEqualityComparer.Instance);
            var piles = input?.PileLayoutItems ?? [];
            foreach (var pile in piles)
            {
                if (pile == null) continue;
                var soilPile = pile.SoilPileAt(input);
                var reactions = soilPile?.HorizontalSoilReactions;
                var pileNodes = pile.PileNodes;
                bool isFront = rep != null && pile.IsFrontFor(rep);
                if (AnalyzedCases().Any(lc => pile.IsFrontFor(lc) != isFront)) frontDiffers.Add(pile.No);
                var effect = GroupPileEffect.For(pile);   // 液状化なし (βL = 1)
                int n = reactions?.Count ?? 0;

                for (int i = 0; i < pile.HorizontalSoilSprings.Count; i++)
                {
                    var s = pile.HorizontalSoilSprings[i];
                    if (s == null) continue;
                    int nodeIndex = i, nodeCount = pileNodes.Count;
                    // 解析 (PrepareKmat) と同じ: 節点の上の要素 (isTop: false) と下の要素 (isTop: true) の割線剛性の和 × 変位
                    double Force(double y)
                    {
                        double abs = Math.Abs(y), k = 0;
                        if (nodeIndex > 0 && nodeIndex - 1 < n)
                            k += reactions![nodeIndex - 1].GetSoilSecantReactionCoefficient(abs, isTop: false, isFront, effect, mode);
                        if (nodeIndex < nodeCount - 1 && nodeIndex < n)
                            k += reactions![nodeIndex].GetSoilSecantReactionCoefficient(abs, isTop: true, isFront, effect, mode);
                        k = SafeK(k);
                        if (k <= 0.0 && nodeIndex == 0) k = 1.0e-3;   // 解析と同じ最上端の下限
                        return k * y;
                    }
                    var ground = soilPile != null && i < soilPile.ZDataItems.Count ? soilPile.ZDataItems[i] : null;
                    AddHorizontal(s, $"杭 No.{pile.No} の水平地盤ばね{SpringLabel(s, i)}", Force, Force, ground);
                }

                for (int k = 0; k < pile.VerticalNodeSprings.Count; k++)
                {
                    var s = pile.VerticalNodeSprings[k];
                    if (s == null) continue;
                    classified.Add(s);
                    string label = $"杭 No.{pile.No} の鉛直ばね{SpringLabel(s, k)}";
                    var model = k < pile.VerticalNodeSpringModels.Count ? pile.VerticalNodeSpringModels[k] : null;
                    var curve = k < pile.VerticalNodeSpringCurves.Count ? pile.VerticalNodeSpringCurves[k] : null;
                    var p = NodeProblem(s.NodeI, "杭側")
                            ?? (model == null && curve == null ? "沈下の曲線がありません" : null);
                    if (p != null) { problems.Add($"・{label}: {p}"); continue; }
                    // 解析 (PrepareKmat) と同じ割線剛性 × 沈下。沈下 (下向き) が正
                    double Force(double settlement)
                    {
                        double kz;
                        if (model != null)
                        {
                            kz = model.GetSecantStiffness(settlement);
                            if (!(double.IsFinite(kz) && kz > 0)) kz = Math.Max(model.InitialTangentStiffness * 1e-6, 1e-3);
                        }
                        else
                        {
                            kz = curve!.GetSecantStiffness(settlement);
                            if (!(double.IsFinite(kz) && kz > 0)) kz = Math.Max(curve.InitialTangentStiffness * 0.01, 1.0);
                        }
                        kz = SafeK(kz);
                        if (kz <= 0.0) kz = 1.0;
                        return kz * settlement;
                    }
                    // 地盤側の仮想節点は杭の節点の 1 m 上に置く。杭が沈下するとばねが伸びる (変形が正) 向きになるので、
                    // 変形 = 沈下、力 = 杭を押し上げる反力 (正) で書ける
                    var both = VerticalDispSamples.Skip(1).Reverse().Select(d => -d).Concat(VerticalDispSamples).ToArray();
                    plan.SoilLinks.Add(new SoilLink
                    {
                        Label = label, PileSide = s.NodeI, GroundNodeId = VirtualAt(s.NodeI, 0, 0, SoilNodeOffsetZ),
                        Direction = LinkDirection.Z, Curve = Sample(both, Force), Symmetric = false,
                    });
                }
                if (pile.VerticalNodeSprings.Count > 0) plan.PilesWithVerticalSprings.Add(pile);

                // ── 杭頭の回転ばね ──
                var head = pile.PileTopRotationalSpring;
                if (head != null)
                {
                    classifiedHeads.Add(head);
                    string label = $"杭 No.{pile.No} の杭頭の回転ばね";
                    var p = NodeProblem(head.NodeI, "キャップ側") ?? NodeProblem(head.NodeJ, "杭頭側");
                    var (kTheta, note, unsupported) = RepresentativeHeadRotation(pile, rep);
                    if (p != null) problems.Add($"・{label}: {p}");
                    else if (unsupported != null) problems.Add($"・{label}: {unsupported}");
                    else
                    {
                        plan.PileHeads.Add(new PileHeadLink { Spring = head, KTheta = kTheta });
                        if (note != null) headNotes.Add(pile.No);
                    }
                }
            }

            // ── 土圧合力ばね ──
            var dgb = input?.ElementDivision?.DoatsuGoryokuBane;
            if (dgb != null && input!.ElementDivision.HasEarthPressureSprings())
            {
                var contributions = new Dictionary<HorizontalSoilSpring, List<DoatsuGoryokuBaneItem>>(ReferenceEqualityComparer.Instance);
                var order = new List<HorizontalSoilSpring>();
                void Add(HorizontalSoilSpring? s, DoatsuGoryokuBaneItem item)
                {
                    if (s == null) return;
                    if (!contributions.TryGetValue(s, out var list)) { contributions[s] = list = []; order.Add(s); }
                    list.Add(item);
                }
                foreach (var item in dgb.Items)
                {
                    Add(item.TopHorizontalSoilSpring, item);
                    Add(item.BtmHorizontalSoilSpring, item);
                }
                var embedmentZ = input.ElementDivision.SoilEmbedment?.ZDataItems ?? [];
                for (int i = 0; i < order.Count; i++)
                {
                    var s = order[i];
                    var items = contributions[s];
                    // 解析 (PrepareKmat) と同じ: この節点を上端・下端に持つ区間の割線剛性 (区間の半分) の和 × 変位。X と Y で受ける面が違う
                    double ForceX(double u) => items.Sum(it => SafeK(it.GetSecantStiffnessVector(new NodeDisp(u, 0, 0, 0, 0, 0)).Kx)) * u;
                    double ForceY(double u) => items.Sum(it => SafeK(it.GetSecantStiffnessVector(new NodeDisp(0, u, 0, 0, 0, 0)).Ky)) * u;
                    double z = s.NodeJ?.Coord.Z ?? double.NaN;
                    var ground = embedmentZ.FirstOrDefault(zd => Math.Abs(zd.Z - z) < NumericalConstants.COORDINATE_TOLERANCE);
                    AddHorizontal(s, $"土圧合力ばね (Z = {z:0.###})", ForceX, ForceY, ground);
                }
            }

            // ── どの種類にも当てはまらないばね ──
            var all = _anaModel.HorizontalSoilSprings ?? [];
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null) problems.Add($"・ばね #{i + 1}: ばねがありません (空の要素)");
                else if (!classified.Contains(s))
                    problems.Add($"・ばね{SpringLabel(s, i)}: 杭の水平地盤ばね・鉛直ばね・土圧合力ばねのどれでもなく、向きと荷重-変形関係が決まりません");
            }
            var heads = _anaModel.RotationalSprings ?? [];
            for (int i = 0; i < heads.Count; i++)
                if (heads[i] == null || !classifiedHeads.Contains(heads[i]))
                    problems.Add($"・回転ばね{SpringLabel(heads[i], i)}: どの杭の杭頭のばねでもなく、剛性が決まりません");
            ThrowIfCannotWrite("ばね", "個", problems, "ばねの欠けたモデル (地盤ばね・杭頭の連結が無い) になります");

            // 同じ荷重-変形関係は 1 つの関数にまとめる (杭の X・Y 方向は同じ曲線)
            var functionIds = new Dictionary<string, int>();
            foreach (var link in plan.SoilLinks)
            {
                string key = (link.Symmetric ? "S|" : "N|") + string.Join(";", link.Curve.Select(c => $"{c.Disp:R},{c.Force:R}"));
                if (!functionIds.TryGetValue(key, out int fid))
                {
                    plan.Functions.Add((link.Curve, link.Symmetric));
                    fid = plan.Functions.Count;
                    functionIds[key] = fid;
                }
                link.FunctionId = fid;
            }

            CollectApproximations(plan, rep, mode, hasY, frontDiffers, headNotes);
            nextNodeId = idCounter;
            return plan;
        }

        private static (double Disp, double Force)[] Sample(double[] disps, Func<double, double> force)
            => disps.Select(d => (d, d == 0 ? 0.0 : force(d))).ToArray();

        private static double SafeK(double v) => (double.IsFinite(v) && v > 0.0) ? v : 0.0;

        /// <summary>解析した荷重ケース (結果があるもの)。解析していなければ空。</summary>
        private IEnumerable<LoadCase> AnalyzedCases()
            => (_anaModel.AnalysisStepResults ?? []).Select(r => r.LoadCase).Where(lc => lc != null && lc.LoadName != "VL")
                .Distinct(ReferenceEqualityComparer.Instance).Cast<LoadCase>();

        /// <summary>
        /// 代表の荷重ケース (<see cref="RepresentativeCase"/>) での杭頭の回転剛性 (kN·m/rad)。
        /// 解析 (SetupNonlinearMThetaForLoadCase) と同じ条件で剛・M-θ を選び、M-θ のときは常時軸力 (VL) での初期剛性を返す
        /// (note に印)。解析は荷重ケースの軸力ごとに曲線を作り、ひび割れ・降伏で剛性が下がる。
        /// </summary>
        private (double K, string? Note, string? Unsupported) RepresentativeHeadRotation(PileLayoutDataItem pile, LoadCase? rep)
        {
            var body = _anaModel.InputModel?.PileBodyAt(pile.PileBodyNo);
            if (body == null) return (RigidRotation, null, null);
            bool selectable = body.HasSelectablePileHeadRotation();
            bool useHeadRotation = selectable
                ? (_anaModel.InputModel?.FundamentalInput?.ConsiderPileHeadRotation ?? true)
                : (rep?.IsPileNonLinear ?? false);
            if (!useHeadRotation) return (RigidRotation, null, null);

            var def = body.GetMThetaRelationship(pile.AxialForceVL);
            switch (def.Mode)
            {
                case PileHeadRotationMode.Rigid:
                    return (RigidRotation, null, null);
                case PileHeadRotationMode.CombinedXY:
                    if (def.CurveXY == null) return (def.KthetaXY is > 0 ? def.KthetaXY.Value : 1e-6, null, null);
                    // ひび割れモーメントがあれば、ひび割れるまでは剛 (解析と同じ)
                    double k = def.McrXY.HasValue ? RigidRotation
                        : def.KthetaXY is > 0 ? def.KthetaXY.Value
                        : Math.Max(Math.Max(def.CurveXY.EvaluateTangent(1e-6), 0.0), 1e-6);
                    return (k, "M-θ", null);
                default:
                    return (0, null, "X・Y 軸で別々の M-θ を持つ杭頭は MGT に書けません");
            }
        }

        private void CollectApproximations(LinkPlan plan, LoadCase? rep, SoilNonlinearityMode mode, bool hasY,
            List<int> frontDiffers, List<int> headNotes)
        {
            var notes = plan.Approximations;
            string repName = rep == null ? "(荷重ケースなし)" : $"レベル1「{rep.LoadName}」";
            var analyzed = AnalyzedCases().ToList();
            bool anyLiquefaction = (_anaModel.AnalysisStepResults ?? []).Any(r => r.IsLiquefaction);

            notes.Add($"地盤ばねは荷重ケースごとに変えられないため、液状化を考慮しない状態 (βL = 1)・{repName} の前方杭/後方杭・"
                      + $"地盤非線形「{ModeName(mode)}」の 1 組で書きます。");
            if (anyLiquefaction)
                notes.Add("液状化を考慮したケースも、地盤ばねは液状化を考慮しない値です (地盤変位は液状化の値を与えます)。");
            var otherModes = analyzed.Select(lc => lc.SoilNonlinearityMode).Where(m => m != mode).Distinct().ToList();
            if (otherModes.Count > 0)
                notes.Add($"地盤非線形の扱いが違う荷重ケースがあります ({string.Join("・", otherModes.Select(ModeName))})。");
            if (frontDiffers.Count > 0)
                notes.Add($"前方杭/後方杭の判定が荷重ケースで変わる杭があります (杭 No.{string.Join(", ", frontDiffers)})。");
            if (hasY && plan.SoilLinks.Any(l => l.Direction == LinkDirection.Y))
                notes.Add("X・Y 方向の地盤ばねは別々のばねです。解析は X・Y を合わせた変位で剛性を決めます。");
            if (SeismicAxialForces.IsSeparated(_anaModel.InputModel))
                notes.Add("地震時軸力を分離入力 (VL + βL·ΔL(L) + βU·ΔL(U)) にしています。荷重ケースの杭の軸力は組合せごとに変えられないため、"
                          + "組合せのうち最大 (圧縮側) の値を書きます。");
            if (headNotes.Count > 0)
                notes.Add($"杭頭の回転ばねは、常時軸力 (VL) での M-θ の初期剛性 (ひび割れ前) の線形ばねです (杭 No.{string.Join(", ", headNotes)})。"
                          + "解析は荷重ケースの軸力ごとの M-θ で、ひび割れ・降伏で剛性が下がります。");
            if (analyzed.Any(lc => lc.IsPileNonLinear))
                notes.Add("杭体は弾性の梁です。解析で杭体の非線形 (M-φ) を考慮した荷重ケースの結果とは一致しません。");
        }

        private static string ModeName(SoilNonlinearityMode m) => m switch
        {
            SoilNonlinearityMode.Linear => "線形",
            SoilNonlinearityMode.KhReduction => "kh 低減のみ",
            _ => "kh 低減 + py 頭打ち",
        };
    }
}
