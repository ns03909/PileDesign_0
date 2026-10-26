using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PileDesign.FEM;
using PileDesign.Models.InputData;

namespace PileDesign.Output
{
    // MGT の荷重関連セクション（STLDCASE, CONLOAD, SPDISP, LOADCOMB）を出力する partial。
    public partial class MgtExporter
    {
        // 荷重ケース名生成（ハイフンは算術演算子と誤認されるためアンダースコアを使用）
        private static string UFName(LoadCase lc) => $"UF_L{lc.Level}_{lc.No}";          // 上部構造慣性力
        private static string FFName(LoadCase lc) => $"FF_L{lc.Level}_{lc.No}";          // 基礎構造慣性力
        private static string GDName(LoadCase lc, bool isLiq) => $"GD_L{lc.Level}_{lc.No}{(isLiq ? "_L" : "")}"; // 地盤変位
        private static string CombName(LoadCase lc, LoadCombination comb, bool isLiq) => $"C_L{lc.Level}_{lc.No}_{comb.No}{(isLiq ? "_L" : "")}";
        private static string AXName(LoadCase lc) => $"AX_L{lc.Level}_{lc.No}";          // 杭の軸力と自重 (鉛直ばねを使うとき)

        /// <summary>常時 (VL) のケース。解析は地盤変位を与えず、杭の軸力は常時軸力を使う。</summary>
        private static bool IsVL(LoadCase lc) => lc.LoadName == "VL";

        // 説明文のサニタイズ（カンマは区切り文字と誤認されるため除去）
        /// <summary>
        /// 解析した荷重ケースの「レベル + 番号」が重複していないことを確かめる。重複していれば、
        /// どのケースかを示して出力を止める (<see cref="InvalidOperationException"/>)。
        ///
        /// MGT の荷重ケースは「レベル + 番号」で区別し (UF_L{Level}_{No} など)、同じ組の解析結果は先頭の
        /// ケースだけを書く。<see cref="LoadCase.No"/> の setter は重複を拒まないので、手で直したファイルなどで
        /// 同じレベル・番号のケースが 2 つあると、片方が<b>黙って</b>出力から欠けた。
        /// 同じケースは解析結果の中で同じ実体を指す (保存しても $ref で保たれる) ので、実体が違えば別のケースとみなす。
        /// </summary>
        internal static void EnsureLoadCaseNumbersAreUnique(IEnumerable<AnalysisStepResult>? steps)
        {
            if (steps == null) return;

            var duplicates = steps
                .Select(r => r.LoadCase)
                .Where(lc => lc != null)
                .Distinct(ReferenceEqualityComparer.Instance)
                .Cast<LoadCase>()
                .GroupBy(lc => (lc.Level, lc.No))
                .Where(g => g.Count() > 1)
                .OrderBy(g => g.Key.Level).ThenBy(g => g.Key.No)
                .Select(g => $"・レベル{g.Key.Level} の番号 {g.Key.No}: "
                             + string.Join("、", g.Select(lc => $"「{lc.LoadName}」")))
                .ToList();
            if (duplicates.Count == 0) return;

            throw new InvalidOperationException(
                "荷重ケースの番号が重複しているため、MGT を出力できません。\n"
                + "MGT では荷重ケースをレベルと番号で区別するので、このまま出力すると片方のケースが欠けます。\n"
                + string.Join("\n", duplicates) + "\n"
                + "荷重ケースの番号は画面では変更できません。ファイルを手で編集した場合は元に戻すか、"
                + "荷重ケースを入力し直したファイルで出力してください。");
        }

