using MathNet.Numerics.LinearAlgebra;
using System.Collections.Generic;

namespace PileDesign.FEM
{
    /// <summary>
    /// Phase 1 (2026-05-06): step-level cut-back retry のための end-of-step スナップショット。
    /// 1 ステップが収束した直後に <see cref="Capture"/> で AnaModel の path-dependent な状態を
    /// クローン保持し、後続ステップで失敗した場合に <see cref="Restore"/> で end-of-step k に
    /// 巻き戻して、failed step だけサブステップ化して再試行する用途。
    ///
    /// 保存対象:
    ///   - AnaModel: VectorD / VectorDD / VectorF / VectorDF / VectorR
    ///   - Node: 累積/増分 Disp / Reaction / SpringForce / SoilDisp / Load / ForcedDisp
    ///   - Beam: 累積/増分 Disp / Force
    ///   - HorizontalSoilSpring (継承先): 累積/増分 Disp / Force
    ///   - RotationalSpring (継承先): 累積/増分 Disp / Force, post-crack 方向ロック (HasCrackedXY/CrackNx/CrackNy/ThetaProjMax)
    /// 保存しないもの:
    ///   - 接線剛性 (TangentSpring/SecantSpring/KeTan/KeSec) — 現在変位から再計算可能なため
    ///   - InputModel 配下のデータ — ケース内不変
    /// </summary>
    public sealed class StepCheckpoint
    {
        // ── AnaModel レベルのベクトル ──
        public Vector<double>? VectorD;
        public Vector<double>? VectorDD;
        public Vector<double>? VectorF;
        public Vector<double>? VectorDF;
        public Vector<double>? VectorR;

        // ── Node ごとの累積/増分量 ──
        public NodeSnapshot[] Nodes = System.Array.Empty<NodeSnapshot>();

        // ── Beam ごとの累積/増分量 ──
        public BeamSnapshot[] Beams = System.Array.Empty<BeamSnapshot>();

        // ── HorizontalSoilSpring ごとの累積/増分量 ──
        public BeamSnapshot[] HorizontalSoilSprings = System.Array.Empty<BeamSnapshot>();

        // ── RotationalSpring ごとの累積/増分量 + post-crack 状態 ──
        public RotSpringSnapshot[]? RotationalSprings;

        // ── ステップ番号 (デバッグ/ロギング用) ──
        public int StepIndex;

        public struct NodeSnapshot
        {
            public NodeDisp? IncrementalDisp;
            public NodeDisp? CumulativeDisp;
            public NodeReaction? IncrementalReaction;
            public NodeReaction? CumulativeReaction;
            public NodeLoad? IncrementalLoad;
            public NodeLoad? CumulativedLoad;
            public NodeLoad? IncrementalSpringForce;
            public NodeLoad? CumulativeSpringForce;
            public NodeDisp? SoilDisp;
            public NodeDisp? SoilDispIncrement;
            public NodeDisp? IncrementalForcedDisp;
            public NodeDisp? CumulativeForcedDisp;
        }

        public struct BeamSnapshot
        {
            public BeamDisp? IncrementalDisp;
            public BeamDisp? CumulativeDisp;
            public BeamForce? IncrementalForce;
            public BeamForce? CumulativeForce;
        }

        public struct RotSpringSnapshot
        {
            public BeamDisp? IncrementalDisp;
            public BeamDisp? CumulativeDisp;
            public BeamForce? IncrementalForce;
            public BeamForce? CumulativeForce;
            public bool HasCrackedXY;
            public double? CrackNx;
            public double? CrackNy;
            public double ThetaProjMax;
        }

