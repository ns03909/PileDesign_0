using CommunityToolkit.Mvvm.ComponentModel;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// F.T.Pile 構法の FT キャップを選んでいない杭体を見つけ、選んだキャップを反映する。
    ///
    /// キャップを選ばないとキャップの杭径 Phi が 0 のままで、支圧係数 φc が 0 になり、杭頭の M-θ と
    /// 許容回転角が数値にならない (検定は「判定できない」になる)。解析の前に知らせ、その場で選べるようにする。
    /// </summary>
    public static class FTCapSelection
    {
        /// <summary>FT キャップを選んでいない F.T.Pile 構法の杭体ごとの行。杭径に合うキャップを最初から選んでおく。</summary>
        public static List<FTCapSelectionRow> FindUnselected(InputModel? inputModel)
        {
            var rows = new List<FTCapSelectionRow>();
            var bodies = inputModel?.PileBodies;
            if (bodies == null) return rows;
            for (int i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                var ft = body?.PileTop?.FTPile;
                if (ft == null || !(body!.PileTopType ?? "").Contains("FT-Pile構法") || !ft.IsCapUnselected()) continue;
                double diameter = body.PileBodySegments?.FirstOrDefault()?.PileSection?.PileDiameter ?? 0.0;
                var options = (ft.FTCaps ?? []).Where(c => c != null && c.Phi > 0).Select(c => c.Phi).ToList();
                var suggested = ft.CapForPileDiameter(diameter);
                rows.Add(new FTCapSelectionRow(i, $"杭体 {i + 1}", diameter, options)
                {
                    SelectedPhi = suggested?.Phi,
                });
            }
            return rows;
        }

        /// <summary>
        /// 選んだキャップを杭体へ反映する。杭頭部の画面でキャップを選んだときと同じく、杭の寸法を杭断面から入れてから選ぶ。
        /// 反映した杭体の数を返す (キャップを選んでいない行は飛ばす)。
        /// </summary>
        public static int Apply(InputModel inputModel, IEnumerable<FTCapSelectionRow> rows)
        {
            int applied = 0;
            foreach (var row in rows)
            {
                if (row.SelectedPhi is not double phi) continue;
                var body = row.PileBodyIndex >= 0 && row.PileBodyIndex < inputModel.PileBodies.Count ? inputModel.PileBodies[row.PileBodyIndex] : null;
                var ft = body?.PileTop?.FTPile;
                if (ft == null) continue;
                var section = body!.PileBodySegments?.FirstOrDefault()?.PileSection;
                if (section != null) ft.SetDimensionsFromSection(section.PileDiameter, section.ConcreteThickness);
                if (!ft.SelectCap(phi)) continue;
                body.PileTop!.SelectedPileTopSpecification = ft.GetCombinedSpecs();
                applied++;
            }
            return applied;
        }
    }

    /// <summary>FT キャップを選ぶ画面の 1 行 (杭体 1 つ)。</summary>
    public partial class FTCapSelectionRow(int pileBodyIndex, string pileBodyName, double pileDiameter, IReadOnlyList<double> options)
        : ObservableObject
    {
        public int PileBodyIndex { get; } = pileBodyIndex;
        public string PileBodyName { get; } = pileBodyName;
        /// <summary>杭径 [mm] (杭頭の区間の断面)。</summary>
        public double PileDiameter { get; } = pileDiameter;
        /// <summary>選べるキャップの杭径 [mm]。</summary>
        public IReadOnlyList<double> Options { get; } = options;

        /// <summary>選んだキャップの杭径 [mm]。未選択は null。</summary>
        [ObservableProperty]
        private double? _selectedPhi;

        /// <summary>選んだキャップの杭径が杭径と違うか (画面で知らせる)。</summary>
        public bool DiffersFromPileDiameter => SelectedPhi is double phi && System.Math.Abs(phi - PileDiameter) >= 0.5;

        partial void OnSelectedPhiChanged(double? value) => OnPropertyChanged(nameof(DiffersFromPileDiameter));

        /// <summary>画面の読み上げ (UI Automation の名前) に杭体名を出す (既定ではクラス名になる)。</summary>
        public override string ToString() => PileBodyName;
    }
}
