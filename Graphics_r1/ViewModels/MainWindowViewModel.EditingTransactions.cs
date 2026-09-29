using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using PileDesign.Services;

namespace PileDesign.ViewModels;

public partial class MainWindowViewModel
{
    // Callers prepare/validate derived values before passing an action that updates the live model.
    internal bool TryApplyInputEdit(bool hasChanges, Action edit, bool confirmElementSplit = false,
        AnalysisInputScope scope = AnalysisInputScope.All,
        [System.Runtime.CompilerServices.CallerMemberName] string? description = null)
    {
        if (!hasChanges) return false;
        if (confirmElementSplit && !ConfirmDiscardInvalidatedByInputChange(true)) return false;
        var before = CaptureInputEdit();
        edit();
        CompleteInputEdit(before, description, scope);
        return true;
    }

    internal InputModel CaptureInputEdit()
    {
        if (_undoBatchActive) FlushPendingUndoSnapshot();
        using var perf = PileDesign.Common.PerfLog.Measure("編集の控え (変更前)", CurrentInputModel?.PileLayoutItems?.Count ?? 0, "本の杭");
        return CurrentInputModel.DeepCopy();
    }

    internal void CompleteInputEdit(InputModel before,
        [System.Runtime.CompilerServices.CallerMemberName] string? description = null, AnalysisInputScope scope = AnalysisInputScope.All)
    {
        using var perf = PileDesign.Common.PerfLog.Measure("編集の確定", CurrentInputModel?.PileLayoutItems?.Count ?? 0, "本の杭");
        _undoManager.SaveSnapshotEdit(before, CurrentInputModel.DeepCopy(), FormatHistoryDescription(description));
        MarkUnsavedWork();
        InputEditVersion++;
        MarkInputChangedSinceAnalysis(scope, FormatHistoryDescription(description));
        RaiseUndoStateChanged();
        RequestUpdateWindow();
    }

    private bool ValidatePanelValue(PropertyPanelItem item, double value)
    {
        bool valid = double.IsFinite(value);
        var piles = CurrentInputModel.PileLayoutItems?.Where(p => p.IsSelected).ToArray() ?? [];
        if (item.Name == "群杭係数 ξ") valid &= PileGroupFactor.IsValidFactor(value);
        foreach (var pile in piles)
        {
            if (item.Name == "ΔZc") valid &= double.IsFinite(pile.Z - value);
            if (item.Name == "Z") valid &= double.IsFinite(value - pile.FoundationBeamDeltaZc);
            if (item.Name == "軸力 VL")
                valid &= PileLayoutService.DescribeBulkEditProblem([pile], new PileLayoutService.BulkEditOptions
                { ApplyAxialForceVL = true, AxialForceVL = value }) == null;
            else if (item.Name.StartsWith("ΔN ", StringComparison.Ordinal)) valid &= double.IsFinite(value + pile.AxialForceVL);
            else if (item.Name.StartsWith("軸力 ", StringComparison.Ordinal)) valid &= double.IsFinite(value - pile.AxialForceVL);
        }
        if (!valid) MessageService.Show("入力値が許容範囲外です。値は変更しません。");
        return valid;
    }

    public int EmbedmentGroundNo
    {
        get => CurrentInputModel?.EmbedmentInput?.GroundNo ?? 1;
        set
        {
            if (value == EmbedmentGroundNo) return;
            if (value < 1 || value > (CurrentInputModel.GroundsInput?.Count ?? 0)) throw new ArgumentOutOfRangeException(nameof(value));
            if (!ConfirmDiscardInvalidatedByInputChange(true)) { OnPropertyChanged(); return; }
            var before = CaptureInputEdit();
            CurrentInputModel.EmbedmentInput.GroundNo = value;
            CompleteInputEdit(before);
            OnPropertyChanged();
        }
    }

