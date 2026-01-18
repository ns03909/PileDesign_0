using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PileDesignCore.Shared
{
    /// <summary>
    /// 杭種別に応じた選択肢のマッピングを管理するヘルパークラス
    /// </summary>
    public static class PileTypeOptionsMapper
    {
        // 杭種別の定数
        public const string InsituReinforcedConcrete = "場所打ち鉄筋コンクリート杭";
        public const string InsituSteelPipedConcrete = "場所打ち鋼管コンクリート杭";
        public const string PrecastConcrete = "既製コンクリート杭";
        public const string SteelPipe = "鋼管杭";

        // 工法種別
        public const string InsituConstruction = "場所打ちコンクリート杭";

        // 接合工法
        public const string RebarAnchorage = "鉄筋定着工法";
        public const string CaptainPile = "キャプテンパイル工法";
        public const string FTPile = "FT-Pile構法";

        /// <summary>
        /// 工法選択肢のマッピング
        /// </summary>
        private static readonly Dictionary<string, ObservableCollection<string>> ConstructionTypeOptions = new Dictionary<string, ObservableCollection<string>>
        {
            { InsituReinforcedConcrete, new ObservableCollection<string> { InsituConstruction } },
            { InsituSteelPipedConcrete, new ObservableCollection<string> { InsituConstruction } },
            { PrecastConcrete, new ObservableCollection<string>
                {
                    "埋込み杭（プレボーリング）",
                    "埋込み杭（中掘り）",
                    "打込み杭"
                }
            },
            { SteelPipe, new ObservableCollection<string>
                {
                    "埋込み杭（プレボーリング）",
                    "埋込み杭（中掘り）",
                    "回転貫入杭"
                }
            }
        };

        /// <summary>
        /// 杭頭接合選択肢のマッピング
        /// </summary>
        private static readonly Dictionary<string, ObservableCollection<string>> PileTopTypeOptions = new Dictionary<string, ObservableCollection<string>>
        {
            { InsituReinforcedConcrete, new ObservableCollection<string>
                {
                    RebarAnchorage,
                    CaptainPile
                }
            },
            { InsituSteelPipedConcrete, new ObservableCollection<string> { RebarAnchorage } },
            { PrecastConcrete, new ObservableCollection<string>
                {
                    RebarAnchorage,
                    FTPile
                }
            },
            { SteelPipe, new ObservableCollection<string> { RebarAnchorage } }
        };

        /// <summary>
        /// 杭種別に対応するデフォルトの工法を取得
        /// </summary>
        private static readonly Dictionary<string, string> DefaultConstructionTypes = new Dictionary<string, string>
        {
            { InsituReinforcedConcrete, InsituConstruction },
            { InsituSteelPipedConcrete, InsituConstruction },
            { PrecastConcrete, "埋込み杭（プレボーリング）" },
            { SteelPipe, "埋込み杭（プレボーリング）" }
        };

        /// <summary>
        /// 杭種別に対応するデフォルトの杭頭接合方法を取得
        /// </summary>
        private static readonly Dictionary<string, string> DefaultPileTopTypes = new Dictionary<string, string>
        {
            { InsituReinforcedConcrete, RebarAnchorage },
            { InsituSteelPipedConcrete, RebarAnchorage },
            { PrecastConcrete, RebarAnchorage },
            { SteelPipe, RebarAnchorage }
        };

        /// <summary>
        /// 指定した杭種別に対応する工法選択肢を取得
        /// </summary>
        public static ObservableCollection<string> GetConstructionTypeOptions(string pileBodyType)
        {
            if (string.IsNullOrEmpty(pileBodyType))
                return new ObservableCollection<string>();

            return ConstructionTypeOptions.TryGetValue(pileBodyType, out var options)
                ? options
                : new ObservableCollection<string>();
        }

        /// <summary>
        /// 指定した杭種別に対応する杭頭接合選択肢を取得
        /// </summary>
        public static ObservableCollection<string> GetPileTopTypeOptions(string pileBodyType)
        {
            if (string.IsNullOrEmpty(pileBodyType))
                return new ObservableCollection<string>();

            return PileTopTypeOptions.TryGetValue(pileBodyType, out var options)
                ? options
                : new ObservableCollection<string>();
        }

        /// <summary>
        /// 指定した杭種別に対応するデフォルトの工法を取得
        /// </summary>
        public static string GetDefaultConstructionType(string pileBodyType)
        {
            if (string.IsNullOrEmpty(pileBodyType))
                return string.Empty;

            return DefaultConstructionTypes.TryGetValue(pileBodyType, out var defaultValue)
                ? defaultValue
                : string.Empty;
        }

        /// <summary>
        /// 指定した杭種別に対応するデフォルトの杭頭接合方法を取得
        /// </summary>
        public static string GetDefaultPileTopType(string pileBodyType)
        {
            if (string.IsNullOrEmpty(pileBodyType))
                return string.Empty;

            return DefaultPileTopTypes.TryGetValue(pileBodyType, out var defaultValue)
                ? defaultValue
                : string.Empty;
        }

        /// <summary>
        /// 杭種別が変更されたときの選択肢とデフォルト値を一括更新
        /// </summary>
        public static PileTypeSelectionUpdate UpdateOptionsForPileBodyType(string pileBodyType)
        {
            return new PileTypeSelectionUpdate
            {
                ConstructionTypeOptions = GetConstructionTypeOptions(pileBodyType),
                DefaultConstructionType = GetDefaultConstructionType(pileBodyType),
                PileTopTypeOptions = GetPileTopTypeOptions(pileBodyType),
                DefaultPileTopType = GetDefaultPileTopType(pileBodyType)
            };
        }
    }

    /// <summary>
    /// 杭種別変更時の更新情報を保持するクラス
    /// </summary>
    public class PileTypeSelectionUpdate
    {
        public ObservableCollection<string> ConstructionTypeOptions { get; set; }
        public string DefaultConstructionType { get; set; }
        public ObservableCollection<string> PileTopTypeOptions { get; set; }
        public string DefaultPileTopType { get; set; }
    }
}