        /// <summary>
        /// 解析した荷重組合せの番号が、同じ荷重ケース・液状化の有無の中で重複していないことを確かめる。
        /// 重複していれば、どれかを示して出力を止める (<see cref="InvalidOperationException"/>)。
        ///
        /// MGT の荷重組合せは「荷重ケースのレベル・番号 + 組合せ番号 + 液状化の有無」で区別し
        /// (C_L{Level}_{No}_{Comb}{_L})、同じ組の解析結果は先頭の組合せだけを書く。組合せの番号は重複を拒まないので、
        /// 保存データなどで係数の違う組合せが同じ番号を持つと、片方が<b>黙って</b>出力から欠けた。
        /// 同じ番号でも係数 (β1・β2・α1。MGT に書くのはこの 3 つ) がすべて同じなら、書く内容も同じなので重複とはみなさない。
        /// </summary>
        internal static void EnsureLoadCombinationNumbersAreUnique(IEnumerable<AnalysisStepResult>? steps)
        {
            if (steps == null) return;

            var duplicates = steps
                .Where(r => r.LoadCase != null && r.LoadCombination != null)
                .GroupBy(r => (Level: r.LoadCase.Level, CaseNo: r.LoadCase.No, CombNo: r.LoadCombination.No, r.IsLiquefaction))
                .Select(g => (g.Key, g.First().LoadCase, Factors: g
                    .Select(r => (r.LoadCombination.Beta1, r.LoadCombination.Beta2, r.LoadCombination.Alpha1))
                    .Distinct()
                    .ToList()))
                .Where(x => x.Factors.Count > 1)
                .OrderBy(x => x.Key.Level).ThenBy(x => x.Key.CaseNo).ThenBy(x => x.Key.CombNo).ThenBy(x => x.Key.IsLiquefaction)
                .Select(x => $"・レベル{x.Key.Level}「{x.LoadCase.LoadName}」{(x.Key.IsLiquefaction ? " (液状化あり)" : "")} の組合せ番号 {x.Key.CombNo}: "
                             + string.Join(" / ", x.Factors.Select(f => $"β1={f.Beta1} β2={f.Beta2} α1={f.Alpha1}")))
                .ToList();
            if (duplicates.Count == 0) return;

            throw new InvalidOperationException(
                "荷重組合せの番号が重複しているため、MGT を出力できません。\n"
                + "MGT では荷重組合せを荷重ケースと組合せ番号で区別するので、このまま出力すると片方の組合せが欠けます。\n"
                + string.Join("\n", duplicates) + "\n"
                + "荷重組合せは、荷重ケースのウィンドウの組合せ係数から自動で作られます (番号は画面では変更できません)。"
                + "組合せ係数を設定し直して組合せを作り直し、解析をやり直してから出力してください。");
        }

