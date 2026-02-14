using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 杭配置の操作（移動、コピー、編集）を担当するサービスクラス
    /// </summary>
    public class PileLayoutService
    {
        /// <summary>
        /// 選択された杭を移動
        /// </summary>
        /// <param name="pileLayoutItems">杭配置アイテムのコレクション</param>
        /// <param name="deltaX">X方向の移動量</param>
        /// <param name="deltaY">Y方向の移動量</param>
        public void MoveSelectedPiles(ObservableCollection<PileLayoutDataItem> pileLayoutItems, double deltaX, double deltaY)
        {
            if (pileLayoutItems == null)
                throw new ArgumentNullException(nameof(pileLayoutItems));

            foreach (var pile in pileLayoutItems)
            {
                if (pile.IsSelected)
                {
                    pile.X += deltaX;
                    pile.Y += deltaY;
                    pile.IsSelected = false;
                }
            }
        }

        /// <summary>
        /// 選択された杭をコピー（同期版）
        /// </summary>
        /// <param name="pileLayoutItems">杭配置アイテムのコレクション</param>
        /// <param name="deltaX">X方向のコピー間隔</param>
        /// <param name="deltaY">Y方向のコピー間隔</param>
        /// <param name="repetitionNumber">繰り返し回数</param>
        /// <param name="viewModelSetter">MainWindowViewModelをセットするアクション</param>
        /// <returns>新しい杭を含む結合されたコレクション</returns>
        public ObservableCollection<PileLayoutDataItem> CopySelectedPiles(
            ObservableCollection<PileLayoutDataItem> pileLayoutItems,
            double deltaX,
            double deltaY,
            int repetitionNumber,
            Action<PileLayoutDataItem> viewModelSetter)
        {
            if (pileLayoutItems == null)
                throw new ArgumentNullException(nameof(pileLayoutItems));
            if (viewModelSetter == null)
                throw new ArgumentNullException(nameof(viewModelSetter));

            var selectedItems = pileLayoutItems.Where(p => p.IsSelected).ToList();
            int totalCount = selectedItems.Count * repetitionNumber;

            // 一時リストに事前にすべて作成（容量を事前確保して高速化）
            var newItems = new List<PileLayoutDataItem>(totalCount);

            foreach (var pile in selectedItems)
            {
                for (int i = 0; i < repetitionNumber; i++)
                {
                    var newItem = new PileLayoutDataItem
                    {
                        X = pile.X + deltaX * (i + 1),
                        Y = pile.Y + deltaY * (i + 1)
                    };
                    viewModelSetter(newItem);
                    newItems.Add(newItem);
                }
                pile.IsSelected = false;
            }

            // 既存アイテムと新規アイテムを結合した新しいコレクションを作成
            return new ObservableCollection<PileLayoutDataItem>(pileLayoutItems.Concat(newItems));
        }

        /// <summary>
        /// 選択された杭の属性を一括編集
        /// </summary>
        public class BulkEditOptions
        {
            public bool ApplyPileBodyNo { get; set; }
            public int PileBodyNo { get; set; }

            public bool ApplyGroundNo { get; set; }
            public int GroundNo { get; set; }

            public bool ApplyPileTopLevel { get; set; }
            public bool IsAddPileTopLevel { get; set; }
            public double PileTopLevel { get; set; }

            public bool ApplyFoundationBeamDeltaZc { get; set; }
            public bool IsAddFoundationBeamDeltaZc { get; set; }
            public double FoundationBeamDeltaZc { get; set; }

            public bool ApplyPileGroupFactor { get; set; }
            public bool IsAddPileGroupFactor { get; set; }
            public double PileGroupFactor { get; set; }

            public bool ApplyAxialForceVL { get; set; }
            public bool IsAddAxialForceVL { get; set; }
            public double AxialForceVL { get; set; }

            public bool ApplyAxialForceVLAdditional { get; set; }
            public bool IsAddAxialForceVLAdditional { get; set; }
            public double AxialForceVLAdditional { get; set; }

            // Level1 荷重 (E1_1 ~ E1_4)
            public bool[] ApplyLevel1 { get; set; } = new bool[4];
            public bool[] IsAddLevel1 { get; set; } = new bool[4];
            public double[] Level1Values { get; set; } = new double[4];

            // Level2 荷重 (E2_1 ~ E2_4)
            public bool[] ApplyLevel2 { get; set; } = new bool[4];
            public bool[] IsAddLevel2 { get; set; } = new bool[4];
            public double[] Level2Values { get; set; } = new double[4];
        }

        /// <summary>
        /// 選択された杭の属性を一括編集
        /// </summary>
        /// <param name="pileLayoutItems">杭配置アイテムのコレクション</param>
        /// <param name="options">編集オプション</param>
        public void BulkEditSelectedPiles(ObservableCollection<PileLayoutDataItem> pileLayoutItems, BulkEditOptions options)
        {
            if (pileLayoutItems == null)
                throw new ArgumentNullException(nameof(pileLayoutItems));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var selectedItems = pileLayoutItems.Where(p => p.IsSelected).ToList();

            // 杭体番号
            if (options.ApplyPileBodyNo)
            {
                foreach (var item in selectedItems)
                    item.PileBodyNo = options.PileBodyNo;
            }

            // 地盤番号
            if (options.ApplyGroundNo)
            {
                foreach (var item in selectedItems)
                    item.GroundNo = options.GroundNo;
            }

            // 杭頭レベル
            if (options.ApplyPileTopLevel)
            {
                foreach (var item in selectedItems)
                {
                    item.X = item.Point3D.X;
                    item.Y = item.Point3D.Y;
                    item.Z = options.IsAddPileTopLevel
                        ? item.Point3D.Z + options.PileTopLevel
                        : options.PileTopLevel;
                }
            }

            // 接合節点ΔZc
            if (options.ApplyFoundationBeamDeltaZc)
            {
                foreach (var item in selectedItems)
                {
                    item.FoundationBeamDeltaZc = options.IsAddFoundationBeamDeltaZc
                        ? item.FoundationBeamDeltaZc + options.FoundationBeamDeltaZc
                        : options.FoundationBeamDeltaZc;
                }
            }

            // 群杭係数
            if (options.ApplyPileGroupFactor)
            {
                foreach (var item in selectedItems)
                {
                    item.GroupPileFactor = options.IsAddPileGroupFactor
                        ? item.GroupPileFactor + options.PileGroupFactor
                        : options.PileGroupFactor;
                }
            }

            // 常時軸力 VL
            if (options.ApplyAxialForceVL)
            {
                foreach (var item in selectedItems)
                {
                    item.AxialForceVL0 = options.IsAddAxialForceVL
                        ? item.AxialForceVL0 + options.AxialForceVL
                        : options.AxialForceVL;
                }
            }

            // 付加軸力 VLadd
            if (options.ApplyAxialForceVLAdditional)
            {
                foreach (var item in selectedItems)
                {
                    item.AxialForceVLAdditional = options.IsAddAxialForceVLAdditional
                        ? item.AxialForceVLAdditional + options.AxialForceVLAdditional
                        : options.AxialForceVLAdditional;
                }
            }

            // Level1 荷重 (E1_1 ~ E1_4)
            for (int i = 0; i < 4; i++)
            {
                if (options.ApplyLevel1[i])
                {
                    foreach (var item in selectedItems)
                    {
                        item.AxialForceLevel1s[i] = options.IsAddLevel1[i]
                            ? item.AxialForceLevel1s[i] + options.Level1Values[i]
                            : options.Level1Values[i];
                    }
                }
            }

            // Level2 荷重 (E2_1 ~ E2_4)
            for (int i = 0; i < 4; i++)
            {
                if (options.ApplyLevel2[i])
                {
                    foreach (var item in selectedItems)
                    {
                        item.AxialForceLevel2s[i] = options.IsAddLevel2[i]
                            ? item.AxialForceLevel2s[i] + options.Level2Values[i]
                            : options.Level2Values[i];
                    }
                }
            }
        }
    }
}