        /// <summary>
        /// 指定 AnaModel の現在状態をスナップショットとしてクローン保持する。
        /// </summary>
        public static StepCheckpoint Capture(AnaModel model, int stepIndex)
        {
            var cp = new StepCheckpoint
            {
                StepIndex = stepIndex,
                VectorD = model.VectorD?.Clone(),
                VectorDD = model.VectorDD?.Clone(),
                VectorF = model.VectorF?.Clone(),
                VectorDF = model.VectorDF?.Clone(),
                VectorR = model.VectorR?.Clone(),
                Nodes = new NodeSnapshot[model.Nodes.Count],
                Beams = new BeamSnapshot[model.Beams.Count],
                HorizontalSoilSprings = new BeamSnapshot[model.HorizontalSoilSprings.Count],
            };

            for (int i = 0; i < model.Nodes.Count; i++)
            {
                var n = model.Nodes[i];
                cp.Nodes[i] = new NodeSnapshot
                {
                    IncrementalDisp = n.IncrementalDisp?.Clone(),
                    CumulativeDisp = n.CumulativeDisp?.Clone(),
                    IncrementalReaction = n.IncrementalReaction?.Clone(),
                    CumulativeReaction = n.CumulativeReaction?.Clone(),
                    IncrementalLoad = n.IncrementalLoad?.Clone(),
                    CumulativedLoad = n.CumulativedLoad?.Clone(),
                    IncrementalSpringForce = n.IncrementalSpringForce?.Clone(),
                    CumulativeSpringForce = n.CumulativeSpringForce?.Clone(),
                    SoilDisp = n.SoilDisp?.Clone(),
                    SoilDispIncrement = n.SoilDispIncrement?.Clone(),
                    IncrementalForcedDisp = n.IncrementalForcedDisp?.Clone(),
                    CumulativeForcedDisp = n.CumulativeForcedDisp?.Clone(),
                };
            }

            for (int i = 0; i < model.Beams.Count; i++)
            {
                var b = model.Beams[i];
                cp.Beams[i] = new BeamSnapshot
                {
                    IncrementalDisp = b.IncrementalDisp?.Clone(),
                    CumulativeDisp = b.CumulativeDisp?.Clone(),
                    IncrementalForce = b.IncrementalForce?.Clone(),
                    CumulativeForce = b.CumulativeForce?.Clone(),
                };
            }

            for (int i = 0; i < model.HorizontalSoilSprings.Count; i++)
            {
                var s = model.HorizontalSoilSprings[i];
                cp.HorizontalSoilSprings[i] = new BeamSnapshot
                {
                    IncrementalDisp = s.IncrementalDisp?.Clone(),
                    CumulativeDisp = s.CumulativeDisp?.Clone(),
                    IncrementalForce = s.IncrementalForce?.Clone(),
                    CumulativeForce = s.CumulativeForce?.Clone(),
                };
            }

            if (model.RotationalSprings != null)
            {
                cp.RotationalSprings = new RotSpringSnapshot[model.RotationalSprings.Count];
                for (int i = 0; i < model.RotationalSprings.Count; i++)
                {
                    var r = model.RotationalSprings[i];
                    cp.RotationalSprings[i] = new RotSpringSnapshot
                    {
                        IncrementalDisp = r.IncrementalDisp?.Clone(),
                        CumulativeDisp = r.CumulativeDisp?.Clone(),
                        IncrementalForce = r.IncrementalForce?.Clone(),
                        CumulativeForce = r.CumulativeForce?.Clone(),
                        HasCrackedXY = r.HasCrackedXY,
                        CrackNx = r.CrackNx,
                        CrackNy = r.CrackNy,
                        ThetaProjMax = r.ThetaProjMax,
                    };
                }
            }

            return cp;
        }

