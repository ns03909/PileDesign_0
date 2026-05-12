using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Collections.Generic;

namespace PileDesign.Output
{
    // docx 出力中のみ使用するローカル結果検索キャッシュ。
    // Beam.GetBeamResult / Node.GetNodeResult / AnaModel.GetAnalysisLastStep は
    // いずれも List を線形走査 + 文字列比較で対応する結果を探すため、
    // ホットループから多数回呼ぶと N×M 級のコストになる。
    // 出力中は解析結果が不変であることを利用し、開始時に Dictionary を 1 回構築して O(1) 検索する。
    internal partial class WordDocument
    {
        // (LoadCase.LoadName, LoadCombination.Name, IsLiquefaction) → 最大 step
        private Dictionary<(string?, string?, bool), int>? _lastStepCache;

        // (Beam, LoadCase.LoadName, LoadCombination.Name, IsLiquefaction, step) → BeamResult
        private Dictionary<(Beam, string?, string?, bool, int), BeamResult>? _beamResultCache;

        // (Node, LoadCase.LoadName, LoadCombination.Name, IsLiquefaction, step) → NodeResult
        private Dictionary<(Node, string?, string?, bool, int), NodeResult>? _nodeResultCache;

        private void BuildResultLookupCaches()
        {
            if (anaModel == null) return;

            _lastStepCache = new Dictionary<(string?, string?, bool), int>();
            foreach (var r in anaModel.AnalysisStepResults)
            {
                var key = (r.LoadCase?.LoadName, r.LoadCombination?.Name, r.IsLiquefaction);
                if (_lastStepCache.TryGetValue(key, out int existing))
                {
                    if (r.Step > existing) _lastStepCache[key] = r.Step;
                }
                else
                {
                    _lastStepCache[key] = r.Step;
                }
            }

            _beamResultCache = new Dictionary<(Beam, string?, string?, bool, int), BeamResult>();
            if (anaModel.Beams != null)
            {
                foreach (var beam in anaModel.Beams)
                {
                    if (beam?.BeamResults == null) continue;
                    foreach (var br in beam.BeamResults)
                    {
                        var key = (beam, br.LoadCase?.LoadName, br.LoadCombination?.Name, br.IsLiquefaction, br.Step);
                        _beamResultCache[key] = br;
                    }
                }
            }

            _nodeResultCache = new Dictionary<(Node, string?, string?, bool, int), NodeResult>();
            if (anaModel.Nodes != null)
            {
                foreach (var node in anaModel.Nodes)
                {
                    if (node?.NodeResults == null) continue;
                    foreach (var nr in node.NodeResults)
                    {
                        var key = (node, nr.LoadCase?.LoadName, nr.LoadCombination?.Name, nr.IsLiquefaction, nr.Step);
                        _nodeResultCache[key] = nr;
                    }
                }
            }
        }

        private int GetLastStepCached(LoadCase? lc, LoadCombination? comb, bool isLiq)
        {
            if (_lastStepCache == null)
                return anaModel?.GetAnalysisLastStep(lc!, comb!, isLiq) ?? -1;

            if (_lastStepCache.TryGetValue((lc?.LoadName, comb?.Name, isLiq), out int step))
                return step;
            if (_lastStepCache.TryGetValue((lc?.LoadName, comb?.Name, !isLiq), out int fallback))
                return fallback;
            return -1;
        }

        private BeamResult? GetBeamResultCached(Beam beam, LoadCase? lc, LoadCombination? comb, bool isLiq, int step = -1)
        {
            if (_beamResultCache == null || _lastStepCache == null)
                return beam.GetBeamResult(anaModel!, lc!, comb!, isLiq, step);

            if (step == -1)
                step = GetLastStepCached(lc, comb, isLiq);
            if (step < 0) return null;

            if (_beamResultCache.TryGetValue((beam, lc?.LoadName, comb?.Name, isLiq, step), out var br))
                return br;

            int fallbackStep = GetLastStepCached(lc, comb, !isLiq);
            if (fallbackStep < 0) return null;
            if (_beamResultCache.TryGetValue((beam, lc?.LoadName, comb?.Name, !isLiq, fallbackStep), out var br2))
                return br2;
            return null;
        }

        private NodeResult? GetNodeResultCached(Node node, LoadCase? lc, LoadCombination? comb, bool isLiq, int step = -1)
        {
            if (_nodeResultCache == null || _lastStepCache == null)
                return node.GetNodeResult(anaModel!, lc!, comb!, isLiq, step);

            if (step == -1)
                step = GetLastStepCached(lc, comb, isLiq);
            if (step < 0) return null;

            if (_nodeResultCache.TryGetValue((node, lc?.LoadName, comb?.Name, isLiq, step), out var nr))
                return nr;

            int fallbackStep = GetLastStepCached(lc, comb, !isLiq);
            if (fallbackStep < 0) return null;
            if (_nodeResultCache.TryGetValue((node, lc?.LoadName, comb?.Name, !isLiq, fallbackStep), out var nr2))
                return nr2;
            return null;
        }
    }
}
