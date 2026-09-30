using PileDesign.Common;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;


namespace PileDesign.Services
{
    /// <summary>
    /// 解析を始める前の入力検査。<b>画面から呼ぶ</b>もので、問題があればダイアログで知らせる。
    ///
    /// 以前は Models/ にあったが、ここは断面計算や FEM から呼ばれない
    /// (呼ぶのは水平解析・単杭沈下・群杭沈下の各 ViewModel)。
    /// Models/ に置いたままだと「下の層は画面を触らない」という決まりの例外になり、
    /// 本当に直すべき違反 (解析の途中からダイアログを出すもの) が見つけにくくなる。
    /// </summary>
    class CheckInputData
    {
        /// <summary>
        /// 解析実行は止めないがユーザーに確認させたい「注意レベル」の入力警告を収集する (文だけ)。
        /// 重さ・場所・推奨する操作つきは <see cref="CollectInputWarningDiagnostics"/>。
        /// </summary>
        public static List<string> CollectInputWarnings(InputModel inputModel)
            => CollectInputWarningDiagnostics(inputModel).Select(d => d.Message).ToList();

        /// <summary>
        /// 解析前の確認の一覧に出す行。重い順 (結果に影響 → 情報) に並べ、行ごとに重さと推奨する操作を付ける。
        /// </summary>
        public static List<string> DescribeInputWarnings(InputModel inputModel)
            => DiagnosticSelection.BySeverity(CollectInputWarningDiagnostics(inputModel)).Select(DiagnosticSelection.FormatForList).ToList();

