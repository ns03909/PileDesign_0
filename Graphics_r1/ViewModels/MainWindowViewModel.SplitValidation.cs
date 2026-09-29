using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;

namespace PileDesign.ViewModels;

public partial class MainWindowViewModel
{
    internal bool EditDistanceInputHasError { get; set; }

    internal void ObserveEditDistanceInputValidation(object? sender, ValidationErrorEventArgs e)
    {
        if (sender is TextBox field) EditDistanceInputHasError = Validation.GetHasError(field);
    }

    private static bool IsFinitePosition(Point3D p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);

    private bool RejectSplit(string message)
    {
        MessageService.Show(message, "分割入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool ValidateSplitBeams(IEnumerable<FoundationBeam> beams)
    {
        foreach (var beam in beams)
        {
            int number = CurrentInputModel.FoundationBeamInput.Beams.IndexOf(beam) + 1;
            var i = GetNodeAttachPosition(beam.NodeI_Type, beam.NodeI_Id);
            var j = GetNodeAttachPosition(beam.NodeJ_Type, beam.NodeJ_Id);
            if (!i.HasValue || !j.HasValue)
                return RejectSplit($"梁 {number} の端点 {(!i.HasValue ? "I" : "J")} の参照先が存在しません。");
            double lengthSquared = (j.Value - i.Value).LengthSquared;
            if (!IsFinitePosition(i.Value) || !IsFinitePosition(j.Value) || !double.IsFinite(lengthSquared) || lengthSquared < 1e-18)
                return RejectSplit($"梁 {number} の座標または長さが不正です。有限の座標と正の長さが必要です。");
        }
        return true;
    }

    private bool CommitSplitEdit(out InputModel before)
    {
        before = null!;
        if (!ConfirmDiscardInvalidatedByInputChange(true, "梁分割")) return false;
        before = CaptureInputEdit();
        return true;
    }
}
