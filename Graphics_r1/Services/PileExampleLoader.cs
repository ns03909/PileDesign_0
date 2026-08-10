using PileDesign.Constants;
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
    /// 杭例題データのJSONローダー
    /// </summary>
    public static class PileExampleLoader
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
        public static PileExampleData LoadFromFile(string fileName)
        {
            var examplesPath = GetExamplesPath();
            var filePath = Path.Combine(examplesPath, $"{fileName}.json");

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"例題ファイルが見つかりません: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<PileExampleData>(json, _jsonOptions)
                ?? throw new InvalidOperationException($"JSONのデシリアライズに失敗しました: {filePath}");
        }

        /// <summary>
        /// 例題データをInputModelに適用する
        /// </summary>
        public static void ApplyToInputModel(
            InputModel inputModel,
            PileExampleData data,
            MainWindowViewModel viewModel)
        {
            // LoadCasesLevel1 設定
            for (int i = 0; i < inputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
            {
                var loadCase = inputModel.LoadCasesInput.LoadCasesLevel1[i];
                loadCase.ForceActionPoint = new Point3D(
                    data.LoadCaseLevel1.ForceActionPointX,
                    data.LoadCaseLevel1.ForceActionPointY,
                    data.LoadCaseLevel1.ForceActionPointZ);
                loadCase.IsApplicable = true;
                loadCase.UpperMassForce = data.LoadCaseLevel1.UpperMassForce;
                loadCase.FoundationMassForce = data.LoadCaseLevel1.FoundationMassForce;
            }

            // LoadCasesLevel2 設定
            for (int i = 0; i < inputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
            {
                var loadCase = inputModel.LoadCasesInput.LoadCasesLevel2[i];
                loadCase.ForceActionPoint = new Point3D(
                    data.LoadCaseLevel2.ForceActionPointX,
                    data.LoadCaseLevel2.ForceActionPointY,
                    data.LoadCaseLevel2.ForceActionPointZ);
                loadCase.IsApplicable = true;

                // Level2Alt がある場合は交互に適用（例3.1の特殊ケース）
                if (data.LoadCaseLevel2Alt != null && (i == 1 || i == 3))
                {
                    loadCase.UpperMassForce = data.LoadCaseLevel2Alt.UpperMassForce;
                    loadCase.FoundationMassForce = data.LoadCaseLevel2Alt.FoundationMassForce;
                }
                else
                {
                    loadCase.UpperMassForce = data.LoadCaseLevel2.UpperMassForce;
                    loadCase.FoundationMassForce = data.LoadCaseLevel2.FoundationMassForce;
                }
            }

            // PileBodies 設定（バッチ化: List に構築してから一括代入）
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

                // セグメント設定
                pileBody.PileBodySegments.Clear();
                foreach (var segDto in pileBodyDto.Segments)
                {
                    var segment = new PileBodySegment
                    {
                        No = segDto.No,
                        SegmentLength = segDto.SegmentLength,
                        SegmentDepth = segDto.SegmentDepth
                    };

                    // 先にコレクションに追加（CollectionChangedでデフォルト値が設定される）
                    pileBody.PileBodySegments.Add(segment);

                    // 追加後にPileSectionTypeとPrecastPileNameを設定（デフォルト値を上書き）
                    segment.PileSection.PileSectionType = segDto.PileSectionType;

                    // 既製杭 / 鋼管杭ライブラリ選択
                    if (!string.IsNullOrEmpty(segDto.PrecastPileName))
                    {
                        if (segment.PileSection.PileBodyType == PileTypeNames.SteelPipe)
                        {
                            // 鋼管杭ライブラリ (例: "800x17") → SelectedSteelPipePileName セッターが
                            // PipeDia / PipeTs を library から復元する
                            segment.PileSection.SelectedSteelPipePileName = segDto.PrecastPileName;
                        }
                        else
                        {
                            segment.PileSection.SetSelectedPrecastPileByName(segDto.PrecastPileName);
                        }
                    }

                    // 場所打ち杭（鉄筋コンクリート部）の場合
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
                    if (!string.IsNullOrEmpty(segDto.HoopSize))
                        segment.PileSection.HoopSize = segDto.HoopSize;
                    if (segDto.HoopSpacing.HasValue)
                        segment.PileSection.HoopSpacing = segDto.HoopSpacing.Value;
                }

                // キャプテンパイル工法設定の適用 (PCリング、絞り率、引張定着筋)
                if (pileBodyDto.CaptainPile != null && pileBodyDto.PileTopType == "キャプテンパイル工法")
                {
                    ApplyCaptainPileSettings(pileBody, pileBodyDto.CaptainPile);
                }

                // キャプリングパイル工法設定の適用 (PCリング、引張定着筋)
                if (pileBodyDto.CapringPile != null && pileBodyDto.PileTopType == "キャプリングパイル工法")
                {
                    ApplyCapringPileSettings(pileBody, pileBodyDto.CapringPile);
                }

                pileBodyList.Add(pileBody);
            }
            inputModel.PileBodies = new ObservableCollection<PileBodyInput>(pileBodyList);

            // 根入れ情報を設定（ある場合）
            if (data.Embedment != null)
            {
                // ElementDivision と SoilEmbedment を初期化
                inputModel.ElementDivision ??= new ElementDivision();

                // ZDataItemsを作成
                var zDataItems = new ObservableCollection<EmbedmentZDataItem>();
                if (data.Embedment.Zs != null)
                {
                    foreach (var z in data.Embedment.Zs)
                    {
                        zDataItems.Add(new EmbedmentZDataItem { Z = z });
                    }
                }

                // SoilEmbedment を設定
                inputModel.ElementDivision.SoilEmbedment = new SoilEmbedment
                {
                    GroundNo = data.Embedment.GroundNo,
                    EmbedmentTopAltitude = data.Embedment.EmbedmentTopAltitude,
                    EmbedmentBottomAltitude = data.Embedment.EmbedmentBottomAltitude,
                    ZDataItems = zDataItems
                };

                // EmbedmentInput を設定
                inputModel.EmbedmentInput ??= new EmbedmentInput();
                inputModel.EmbedmentInput.GroundNo = data.Embedment.GroundNo;
                inputModel.EmbedmentInput.BottomAltitude = data.Embedment.EmbedmentBottomAltitude;
                // EmbedmentLayers をバッチ構築して一括代入（スレッド安全）
                if (data.Embedment.EmbedmentLayers != null)
                {
                    int no = 1;
                    var embedLayerList = new List<EmbedmentDataItem>(data.Embedment.EmbedmentLayers.Count);
                    foreach (var layer in data.Embedment.EmbedmentLayers)
                    {
                        embedLayerList.Add(new EmbedmentDataItem
                        {
                            No = no++,
                            TopAltitude = layer.TopAltitude,
                            BottomAltitude = layer.BottomAltitude,
                            LayerThickness = layer.TopAltitude - layer.BottomAltitude,
                            X1 = layer.X1,
                            X2 = layer.X2,
                            Y1 = layer.Y1,
                            Y2 = layer.Y2
                        });
                    }
                    inputModel.EmbedmentInput.EmbedmentLayers = new ObservableCollection<EmbedmentDataItem>(embedLayerList);
                    inputModel.EmbedmentInput.EmbedmentLayersCount = inputModel.EmbedmentInput.EmbedmentLayers.Count;
                }
            }

            // PileLayoutItems 設定（バッチ化: List に構築してから一括代入）
            var pileLayoutList = new List<PileLayoutDataItem>(data.PileLayoutItems.Count);
            foreach (var layoutDto in data.PileLayoutItems)
            {
                var item = new PileLayoutDataItem
                {
                    PileNo = layoutDto.PileNo,
                    PileBodyNo = layoutDto.PileBodyNo,
                    GroundNo = layoutDto.GroundNo,
                    SoilPileAltNo = layoutDto.SoilPileAltNo,
                    GroupPileFactor = layoutDto.GroupPileFactor,
                    PileSpacingFactor = layoutDto.PileSpacingFactor,
                    X = layoutDto.X,
                    Y = layoutDto.Y,
                    Z = layoutDto.Z,
                    AxialForceVL0 = layoutDto.AxialForceVL0,
                    // 配列 -> ObservableCollection に変換（4要素未満の場合はデフォルト値を使用）
                    AxialForceLevel1s = layoutDto.AxialForceLevel1s?.Length >= 4
                        ? new ObservableCollection<double>(layoutDto.AxialForceLevel1s)
                        : new ObservableCollection<double>([0.0, 0.0, 0.0, 0.0]),
                    AxialForceLevel2s = layoutDto.AxialForceLevel2s?.Length >= 4
                        ? new ObservableCollection<double>(layoutDto.AxialForceLevel2s)
                        : new ObservableCollection<double>([0.0, 0.0, 0.0, 0.0])
                };
                if (layoutDto.DeltaZc.HasValue)
                    item.FoundationBeamDeltaZc = layoutDto.DeltaZc.Value;
                item.SetMainWindowViewModel(viewModel);
                pileLayoutList.Add(item);
            }
            inputModel.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>(pileLayoutList);

            // InputNodes 設定（一般節点、バッチ化）
            var inputNodesList = new List<InputNode>(data.InputNodes.Count);
            foreach (var nodeDto in data.InputNodes)
            {
                inputNodesList.Add(new InputNode
                {
                    No = nodeDto.No,
                    Type = NodeType.General,
                    X = nodeDto.X,
                    Y = nodeDto.Y,
                    Z = nodeDto.Z
                });
            }
            inputModel.InputNodes = new ObservableCollection<InputNode>(inputNodesList);

            // GridXItems 設定（バッチ化）
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

            // GridYItems 設定（バッチ化）
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

                // 材料（バッチ化）— No プロパティは廃止 (位置 = ID)、配列順がそのまま 1-based の番号となる
                var matList = new List<BeamMaterial>(data.FoundationBeamInput.Materials.Count);
                foreach (var matDto in data.FoundationBeamInput.Materials)
                {
                    matList.Add(new BeamMaterial
                    {
                        Name = matDto.Name,
                        YoungModulus = matDto.YoungModulus,
                        ShearModulus = matDto.ShearModulus,
                        PoissonRatio = matDto.PoissonRatio
                    });
                }
                inputModel.FoundationBeamInput.Materials = new ObservableCollection<BeamMaterial>(matList);

                // 断面（バッチ化）— No プロパティは廃止 (位置 = ID)
                var secList = new List<BeamSection>(data.FoundationBeamInput.Sections.Count);
                foreach (var secDto in data.FoundationBeamInput.Sections)
                {
                    var section = new BeamSection
                    {
                        Name = secDto.Name,
                        Width = secDto.Width,
                        Height = secDto.Height
                    };
                    section.RecalculateProperties();
                    secList.Add(section);
                }
                inputModel.FoundationBeamInput.Sections = new ObservableCollection<BeamSection>(secList);

                // 梁要素（バッチ化 + Dictionary参照解決）— No プロパティは廃止 (位置 = ID)
                var pileLookup = inputModel.PileLayoutItems.ToDictionary(p => p.PileNo, p => p.UniqueId);
                var nodeLookup = inputModel.InputNodes.ToDictionary(n => n.No, n => n.UniqueId);

                var beamList = new List<FoundationBeam>(data.FoundationBeamInput.Beams.Count);
                foreach (var beamDto in data.FoundationBeamInput.Beams)
                {
                    var beam = new FoundationBeam
                    {
                        MaterialNo = beamDto.MaterialNo,
                        SectionNo = beamDto.SectionNo
                    };

                    // I端ノード参照を解決（Dictionary O(1)）
                    ResolveBeamNodeReferenceFast(beamDto.NodeI_Type, beamDto.NodeI_No,
                        pileLookup, nodeLookup, out var iType, out var iId);
                    beam.NodeI_Type = iType;
                    beam.NodeI_Id = iId;

                    // J端ノード参照を解決（Dictionary O(1)）
                    ResolveBeamNodeReferenceFast(beamDto.NodeJ_Type, beamDto.NodeJ_No,
                        pileLookup, nodeLookup, out var jType, out var jId);
                    beam.NodeJ_Type = jType;
                    beam.NodeJ_Id = jId;

                    beamList.Add(beam);
                }
                inputModel.FoundationBeamInput.Beams = new ObservableCollection<FoundationBeam>(beamList);

                // 材料・断面が空のとき、自動生成梁の参照先を保証するためデフォルトエントリを追加
                inputModel.FoundationBeamInput.EnsureDefaultMaterialAndSection();

                // 梁要素 ComboBox 用の節点候補リストを再構築 (FoundationNode が含まれる場合のため)
                inputModel.RefreshAvailableNodeReferenceOptions();
            }
            else
            {
                // FoundationBeamInput がない場合はクリア（バッチ置換でスレッド安全）
                if (inputModel.FoundationBeamInput != null)
                {
                    inputModel.FoundationBeamInput.Materials = new ObservableCollection<BeamMaterial>();
                    inputModel.FoundationBeamInput.Sections = new ObservableCollection<BeamSection>();
                    inputModel.FoundationBeamInput.Beams = new ObservableCollection<FoundationBeam>();
                }
            }

            // PileGroupSettlement クリア（解析結果を含む、バッチ置換でスレッド安全）
            if (inputModel.PileGroupSettlement != null)
            {
                inputModel.PileGroupSettlement.RectLoads = new ObservableCollection<RectLoad>();
                inputModel.PileGroupSettlement.SettlementSoilLayers = new ObservableCollection<SettlementSoilLayer>();
                inputModel.PileGroupSettlement.SettlementGridData = new ObservableCollection<SettlementGridDataItem>();
                inputModel.PileGroupSettlement.SettlementGridX = new ObservableCollection<double>();
                inputModel.PileGroupSettlement.SettlementGridY = new ObservableCollection<double>();
                inputModel.PileGroupSettlement.LoadingPlaneAltitude = 0.0;
            }

            // 根入れクリア（JSONに根入れ情報がない場合のみ）
            if (data.Embedment == null)
            {
                inputModel.EmbedmentInput.EmbedmentLayers = new ObservableCollection<EmbedmentDataItem>();
                inputModel.ElementDivision.SoilEmbedment = null;
                inputModel.ElementDivision.DoatsuGoryokuBane = null;
            }
        }

        /// <summary>
        /// DTO の節点参照を実際の InputNode/PileLayoutDataItem インスタンスに解決する
        /// </summary>
        private static InputNode? ResolveNodeReference(
            ElementNodeRefDto nodeRef,
            ObservableCollection<PileLayoutDataItem> piles,
            ObservableCollection<InputNode> nodes)
        {
            return nodeRef.NodeType?.ToLowerInvariant() switch
            {
                "pile" => piles?.FirstOrDefault(p => p.PileNo == nodeRef.NodeNo),
                "general" => nodes?.FirstOrDefault(n => n.No == nodeRef.NodeNo),
                _ => null
            };
        }

        /// <summary>
        /// 梁要素の節点参照（type + no）を NodeReferenceType + Guid に変換する
        /// </summary>
        private static void ResolveBeamNodeReference(
            string typeStr, int nodeNo,
            ObservableCollection<PileLayoutDataItem> piles,
            ObservableCollection<InputNode> nodes,
            out NodeReferenceType type, out Guid id)
        {
            switch (typeStr?.ToLowerInvariant())
            {
                case "pile":
                    var pile = piles?.FirstOrDefault(p => p.PileNo == nodeNo);
                    type = NodeReferenceType.PileLayout;
                    id = pile?.UniqueId ?? Guid.Empty;
                    return;
                case "general":
                    var node = nodes?.FirstOrDefault(n => n.No == nodeNo);
                    type = NodeReferenceType.GeneralNode;
                    id = node?.UniqueId ?? Guid.Empty;
                    return;
                default:
                    type = NodeReferenceType.FoundationNode;
                    id = Guid.Empty;
                    return;
            }
        }

        /// <summary>
        /// 梁要素の節点参照を Dictionary で O(1) 解決する高速版
        /// </summary>
        private static void ResolveBeamNodeReferenceFast(
            string typeStr, int nodeNo,
            Dictionary<int, Guid> pileLookup,
            Dictionary<int, Guid> nodeLookup,
            out NodeReferenceType type, out Guid id)
        {
            switch (typeStr?.ToLowerInvariant())
            {
                case "pile":
                    type = NodeReferenceType.PileLayout;
                    id = pileLookup.TryGetValue(nodeNo, out var pileId) ? pileId : Guid.Empty;
                    return;
                case "general":
                    type = NodeReferenceType.GeneralNode;
                    id = nodeLookup.TryGetValue(nodeNo, out var nodeId) ? nodeId : Guid.Empty;
                    return;
                default:
                    type = NodeReferenceType.FoundationNode;
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
            // 単一ファイル発行 (PublishSingleFile=true) では Assembly.Location が空文字を返すため AppContext.BaseDirectory を使用
            var assemblyDir = AppContext.BaseDirectory;
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

        /// <summary>
        /// キャプテンパイル工法の設定を pileBody.PileTop.CaptainPile に適用する。
        ///   - PCRingName: PCリング名 (例: "2400-N") → CaptainPile.PCRing と SelectedPCRingName を設定
        ///   - Nu: 絞り率
        ///   - HasTensionRebars: 引張定着筋有無
        ///   - TensionRebarArrangement: "正方形配置" / "円形配置"
        ///   - TensionRebarNumber: 本数
        ///   - TensionRebarDia: 呼び径 (例: "D38", "D41")
        ///   - TensionRebarGrade: 規格 (例: "SD390", "SD685")
        ///   - TDorTB: 配置直径 / 配置辺長 (mm)
        /// 設定後 CaptainPile.Update() で SetBasicProperties / MN-N インタラクションを再計算する。
        /// </summary>
        private static void ApplyCaptainPileSettings(PileBodyInput pileBody, CaptainPileDto dto)
        {
            try
            {
            var pileTop = pileBody.PileTop;
            if (pileTop == null) return;

            // CaptainPile 未生成なら作成 (PileCapFc/Ec をパイルキャップから取得)
            pileTop.CaptainPile ??= new PileDesign.Models.CaptainPile(pileTop.PileCapFc, pileTop.PileCapEc);
            var cp = pileTop.CaptainPile;

            // PCリング選択 (PCRings リストから名前一致するものを採用)
            if (!string.IsNullOrEmpty(dto.PCRingName) && cp.PCRings != null)
            {
                var target = cp.PCRings.FirstOrDefault(r => r.Name == dto.PCRingName);
                if (target != null)
                {
                    cp.PCRing = target;
                    cp.SelectedPCRingName = dto.PCRingName;
                    cp.D = target.D;
                }
            }

            // 絞り率
            if (dto.Nu.HasValue) cp.Nu = dto.Nu.Value;

            // 引張定着筋
            if (cp.CTPTensionRebars != null)
            {
                cp.CTPTensionRebars.HasTensionRebars = dto.HasTensionRebars;
                if (dto.HasTensionRebars)
                {
                    if (!string.IsNullOrEmpty(dto.TensionRebarArrangement))
                    {
                        bool isCircle = dto.TensionRebarArrangement == "円形配置";
                        cp.CTPTensionRebars.IsCircleArrangement = isCircle;
                        cp.CTPTensionRebars.IsSquareArrangement = !isCircle;
                    }
                    if (dto.TensionRebarNumber.HasValue)
                    {
                        if (cp.CTPTensionRebars.IsCircleArrangement)
                            cp.CTPTensionRebars.SelectedBarNumberCircle = dto.TensionRebarNumber.Value;
                        else
                            cp.CTPTensionRebars.SelectedBarNumberSquare = dto.TensionRebarNumber.Value;
                    }
                    if (!string.IsNullOrEmpty(dto.TensionRebarDia))
                        cp.CTPTensionRebars.SelectedTensionAnchorDia = dto.TensionRebarDia;
                    if (!string.IsNullOrEmpty(dto.TensionRebarGrade))
                        cp.CTPTensionRebars.SelectedTensionAnchorGrade = dto.TensionRebarGrade;
                    if (dto.TDorTB.HasValue)
                        cp.CTPTensionRebars.TDorTB = dto.TDorTB.Value;
                }
            }

            // 再計算 (CTPConcrete 再構築 + MN-N インタラクション + θ-M 関係)
            cp.Update();

            // 諸元表示を更新
            pileTop.SelectedPileTopSpecification = cp.GetCombinedSpecs();
            }
            catch (Exception ex)
            {
                // CaptainPile 設定の失敗は致命的でない — ユーザーが手動で再設定可能
                System.Diagnostics.Debug.WriteLine(
                    $"[PileExampleLoader.ApplyCaptainPileSettings] CaptainPile 設定中にエラー: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// キャプリングパイル工法の設定を pileBody.PileTop.CapringPile に適用する。
        ///   - PCRingName: PCリング名 (例: "800N" - CSV 表記、ハイフン無し)
        ///   - HasTensionBars: 引張定着筋の有無
        ///   - TensionBarName: 引張定着筋テーブル名
        /// PileBodyInput.GetMThetaRelationship (line 558〜) の自動初期化ロジックを踏襲。
        /// 失敗してもロード全体は継続する (CapringPile はその後ユーザーが手動で再設定可能)。
        /// </summary>
        private static void ApplyCapringPileSettings(PileBodyInput pileBody, CapringPileDto dto)
        {
            try
            {
                var pileTop = pileBody.PileTop;
                if (pileTop == null) return;

                // CapringPile を新規作成 (PileBodyInput.cs:558 のパターン)
                var cp = new PileDesign.Models.CapringPile(pileTop.PileCapEc)
                {
                    PileBodyType = pileBody.PileBodyType,
                    PileCapFc = pileTop.PileCapFc,
                    PileCapEc = pileTop.PileCapEc,
                };
                cp.LoadPCRingOptions();
                cp.LoadTensionBarOptions();

                // 最上部杭区間から杭径取得
                var topSec = pileBody.PileBodySegments?.FirstOrDefault()?.PileSection;
                if (topSec != null && cp.PCRings != null && cp.PCRings.Count > 0)
                {
                    bool isSp = (pileBody.PileBodyType ?? "").Contains(PileTypeNames.SteelPipe);
                    double dia = (isSp && topSec.PipeDia > 0) ? topSec.PipeDia : topSec.PileDiameter;

                    // PCリング選択: 指定名 優先、なければ杭径から自動
                    PileDesign.Models.PileLibrary.CapringPCRing? ring = null;
                    if (!string.IsNullOrEmpty(dto.PCRingName))
                    {
                        ring = cp.PCRings.FirstOrDefault(r => r.Name == dto.PCRingName);
                    }
                    if (ring == null && dia > 0)
                    {
                        int targetSize = (int)Math.Ceiling(dia / 50.0) * 50;
                        if (targetSize < 300) targetSize = 300;
                        if (targetSize > 1200) targetSize = 1200;
                        ring = cp.PCRings.FirstOrDefault(r => r.Name == $"{targetSize}N")
                            ?? cp.PCRings.FirstOrDefault(r => (r.Name ?? "").EndsWith("N") && r.D >= dia)
                            ?? cp.PCRings.FirstOrDefault();
                    }
                    if (ring != null)
                    {
                        cp.PCRing = ring;
                        cp.D = ring.D;
                        cp.SelectedPCRingName = ring.Name ?? "";
                    }
                    if (isSp)
                    {
                        cp.IsConcreteFilledSteelPipe = true;
                        cp.SteelPipeWallThickness = topSec.PipeTs;
                    }
                }

                // 引張定着筋
                cp.HasTensionBars = dto.HasTensionBars;
                if (dto.HasTensionBars && !string.IsNullOrEmpty(dto.TensionBarName)
                    && cp.TensionBars != null && cp.TensionBars.Any(b => b.Name == dto.TensionBarName))
                {
                    cp.SelectedTensionBarName = dto.TensionBarName;
                }

                cp.Update();
                pileTop.CapringPile = cp;
                if (cp.PCRing != null)
                    pileTop.SelectedPileTopSpecification = cp.GetCombinedSpecs();
            }
            catch (Exception ex)
            {
                // CapringPile 設定の失敗は致命的でない — ユーザーが手動で再設定可能
                System.Diagnostics.Debug.WriteLine(
                    $"[PileExampleLoader.ApplyCapringPileSettings] CapringPile 設定中にエラー: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
