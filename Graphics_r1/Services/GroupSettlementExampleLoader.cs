using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media.Media3D;

namespace PileDesign.Services
{
    /// <summary>
    /// 群杭沈下解析例題データのJSONローダー
    /// </summary>
    public static class GroupSettlementExampleLoader
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        /// <summary>
        /// JSONファイルから例題データを読み込む
        /// </summary>
        /// <param name="fileName">JSONファイル名（拡張子なし）</param>
        /// <returns>例題データ</returns>
        public static GroupSettlementExampleData LoadFromFile(string fileName)
        {

            var examplesPath = GetExamplesPath();
            var filePath = Path.Combine(examplesPath, $"{fileName}.json");

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"例題ファイルが見つかりません: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<GroupSettlementExampleData>(json, _jsonOptions)
                ?? throw new InvalidOperationException($"JSONのデシリアライズに失敗しました: {filePath}");
        }

        /// <summary>
        /// 例題データをInputModelに適用する
        /// </summary>
        public static void ApplyToInputModel(
            InputModel inputModel,
            GroupSettlementExampleData data,
            MainWindowViewModel viewModel)
        {
            // 群杭沈下解析結果をクリア（バッチ置換でスレッド安全）
            inputModel.PileGroupSettlement.SettlementGridData = new ObservableCollection<SettlementGridDataItem>();
            inputModel.PileGroupSettlement.SettlementGridX = new ObservableCollection<double>();
            inputModel.PileGroupSettlement.SettlementGridY = new ObservableCollection<double>();

            // 慣性力中心点を設定
            if (data.InertiaPoint != null)
            {
                var inertiaPoint = new Point3D(
                    data.InertiaPoint.X,
                    data.InertiaPoint.Y,
                    data.InertiaPoint.Z);

                foreach (var loadCase in inputModel.LoadCasesInput.LoadCasesLevel1)
                {
                    loadCase.ForceActionPoint = inertiaPoint;
                }
                foreach (var loadCase in inputModel.LoadCasesInput.LoadCasesLevel2)
                {
                    loadCase.ForceActionPoint = inertiaPoint;
                }
            }

            // 載荷面標高を設定
            inputModel.PileGroupSettlement.LoadingPlaneAltitude = data.LoadingPlaneAltitude;

            // 矩形荷重を設定（バッチ化）
            var rectLoadList = new List<RectLoad>(data.RectLoads.Count);
            foreach (var rectLoadDto in data.RectLoads)
            {
                rectLoadList.Add(new RectLoad
                {
                    X1 = rectLoadDto.X1,
                    X2 = rectLoadDto.X2,
                    Y1 = rectLoadDto.Y1,
                    Y2 = rectLoadDto.Y2,
                    QA = rectLoadDto.QA
                });
            }
            inputModel.PileGroupSettlement.RectLoads = new ObservableCollection<RectLoad>(rectLoadList);

            // 沈下計算用地層を設定（バッチ化）
            var soilLayerList = new List<SettlementSoilLayer>(data.SettlementSoilLayers.Count);
            foreach (var layerDto in data.SettlementSoilLayers)
            {
                soilLayerList.Add(new SettlementSoilLayer
                {
                    BottomAltitude = layerDto.BottomAltitude,
                    Ek = layerDto.Ek,
                    PoissonsRatio = layerDto.PoissonsRatio,
                    Thickness = layerDto.Thickness
                });
            }
            inputModel.PileGroupSettlement.SettlementSoilLayers = new ObservableCollection<SettlementSoilLayer>(soilLayerList);

            // 杭体情報（pileBodies）の反映（バッチ化: List に構築してから一括代入）
            if (data.PileBodies != null && data.PileBodies.Count > 0)
            {
                var pileBodyList = new List<PileBodyInput>(data.PileBodies.Count);
                foreach (var pileBodyDto in data.PileBodies)
                {
                    var pileBody = new PileBodyInput
                    {
                        PileBodyRef = pileBodyDto.PileBodyRef,
                        PileBodyType = pileBodyDto.PileBodyType,
                        PileTopType = pileBodyDto.PileTopType,
                        PileConstructionType = pileBodyDto.PileConstructionType,
                        PileToeDia = pileBodyDto.PileToeDia,
                        TipNonPermability = pileBodyDto.TipNonPermability,
                        EmbedmentIntoBearingSoil = pileBodyDto.EmbedmentIntoBearingSoil,
                        PileInnerDia = pileBodyDto.PileInnerDia,
                        PileTipStyle = pileBodyDto.PileTipStyle,
                        SettlePileToeDia = pileBodyDto.SettlePileToeDia,
                        SettleAlpha = pileBodyDto.SettleAlpha,
                        SettleN = pileBodyDto.SettleN
                    };
                    pileBody.PileBodySegments.Clear();
                    foreach (var segDto in pileBodyDto.Segments)
                    {
                        var segment = new PileBodySegment
                        {
                            No = segDto.No,
                            SegmentLength = segDto.SegmentLength,
                            SegmentDepth = segDto.SegmentDepth
                        };

                        // 先にコレクションに追加
                        pileBody.PileBodySegments.Add(segment);

                        // 追加後にPileSectionTypeとPrecastPileNameを設定
                        segment.PileSection.PileSectionType = segDto.PileSectionType;
                        if (!string.IsNullOrEmpty(segDto.PrecastPileName))
                            segment.PileSection.SetSelectedPrecastPileByName(segDto.PrecastPileName);

                        // 場所打ち杭用プロパティ
                        if (segDto.ConcreteOutDia.HasValue)
                            segment.PileSection.ConcreteOutDia = segDto.ConcreteOutDia.Value;
                        if (segDto.ConcreteThickness.HasValue)
                            segment.PileSection.ConcreteThickness = segDto.ConcreteThickness.Value;
                        if (segDto.MainBarDr.HasValue)
                            segment.PileSection.MainBarDr = segDto.MainBarDr.Value;
                        if (segDto.PipeTs.HasValue)
                            segment.PileSection.PipeTs = segDto.PipeTs.Value;
                        if (segDto.PipeDia.HasValue)
                            segment.PileSection.PipeDia = segDto.PipeDia.Value;
                        if (segDto.MainBarNum.HasValue)
                            segment.PileSection.MainBarNum = segDto.MainBarNum.Value;
                        if (!string.IsNullOrEmpty(segDto.MainBarSize))
                            segment.PileSection.MainBarSize = segDto.MainBarSize;
                        if (segDto.ConcreteFc.HasValue)
                            segment.PileSection.ConcreteFc = segDto.ConcreteFc.Value;
                        if (segDto.ConcreteGsi.HasValue)
                            segment.PileSection.ConcreteGsi = segDto.ConcreteGsi.Value;
                        if (segDto.ConcreteGamma.HasValue)
                            segment.PileSection.ConcreteGamma = segDto.ConcreteGamma.Value;
                    }
                    pileBodyList.Add(pileBody);
                }
                inputModel.PileBodies = new ObservableCollection<PileBodyInput>(pileBodyList);
            }
            else
            {
                inputModel.PileBodies = new ObservableCollection<PileBodyInput>();
            }

            // 杭位置を設定（ある場合、バッチ化）
            if (data.PileLayoutPositions != null && data.PileLayoutPositions.Count > 0)
            {
                var pileLayoutList = new List<PileLayoutDataItem>(data.PileLayoutPositions.Count);
                foreach (var pos in data.PileLayoutPositions)
                {
                    var item = new PileLayoutDataItem
                    {
                        X = pos.X,
                        Y = pos.Y
                    };
                    if (data.FoundationBeamDeltaZc.HasValue)
                        item.FoundationBeamDeltaZc = data.FoundationBeamDeltaZc.Value;
                    item.SetMainWindowViewModel(viewModel);
                    pileLayoutList.Add(item);
                }
                inputModel.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>(pileLayoutList);

                // PileNo を早期設定（FoundationBeamInput の節点解決に必要）
                for (int i = 0; i < inputModel.PileLayoutItems.Count; i++)
                    inputModel.PileLayoutItems[i].PileNo = i + 1;
            }
            else
            {
                inputModel.PileLayoutItems = [];
            }

            // 旧要素はクリア（後方互換用に読み込まれた場合に備える）
            inputModel.Elements = new ObservableCollection<Element>();

            // 一般節点をクリア
            inputModel.InputNodes = new ObservableCollection<InputNode>();

            // FoundationBeamInput 設定（基礎梁入力）
            if (data.FoundationBeamInput != null)
            {
                inputModel.FoundationBeamInput ??= new FoundationBeamInput();

                // 接続モード
                inputModel.FoundationBeamInput.ConnectionMode = data.FoundationBeamInput.ConnectionMode switch
                {
                    "RigidBody" => FoundationBeamConnectionMode.RigidBody,
                    "RigidFloor" => FoundationBeamConnectionMode.RigidFloor,
                    _ => FoundationBeamConnectionMode.RigidBody
                };

                // 材料（バッチ化）
                var matList = new List<BeamMaterial>(data.FoundationBeamInput.Materials.Count);
                foreach (var matDto in data.FoundationBeamInput.Materials)
                {
                    matList.Add(new BeamMaterial
                    {
                        No = matDto.No,
                        Name = matDto.Name,
                        YoungModulus = matDto.YoungModulus,
                        ShearModulus = matDto.ShearModulus,
                        PoissonRatio = matDto.PoissonRatio
                    });
                }
                inputModel.FoundationBeamInput.Materials = new ObservableCollection<BeamMaterial>(matList);

                // 断面（バッチ化）
                var secList = new List<BeamSection>(data.FoundationBeamInput.Sections.Count);
                foreach (var secDto in data.FoundationBeamInput.Sections)
                {
                    var section = new BeamSection
                    {
                        No = secDto.No,
                        Name = secDto.Name,
                        Width = secDto.Width,
                        Height = secDto.Height
                    };
                    section.RecalculateProperties();
                    secList.Add(section);
                }
                inputModel.FoundationBeamInput.Sections = new ObservableCollection<BeamSection>(secList);

                // 梁要素（バッチ化）
                var beamList = new List<FoundationBeamElement>(data.FoundationBeamInput.Beams.Count);
                foreach (var beamDto in data.FoundationBeamInput.Beams)
                {
                    var beam = new FoundationBeamElement
                    {
                        No = beamDto.No,
                        MaterialNo = beamDto.MaterialNo,
                        SectionNo = beamDto.SectionNo
                    };

                    // I端ノード参照を解決
                    ResolveBeamNodeReference(beamDto.NodeI_Type, beamDto.NodeI_No,
                        inputModel.PileLayoutItems,
                        out var iType, out var iId);
                    beam.NodeI_Type = iType;
                    beam.NodeI_Id = iId;

                    // J端ノード参照を解決
                    ResolveBeamNodeReference(beamDto.NodeJ_Type, beamDto.NodeJ_No,
                        inputModel.PileLayoutItems,
                        out var jType, out var jId);
                    beam.NodeJ_Type = jType;
                    beam.NodeJ_Id = jId;

                    beamList.Add(beam);
                }
                inputModel.FoundationBeamInput.Beams = new ObservableCollection<FoundationBeamElement>(beamList);
            }
            else
            {
                // FoundationBeamInput がない場合はクリア（バッチ置換でスレッド安全）
                if (inputModel.FoundationBeamInput != null)
                {
                    inputModel.FoundationBeamInput.Materials = new ObservableCollection<BeamMaterial>();
                    inputModel.FoundationBeamInput.Sections = new ObservableCollection<BeamSection>();
                    inputModel.FoundationBeamInput.Beams = new ObservableCollection<FoundationBeamElement>();
                }
            }

            // グリッドを設定（ある場合、バッチ化）
            if (data.GridXItems != null)
            {
                var gridXList = new List<GridDataItem>(data.GridXItems.Count);
                foreach (var gridDto in data.GridXItems)
                {
                    gridXList.Add(new GridDataItem
                    {
                        Name = gridDto.Name,
                        Coord = gridDto.Coord,
                        Spacing = gridDto.Spacing
                    });
                }
                inputModel.GridXItems = new ObservableCollection<GridDataItem>(gridXList);
            }
            else
            {
                inputModel.GridXItems = new ObservableCollection<GridDataItem>();
            }

            if (data.GridYItems != null)
            {
                var gridYList = new List<GridDataItem>(data.GridYItems.Count);
                foreach (var gridDto in data.GridYItems)
                {
                    gridYList.Add(new GridDataItem
                    {
                        Name = gridDto.Name,
                        Coord = gridDto.Coord,
                        Spacing = gridDto.Spacing
                    });
                }
                inputModel.GridYItems = new ObservableCollection<GridDataItem>(gridYList);
            }
            else
            {
                inputModel.GridYItems = new ObservableCollection<GridDataItem>();
            }

            // 沈下解析オフセットを設定（ある場合はJSONから、ない場合はデフォルト値）
            if (data.SettlementOffsets != null)
            {
                viewModel.GroupPileSettlementXOffset = data.SettlementOffsets.XOffset;
                viewModel.GroupPileSettlementYOffset = data.SettlementOffsets.YOffset;
                viewModel.GroupPileSettlementXSpacing = data.SettlementOffsets.XSpacing;
                viewModel.GroupPileSettlementYSpacing = data.SettlementOffsets.YSpacing;
            }
            else
            {
                viewModel.GroupPileSettlementXOffset = 0.0;
                viewModel.GroupPileSettlementYOffset = 0.0;
                viewModel.GroupPileSettlementXSpacing = 1.0;
                viewModel.GroupPileSettlementYSpacing = 1.0;
            }

            // 根入れ部を無効化（バッチ置換でスレッド安全）
            inputModel.EmbedmentInput.EmbedmentLayers = new ObservableCollection<EmbedmentDataItem>();
            inputModel.ElementDivision.SoilEmbedment = null;
        }