        /// <summary>
        /// スナップショットの内容を AnaModel に書き戻す。要素数が一致しない場合は例外。
        /// </summary>
        public void Restore(AnaModel model)
        {
            if (model.Nodes.Count != Nodes.Length)
                throw new System.InvalidOperationException(
                    $"StepCheckpoint.Restore: Node count mismatch (model={model.Nodes.Count}, checkpoint={Nodes.Length})");
            if (model.Beams.Count != Beams.Length)
                throw new System.InvalidOperationException(
                    $"StepCheckpoint.Restore: Beam count mismatch (model={model.Beams.Count}, checkpoint={Beams.Length})");
            if (model.HorizontalSoilSprings.Count != HorizontalSoilSprings.Length)
                throw new System.InvalidOperationException(
                    $"StepCheckpoint.Restore: HorizontalSoilSpring count mismatch (model={model.HorizontalSoilSprings.Count}, checkpoint={HorizontalSoilSprings.Length})");

            // AnaModel ベクトル (private set のため AnaModel 側ヘルパー経由で復元)
            model.RestoreFEMVectors(
                VectorD?.Clone(), VectorDD?.Clone(),
                VectorF?.Clone(), VectorDF?.Clone(),
                VectorR?.Clone());

            // Node
            for (int i = 0; i < Nodes.Length; i++)
            {
                var s = Nodes[i];
                var n = model.Nodes[i];
                n.IncrementalDisp = s.IncrementalDisp?.Clone() ?? new NodeDisp(0, 0, 0, 0, 0, 0);
                n.CumulativeDisp = s.CumulativeDisp?.Clone() ?? new NodeDisp(0, 0, 0, 0, 0, 0);
                n.IncrementalReaction = s.IncrementalReaction?.Clone() ?? new NodeReaction(0, 0, 0, 0, 0, 0);
                n.CumulativeReaction = s.CumulativeReaction?.Clone() ?? new NodeReaction(0, 0, 0, 0, 0, 0);
                n.IncrementalLoad = s.IncrementalLoad?.Clone() ?? new NodeLoad(0, 0, 0, 0, 0, 0);
                n.CumulativedLoad = s.CumulativedLoad?.Clone() ?? new NodeLoad(0, 0, 0, 0, 0, 0);
                n.IncrementalSpringForce = s.IncrementalSpringForce?.Clone() ?? new NodeLoad(0, 0, 0, 0, 0, 0);
                n.CumulativeSpringForce = s.CumulativeSpringForce?.Clone() ?? new NodeLoad(0, 0, 0, 0, 0, 0);
                n.SoilDisp = s.SoilDisp?.Clone() ?? new NodeDisp(0, 0, 0, 0, 0, 0);
                n.SoilDispIncrement = s.SoilDispIncrement?.Clone() ?? new NodeDisp(0, 0, 0, 0, 0, 0);
                n.IncrementalForcedDisp = s.IncrementalForcedDisp?.Clone() ?? new NodeDisp(0, 0, 0, 0, 0, 0);
                n.CumulativeForcedDisp = s.CumulativeForcedDisp?.Clone() ?? new NodeDisp(0, 0, 0, 0, 0, 0);
            }

            // Beam
            for (int i = 0; i < Beams.Length; i++)
            {
                var s = Beams[i];
                var b = model.Beams[i];
                if (s.IncrementalDisp != null) b.IncrementalDisp = s.IncrementalDisp.Clone();
                if (s.CumulativeDisp != null) b.CumulativeDisp = s.CumulativeDisp.Clone();
                if (s.IncrementalForce != null) b.IncrementalForce = s.IncrementalForce.Clone();
                if (s.CumulativeForce != null) b.CumulativeForce = s.CumulativeForce.Clone();
            }

            // HorizontalSoilSpring
            for (int i = 0; i < HorizontalSoilSprings.Length; i++)
            {
                var s = HorizontalSoilSprings[i];
                var hs = model.HorizontalSoilSprings[i];
                if (s.IncrementalDisp != null) hs.IncrementalDisp = s.IncrementalDisp.Clone();
                if (s.CumulativeDisp != null) hs.CumulativeDisp = s.CumulativeDisp.Clone();
                if (s.IncrementalForce != null) hs.IncrementalForce = s.IncrementalForce.Clone();
                if (s.CumulativeForce != null) hs.CumulativeForce = s.CumulativeForce.Clone();
            }

            // RotationalSpring
            if (RotationalSprings != null && model.RotationalSprings != null)
            {
                if (model.RotationalSprings.Count != RotationalSprings.Length)
                    throw new System.InvalidOperationException(
                        $"StepCheckpoint.Restore: RotationalSpring count mismatch (model={model.RotationalSprings.Count}, checkpoint={RotationalSprings.Length})");

                for (int i = 0; i < RotationalSprings.Length; i++)
                {
                    var s = RotationalSprings[i];
                    var rs = model.RotationalSprings[i];
                    if (s.IncrementalDisp != null) rs.IncrementalDisp = s.IncrementalDisp.Clone();
                    if (s.CumulativeDisp != null) rs.CumulativeDisp = s.CumulativeDisp.Clone();
                    if (s.IncrementalForce != null) rs.IncrementalForce = s.IncrementalForce.Clone();
                    if (s.CumulativeForce != null) rs.CumulativeForce = s.CumulativeForce.Clone();
                    rs.RestoreLockState(s.HasCrackedXY, s.CrackNx, s.CrackNy, s.ThetaProjMax);
                }
            }
        }
    }
}
