using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.Models.InputData
{
    // PileSection のうち、製品ライブラリ (CSV) の読込と、既製杭・鋼管杭の製品の選択と諸元の転記。
    public partial class PileSection
    {
        // 静的キャッシュ（CSVデータは一度だけ読み込む）
        // PHC杭 は JIS 汎用ライブラリに加え、メーカー製品ライブラリを連結する。
        // ストレート杭は断面の挙動が PHC杭 と完全に同じなので、断面タイプを増やさず
        // 同じ製品一覧に並べる (節杭のように形状・自重が違うものだけ断面タイプを分ける)。
        private static readonly Lazy<List<PrecastPile>> _cachedPHCs = new(() =>
            [.. LoadPrecastPilesFromCsv("pile_library_PHC.csv"),
             .. LoadPrecastPilesFromCsv("pile_library_PHC_MSHI105.csv")]);
        // PRC杭 も PHC杭 / SC杭 と同様、JIS 汎用ライブラリにメーカー製品ライブラリを連結する。
        private static readonly Lazy<List<PrecastPile>> _cachedPRCs = new(() =>
            [.. LoadPrecastPilesFromCsv("pile_library_PRC.csv"),
             .. LoadPrecastPilesFromCsv("pile_library_PRC_DAM105.csv")]);
        // SC杭 も PHC杭 と同様、JIS 汎用ライブラリにメーカー製品ライブラリを連結する。
        private static readonly Lazy<List<PrecastPile>> _cachedSCs = new(() =>
            [.. LoadPrecastPilesFromCsv("pile_library_SC.csv"),
             .. LoadPrecastPilesFromCsv("pile_library_SC_HISC105.csv")]);
        private static readonly Lazy<List<SteelPipePile>> _cachedSteelPipePiles = new(() => LoadSteelPipePilesFromCsv("pile_library_SteelPile.csv"));
        // 節杭は Do/質量など既製杭 DTO に無い列を持つため専用ローダーで読み、
        // 断面転記の直前に PrecastPile へ詰め替える (NodularPile.ToPrecastPile)。
        private static readonly Lazy<List<NodularPile>> _cachedNodularPiles = new(LoadNodularPiles);
        private static readonly Lazy<List<NodularPileHead>> _cachedNodularPileHeads = new(LoadNodularPileHeads);
        // PRC節杭は 1 製品が PRC部 / PHC部 の 2 断面を持つので、同じライブラリから
        // 詰め替え先を 2 系統作る (ToPrecastPile(phcPart:) の引数だけが違う)。
        private static readonly Lazy<List<NodularPrcPile>> _cachedNodularPrcPiles = new(LoadNodularPrcPiles);
        private static readonly Lazy<List<NodularPrcPileHead>> _cachedNodularPrcPileHeads = new(LoadNodularPrcPileHeads);
        // BF.S は 1 製品が頭部軸部 / 先端軸部 の 2 断面を持つ (外径・肉厚が違う)。
        private static readonly Lazy<List<BfsPile>> _cachedBfsPiles = new(LoadBfsPiles);

        // オプションリストも静的キャッシュ（一度だけ構築）
        private static readonly Lazy<ObservableCollection<string>> _cachedPHCOption = new(() =>
            new ObservableCollection<string>(_cachedPHCs.Value.Select(p => p.Name)));
        private static readonly Lazy<ObservableCollection<string>> _cachedPRCOption = new(() =>
            new ObservableCollection<string>(_cachedPRCs.Value.Select(p => p.Name)));
        private static readonly Lazy<ObservableCollection<string>> _cachedSCOption = new(() =>
            new ObservableCollection<string>(_cachedSCs.Value.Select(p => p.Name)));
        private static readonly Lazy<ObservableCollection<string>> _cachedSteelPipeOption = new(() =>
            new ObservableCollection<string>(_cachedSteelPipePiles.Value.Select(p => $"{p.Diameter}x{p.Thickness}")));
        private static readonly Lazy<ObservableCollection<string>> _cachedNodularPileOption = new(() =>
            new ObservableCollection<string>(_cachedNodularPiles.Value.Select(p => p.DisplayName)));
        private static readonly Lazy<ObservableCollection<string>> _cachedNodularPrcOption = new(() =>
            new ObservableCollection<string>(_cachedNodularPrcPiles.Value.Select(p => p.DisplayName)));
        private static readonly Lazy<ObservableCollection<string>> _cachedNodularPrcPhcPartOption = new(() =>
            new ObservableCollection<string>(_cachedNodularPrcPiles.Value.Select(p => p.PhcPartDisplayName)));
        private static readonly Lazy<ObservableCollection<string>> _cachedBfsHeadOption = new(() =>
            new ObservableCollection<string>(_cachedBfsPiles.Value.Select(p => p.DisplayName)));
        private static readonly Lazy<ObservableCollection<string>> _cachedBfsTipOption = new(() =>
            new ObservableCollection<string>(_cachedBfsPiles.Value.Select(p => p.TipDisplayName)));
        // 既製杭と同じ転記処理に載せるための詰め替え済みリスト (毎回変換しないようキャッシュ)
        private static readonly Lazy<List<PrecastPile>> _cachedNodularAsPrecast = new(() =>
            [.. _cachedNodularPiles.Value.Select(p => p.ToPrecastPile())]);
        private static readonly Lazy<List<PrecastPile>> _cachedNodularPrcAsPrecast = new(() =>
            [.. _cachedNodularPrcPiles.Value.Select(p => p.ToPrecastPile())]);
        private static readonly Lazy<List<PrecastPile>> _cachedNodularPrcPhcPartAsPrecast = new(() =>
            [.. _cachedNodularPrcPiles.Value.Select(p => p.ToPrecastPile(phcPart: true))]);
        private static readonly Lazy<List<PrecastPile>> _cachedBfsHeadAsPrecast = new(() =>
            [.. _cachedBfsPiles.Value.Select(p => p.ToPrecastPile())]);
        private static readonly Lazy<List<PrecastPile>> _cachedBfsTipAsPrecast = new(() =>
            [.. _cachedBfsPiles.Value.Select(p => p.ToPrecastPile(tipPart: true))]);

        private static List<PrecastPile> LoadPrecastPilesFromCsv(string fileName)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string filePath = Path.Combine(baseDir, "Models", "PileLibrary", fileName);
                return PrecastPileLoader.LoadFromCsv(filePath) ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] 杭ライブラリ読込失敗 ({fileName}): {ex.Message}");
                return [];
            }
        }

        private static List<NodularPile> LoadNodularPiles()
        {
            try
            {
                return NodularPileLoader.LoadDefault() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] 節杭ライブラリ読込失敗: {ex.Message}");
                return [];
            }
        }

        private static List<NodularPileHead> LoadNodularPileHeads()
        {
            try
            {
                return NodularPileLoader.LoadDefaultHeads() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] 節杭拡頭形状ライブラリ読込失敗: {ex.Message}");
                return [];
            }
        }

        private static List<NodularPrcPile> LoadNodularPrcPiles()
        {
            try
            {
                return NodularPrcPileLoader.LoadDefault() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] PRC節杭ライブラリ読込失敗: {ex.Message}");
                return [];
            }
        }

        private static List<NodularPrcPileHead> LoadNodularPrcPileHeads()
        {
            try
            {
                return NodularPrcPileLoader.LoadDefaultHeads() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] PRC節杭拡頭形状ライブラリ読込失敗: {ex.Message}");
                return [];
            }
        }

        private static List<BfsPile> LoadBfsPiles()
        {
            try
            {
                return BfsPileLoader.LoadDefault() ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] BF.S パイルライブラリ読込失敗: {ex.Message}");
                return [];
            }
        }

        private static List<SteelPipePile> LoadSteelPipePilesFromCsv(string fileName)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string filePath = Path.Combine(baseDir, "Models", "PileLibrary", fileName);
                return SteelPipePileLoader.LoadFromCsv(filePath) ?? [];
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection] 鋼管杭ライブラリ読込失敗 ({fileName}): {ex.Message}");
                return [];
            }
        }

        // 選択したPrecast杭
        private PrecastPile _selectedPrecastPile = new();
        public PrecastPile SelectedPrecastPile
        {
            get => _selectedPrecastPile;
            set => SetProperty(ref _selectedPrecastPile, value);
        }

        public SteelPipePile SelectedSteelPipePile = new();

        // 鋼管杭リスト（静的キャッシュを参照）
        private static List<SteelPipePile> SteelPipePiles => _cachedSteelPipePiles.Value;
        readonly PrecastPileLoader precastPileLoader = new();

        // クラス PileSection 内に追加するメソッド
        public void SetSelectedPrecastPileByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            // 既に同じ選択なら何もしない
            if (SelectedPrecastPile?.Name == name) return;

            // 名前をセットして再計算
            SelectedPrecastPile = new PrecastPile { Name = name };
            RecalculateSelectedPrecastPile();

            // 再計算結果に基づき関連値を更新して表示を整える
            RecalculatePileDia();
            RecalculateConcreteE();
            SetSpecs();

            // キャッシュ無効化（必要に応じて）
            InvalidateAllCaches();

            // 通知（SelectedPrecastPile と他プロパティの変更を View に反映）
            OnPropertyChanged(nameof(SelectedPrecastPile));
            OnPropertyChanged(nameof(PileDiameter));
            OnPropertyChanged(nameof(ConcreteOutDia));
        }

        /// <summary>
        /// 断面タイプに対応する既製杭ライブラリを返す。既製コンクリート杭以外は null。
        /// 節杭は専用 DTO を PrecastPile へ詰め替えたキャッシュを返すため、
        /// 呼び出し側は PHC/PRC/SC と同じ扱いができる。
        /// </summary>
        private static List<PrecastPile>? GetPrecastLibrary(string pileSectionType) => pileSectionType switch
        {
            PileTypeNames.Phc => PHCs,
            PileTypeNames.Prc => PRCs,
            PileTypeNames.Sc => SCs,
            PileTypeNames.PhcNodular => _cachedNodularAsPrecast.Value,
            PileTypeNames.PrcNodular => _cachedNodularPrcAsPrecast.Value,
            PileTypeNames.PrcNodularPhcPart => _cachedNodularPrcPhcPartAsPrecast.Value,
            PileTypeNames.BfsHead => _cachedBfsHeadAsPrecast.Value,
            PileTypeNames.BfsTip => _cachedBfsTipAsPrecast.Value,
            _ => null
        };

        /// <summary>
        /// SelectedPrecastPile.Name がライブラリ (PHCs/PRCs/SCs/節杭) に存在するかチェックする。副作用なし。
        /// 名前が未指定、または PileSectionType が既製コンクリート杭以外のときは true を返す (検査対象外)。
        /// </summary>
        public bool IsSelectedPrecastPileInLibrary()
        {
            string? name = SelectedPrecastPile?.Name;
            if (string.IsNullOrWhiteSpace(name)) return true;

            List<PrecastPile>? candidates = GetPrecastLibrary(PileSectionType);
            if (candidates == null) return true; // 既製コンクリート杭以外は対象外

            foreach (var p in candidates)
            {
                if (p.Name == name) return true;
            }
            return false;
        }

        /// <summary>
        /// 節杭固有の諸元 (節部径・カタログ標準質量・姿図寸法) を製品ライブラリから転記する。
        /// これらは <see cref="PrecastPile"/> に載らないので断面転記とは別経路になる。
        /// 断面タイプが節杭でない間は 0 のままにして、参照されても影響が出ないようにする。
        /// </summary>
        private void ApplyNodularProductGeometry()
        {
            string? name = SelectedPrecastPile?.Name;
            double do_ = 0.0, mass = 0.0, pitch = 0.0, head = 0.0, toe = 0.0, flat = 0.0;

            if (PileSectionType == PileTypeNames.PhcNodular)
            {
                var p = NodularPiles.FirstOrDefault(x => x.DisplayName == name);
                if (p != null)
                    (do_, mass, pitch, head, toe) = (p.Do, p.MassPerM, p.NodePitch, p.HeadOffset, p.ToeOffset);
            }
            else if (PileSectionType is PileTypeNames.BfsHead or PileTypeNames.BfsTip)
            {
                // 頭部軸部 / 先端軸部 は同じ製品の別部位。節部径・節寸法は共通で、
                // 軸部径だけが違う (= 節の出寸法が部位によって変わる)。
                // このカタログには標準質量表が無いので自重は軸部体積から求まる (節の分は未計上)。
                var p = FindBfsProduct(name);
                if (p != null)
                    (do_, pitch, head, toe, flat) =
                        (p.NodeDia, p.NodePitch, p.HeadOffset, p.ToeOffset, p.NodeFlatLength);
            }
            else if (PileTypeNames.IsNodularSection(PileSectionType))
            {
                // PRC部 / PHC部 は同じ製品の別断面なので、形状はどちらも製品行そのもの
                var p = FindNodularPrcProduct(name);
                if (p != null)
                    (do_, mass, pitch, head, toe) = (p.Do, p.MassPerM, p.NodePitch, p.HeadOffset, p.ToeOffset);
            }

            NodeDiameter = do_;
            CatalogMassPerM = mass;
            NodePitch = pitch;
            NodeHeadOffset = head;
            NodeToeOffset = toe;
            CatalogNodeFlatLength = flat;
        }

        /// <summary>表示名 (頭部軸部 / 先端軸部 のどちらでも) から BF.S の製品行を引く。</summary>
        private static BfsPile? FindBfsProduct(string? displayName) =>
            string.IsNullOrEmpty(displayName)
                ? null
                : BfsPiles.FirstOrDefault(
                    p => p.DisplayName == displayName || p.TipDisplayName == displayName);

        /// <summary>表示名 (PRC部 / PHC部 のどちらでも) から PRC節杭 の製品行を引く。</summary>
        private static NodularPrcPile? FindNodularPrcProduct(string? displayName) =>
            string.IsNullOrEmpty(displayName)
                ? null
                : NodularPrcPiles.FirstOrDefault(
                    p => p.DisplayName == displayName || p.PhcPartDisplayName == displayName);

        public void RecalculateSelectedPrecastPile()
        {
            List<PrecastPile> precastPiles = GetPrecastLibrary(PileSectionType) ?? [];

            ApplyNodularProductGeometry();

            bool isFound = false;

            foreach (PrecastPile pipe in precastPiles)
            {
                if (SelectedPrecastPile.Name == pipe.Name)
                {
                    SelectedPrecastPile = new PrecastPile
                    {
                        No = pipe.No,
                        ThicknessType = pipe.ThicknessType,
                        PrestressType = pipe.PrestressType,
                        Name = pipe.Name,
                        PileType = pipe.PileType,
                        PileDiameter = pipe.PileDiameter,
                        PileThickness = pipe.PileThickness,
                        Fc = pipe.Fc,
                        SFc = pipe.SFc,
                        Fbc = pipe.Fbc,
                        SigmaE = pipe.SigmaE,
                        Ec = pipe.Ec,
                        Ap = pipe.Ap,
                        Dp = pipe.Dp,
                        Ftp = pipe.Ftp,
                        SigmaPu = pipe.SigmaPu,
                        Ep = pipe.Ep,
                        HasReinf = pipe.HasReinf,
                        Nr = pipe.Nr,
                        RDesignation = pipe.RDesignation,
                        Ag = pipe.Ag,
                        Dr = pipe.Dr,
                        Ftr = pipe.Ftr,
                        Er = pipe.Er,
                        Ts = pipe.Ts,
                        Fts = pipe.Fts,
                        Es = pipe.Es,
                        PsSigmaY = pipe.PsSigmaY
                    };
                    PileDiameter = pipe.PileDiameter;
                    ConcreteOutDia = pipe.PileDiameter - pipe.Ts * 2.0;
                    ConcreteThickness = pipe.PileThickness - pipe.Ts;
                    TendonDp = pipe.Dp;
                    MainBarNum = pipe.Nr;
                    MainBarSize = pipe.RDesignation;
                    MainBarDr = pipe.Dr;
                    MainBarCenterCover = (ConcreteOutDia - MainBarDr) * 0.5;
                    PipeDia = pipe.PileDiameter;
                    PipeTs = pipe.Ts;
                    ConcreteFc = pipe.Fc;
                    Prestress = pipe.SigmaE;
                    ConcreteE = pipe.Ec;
                    TendonAp = pipe.Ap;
                    TendonEp = pipe.Ep;
                    TendonSigmaPy = pipe.Ftp;
                    TendonSigmaPu = pipe.SigmaPu;
                    MainBarAg = pipe.Ag;
                    MainBarEr = pipe.Er;
                    PipeEs = pipe.Es;
                    HoopPsSigmay = pipe.PsSigmaY;

                    ApplyGuideYoungsModulusIfEnabled();

                    isFound = true;
                    break;
                }
            }

            if (!isFound && precastPiles.Count > 0)
            {
                // 断面タイプ切替（PHC⇔PRC⇔SC）などで旧選択名が新ライブラリに存在しない場合:
                // 旧選択と同径（同径が無ければ最も近い径）の杭を既定選択し、断面欄が空欄のまま
                // 旧タイプの諸元で計算が続くのを防ぐ。同径内はライブラリ順（No 昇順）の先頭を採る。
                double prevDia = SelectedPrecastPile?.PileDiameter ?? 0.0;
                PrecastPile fallback = precastPiles
                    .OrderBy(p => Math.Abs(p.PileDiameter - prevDia))
                    .ThenBy(p => p.No)
                    .First();

                Serilog.Log.Debug(
                    "[RecalculateSelectedPrecastPile] '{Old}' は {Type} ライブラリに存在しないため同径の '{New}' を既定選択",
                    SelectedPrecastPile?.Name, PileSectionType, fallback.Name);

                SelectedPrecastPile = new PrecastPile { Name = fallback.Name };
                RecalculateSelectedPrecastPile(); // 名前一致で全諸元を反映（必ず isFound になるため再帰は 1 回）
            }
            else if (!isFound)
            {
                Serilog.Log.Debug($"Error: SelectedPrecastPile.Name '{SelectedPrecastPile.Name}' not found in precastPiles.");
            }

            // 最後に曲線を捨てる。杭断面ウィンドウはこのメソッドを直に呼ぶ
            // (SetSelectedPrecastPileByName を通らない) ので、ここで捨てないと
            // 寸法・Fc の同じ製品どうし (σe と Ap だけが違う PHC の B 種と C 種など) では
            // 捨てる契機が一つもなく、前の製品の曲線がそのまま出ていた。
            // セッター側でも捨てているが、転記の途中で曲線が読まれても最後に必ず捨て直す。
            InvalidateAllCaches();
        }

        // 選択したS杭
        private string _selectedSteelPipePileName = string.Empty;
        public string SelectedSteelPipePileName
        {
            get => _selectedSteelPipePileName;
            set
            {
                if (SetProperty(ref _selectedSteelPipePileName, value))
                {
                    // 読み込み中はライブラリから寸法を復元しない。
                    //
                    // ファイルには製品名と PipeDia / PipeTs の両方が入っているので、
                    // 名前が先に来れば寸法をライブラリ値で上書きし、寸法が先に来れば
                    // ファイルの値が残る。<b>値の並び順で読み込み結果が変わっていた</b>
                    // (揺らした並び順の網が鋼管杭で検出)。
                    // ファイルの寸法は保存時にライブラリから導いた値なので、
                    // そのまま使えば同じ結果になり、しかも順序に依存しない。
                    if (!_isDeserializing) RecalculateSelectedSteelPipePipe();
                }
            }
        }

        // S杭選択時のメソッド
        private void RecalculateSelectedSteelPipePipe()
        {
            foreach (var pipe in SteelPipePiles)
            {
                if (SelectedSteelPipePileName == $"{pipe.Diameter}x{pipe.Thickness}")
                {
                    SelectedSteelPipePile = pipe;
                    PipeDia = pipe.Diameter;
                    PipeTs = pipe.Thickness;

                    break;
                }
            }
        }

        // PHC（静的キャッシュを参照）
        public static ObservableCollection<string> PHCOption => _cachedPHCOption.Value;
        public static List<PrecastPile> PHCs => _cachedPHCs.Value;

        // PRC（静的キャッシュを参照）
        public static ObservableCollection<string> PRCOption => _cachedPRCOption.Value;
        public static List<PrecastPile> PRCs => _cachedPRCs.Value;

        // SC（静的キャッシュを参照）
        public static ObservableCollection<string> SCOption => _cachedSCOption.Value;
        public static List<PrecastPile> SCs => _cachedSCs.Value;

        // 鋼管（静的キャッシュを参照）
        public static ObservableCollection<string> SteelPipeOption => _cachedSteelPipeOption.Value;

        // PHC節杭（静的キャッシュを参照）
        public static ObservableCollection<string> NodularPileOption => _cachedNodularPileOption.Value;
        public static List<NodularPile> NodularPiles => _cachedNodularPiles.Value;
        /// <summary>拡頭中間径タイプ / 拡頭タイプ の形状一覧（呼び名 × 拡頭径 Dt）。</summary>
        public static List<NodularPileHead> NodularPileHeads => _cachedNodularPileHeads.Value;

        /// <summary>PRC節杭 (PRC部) の製品選択肢。</summary>
        public static ObservableCollection<string> NodularPrcOption => _cachedNodularPrcOption.Value;
        /// <summary>PRC節杭 (PHC部) の製品選択肢。</summary>
        public static ObservableCollection<string> NodularPrcPhcPartOption => _cachedNodularPrcPhcPartOption.Value;
        public static List<NodularPrcPile> NodularPrcPiles => _cachedNodularPrcPiles.Value;
        /// <summary>PRC節杭 の拡頭中間径タイプ / 拡頭タイプ の形状一覧。</summary>
        public static List<NodularPrcPileHead> NodularPrcPileHeads => _cachedNodularPrcPileHeads.Value;

        /// <summary>BF.S (頭部厚型節付き杭) 頭部軸部 の製品選択肢。</summary>
        public static ObservableCollection<string> BfsHeadOption => _cachedBfsHeadOption.Value;
        /// <summary>BF.S (頭部厚型節付き杭) 先端軸部 の製品選択肢。</summary>
        public static ObservableCollection<string> BfsTipOption => _cachedBfsTipOption.Value;
        public static List<BfsPile> BfsPiles => _cachedBfsPiles.Value;


    }
}