    public double EmbedmentBottomAltitude
    {
        get => CurrentInputModel?.EmbedmentInput?.BottomAltitude ?? 0;
        set
        {
            if (value == EmbedmentBottomAltitude) return;
            var alt = value;
            foreach (var layer in CurrentInputModel.EmbedmentInput.EmbedmentLayers.Reverse())
            {
                alt += layer.LayerThickness;
                if (!double.IsFinite(alt)) throw new ArgumentOutOfRangeException(nameof(value), "根入部の標高が数値の範囲外です。");
            }
            if (!double.IsFinite(value) || !double.IsFinite(alt)) throw new ArgumentOutOfRangeException(nameof(value), "根入部の標高が数値の範囲外です。");
            if (!ConfirmDiscardInvalidatedByInputChange(true)) { OnPropertyChanged(); return; }
            var before = CaptureInputEdit();
            CurrentInputModel.EmbedmentInput.BottomAltitude = value;
            UpdateEmbedment();
            CompleteInputEdit(before);
            OnPropertyChanged();
        }
    }

    public int EmbedmentLayerCount
    {
        get => CurrentInputModel?.EmbedmentInput?.EmbedmentLayersCount ?? 0;
        set
        {
            var input = CurrentInputModel.EmbedmentInput;
            if (value == EmbedmentLayerCount) return;
            if (value < 0 || value > 5) throw new ArgumentOutOfRangeException(nameof(value));
            // Prepare the complete layer list before changing the live model.
            var planned = PileDesign.Common.DeepCopyUtil.CloneJson(input);
            while (planned.EmbedmentLayers.Count > value) planned.EmbedmentLayers.RemoveAt(planned.EmbedmentLayers.Count - 1);
            while (planned.EmbedmentLayers.Count < value) planned.EmbedmentLayers.Add(planned.CreateNewEmbedmentDataItem(planned.EmbedmentLayers.Count));
            double alt = input.BottomAltitude;
            foreach (var layer in planned.EmbedmentLayers.Reverse())
            {
                layer.BottomAltitude = alt;
                alt += layer.LayerThickness;
                if (!double.IsFinite(alt)) throw new ArgumentOutOfRangeException(nameof(value), "根入部の標高が数値の範囲外です。");
                layer.TopAltitude = alt;
            }
            if (!ConfirmDiscardInvalidatedByInputChange(true)) { OnPropertyChanged(); return; }
            var before = CaptureInputEdit();
            input.EmbedmentLayers = planned.EmbedmentLayers;
            input.EmbedmentLayersCount = value;
            IsEmbedmentBoxVisible = true;
            CompleteInputEdit(before);
            OnPropertyChanged();
        }
    }

    private void NotifyEmbedmentInputs()
    {
        OnPropertyChanged(nameof(EmbedmentGroundNo));
        OnPropertyChanged(nameof(EmbedmentBottomAltitude));
        OnPropertyChanged(nameof(EmbedmentLayerCount));
    }

    // Deleted pile links become unlinked; surviving links continue to refer to the same pile.
    private void RemapRemainingPileLinks(Dictionary<Guid, (int No, int PileNo)> oldNumbers)
    {
        var noMap = new Dictionary<int, int>();
        var pileNoMap = new Dictionary<int, int>();
        for (int i = 0; i < CurrentInputModel.PileLayoutItems.Count; i++)
            if (oldNumbers.TryGetValue(CurrentInputModel.PileLayoutItems[i].UniqueId, out var old))
            {
                noMap[old.No] = i + 1;
                pileNoMap[old.PileNo] = i + 1;
            }
        foreach (var node in CurrentInputModel.InputNodes ?? [])
            if (node.LinkedPileNo is int oldNo)
            {
                if (noMap.TryGetValue(oldNo, out int newNo)) node.LinkedPileNo = newNo;
                else
                {
                    node.LinkedPileNo = null;
                    if (node.Type == NodeType.Pile) node.Type = NodeType.General;
                }
            }
        foreach (var load in CurrentInputModel.PileGroupSettlement?.RectLoads ?? [])
            if (load.LinkedPileNo > 0)
                load.LinkedPileNo = pileNoMap.GetValueOrDefault(load.LinkedPileNo, 0);
    }
}