        /// <summary>
        /// 梁要素の節点参照（type + no）を NodeReferenceType + Guid に変換する
        /// </summary>
        private static void ResolveBeamNodeReference(
            string typeStr, int nodeNo,
            ObservableCollection<PileLayoutDataItem> piles,
            out NodeReferenceType type, out Guid id)
        {
            switch (typeStr?.ToLowerInvariant())
            {
                case "pile":
                    var pile = piles?.FirstOrDefault(p => p.PileNo == nodeNo);
                    type = NodeReferenceType.PileLayout;
                    id = pile?.UniqueId ?? Guid.Empty;
                    return;
                default:
                    type = NodeReferenceType.PileLayout;
                    id = Guid.Empty;
                    return;
            }
        }

        /// <summary>
        /// Examplesフォルダのパスを取得
        /// </summary>
        private static string GetExamplesPath()
        {
            // 実行ファイルのディレクトリからExamplesフォルダを探す
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? ".";
            var examplesPath = Path.Combine(assemblyDir, "Examples");

            if (Directory.Exists(examplesPath))
            {
                return examplesPath;
            }

            // 開発時: プロジェクトディレクトリのExamplesフォルダ
            var projectDir = FindProjectDirectory(assemblyDir);
            if (projectDir != null)
            {
                var devExamplesPath = Path.Combine(projectDir, "Examples");
                if (Directory.Exists(devExamplesPath))
                {
                    return devExamplesPath;
                }
            }

            throw new DirectoryNotFoundException($"Examplesフォルダが見つかりません: {examplesPath}");
        }

        /// <summary>
        /// プロジェクトディレクトリを探す
        /// </summary>
        private static string? FindProjectDirectory(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                // .csprojファイルがあればプロジェクトディレクトリ
                if (Directory.GetFiles(dir.FullName, "*.csproj").Length > 0)
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }
    }
}
