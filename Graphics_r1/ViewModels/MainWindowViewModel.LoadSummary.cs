using PileDesign.Models.InputData;
using System.Linq;
using System.Windows.Media.Media3D;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel — 鉛直力の合計と転倒モーメント。
    ///
    /// 画面上部に出す集計値。杭配置の軸力から、レベル・方向ごとの合計と、
    /// VL+VLadd の重心まわりの転倒モーメントを求める。すべて算出プロパティで、
    /// 状態は持たない（入力が変われば読み直すたびに変わる）。
    ///
    /// 以前は <c>MainWindowViewModel.Constructor.cs</c> の中にあった。
    /// </summary>
    public partial class MainWindowViewModel
    {
        // VL0合計
        public double SumVL0 => GetSumVL0();

        // VLadd合計
        public double SumVLadd => GetSumVLadd();

        // VL+VLadd合計
        public double SumVL => GetSumVL0() + GetSumVLadd();

        // VL重心
        public Point3D GravityCenterVL0 => CurrentInputModel.GetVLGravityCenter();

        // VLadd重心
        public Point3D GravityCenterVLadd => CurrentInputModel.GetVLaddGravityCenter();

        // VL+VLadd重心
        public Point3D GravityCenterVLPlusVLadd => CurrentInputModel.GetVLplusVLaddGravityCenter();

        private double GetSumVL0()
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceVL0;
            return sum;
        }


        private double GetSumVLadd()
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceVLAdditional;
            return sum;
        }

        // sum（get専用の計算プロパティに変更）
        public double Sum1_1 => GetSumLevel1(1);
        public double Sum1_2 => GetSumLevel1(2);
        public double Sum1_3 => GetSumLevel1(3);
        public double Sum1_4 => GetSumLevel1(4);

        public double Sum2_1 => GetSumLevel2(1);
        public double Sum2_2 => GetSumLevel2(2);
        public double Sum2_3 => GetSumLevel2(3);
        public double Sum2_4 => GetSumLevel2(4);

        // 合計計算（null/空に強い）
        private double GetSumLevel1(int no)
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceLevel1s[no - 1];
            return sum;
        }

        private double GetSumLevel2(int no)
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceLevel2s[no - 1];
            return sum;
        }

        // OTM（get専用の計算プロパティに変更）
        public double OverturningMoment1_1X => GetOverturningMoment(level: 1, dir: 1, axis: 'X');
        public double OverturningMoment1_2X => GetOverturningMoment(level: 1, dir: 2, axis: 'X');
        public double OverturningMoment1_3X => GetOverturningMoment(level: 1, dir: 3, axis: 'X');
        public double OverturningMoment1_4X => GetOverturningMoment(level: 1, dir: 4, axis: 'X');

        public double OverturningMoment1_1Y => GetOverturningMoment(level: 1, dir: 1, axis: 'Y');
        public double OverturningMoment1_2Y => GetOverturningMoment(level: 1, dir: 2, axis: 'Y');
        public double OverturningMoment1_3Y => GetOverturningMoment(level: 1, dir: 3, axis: 'Y');
        public double OverturningMoment1_4Y => GetOverturningMoment(level: 1, dir: 4, axis: 'Y');

        public double OverturningMoment2_1X => GetOverturningMoment(level: 2, dir: 1, axis: 'X');
        public double OverturningMoment2_2X => GetOverturningMoment(level: 2, dir: 2, axis: 'X');
        public double OverturningMoment2_3X => GetOverturningMoment(level: 2, dir: 3, axis: 'X');
        public double OverturningMoment2_4X => GetOverturningMoment(level: 2, dir: 4, axis: 'X');

        public double OverturningMoment2_1Y => GetOverturningMoment(level: 2, dir: 1, axis: 'Y');
        public double OverturningMoment2_2Y => GetOverturningMoment(level: 2, dir: 2, axis: 'Y');
        public double OverturningMoment2_3Y => GetOverturningMoment(level: 2, dir: 3, axis: 'Y');
        public double OverturningMoment2_4Y => GetOverturningMoment(level: 2, dir: 4, axis: 'Y');

        // OTM計算ヘルパー（回転中心はVL+VLadd重心を採用）
        private double GetOverturningMoment(int level, int dir, char axis)
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            // 回転中心（必要に応じて 0,0 に変更可）
            var pivot = GravityCenterVLPlusVLadd;

            double sum = 0.0;
            foreach (var item in items)
            {
                // レベル/方向別の鉛直力成分
                double f = level == 1
                    ? item.AxialForceLevel1s[dir - 1]
                    : item.AxialForceLevel2s[dir - 1];

                // X回りはY距離、Y回りはX距離
                if (axis == 'X')
                    sum += f * (item.Y - pivot.Y);
                else
                    sum += f * (item.X - pivot.X);
            }
            return sum;
        }
    }
}