        /// <summary>
        /// 利用者が付けた名前 (荷重ケース名など) を、MGT の 1 行の説明に入れられる形にする。
        ///
        /// MGT は 1 行が 1 件で、項目はカンマで区切る。以前はカンマだけを空白にしていたので、
        /// 名前に改行 (表への貼り付けなどで入る) があると 1 件の説明が複数行に割れ、
        /// 続く行が別のレコードとして読まれてファイルの構造が崩れた。改行・タブなどの制御文字と
        /// 行の区切り文字 (U+2028 / U+2029) も空白にし、連続する空白を 1 つにまとめる。
        /// </summary>
        internal static string SanitizeDesc(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            bool lastWasSpace = false;
            foreach (char c in s)
            {
                bool blank = c == ',' || char.IsControl(c) || char.IsWhiteSpace(c)
                             || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator
                                                          or System.Globalization.UnicodeCategory.ParagraphSeparator;
                if (blank)
                {
                    if (!lastWasSpace) sb.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        private void WriteLoadCases(StreamWriter writer, ExportContext ctx)
        {
            var nodeIdMap = ctx.NodeIdMap;
            var inputModel = _anaModel.InputModel;
            if (inputModel?.LoadCasesInput == null) return;
            if (_anaModel.AnalysisStepResults == null || _anaModel.AnalysisStepResults.Count == 0) return;

            // 解析済み (LoadCase, IsLiquefaction) の一意化。
            //
            // 荷重ケースは「レベル + 番号」で区別する (出力するケース名 UF_L{Level}_{No} もこれで作る)。
            // 以前は「名前 + レベル」でまとめていた。名前は利用者が自由に付けられて重複も拒まないので、
            // 同じレベルに同じ名前のケースが 2 つあると、先頭の 1 つだけが出力され、もう 1 つが黙って欠けた。
            var analyzedLCLiqs = _anaModel.AnalysisStepResults
                .Where(r => r.LoadCase != null)
                .GroupBy(r => (r.LoadCase.Level, r.LoadCase.No, r.IsLiquefaction))
                .Select(g => (LC: g.First().LoadCase, IsLiq: g.Key.IsLiquefaction))
                .ToList();

            if (analyzedLCLiqs.Count == 0) return;

            // 解析済み LoadCase の一意化（レベル+番号）
            var analyzedLCs = analyzedLCLiqs
                .GroupBy(x => (x.LC.Level, x.LC.No))
                .Select(g => g.First().LC)
                .ToList();

            // 解析済み (LoadCase, LoadCombination, IsLiquefaction)。荷重ケースは上と同じく「レベル + 番号」で区別する
            var analyzedCombos = _anaModel.AnalysisStepResults
                .Where(r => r.LoadCase != null && r.LoadCombination != null)
                .GroupBy(r => (r.LoadCase.Level, r.LoadCase.No, r.LoadCombination.No, r.IsLiquefaction))
                .Select(g => (LC: g.First().LoadCase, Comb: g.First().LoadCombination, IsLiq: g.Key.IsLiquefaction))
                .ToList();

            // 地盤変位を与える節点 (地盤ばねの地盤側)。X 方向のばねは地盤節点に X 成分、Y 方向のばねは仮想節点に Y 成分を与える。
            // 以前は杭周の地盤節点に X・Y 両方を与えていたが、Y 方向のばねはその節点につながっていないので Y 成分が効かず、
            // 根入部 (土圧合力ばね) の地盤節点には何も与えていなかった (解析はどちらにも与える)。
            var groundLinks = ctx.Links.SoilLinks
                .Where(l => l.GroundDisplacement != null && l.Direction != LinkDirection.Z)
                .GroupBy(l => l.GroundNodeId).Select(g => g.First()).ToList();

            // 代表節点 (慣性力の作用点) = 解析モデルの先頭節点。解析モデルはレベル1 の最初の荷重ケースの作用点に置いている
            var masterNode = _anaModel.Nodes.FirstOrDefault();
            int masterId = (masterNode != null && nodeIdMap.TryGetValue(masterNode, out int mid)) ? mid : -1;

            // 杭の鉛直ばねを使うときは、解析と同じく杭の軸力と杭体の自重を節点荷重で与える
            bool writeAxial = ctx.Links.PilesWithVerticalSprings.Count > 0;

            // === STLDCASE 定義 ===
            writer.WriteLine("*STLDCASE    ; Static Load Cases");
            writer.WriteLine("; LCNAME, LCTYPE, DESC");
            foreach (var lc in analyzedLCs)
            {
                writer.WriteLine($"   {UFName(lc),-14}, USER, L{lc.Level} {SanitizeDesc(lc.LoadName)} Upper Mass Force ({lc.LoadAngle}deg)");
            }
            foreach (var lc in analyzedLCs)
            {
                writer.WriteLine($"   {FFName(lc),-14}, USER, L{lc.Level} {SanitizeDesc(lc.LoadName)} Foundation Mass Force ({lc.LoadAngle}deg)");
            }
            foreach (var (lc, isLiq) in analyzedLCLiqs)
            {
                writer.WriteLine($"   {GDName(lc, isLiq),-14}, USER, L{lc.Level} {SanitizeDesc(lc.LoadName)} Ground Disp ({lc.LoadAngle}deg{(isLiq ? " liq" : "")})");
            }
            if (writeAxial)
                foreach (var lc in analyzedLCs)
                    writer.WriteLine($"   {AXName(lc),-14}, USER, L{lc.Level} {SanitizeDesc(lc.LoadName)} Pile Axial Force and Weight");
            writer.WriteLine();

            // === 上部構造慣性力・基礎構造慣性力 ===
            foreach (var lc in analyzedLCs) WriteInertiaForce(writer, lc, UFName(lc), lc.UpperMassForce, masterNode, masterId);
            foreach (var lc in analyzedLCs) WriteInertiaForce(writer, lc, FFName(lc), lc.FoundationMassForce, masterNode, masterId);

            // === 地盤強制変位 ===
            foreach (var (lc, isLiq) in analyzedLCLiqs)
            {
                double rad = lc.LoadAngle * Math.PI / 180.0;

                writer.WriteLine($"*USE-STLD, {GDName(lc, isLiq)}");
                writer.WriteLine();
                // 常時 (VL) のケースは地盤変位を与えない (解析と同じ)
                if (groundLinks.Count > 0 && !IsVL(lc))
                {
                    var lines = new List<string>();
                    foreach (var link in groundLinks)
                    {
                        var zData = link.GroundDisplacement!;
                        double dispMm = lc.Level == 1
                            ? (isLiq ? zData.GroundDisp1L : zData.GroundDisp1)
                            : (isLiq ? zData.GroundDisp2L : zData.GroundDisp2);
                        double disp = dispMm / 1000.0; // mm → m
                        bool isX = link.Direction == LinkDirection.X;
                        double d = isX ? disp * Math.Cos(rad) : disp * Math.Sin(rad);
                        if (Math.Abs(d) <= 1e-9) continue;
                        lines.Add(isX
                            ? $"   {link.GroundNodeId}, 100000, {d:E4}, 0, 0, 0, 0, 0, "
                            : $"   {link.GroundNodeId}, 010000, 0, {d:E4}, 0, 0, 0, 0, ");
                    }
                    if (lines.Count > 0)
                    {
                        writer.WriteLine("*SPDISP    ; Specified Displacement of Supports");
                        writer.WriteLine("; NODE_LIST, FLAG, Dx, Dy, Dz, Rx, Ry, Rz, GROUP");
                        foreach (var line in lines) writer.WriteLine(line);
                        writer.WriteLine();
                    }
                }
                writer.WriteLine($"; End of data for load case [{GDName(lc, isLiq)}] -------------------------");
                writer.WriteLine();
            }

            // === 杭の軸力と自重 (杭の鉛直ばねを使うとき) ===
            if (writeAxial)
                foreach (var lc in analyzedLCs) WriteAxialLoads(writer, lc, nodeIdMap, ctx.Links.PilesWithVerticalSprings);

            // === LOADCOMB（荷重組合せ） ===
            if (analyzedCombos.Count > 0)
            {
                writer.WriteLine("*LOADCOMB    ; Combinations");
                writer.WriteLine("; NAME=NAME, KIND, ACTIVE, bES, iTYPE, DESC");
                writer.WriteLine(";         ANAL1, LCNAME1, FACT1, ANAL2, LCNAME2, FACT2, ...");
                foreach (var (lc, comb, isLiq) in analyzedCombos)
                {
                    string name = CombName(lc, comb, isLiq);
                    string desc = $"L{lc.Level} {SanitizeDesc(lc.LoadName)} Comb{comb.No}{(isLiq ? " liq" : "")} b1={comb.Beta1} b2={comb.Beta2} a1={comb.Alpha1}";
                    writer.WriteLine($"   NAME={name}, GEN, ACTIVE, 0, 0, {desc}");
                    writer.WriteLine($"        ST, {UFName(lc),-14}, {comb.Beta1:F4}, ST, {FFName(lc),-14}, {comb.Beta2:F4}, ST, {GDName(lc, isLiq),-14}, {comb.Alpha1:F4}"
                                     // 杭の軸力は組合せの係数によらず全量を与える (解析と同じ)
                                     + (writeAxial ? $", ST, {AXName(lc),-14}, 1.0000" : ""));
                }
                writer.WriteLine();
            }
        }
            /// <summary>
        /// 慣性力を代表節点に書く。荷重ケースの作用点 (慣性力中心) が代表節点と違えば、位置の差から生じるモーメント (r × F) も足す。
        ///
        /// 解析は荷重ケースごとに作用点を慣性力中心へ動かして解く (<see cref="AnaModel.RelocateActionPoint"/>)。
        /// 作用点は剛体連結の親なので、「作用点 P に力 F」は「代表節点 M に力 F とモーメント (P − M) × F」と同じ。
        /// 以前は全ケースを代表節点の位置で書き、モーメントを 0 にしていたので、偏心のねじれ・転倒の効果が消えていた。
        /// </summary>
        private static void WriteInertiaForce(StreamWriter writer, LoadCase lc, string name, double force, Node? master, int masterId)
        {
            writer.WriteLine($"*USE-STLD, {name}");
            writer.WriteLine();
            if (master != null && masterId > 0 && Math.Abs(force) > 1e-9)
            {
                var (fx, fy, mx, my, mz) = InertiaLoadAtMaster(lc, force, master.Coord);
                writer.WriteLine("*CONLOAD    ; Nodal Loads");
                writer.WriteLine("; NODE_LIST, FX, FY, FZ, MX, MY, MZ, GROUP");
                writer.WriteLine($"   {masterId}, {fx:F4}, {fy:F4}, 0, {mx:F4}, {my:F4}, {mz:F4}, ,");
                writer.WriteLine();
            }
            writer.WriteLine($"; End of data for load case [{name}] -------------------------");
            writer.WriteLine();
        }

        /// <summary>荷重ケースの作用点に掛かる水平力を、代表節点 (座標 <paramref name="master"/>) の力とモーメントに置き換える。</summary>
        internal static (double Fx, double Fy, double Mx, double My, double Mz) InertiaLoadAtMaster(
            LoadCase lc, double force, System.Windows.Media.Media3D.Point3D master)
        {
            double rad = lc.LoadAngle * Math.PI / 180.0;
            double fx = force * Math.Cos(rad), fy = force * Math.Sin(rad);
            double rx = lc.ForceActionPointX - master.X, ry = lc.ForceActionPointY - master.Y, rz = lc.ForceActionPointAltitude - master.Z;
            if (!double.IsFinite(rx) || !double.IsFinite(ry) || !double.IsFinite(rz)) rx = ry = rz = 0;
            // M = r × F (F の Z 成分は 0)
            return (fx, fy, -rz * fy, rz * fx, rx * fy - ry * fx);
        }

        /// <summary>
        /// 杭の軸力と杭体の自重 (杭の鉛直ばねを使うとき)。解析 (HorizontalCalculationViewModel.SetVectorDF) と同じく、
        /// 軸力は杭の接合節点に下向き、自重は各杭節点に下向き (杭頭の分は接合節点) に与える。
        /// </summary>
        private void WriteAxialLoads(StreamWriter writer, LoadCase lc, Dictionary<Node, int> nodeIdMap, List<PileLayoutDataItem> piles)
        {
            writer.WriteLine($"*USE-STLD, {AXName(lc)}");
            writer.WriteLine();
            var loads = new Dictionary<int, double>();
            var problems = new List<string>();
            void Add(Node? node, double fz, PileLayoutDataItem pile)
            {
                if (node == null || !nodeIdMap.TryGetValue(node, out int id)) { problems.Add($"・杭 No.{pile.No}: 軸力を与える節点がありません"); return; }
                loads[id] = loads.TryGetValue(id, out double v) ? v + fz : fz;
            }
            foreach (var pile in piles)
            {
                int iLC = lc.No - 1;
                // 地震時軸力の分離入力: MGT の荷重ケースは組合せごとに軸力を変えられないので、組合せのうち最大 (圧縮側) を書く
                // (ファイル先頭に注記。CollectApproximations)
                double n = IsVL(lc) ? pile.AxialForceVL
                    : SeismicAxialForces.IsSeparated(_anaModel.InputModel)
                        ? SeismicAxialForces.Envelope(_anaModel.InputModel, pile, lc.No, lc.Level, reportFallback: false).Max
                    : lc.Level == 1 ? (pile.AxialForceLevel1s != null && iLC >= 0 && iLC < pile.AxialForceLevel1s.Count ? pile.AxialForceLevel1s[iLC] : pile.AxialForceVL)
                    : (pile.AxialForceLevel2s != null && iLC >= 0 && iLC < pile.AxialForceLevel2s.Count ? pile.AxialForceLevel2s[iLC] : pile.AxialForceVL);
                var joint = _anaModel.PileJointNodeOf(pile.No);
                if (double.IsFinite(n) && Math.Abs(n) >= 1e-12) Add(joint, -n, pile);
                var models = pile.VerticalNodeSpringModels;
                int nw = Math.Min(pile.PileNodes.Count, models.Count);
                for (int k = 0; k < nw; k++)
                {
                    var md = models[k];
                    if (md == null || md.Weight <= 0.0) continue;
                    Add(k == 0 ? joint : pile.PileNodes[k], -md.Weight, pile);
                }
            }
            ThrowIfCannotWrite("杭の軸力", "件", problems, "杭の軸力が欠けたモデルになります");
            if (loads.Count > 0)
            {
                writer.WriteLine("*CONLOAD    ; Nodal Loads");
                writer.WriteLine("; NODE_LIST, FX, FY, FZ, MX, MY, MZ, GROUP");
                foreach (var (id, fz) in loads.OrderBy(kv => kv.Key))
                    writer.WriteLine($"   {id}, 0, 0, {fz:F4}, 0, 0, 0, ,");
                writer.WriteLine();
            }
            writer.WriteLine($"; End of data for load case [{AXName(lc)}] -------------------------");
            writer.WriteLine();
        }
    }
}