        /// <summary>
        /// 解析実行は止めないがユーザーに確認させたい入力の注意を、重さ・場所・推奨する操作つきで集める。
        /// <list type="bullet">
        /// <item>結果に影響 (<see cref="DiagnosticSeverity.Warning"/>): 支持条件・地盤反力・適用範囲など、計算の値が変わるもの。</item>
        /// <item>情報 (<see cref="DiagnosticSeverity.Info"/>): 解析では無視する・慣例と違うだけで、結果は変わらないもの。</item>
        /// </list>
        /// </summary>
        public static List<Diagnostic> CollectInputWarningDiagnostics(InputModel inputModel)
        {
            var warnings = new List<Diagnostic>();
            if (inputModel == null) return warnings;
            const DiagnosticSeverity affects = DiagnosticSeverity.Warning;

            // 杭の鉛直地盤ばね (P-S ばね) を入力のとおりに付けられない杭。
            // 付けられないとモデル作成は杭先端を鉛直に固定して続けるので、支持条件が変わることを先に知らせる
            foreach (var problem in PileDesign.FEM.AnalysisModelling.DescribeVerticalSpringProblems(inputModel))
                warnings.Add(Diagnostic.Notice(affects, DiagnosticTarget.Nowhere, "鉛直地盤ばね: " + problem,
                    "杭体の先端の入力と、地盤の支持層の入力を確かめる"));

            // 杭体の形状・材料の注意 (節杭が最上段にある・Ec が低め等)。解析は止めない
            warnings.AddRange(CollectPileBodyNotices(inputModel));

            // 各杭の ΔZc (接合点 − 杭頭オフセット)
            if (inputModel.PileLayoutItems != null)
            {
                for (int i = 0; i < inputModel.PileLayoutItems.Count; i++)
                {
                    var p = inputModel.PileLayoutItems[i];
                    if (p == null) continue;
                    var at = DiagnosticTarget.Pile(p.No);
                    if (p.FoundationBeamDeltaZc <= 0)
                        warnings.Add(Diagnostic.Notice(affects, at,
                            $"杭 No.{p.No}: 接合-杭頭 ΔZc = {p.FoundationBeamDeltaZc:N3} (>0 で接合点が杭頭の上に来るのが正常)。"));

                    // 群杭係数 ξ は kh0 に、杭間隔比 R/B は後方杭の py に効く。
                    // どちらも杭配置の入力で、入れ忘れると群杭の影響が消えたまま計算が通る。
                    if (p.GroupPileFactor > 1.0)
                        warnings.Add(Diagnostic.Notice(affects, at, $"杭 No.{p.No}: 群杭係数 ξ = {p.GroupPileFactor:N3} が 1 を超えています " +
                                     "(群杭は水平地盤反力を下げる側なので 1 以下が通常)。"));
                    if (!(p.PileSpacingFactor > 0))
                        warnings.Add(Diagnostic.Notice(affects, at, $"杭 No.{p.No}: 杭間隔比 R/B が未入力です。" +
                                     "後方杭の塑性水平地盤反力 py は群杭の影響を考えない (単杭と同じ) 扱いで計算します。"));
                }
            }

            // 各地盤の既存検証 (Es=0 / 粘性土 Cu=0 / 層・土質点深度順序逆 / N=0)
            if (inputModel.GroundsInput != null)
            {
                for (int i = 0; i < inputModel.GroundsInput.Count; i++)
                {
                    var gi = inputModel.GroundsInput[i];
                    if (gi == null) continue;
                    if (!gi.ValidateForAnalysis(out string msg))
                    {
                        foreach (var raw in (msg ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = raw.TrimStart('-', ' ', '\t');
                            if (!string.IsNullOrWhiteSpace(t))
                                warnings.Add(Diagnostic.Notice(affects, DiagnosticTarget.Ground(i + 1), $"地盤 {i + 1}: {t.Trim()}"));
                        }
                    }
                }
            }

            // メーカー別の高支持力杭工法の適用範囲。
            // 範囲外でもクランプ後の値で計算は続けるため、エラーではなく警告として出す。
            var soilPiles = inputModel.ElementDivision?.SoilPiles;
            if (soilPiles != null)
            {
                foreach (var soilPile in soilPiles)
                {
                    if (soilPile == null) continue;
                    var at = DiagnosticTarget.PileBody(soilPile.PileBodyNo);
                    const string clamp = "適用範囲の上下限に丸めた値で計算しています。杭体・地盤の入力を確かめる";
                    foreach (var w in soilPile.ValidateSmartMagnumRange())
                        warnings.Add(Diagnostic.Notice(affects, at, $"Smart-MAGNUM {w}", clamp));
                    foreach (var w in soilPile.ValidateHybridKneadingRange())
                        warnings.Add(Diagnostic.Notice(affects, at, $"Hybridニーディング {w}", clamp));
                }
            }

            // BCJ評定-FD0356-08 の適用範囲。評定どおりに設定しているときだけ検査する。
            // 範囲外でも計算は止めず、警告として出す。
            if (ConcreteModelOptions.FollowsKctbEvaluation && inputModel.PileBodies != null)
            {
                for (int i = 0; i < inputModel.PileBodies.Count; i++)
                {
                    foreach (var w in KctbApplicableRange.Validate(inputModel.PileBodies[i]))
                        warnings.Add(Diagnostic.Notice(affects, DiagnosticTarget.PileBody(i + 1), $"BCJ評定-FD0356-08 {w}"));
                }
            }

            // どこにもつながっていない一般節点。
            // 解析モデルからは取り除いて計算を続けるので、結果には影響しない (情報)。
            // 節点を作ったあとに基礎梁を消した (まだ作っていない) 場合に出る。
            foreach (var n in inputModel.GetUnconnectedGeneralNodes())
            {
                warnings.Add(Diagnostic.Notice(DiagnosticSeverity.Info, DiagnosticTarget.Nowhere,
                    $"一般節点 No.{n.No} (X={n.X:N3}, Y={n.Y:N3}, Z={n.Z:N3}): どの基礎梁にもつながっていません。解析では無視します。",
                    "不要なら一般節点を消す"));
            }

            // 意図しない入力の可能性が高いもの (同じ位置に複数の杭など)。
            // 解析は通るので警告にとどめる。
            foreach (var w in ModelConnectivityCheck.CollectWarnings(inputModel))
                warnings.Add(Diagnostic.Notice(affects, DiagnosticTarget.Nowhere, w, "杭配置・基礎梁の配置を確かめる"));

            return warnings;
        }

        /// <summary>
        /// 解析実行ゲート: 入力データの整合性を検証する。
        ///   - エラー検出時: エラー内容とともに警告ダイアログを表示し false を返す (解析中止)
        ///   - 問題なし: ダイアログを出さずに true を返す (解析続行)
        /// 水平解析・単杭沈下・群杭沈下・基礎梁考慮鉛直 各解析の開始直前に呼ぶこと。
        /// </summary>
        public static bool ValidateForAnalysis(InputModel inputModel, string analysisName = "解析")
        {
            var problems = CollectAnalysisBlockers(inputModel);
            if (problems.Count == 0) return true; // OK: ダイアログなしで続行
            ShowBlockers(inputModel, problems, analysisName, "入力データ");
            return false;
        }

        /// <summary>
        /// 解析を止める入力の問題をすべて集める (場所つき)。<see cref="ValidateForAnalysis"/> の中身。
        /// </summary>
        internal static List<Diagnostic> CollectAnalysisBlockers(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            problems.AddRange(CollectSoilPileProblems(inputModel));
            problems.AddRange(CollectEmbedmentProblems(inputModel));
            problems.AddRange(CollectPileBodyGeometryProblems(inputModel));
            problems.AddRange(CollectGroundLayerGeometryProblems(inputModel));
            problems.AddRange(CollectGroupPileFactorProblems(inputModel));
            problems.AddRange(CollectLoadCombinationProblems(inputModel));

            // モデルの「つながり」。剛性行列を組んでから初めて分かる不安定は、
            // 利用者に原因が読み取れない (「対角成分がゼロ」としか出ない)。
            // 入力の段階で分かるものは、ここで名指しで止める。
            problems.AddRange(ModelConnectivityCheck.CollectErrorDiagnostics(inputModel));
            return problems;
        }

        /// <summary>
        /// 解析を止めた入力の問題を知らせる。問題の場所 (番号は文から拾わず <see cref="Diagnostic.Target"/> から) で
        /// メイン画面の杭を選び、その範囲 (特定の杭か、共有の杭体・地盤を使う杭すべてか) を書く。
        /// 入力画面を開ける場所なら、解析のウィンドウを閉じたあとに開くかを訊く
        /// (解析のウィンドウを開いたまま入力を直すと、要素分割が済んだ前提が崩れるので、閉じてから開く)。
        /// </summary>
        internal static void ShowBlockers(InputModel? inputModel, IReadOnlyList<Diagnostic> problems, string analysisName, string what)
        {
            foreach (var p in problems)
                Serilog.Log.Warning("[入力の検査] {AnalysisName}: {Diagnostic}", analysisName, p.ToLogLine());

            var selection = DiagnosticSelection.Resolve(problems, inputModel);
            inputModel?.SelectForReview(selection);

            string text = $"{what}に以下の問題があります。{analysisName}を中止します。\n\n"
                        + string.Join("\n", problems.Select(p => p.Message));
            if (DiagnosticSelection.DescribeRemedies(problems) is { } remedies) text += "\n\n" + remedies;
            if (DiagnosticSelection.DescribeSelection(selection) is { } scope) text += "\n\n" + scope;
            if (problems.Count > 1)
                text += "\n\n「解析条件/解析」タブの「入力の診断」で、指摘を一覧にして順に直し、再検査できます。";

            var destination = DiagnosticSelection.FirstNavigable(problems);
            if (destination == null || inputModel == null)
            {
                MessageService.Show(text, $"{analysisName} 入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string where = DiagnosticSelection.DestinationName(DiagnosticSelection.DestinationOf(destination));
            text += $"\n\n「はい」を押すと、このウィンドウを閉じたあとに{where} ({destination.Label}) を開きます。";
            if (MessageService.Show(text, $"{analysisName} 入力エラー", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                inputModel.RequestInputNavigation(destination);
        }

        /// <summary>問題の文を 1 行ずつ並べる (文字列で返す旧来の検査の形)。</summary>
        private static string Lines(IEnumerable<Diagnostic> problems) => string.Concat(problems.Select(p => p.Message + "\n"));

        /// <summary>
        /// 任意入力の地盤変位の検査。水平解析の前に、これから解く荷重レベル・液状化の別について見る。
        ///
        /// <para>任意入力が有効な地盤で、そのケースの点が 1 つも無いと、補間は 0 mm を返し、地盤変位を
        /// <b>無視したまま</b>計算が進んだ。画面には警告が出るが解析前の検査を通っていないので、ファイルを
        /// 読み込んだあとなどに気づかず解ける。点の並び (上から下へ標高が下がる)・同じ標高・数値でない値も、
        /// 補間が 0 mm や不定な値を返す原因なので止める。</para>
        ///
        /// 対象は杭か根入部が使っている地盤だけ。
        /// </summary>
        internal static List<string> DescribeCustomDisplacementProblems(
            InputModel inputModel, IEnumerable<(int Level, bool IsLiquefaction)> cases)
            => CollectCustomDisplacementProblems(inputModel, cases).Select(p => p.Message).ToList();

        /// <summary><see cref="DescribeCustomDisplacementProblems"/> の場所つきの形 (地盤)。</summary>
        internal static List<Diagnostic> CollectCustomDisplacementProblems(
            InputModel inputModel, IEnumerable<(int Level, bool IsLiquefaction)> cases)
        {
            var problems = new List<Diagnostic>();
            var grounds = inputModel?.GroundsInput;
            if (grounds == null || grounds.Count == 0) return problems;

            var used = new SortedSet<int>();
            foreach (var p in inputModel!.PileLayoutItems ?? [])
                if (p != null && p.GroundNo >= 1 && p.GroundNo <= grounds.Count) used.Add(p.GroundNo);
            if ((inputModel.EmbedmentInput?.EmbedmentLayersCount ?? 0) > 0
                && inputModel.EmbedmentInput!.GroundNo >= 1 && inputModel.EmbedmentInput.GroundNo <= grounds.Count)
                used.Add(inputModel.EmbedmentInput.GroundNo);

            var caseIndices = cases.Select(c => CustomDisplacementProfile.CaseIndexOf(c.Level, c.IsLiquefaction))
                .Distinct().OrderBy(i => i).ToList();

            foreach (int groundNo in used)
            {
                var custom = grounds[groundNo - 1]?.CustomDisplacementProfile;
                if (custom?.IsEnabled != true) continue;
                foreach (int ci in caseIndices)
                {
                    string where = $"地盤 {groundNo} の任意入力の地盤変位 [{CustomDisplacementProfile.CaseName(ci)}]";
                    var profile = custom.GetProfile(ci);
                    var target = DiagnosticTarget.Ground(groundNo);
                    if (profile == null || profile.Count == 0)
                    {
                        problems.Add(Diagnostic.Input(target, $"{where}: 点が入力されていません。このまま解くと地盤変位を 0 として計算します"));
                        continue;
                    }
                    foreach (var problem in CustomDisplacementProfile.DescribeProblems(profile))
                        problems.Add(Diagnostic.Input(target, $"{where}: {problem}"));
                }
            }
            return problems;
        }

        /// <summary>
        /// 群杭係数 ξ の検査。
        ///
        /// ξ は基準水平地盤反力係数 kh0 に掛かる (基礎指針'19 (6.6.12)) ので、0 以下だと
        /// <b>その杭の水平地盤ばねが全深さで消える</b>。剛性行列が特異になって解析が解けないか、
        /// 解けても意味のない結果になるため、ここで名指しして止める。
        /// 1 を超える値は物理的にありえないが計算は成り立つので、警告
        /// (<see cref="CollectInputWarnings"/>) にとどめる。
        /// </summary>
        public static string CheckGroupPileFactor(InputModel inputModel, string message)
            => message + Lines(CollectGroupPileFactorProblems(inputModel));

        internal static List<Diagnostic> CollectGroupPileFactorProblems(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            if (inputModel?.PileLayoutItems == null) return problems;

            foreach (var p in inputModel.PileLayoutItems)
            {
                if (p == null) continue;
                if (!(p.GroupPileFactor > 0) || !double.IsFinite(p.GroupPileFactor))
                {
                    problems.Add(Diagnostic.InputAt(DiagnosticTarget.Pile(p.No),
                        $"群杭係数 ξ = {p.GroupPileFactor} です " +
                        "(0 より大きく 1 以下で入力してください)。" +
                        "ξ は基準水平地盤反力係数 kh0 に掛かるため、0 以下だと水平地盤ばねが無くなります。"));
                }
            }
            return problems;
        }

        /// <summary>
        /// 慣性力が 0 になる「荷重ケース × 組合せ」の検査。
        ///
        /// <para>非線形反復の収束判定は残差比 ‖R‖²/‖F‖² (<c>AnaModel.FindR</c>) で、分母 F は
        /// 慣性力 (外力) です。βU·上部構造慣性力 と βL·基礎部慣性力 がともに 0 になる組合せでは
        /// F=0 となり、比が固定値 (1e30) のまま反復上限まで回ります。地盤変位を強制変位で
        /// 与えるケースは応答そのものは出るので、<b>収束しないまま結果が残る</b>のが厄介です。</para>
        ///
        /// <para>組合せ係数のスライダーが 0.5 未満だと
        /// <see cref="ViewModels.LoadCaseViewModel.GetCombinations"/> は (0, 0.0, 0.0, 0.0) を返すため、
        /// これは通常操作で到達します。解法 (残差比の分母) は触らず、入力の段階で名指しして止めます。</para>
        ///
        /// <para>慣性力がどちらも 0 の荷重ケースは水平解析側でスキップされる
        /// (<c>HorizontalCalculationViewModel.Run</c>) ので対象外。VL ケースは水平荷重 0 で
        /// 組合せを 1 回だけ走らせる別扱いなので、ここでも対象外です。</para>
        ///
        /// <para><b>基本設定で収束判定の基準値を外力以外にしている場合は止めません。</b>
        /// 強制変位の反力や内力を基準にすれば慣性力 0 でも判定が成り立つので、
        /// 止める理由が無くなります (<see cref="ResidualReferenceMode"/>)。</para>
        /// </summary>
        public static string CheckLoadCombinations(InputModel inputModel, string message)
            => message + Lines(CollectLoadCombinationProblems(inputModel));

        internal static List<Diagnostic> CollectLoadCombinationProblems(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            var loadCases = inputModel?.LoadCasesInput?.AnalysisTargetSeismicLoadCases;
            var combinations = inputModel?.LoadCasesInput?.AllLoadCombinations;
            if (loadCases == null || combinations == null) return problems;

            // 収束判定の基準値が外力以外なら、慣性力 0 でも判定が成り立つので止めない
            var reference = inputModel?.FundamentalInput?.ResidualReference ?? ResidualReferenceModes.Default;
            if (reference.WorksWithoutInertia()) return problems;

            foreach (var lc in loadCases)
            {
                if (lc == null) continue;
                // 慣性力がどちらも 0 のケースは解析側でスキップされる (組合せの責任ではない)。
                if (lc.UpperMassForce == 0 && lc.FoundationMassForce == 0) continue;

                foreach (var comb in combinations)
                {
                    if (comb == null) continue;
                    // 外力ベクトルの大きさが 0 になるのは、上部構造・基礎部の項が両方 0 のとき。
                    // (2 つは別の節点に載るので、符号が逆でも打ち消し合わない)
                    bool upperZero = comb.Beta1 * lc.UpperMassForce == 0;
                    bool foundationZero = comb.Beta2 * lc.FoundationMassForce == 0;
                    if (!upperZero || !foundationZero) continue;

                    string caseName = $"レベル{lc.Level} 荷重ケース No.{lc.No} × 組合せ {comb.Name}";
                    problems.Add(Diagnostic.InputAt(DiagnosticTarget.LoadCase(caseName),
                        "βU と βL で慣性力が 0 になり、収束判定 (残差/外力) が成り立ちません。" +
                        "荷重条件の組合せ係数を 0.5〜1.0 にしてください " +
                        "(基本設定で収束判定の基準値を変えれば、慣性力 0 でも解けます)。"));
                }
            }
            return problems;
        }

        /// <summary>
        /// 杭体ジオメトリ・断面整合性のチェック (MED #5, #6, #7)。
        ///   - 杭セグメント長 ≤ 0
        ///   - ConcreteOutDia ≤ 0 (場所打ち系)
        ///   - MainBarDr が 0 < MainBarDr < ConcreteOutDia を満たすか (場所打ち系)
        ///   - ConcreteFc ≤ 0
        /// 不整合があれば message に追記して返す。
        /// </summary>
        public static string CheckPileBodyGeometry(InputModel inputModel, string message)
            => message + Lines(CollectPileBodyGeometryProblems(inputModel));

        internal static List<Diagnostic> CollectPileBodyGeometryProblems(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            if (inputModel?.PileBodies == null) return problems;

            for (int i = 0; i < inputModel.PileBodies.Count; i++)
            {
                var pb = inputModel.PileBodies[i];
                if (pb?.PileBodySegments == null) continue;
                int pbNo = i + 1;

                for (int j = 0; j < pb.PileBodySegments.Count; j++)
                {
                    var seg = pb.PileBodySegments[j];
                    if (seg == null) continue;
                    var at = DiagnosticTarget.PileBodySegment(pbNo, j + 1);
                    var sec = seg.PileSection;

                    // 数値でない値 (NaN・無限大) も拒む。「0 以下」の比較は NaN で偽になり、素通りする
                    // (入力欄は NaN を受け付けないが、古いファイル・手で編集したファイル・計算途中の値から入りうる)
                    if (!IsPositive(seg.SegmentLength))
                        problems.Add(Diagnostic.InputAt(at, $"区間長が 0 以下か数値ではありません ({seg.SegmentLength})."));

                    if (sec == null) continue;

                    // 節杭の注意 (最上段にある・拡頭径が合わない) は解析を止めない。警告 (CollectInputWarnings) で知らせる

                    // 場所打ち系 (PileBodyType=場所打ち鉄筋コンクリート杭 / 場所打ち鋼管コンクリート杭+鉄筋コンクリート部)
                    bool isInsituRC =
                        sec.PileBodyType == PileTypeNames.InsituRc ||
                        (sec.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && sec.PileSectionType == PileTypeNames.RcSection);

                    if (isInsituRC)
                    {
                        if (!IsPositive(sec.ConcreteOutDia))
                            problems.Add(Diagnostic.InputAt(at, $"コンクリート外径が 0 以下か数値ではありません ({sec.ConcreteOutDia})."));
                        if (sec.MainBarNum > 0 && IsPositive(sec.ConcreteOutDia)
                            && !(IsPositive(sec.MainBarDr) && sec.MainBarDr < sec.ConcreteOutDia))
                        {
                            problems.Add(Diagnostic.InputAt(at, $"主筋配置直径 (MainBarDr={sec.MainBarDr}) が外径 ({sec.ConcreteOutDia}) との関係で不正です " +
                                        $"(0 < MainBarDr < 外径 を満たすこと)."));
                        }
                        // 耐力・剛性の式に入る値。範囲外だと √ の中が負 (せん断補強筋の √(pw·σwy))、
                        // 0 で割る (ヤング係数比 n = Er/Ec) などで、耐力・剛性が数値でなくなる
                        problems.AddRange(CollectInsituRcMaterialProblems(sec, at));
                    }

                    // 断面の剛性 (解析の入力そのもの)。数値でないまま進むと、剛性行列や結果に数値でない値が混ざる
                    problems.AddRange(CollectSectionStiffnessProblems(sec, at));

                    if (!IsPositive(sec.ConcreteFc) && UsesConcrete(sec))
                    {
                        problems.Add(Diagnostic.InputAt(at, $"コンクリート設計基準強度 Fc が 0 以下か数値ではありません ({sec.ConcreteFc})."));
                    }

                    // Ec は正でも、杭のコンクリートとしてありえないほど小さければ止める。
                    // γ を下限 (以前は 0.1 kN/m³) にすると Ec ≈ 0.4 N/mm² になり、「0 以下」の検査を通って
                    // 曲げ剛性が通常の約 7 万分の 1 のまま解析が走った (画面の Ec は整数表示で「0」に見えた)
                    if (UsesConcrete(sec) && IsPositive(sec.ConcreteE) && sec.ConcreteE < MinConcreteE)
                    {
                        problems.Add(Diagnostic.InputAt(at, $"コンクリートのヤング係数 Ec が {PileDesign.Common.NumberDisplay.Modulus(sec.ConcreteE)} N/mm² で、"
                            + $"杭のコンクリートとしては小さすぎます ({MinConcreteE:N0} N/mm² 未満)。単位体積重量 γ・設計基準強度 Fc・ξ を確認してください."));
                    }
                }
            }
            return problems;
        }

        /// <summary>
        /// 場所打ち RC の材料の値の範囲外。ヤング係数・強度は正の数、せん断補強筋比・強度は 0 以上、
        /// せん断補強筋があるならピッチは正の数。
        /// </summary>
        internal static List<Diagnostic> CollectInsituRcMaterialProblems(PileSection sec, DiagnosticTarget at)
        {
            var problems = new List<Diagnostic>();
            void Add(string detail) => problems.Add(Diagnostic.InputAt(at, detail));
            if (!IsPositive(sec.ConcreteE))
                Add($"コンクリートのヤング係数 Ec が 0 以下か数値ではありません ({sec.ConcreteE}).");
            if (!IsPositive(sec.ConcreteGsi))
                Add($"コンクリートの強度の有効係数 ξ が 0 以下か数値ではありません ({sec.ConcreteGsi}).");
            if (sec.MainBarNum > 0)
            {
                if (!IsPositive(sec.MainBarEr))
                    Add($"主筋のヤング係数 Er が 0 以下か数値ではありません ({sec.MainBarEr}).");
                if (!IsPositive(sec.MainBarAg))
                    Add($"主筋の断面積が 0 以下か数値ではありません ({sec.MainBarAg}).");
            }
            if (!(double.IsFinite(sec.HoopPw) && sec.HoopPw >= 0))
                Add($"せん断補強筋比 pw が 0 未満か数値ではありません ({sec.HoopPw}).");
            if (!(double.IsFinite(sec.HoopSigmay) && sec.HoopSigmay >= 0))
                Add($"せん断補強筋の降伏強度が 0 未満か数値ではありません ({sec.HoopSigmay}).");
            if (double.IsFinite(sec.HoopBarArea) && sec.HoopBarArea > 0 && !IsPositive(sec.HoopSpacing))
                Add($"せん断補強筋のピッチが 0 以下か数値ではありません ({sec.HoopSpacing}).");
            return problems;
        }

        /// <summary>
        /// 断面の軸剛性 EA・曲げ剛性 EI が正の有限の数にならなければ、その値。
        /// 入力の個々の値が範囲内でも、組み合わせ (腐食代が肉厚以上など) で 0 以下・数値でなくなることがある。
        /// 断面の計算そのものが失敗したときも、例外で解析を止めずにここで知らせる。
        /// </summary>
        internal static List<Diagnostic> CollectSectionStiffnessProblems(PileSection sec, DiagnosticTarget at)
        {
            try
            {
                var problems = new List<Diagnostic>();
                if (!IsPositive(sec.EA))
                    problems.Add(Diagnostic.InputAt(at, $"断面の軸剛性 EA が正の数になりません ({sec.EA})。断面の寸法・材料を確認してください."));
                if (!IsPositive(sec.EI))
                    problems.Add(Diagnostic.InputAt(at, $"断面の曲げ剛性 EI が正の数になりません ({sec.EI})。断面の寸法・材料を確認してください."));
                return problems;
            }
            catch (Exception ex) when (ex is ArgumentException or ArithmeticException or InvalidOperationException)
            {
                return [Diagnostic.InputAt(at, $"断面の剛性を計算できません ({ex.Message.Split('\n')[0].Trim()}).")];
            }
        }

        /// <summary>
        /// コンクリートのヤング係数 Ec の下限 [N/mm²]。これ未満は杭のコンクリートとしてありえないので解析を止める
        /// (普通コンクリートで 2〜4 万、軽量・低強度でも 1 万前後)。
        /// </summary>
        internal const double MinConcreteE = 1_000;

        /// <summary>これ未満の Ec は、止めないが警告で知らせる [N/mm²] (軽量・低強度など理由がありうる範囲)。</summary>
        internal const double LowConcreteE = 10_000;

        /// <summary>断面がコンクリートを持つか (純鋼管杭と、場所打ち鋼管コンクリート杭の鋼管部は持たない)。</summary>
        private static bool UsesConcrete(PileSection sec)
            => sec.PileBodyType != PileTypeNames.SteelPipe
               && !(sec.PileBodyType == PileTypeNames.InsituSteelPipeConcrete && sec.PileSectionType == PileTypeNames.SteelPipeSection);

        /// <summary>正の有限の数か (NaN・無限大・0 以下は false)。「0 以下」の比較は NaN を素通りさせるので、こちらで判定する。</summary>
        private static bool IsPositive(double value) => value > 0 && double.IsFinite(value);

        /// <summary>
        /// 杭が指す杭体番号・地盤番号が、入力にある範囲の番号か。範囲の外ならその説明 (どの杭か) を返し、よければ null。
        /// </summary>
        internal static string? DescribeBadPileReference(InputModel inputModel, PileLayoutDataItem pile)
        {
            int bodies = inputModel.PileBodies?.Count ?? 0;
            int grounds = inputModel.GroundsInput?.Count ?? 0;
            if (inputModel.PileBodyAt(pile.PileBodyNo) == null)
                return $"杭 No.{pile.No}: 杭体番号 {pile.PileBodyNo} の杭体がありません (杭体は {bodies} 個)。杭配置で杭体を選び直してください。";
            if (inputModel.GroundAt(pile.GroundNo) == null)
                return $"杭 No.{pile.No}: 地盤番号 {pile.GroundNo} の地盤がありません (地盤は {grounds} 個)。杭配置で地盤を選び直してください。";
            return null;
        }

        /// <summary>
        /// 杭体の形状についての注意 (解析は止めない)。<see cref="CollectInputWarnings"/> が警告として出す。
        /// <list type="bullet">
        /// <item>節杭が最上段の区間にある。節杭は上杭に継手で接合する下杭として使うのが一般的だが、
        ///   Smart-MAGNUM / Hybrid ニーディングのように先端が節杭である前提の工法を 1 区間でモデル化することはありうる。</item>
        /// <item>節杭の拡頭径が直上区間の径と合わない。標準タイプとして扱って計算は続ける。</item>
        /// </list>
        /// 以前は解析を止めるエラーの一覧に入れていたので、注意のつもりの項目で解析できなかった。
        /// </summary>
        internal static List<string> DescribePileBodyNotices(InputModel inputModel)
            => CollectPileBodyNotices(inputModel).Select(d => d.Message).ToList();

        /// <summary><see cref="DescribePileBodyNotices"/> の重さ・場所つきの形。</summary>
        internal static List<Diagnostic> CollectPileBodyNotices(InputModel inputModel)
        {
            var notices = new List<Diagnostic>();
            if (inputModel?.PileBodies == null) return notices;
            for (int i = 0; i < inputModel.PileBodies.Count; i++)
            {
                var segments = inputModel.PileBodies[i]?.PileBodySegments;
                if (segments == null) continue;
                for (int j = 0; j < segments.Count; j++)
                {
                    var sec = segments[j]?.PileSection;
                    if (sec == null) continue;
                    var at = DiagnosticTarget.PileBodySegment(i + 1, j + 1);
                    // Ec が止めるほどではないが低い (軽量・低強度など理由がありうる)。剛性に効くので結果に影響する
                    if (UsesConcrete(sec) && sec.ConcreteE >= MinConcreteE && sec.ConcreteE < LowConcreteE)
                        notices.Add(Diagnostic.Notice(DiagnosticSeverity.Warning, at,
                            $"杭体{i + 1} 区間{j + 1}: コンクリートのヤング係数 Ec が {sec.ConcreteE:N0} N/mm² と低めです "
                            + $"({LowConcreteE:N0} N/mm² 未満)。単位体積重量 γ・設計基準強度 Fc・ξ を確認してください。"));
                    if (!sec.IsNodularPile) continue;
                    // 節杭が最上段にあるのは慣例と違うだけで、計算はそのまま成り立つ (情報)
                    if (j == 0)
                        notices.Add(Diagnostic.Notice(DiagnosticSeverity.Info, at,
                            $"杭体{i + 1} 区間{j + 1}: {sec.PileSectionType} が最上段の区間にあります " +
                            "(節杭は上杭に継手で接合する下杭として使うのが一般的です)。"));
                    // 拡頭径が合わないと標準タイプとして扱う (断面が変わるので結果に影響する)
                    if (!string.IsNullOrEmpty(sec.NodularHeadNote) && sec.NodularHeadNote.Contains("一致する拡頭径がありません"))
                        notices.Add(Diagnostic.Notice(DiagnosticSeverity.Warning, at, $"杭体{i + 1} 区間{j + 1}: {sec.NodularHeadNote}。"));
                }
            }
            return notices;
        }

        /// <summary>
        /// 地盤層厚の整合性チェック (MED #6)。
        ///   - 各 GroundLayer の LayerThickness が 0 以下なら指摘
        /// </summary>
        public static string CheckGroundLayerGeometry(InputModel inputModel, string message)
            => message + Lines(CollectGroundLayerGeometryProblems(inputModel));

        internal static List<Diagnostic> CollectGroundLayerGeometryProblems(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            if (inputModel?.GroundsInput == null) return problems;
            for (int g = 0; g < inputModel.GroundsInput.Count; g++)
            {
                var gi = inputModel.GroundsInput[g];
                if (gi?.GroundLayers == null) continue;
                for (int li = 0; li < gi.GroundLayers.Count; li++)
                {
                    var layer = gi.GroundLayers[li];
                    if (layer == null) continue;
                    var at = DiagnosticTarget.GroundLayer(g + 1, li + 1);
                    if (!IsPositive(layer.LayerThickness))
                        problems.Add(Diagnostic.InputAt(at, $"層厚が 0 以下か数値ではありません ({layer.LayerThickness})."));
                    if (!double.IsFinite(layer.BottomAltitude))
                        problems.Add(Diagnostic.InputAt(at, $"層の下端の標高が数値ではありません ({layer.BottomAltitude})."));
                }
            }
            return problems;
        }

        // 杭のすべての高さ内で土質が定義されているかをチェック
        public static string CheckSoilPile(InputModel inputModel, string message)
            => message + Lines(CollectSoilPileProblems(inputModel));

        /// <summary>
        /// 杭配置の各杭が、杭体・地盤・土層-杭セットを引けて、杭の全長が地盤の範囲に入っているか。
        ///
        /// <para>杭そのものの問題 (番号の範囲外・杭頭の高さ・土層-杭セット) は<b>その杭</b>を指す。
        /// 杭体・地盤の中身の問題 (区間・土層が無い) は<b>その杭体・地盤</b>を指す (使う杭すべてに効く)。
        /// 杭と地盤の上下の関係は、同じ杭体・地盤・杭頭の高さの杭で 1 行にまとめ、その杭をすべて指す。</para>
        /// </summary>
        internal static List<Diagnostic> CollectSoilPileProblems(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            if (inputModel.PileLayoutItems == null || inputModel.PileLayoutItems.Count == 0)
            {
                problems.Add(Diagnostic.Input(DiagnosticTarget.Nowhere, "杭配置にデータがありません。"));
                return problems;
            }

            // 杭体・地盤・杭頭の高さが同じ杭は、地盤との上下の関係も同じ。まとめて 1 回だけ見る
            var groups = new List<((int Ground, int Body, double Top) Key, List<PileLayoutDataItem> Piles)>();
            var reportedBodies = new HashSet<int>();
            var reportedGrounds = new HashSet<int>();

            foreach (PileLayoutDataItem pile in inputModel.PileLayoutItems)
            {
                if (pile == null) continue;
                var at = DiagnosticTarget.Pile(pile.No);

                // 番号の範囲を先に見る。以前は番号でそのまま配列を引いていたので、古いファイルなどで範囲の外の番号があると、
                // 入力の問題を知らせる前に例外で落ちた
                string? badReference = DescribeBadPileReference(inputModel, pile);
                if (badReference != null)
                {
                    problems.Add(Diagnostic.Input(at, badReference));
                    continue;
                }
                if (!double.IsFinite(pile.PileHeadZ))
                {
                    problems.Add(Diagnostic.InputAt(at, $"杭頭の高さが数値ではありません ({pile.PileHeadZ})."));
                    continue;
                }
                // 杭体に区間が無ければ、土層-杭セットもできない。セットの有無より先に、元の原因 (杭体) を示す
                if ((inputModel.PileBodyAt(pile.PileBodyNo)!.PileBodySegments?.Count ?? 0) == 0)
                {
                    if (reportedBodies.Add(pile.PileBodyNo))
                        problems.Add(Diagnostic.Input(DiagnosticTarget.PileBody(pile.PileBodyNo), $"杭体{pile.PileBodyNo}に杭区間データがありません。"));
                    continue;
                }
                // 解析は杭ごとの土層-杭セットを SoilPileAltNo で引く。対応が無いまま進むと、範囲の外を引いて落ちる
                if (pile.SoilPileAt(inputModel) == null)
                {
                    problems.Add(Diagnostic.InputAt(at, "この杭の土層-杭セットがまだ作られていません。"
                             + "杭配置・地盤・杭体の入力を確定してから、もう一度実行してください。"));
                    continue;
                }
                // 杭頭高さ。v2 セマンティクスでは pile.Z は接合節点 Z なので PileHeadZ を使う。
                // SoilPile キャッシュ (杭頭基準) との整合のためにも PileHeadZ で揃える。
                var key = (pile.GroundNo, pile.PileBodyNo, pile.PileHeadZ);
                int index = groups.FindIndex(g => g.Key == key);
                if (index < 0) groups.Add((key, [pile]));
                else groups[index].Piles.Add(pile);
            }

            foreach (var ((groundNo, pileBodyNo, pileTopAltitude), piles) in groups)
            {
                var body = inputModel.PileBodyAt(pileBodyNo)!;
                var ground = inputModel.GroundAt(groundNo)!;
                if ((body.PileBodySegments?.Count ?? 0) == 0)
                {
                    if (reportedBodies.Add(pileBodyNo))
                        problems.Add(Diagnostic.Input(DiagnosticTarget.PileBody(pileBodyNo), $"杭体{pileBodyNo}に杭区間データがありません。"));
                    continue;
                }
                if ((ground.GroundLayers?.Count ?? 0) == 0)
                {
                    if (reportedGrounds.Add(groundNo))
                        problems.Add(Diagnostic.Input(DiagnosticTarget.Ground(groundNo), $"地盤{groundNo}に土層データがありません。"));
                    continue;
                }

                double pileBottomAltitude = pileTopAltitude - body.PileBodySegments[^1].SegmentDepth;
                double groundTopAltitude = ground.GroundLayers[0].BottomAltitude + ground.GroundLayers[0].LayerThickness;
                double groundBottomAltitude = ground.GroundLayers[^1].BottomAltitude;

                // 杭頭の高さは杭ごとの入力なので、指すのは杭 (同じ杭体を使うほかの杭ではない)
                string who = "杭 No." + string.Join("・", piles.Take(10).Select(p => p.No))
                           + (piles.Count > 10 ? $" ほか {piles.Count - 10} 本" : "");
                if (groundTopAltitude < pileTopAltitude)
                    problems.Add(PileGroupProblem(piles, $"{who} (杭体{pileBodyNo}・地盤{groundNo}): 杭の最上部が地盤の最上部よりも浅いです。"));
                if (pileBottomAltitude < groundBottomAltitude)
                    problems.Add(PileGroupProblem(piles, $"{who} (杭体{pileBodyNo}・地盤{groundNo}): 杭の最下部が地盤の最下部よりも深いです。"));
            }
            return problems;
        }

        /// <summary>複数の杭にまたがる 1 行の問題 (最初の杭を場所にし、残りの杭も指す)。</summary>
        private static Diagnostic PileGroupProblem(IReadOnlyList<PileLayoutDataItem> piles, string message)
            => Diagnostic.Input(DiagnosticTarget.Pile(piles[0].No), message) with
            {
                MoreTargets = piles.Skip(1).Select(p => DiagnosticTarget.Pile(p.No)).ToList(),
            };

        //根入れのすべての高さ内で土質が定義されているかをチェック
        public static string CheckSoilEmbedment(InputModel inputModel, string message)
            => message + Lines(CollectEmbedmentProblems(inputModel));

        internal static List<Diagnostic> CollectEmbedmentProblems(InputModel inputModel)
        {
            var problems = new List<Diagnostic>();
            var embedment = inputModel.EmbedmentInput;
            // 根入なし (EmbedmentInput 未初期化) なら検証スキップ
            if (embedment == null || embedment.EmbedmentLayersCount == 0) return problems;

            int groundNo = embedment.GroundNo;
            int groundCount = inputModel.GroundsInput?.Count ?? 0;
            if (groundNo < 1 || groundNo > groundCount)
            {
                problems.Add(Diagnostic.Input(DiagnosticTarget.Embedment(),
                    $"根入部で選択された地盤番号{groundNo}の地盤がありません (地盤は {groundCount} 個)。根入部の地盤を選び直してください。"));
                return problems;
            }
            // 層数の欄と層の一覧は別々に持たれている。食い違ったまま進むと、根入部を考慮するか (層数で判断) と
            // どの層を使うか (一覧) が合わない。以前は一覧が空なら黙って検査を終えていた
            int layerRows = embedment.EmbedmentLayers?.Count ?? 0;
            if (layerRows != embedment.EmbedmentLayersCount)
            {
                problems.Add(Diagnostic.Input(DiagnosticTarget.Embedment(),
                    $"根入部の層数 ({embedment.EmbedmentLayersCount}) と層の入力 ({layerRows} 行) が合いません。"
                    + "根入部の層数と表を確認してください。"));
                return problems;
            }

            var layerGeometry = CollectEmbedmentLayerGeometryProblems(embedment);
            if (layerGeometry.Count > 0)
            {
                // 形の壊れた層では下の地盤との比較も意味を持たないので、ここで止める
                problems.AddRange(layerGeometry);
                return problems;
            }

            var ground = inputModel.GroundAt(groundNo)!;
            if ((ground.GroundLayers?.Count ?? 0) == 0)
            {
                problems.Add(Diagnostic.Input(DiagnosticTarget.Ground(groundNo), $"根入部で選択された地盤番号{groundNo}に土層データがありません。"));
                return problems;
            }

            double groundTopAltitude = ground.GroundLayers[0].BottomAltitude + ground.GroundLayers[0].LayerThickness;
            double groundBottomAltitude = ground.GroundLayers[^1].BottomAltitude;
            double embedmentTopAltitude = embedment.EmbedmentLayers[0].TopAltitude;
            double embedmentBottomAltitude = embedment.EmbedmentLayers[^1].BottomAltitude;
            if (groundTopAltitude < embedmentTopAltitude)
                problems.Add(Diagnostic.Input(DiagnosticTarget.Embedment(), $"根入部の最上部が地盤番号{groundNo}の最上部よりも浅いです。"));
            if (embedmentBottomAltitude < groundBottomAltitude)
                problems.Add(Diagnostic.Input(DiagnosticTarget.Embedment(), $"根入部の最下部が地盤番号{groundNo}の最下部よりも深いです。"));
            return problems;
        }

        /// <summary>
        /// 根入部の各層の形の誤り。層は上から並ぶ。
        ///
        /// 解析は各層の上端・下端の標高をそのまま使う (<see cref="InputModel"/> の根入部の分割)。上端・下端は
        /// 層厚と根入部の下端から画面側で求めるが、ファイルを手で直したときなどは食い違いうる。以前は根入部全体の
        /// 上端・下端と地盤の範囲しか見ておらず、厚さが 0 以下・数でない・上下が逆・隣の層との隙間や重なりがあっても
        /// 計算に進んだ。
        /// </summary>
        internal static List<Diagnostic> CollectEmbedmentLayerGeometryProblems(EmbedmentInput embedment)
        {
            const double tol = NumericalConstants.COORDINATE_TOLERANCE;
            var problems = new List<Diagnostic>();
            var layers = embedment.EmbedmentLayers;
            if (layers == null || layers.Count == 0) return problems;

            for (int i = 0; i < layers.Count; i++)
            {
                var at = DiagnosticTarget.Embedment(i + 1);
                void Add(string message) => problems.Add(Diagnostic.Input(at, message));
                var layer = layers[i];
                if (layer == null)
                {
                    Add($"根入部 第{i + 1}層: 層のデータがありません。");
                    continue;
                }
                if (!IsPositive(layer.LayerThickness))
                    Add($"根入部 第{i + 1}層: 層厚が 0 以下か数値ではありません ({layer.LayerThickness}).");
                if (!double.IsFinite(layer.TopAltitude) || !double.IsFinite(layer.BottomAltitude))
                {
                    Add($"根入部 第{i + 1}層: 上端・下端の標高が数値ではありません (上端 {layer.TopAltitude} / 下端 {layer.BottomAltitude}).");
                    continue;
                }
                if (!(layer.TopAltitude > layer.BottomAltitude))
                    Add($"根入部 第{i + 1}層: 上端 ({layer.TopAltitude:F3} m) が下端 ({layer.BottomAltitude:F3} m) より高くありません。");
                else if (IsPositive(layer.LayerThickness)
                         && Math.Abs(layer.TopAltitude - layer.BottomAltitude - layer.LayerThickness) > tol)
                    Add($"根入部 第{i + 1}層: 上端と下端の差 ({layer.TopAltitude - layer.BottomAltitude:F3} m) が層厚 ({layer.LayerThickness:F3} m) と合いません。");

                if (i + 1 < layers.Count && layers[i + 1] is { } below && double.IsFinite(below.TopAltitude))
                {
                    double gap = layer.BottomAltitude - below.TopAltitude;
                    if (gap > tol)
                        Add($"根入部 第{i + 1}層と第{i + 2}層の間に {gap:F3} m の隙間があります (第{i + 1}層の下端 {layer.BottomAltitude:F3} m / 第{i + 2}層の上端 {below.TopAltitude:F3} m)。");
                    else if (gap < -tol)
                        Add($"根入部 第{i + 1}層と第{i + 2}層が {-gap:F3} m 重なっています (第{i + 1}層の下端 {layer.BottomAltitude:F3} m / 第{i + 2}層の上端 {below.TopAltitude:F3} m)。");
                }
            }
            return problems;
        }

        // 杭要素分割が済んでいないことは、ここでは注意にしない。
        //
        // 2026-09-10 に一度足したが、<b>構造上出ない</b>ので取り下げた。
        // 水平解析は MainWindowViewModel.EnsureElementSplit("水平解析") を通ってからしか
        // ウィンドウを開かない。分割が済んでいなければその場で訊き、断られても分割画面で
        // 取り消しても false を返してウィンドウを作らない。そのウィンドウは ShowDialog
        // (モーダル) なので、開いている間に断面を編集して分割を無効化することもできない。
        // つまりこの検査に届く時点で分割は必ず済んでいる。
        //
        // 出ない注意を置くと「保護がある」と読めてしまうのが害。状況そのものを防ぐほうが
        // 上位の解決で、そちらは既に入っている。
        //
        // 分割前の解析が粗いこと自体は事実 (計算例で応答が 10〜27% 過大。詳細は
        // AnalysisOutputInvariantTests.RefiningTheMesh_ConvergesToAFixedAnswer)。
        // 別の入口から分割前に解析できるようになったら、そこで注意を出すこと。
    }
}
