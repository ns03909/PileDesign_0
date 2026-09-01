using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;

namespace PileDesign.Output
{
    // 各種理論解説セクション（鉛直支持力・沈下・水平抵抗・部材性能・液状化・地盤変位）を提供する partial。
    internal partial class WordDocument
    {
        private static void AddSectionVerticalResistance(Body body)
        {
            AddHeader1(body, "杭の支持力検討", 2);

            AddText(body,
                "杭の鉛直支持力は、「基礎指針'19」6.2節、引抜き抵抗力は「基礎指針'19」6.5節による。");

            AddText(body, "極限鉛直支持力", "left");
            AddEq(body, @"R_{u} = R_{p} + R_{f}");
            //AddEquation_Ru(body);

            AddText(body, "極限先端支持力", "left");
            //AddEquation_Rp(body);
            AddEq(body, @"R_{p} = q_{p} A");

            AddText(body, "極限周面支持力", "left");
            //AddEquation_Rf(body);
            AddEq(body, @"R_{f} = R_{fs} + R_{fc}");

            AddText(body, "砂質土部分の周面抵抗力", "left");
            //AddEquation_Rfs(body);
            AddEq(body, @"R_{fs} = \tau_{fs} L_{s} \psi");

            AddText(body, "粘性土部分の周面抵抗力", "left");
            //AddEquation_Rfc(body);
            AddEq(body, @"R_{fc} = \tau_{fc} L_{c} \psi");

            AddText(body, ConcreteModelOptions.MapLimitStateText("使用限界支持力"), "left");
            //AddEquation_Rd_SLS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{u} = \frac{1}{3} R_{u}");

            AddText(body, ConcreteModelOptions.MapLimitStateText("損傷限界支持力"), "left");
            //AddEquation_Rd_DLS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{u} = \frac{1}{1.5} R_{u}");

            AddText(body, "終局限界支持力", "left");
            //AddEquation_Rd_ULS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{u} = \frac{1}{1} R_{u}");



            AddText(body, "最大引抜き抵抗力", "left");
            //AddEquation_RTU(body);
            AddEq(body, @"R_{TU} = \left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + W");

            AddText(body, "残留引抜き抵抗力", "left");
            //AddEquation_RTR(body);
            AddEq(body, @"R_{TR} = \frac{1}{1.2}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + W");

            AddText(body, "降伏引抜き抵抗力", "left");
            //AddEquation_RTY(body);
            AddEq(body, @"R_{TY} = \frac{2}{3}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + W");

            AddText(body, "砂質土の引抜き時の最大周面抵抗力", "left");
            //AddEquation_TauSti(body);
            AddEq(body, @"\tau_{sti} = \frac{2}{3}\tau_{si}");

            AddText(body, "粘性土の引抜き時の最大周面抵抗力", "left");
            //AddEquation_TauCti(body);
            AddEq(body, @"\tau_{cti} = \frac{4}{5}\tau_{ci}");

            AddText(body, ConcreteModelOptions.MapLimitStateText("使用限界引抜力"), "left");
            //AddEquation_Rdt_SLS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{TU} = \frac{1}{3} R_{TU} = \frac{1}{3}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + \frac{1}{3}W");

            AddText(body, ConcreteModelOptions.MapLimitStateText("損傷限界引抜力"), "left");
            //AddEquation_Rdt_DLS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{TY} = \frac{1}{3} R_{TY} = \frac{2}{9}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + \frac{1}{3}W");

            AddText(body, "終局限界引抜力", "left");
            //AddEquation_Rdt_ULS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{TR} = R_{TR} = \frac{1}{1.2}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + W");

            // 記号定義 (この節だけ記号説明がなかったため追加。static メソッドのためタブ位置は定数)
            const double tab = 15; // mm (symbolDescTabPosition と同値)
            AddText(body, "記号");
            AddSymbolDescriptionWithTab(body, tab, [Tex(@"\R_{u}"), ": 極限鉛直支持力 [kN]"]);
            AddSymbolDescriptionWithTab(body, tab, [Tex(@"\R_{p}"), ": 極限先端支持力 [kN]、", Tex(@"\q_{p}"), ": 極限先端支持力度 [kN/m<^2>]、", Tex(@"\A"), ": 杭先端の断面積 [m<^2>]"]);
            AddSymbolDescriptionWithTab(body, tab, [Tex(@"\R_{f}"), ": 極限周面支持力 [kN]（砂質土 ", Tex(@"\R_{fs}"), " と粘性土 ", Tex(@"\R_{fc}"), " の和）"]);
            AddSymbolDescriptionWithTab(body, tab, [Tex(@"\tau_{fs}, \tau_{fc}"), ": 砂質土・粘性土の最大周面抵抗力度 [kN/m<^2>]"]);
            AddSymbolDescriptionWithTab(body, tab, [Tex(@"\L_{s}, \L_{c}"), ": 砂質土・粘性土部分の杭長 [m]、", Tex(@"\psi"), ": 杭の周長 [m]"]);
            AddSymbolDescriptionWithTab(body, tab, [Tex(@"\phi_{R}"),
                ConcreteModelOptions.MapLimitStateText(": 抵抗係数（使用限界 1/3、損傷限界 1/1.5、終局限界 1/1）")]);
            AddSymbolDescriptionWithTab(body, tab, [Tex(@"\R_{TU}, \R_{TR}, \R_{TY}"), ": 最大・残留・降伏引抜き抵抗力 [kN]"]);
            AddSymbolDescriptionWithTab(body, tab, [Tex(@"\tau_{sti}, \tau_{cti}"), ": 砂質土・粘性土の引抜き時最大周面抵抗力度 [kN/m<^2>]（押込み時の 2/3・4/5）"]);
            AddSymbolDescriptionWithTab(body, tab, [Tex(@"\L_{si}, \L_{ci}"), ": 各土層（砂質土・粘性土）の杭長 [m]、", Tex(@"W"), ": 杭の自重 [kN]"]);
        }

        // 沈下量の節
        private void AddSectionSettlement(Body body)
        {
            AddHeader1(body, "杭の沈下検討", 1);

            // 群杭の沈下を検討していない計算書に、群杭の算定方針と式だけが載っていた。
            // 出力する結果に対応する式だけを載せる。ゲートは結果側と同じ
            // IncludeGroupPileSettlement (群杭沈下解析が未実施なら常に false)。
            bool includeGroup = mainWindowViewModel.DocxOutput.IncludeGroupPileSettlement;

            AddInlineMathParagraph(body, [includeGroup
                ? "杭の沈下は、「基礎指針'19」6.3節による。" +
                  "単杭の沈下量は、同節の荷重伝達解析モデルにより求め、" +
                  "群杭の沈下量は、等価荷重面を用いたスタインブレナーの近似解（「基礎指針'19」5.3節）を用いて求める。"
                : "杭の沈下は、「基礎指針'19」6.3節による。" +
                  "単杭の沈下量は、同節の荷重伝達解析モデルにより求める。"]);

            AddText(body, "単杭の先端抵抗-沈下量関係");
            //AddEquation_PileSettlement(body);
            AddEq(body, @"\frac{S_{p}/d_{p}}{0.1} = \alpha \frac{R_{p}/A_{p}}{(R_{p}/A_{p})_{u}} + (1-\alpha)\left(\frac{R_{p}/A_{p}}{(R_{p}/A_{p})_{u}}\right)^{n}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\S_{p}"), ": 杭先端沈下量[m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\d_{p}"), ": 杭先端直径[m]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\R_{p}"), ": 杭先端荷重[kN]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\A_{p}"), ": 杭先端断面積[m<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\left(\frac{R_{p}}{A_{p}}\right)_{u}"), ": 極限先端支持力[kN/m<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\alpha"), ": 曲線の初期接線勾配"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"n"), ": 曲線形状を決定する次数"]);

            AddText(body, "単杭の周面抵抗力度-沈下量関係");

            AddTrilinearCircumResistanceTable(body);

            // ここから下は群杭 (等価荷重面 + スタインブレナー) の式。
            // 群杭の沈下を出力しない計算書では、対応する結果が無いので載せない。
            if (!includeGroup) return;

            AddText(body, "スタインブレナーの解による成層多層地盤の即時沈下量");
            //AddEquation_SettlementDeltaSE(body);
            AddEq(body, @"\Delta S_{E} = S_{E} - S_{E}' = q \frac{B}{E_{S}} I_{S}");
            //AddEquation_SettlementDeltaIS(body);
            AddEq(body, @"I_{S} = \left(1-\nu_{s}^{2}\right)F_{1}+\left(1-\nu_{s}-2\nu_{s}^{2}\right)F_{2}");

            //AddEquation_SettlementF1(body);
            AddEq(body, @"
            F_{1} = \frac{1}{\pi}\left[
                l\log_{e}\frac
                {\left(1+\sqrt{l^{2}+1}\right)\sqrt{l^{2}+d^{2}}}
                {l\left(1+\sqrt{l^{2}+d^{2}+1}\right)}
                + \log_{e}\frac
                {\left(1+\sqrt{l^{2}+1}\right)\sqrt{l^{2}+d^{2}}}
                {l+\sqrt{l^{2}+d^{2}+1}}
            \right]");
            //AddEquation_SettlementF2(body);
            AddEq(body, @"F_{2} = \frac{d}{2\pi}\tan^{-1}\left(\frac{l}{d+\sqrt{l^{2}+d^{2}+1}}\right)");
            //AddEquation_SettlementSE(body);
            AddEq(body, @"S_{E} = \left[\frac{I_{s}(H_{1},\nu_{s1})}{E_{s1}} + \sum_{k=2}^{n} {\frac{I_{s}(H_{k},\nu_{sk}) - I_{s}(H_{k-1},\nu_{sk})}{E_{sk}}} \right] qB");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\S_{E}"), ": 隅角部の即時沈下量"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\I_{s}\left(H_{k},\nu_{sk}\right)"), ": 層厚", Tex(@"\H_{k}"), "ポアソン比", Tex(@"\nu_{sk}"), "の地盤における沈下係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\E_{s}"), ": 地盤の変形係数（kN/m<^2>）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\nu_{sk}"), ": ポアソン比"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"q"), ": 基礎に作用する荷重度（kN/m<^2>）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"B"), ": 基礎の短辺長さ（m）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L"), ": 基礎の長辺長さ（m）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"l"), ": 基礎の長辺長さ／短辺長さ"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\H_{k}"), ": 地表面から", Tex(@"k"), "層下端までの距離"]);
        }

        // 水平抵抗の節
        private void AddSectionHorizontalResistance(Body body)
        {
            AddHeader1(body, "杭の水平抵抗", 1);

            AddInlineMathParagraph(body, ["杭の水平抵抗は、「基礎指針'19」6.6節による。"]);

            AddInlineMathParagraph(body, ["群杭フレームモデル"]);

            // Word の正式な番号付きリストとして追加
            //AddList(body, new[]
            //{
            //    "慣性力の作用点、各杭頭は剛体として連結する。根入部がある場合は、根入部も剛体として連結する。",
            //    "地盤条件および杭径と群杭硬化を考慮して各杭、各深度の水平地盤反力係数を設定し、杭径と支配区間長を乗じて水平地盤ばねを求める。液状化を生じる場合は水平地盤反力係数を低減する。",
            //    "基礎根入れ部がある場合は、土圧合力ばねを設定する。",
            //    "上部、基礎の慣性力を設定する。",
            //    "転倒モーメントは、各杭に軸力として与えて入力する。",
            //    "各杭位置における杭頭軸力から杭の変形性能を設定する。",
            //    "地震時地盤変位を設定する。",
            //    "水平地盤反力の非線形性および杭体の非線形性を考慮して、杭頭水平力および地震時地盤変位を同時に作用させ、杭応力を評価する。"
            //}, 2);

            var connectionMode = inputModel.FoundationBeamInput?.ConnectionMode ?? FoundationBeamConnectionMode.RigidBody;
            if (connectionMode == FoundationBeamConnectionMode.RigidBody)
            {
                AddInlineMathParagraph(body, ["① 慣性力の作用点、各杭頭は剛体として連結する（全6自由度拘束）。" +
                    "根入部がある場合は、根入部も剛体として連結する。"]);
            }
            else
            {
                AddInlineMathParagraph(body, ["① 慣性力の作用点、各杭頭は水平面内で剛床として連結する" +
                    "（$U_x, U_y, R_z$拘束、$U_z, R_x, R_y$自由）。" +
                    "鉛直方向および水平軸まわりの回転は一般梁要素の剛性により負担する。" +
                    "根入部がある場合は、根入部も剛体として連結する。"]);
            }
            AddInlineMathParagraph(body, ["② 地盤条件および杭径と群杭硬化を考慮して各杭、" +
                "各深度の水平地盤反力係数を設定し、" +
                "杭径と支配区間長を乗じて水平地盤ばねを求める。" +
                "液状化を生じる場合は水平地盤反力係数を低減する。"]);
            AddInlineMathParagraph(body, ["③ 基礎根入れ部がある場合は、土圧合力ばねを設定する。"]);
            AddInlineMathParagraph(body, ["④ 上部、基礎の慣性力を設定する。"]);
            AddInlineMathParagraph(body, ["⑤ 転倒モーメントは、各杭に軸力として与えて入力する。"]);
            AddInlineMathParagraph(body, ["⑥ 各杭位置における杭頭軸力から杭の変形性能を設定する。"]);
            AddInlineMathParagraph(body, ["⑦ 地震時地盤変位を設定する。"]);
            AddInlineMathParagraph(body, ["⑧ 水平地盤反力の非線形性および杭体の非線形性を考慮して、杭頭水平力および" +
                "地震時地盤変位を同時に作用させ、杭応力を評価する。"]);

            AddText(body, "水平地盤反力係数", "left");
            AddEquationP(body);

            AddText(body, "0m≦$y$≦0.001m", "left");

            AddEquationKh_1(body);

            AddText(body, "0.001m≦$y$", "left");
            AddEquationKh_2(body);

            AddText(body, "基準水平地盤反力係数", "left");
            AddEquation_kh0(body);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKh() =>
            //    Tex(@"\k_{h}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKh0() =>
            //    Tex(@"\k_{h0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathAlpha() =>
            //    Tex(@"\alpha");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathB0() =>
            //    Tex(@"\B_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathE0() =>
            //    Tex(@"\E_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathGsi() =>
            //    Tex(@"\xi");
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\k_{h}"), ": 水平地盤反力係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\k_{h0}"), ": 基準水平地盤反力係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\alpha"), ": 80m<^-1>"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\B_{0}"), ": 0.01m"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\E_{0}"), ": 基準水平地盤反力係数を評価するための地盤の変形係数[kN/m<^2>]"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\xi"), ": 基準水平地盤反力係数に群杭の影響を考慮する係数"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPySand() =>
            //    Tex(@"\p_{y} = \kappa K_{p}\sigma_{z}'");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKappaFront() =>
            //    Tex(@"\kappa = 3");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKappaRear() =>
            //    Tex(@"\kappa = \min\left(0.55 - 0.007\phi, \frac{R}{B} - 1.0, 0.4\right)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathMu1() =>
            //    Tex(@"\mu = 1.4");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathMu2() =>
            //    Tex(@"\mu = 0.6\left(\dfrac{R}{B}\right) - 0.4");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathLambda1() =>
            //    Tex(@"\lambda = 9.0");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathLambda2() =>
            //    Tex(@"\lambda = 3.0\left(\dfrac{R}{B}\right)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRonBRange1() =>
            //    Tex(@"\frac{R}{B} \ge 3.0");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRonBRange2() =>
            //    Tex(@"\frac{R}{B} < 3.0");

            AddText(body, "砂質土の塑性水平地盤反力係数", "left");

            AddInlineMathParagraph(body, [Tex(@"\p_{y} = \kappa K_{p}\sigma_{z}'")]);

            AddText(body, "受働土圧係数", "left");
            AddEquation_Kp(body);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, ["単杭および前方杭の場合:", Tex(@"\kappa = 3")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, ["後方杭の場合:", Tex(@"\kappa = \min\left(0.55 - 0.007\phi, \frac{R}{B} - 1.0, 0.4\right)")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, ["単杭および前方杭の場合:", Tex(@"\mu = 1.4"), ",", Tex(@"\lambda = 9.0")]);
            AddInlineMathParagraph(body, ["後方杭で", Tex(@"\frac{R}{B} \ge 3.0"), "の場合　:",
                Tex(@"\mu = 1.4"), ",", Tex(@"\lambda = 9.0")]);
            AddInlineMathParagraph(body, ["後方杭で", Tex(@"\frac{R}{B} < 3.0"), "の場合　:",
                Tex(@"\mu = 0.6\left(\dfrac{R}{B}\right) - 0.4"), ",", Tex(@"\lambda = 3.0\left(\dfrac{R}{B}\right)")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPyClayRange1() =>
            //    Tex(@"\frac{z}{B} \le 2.5");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPyClayRange2() =>
            //    Tex(@"\frac{z}{B} > 2.5");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPyClay1() =>
            //    Tex(@"\p_{y} = 2 \left[ 1 + \mu \frac{z}{B} \right] c_u");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPyClay2() =>
            //    Tex(@"\p_{y} = \lambda c_u");

            AddText(body, "粘性土の塑性水平地盤反力係数", "left");
            //AddEquation_Py(body);
            AddInlineMathParagraph(body, [Tex(@"\frac{z}{B} \le 2.5"), "の場合:　", Tex(@"\p_{y} = 2 \left[ 1 + \mu \frac{z}{B} \right] c_u")]);
            AddInlineMathParagraph(body, [Tex(@"\frac{z}{B} > 2.5"), "の場合:　", Tex(@"\p_{y} = \lambda c_u")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathYRange1() =>
            //    Tex(@"y < \Delta_{p}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathYRange2() =>
            //    Tex(@"y \ge \Delta_{p}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPEt1() =>
            //    Tex(@"\P_{Et} = \frac{(P_{p}-P_{0}) \left(\Delta_{p}-y_{sp}\right) y}{2\Delta_{p}y_{sp} + (\Delta_{p}-3)y_{sp} + \left|y\right|}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPEt2() =>
            //    Tex(@"\P_{Et} = P_{p} - P_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathP0() =>
            //    Tex(@"\P_{0} = \tfrac{1}{2}\gamma B z^{2} K_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPp() =>
            //    Tex(@"\P_{p} = 2czB\sqrt{K_{p}} + \tfrac{1}{2}\gamma B z^{2} K_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKp() =>
            //    Tex(@"\K_{p} = \tan^{2}\left(45^{\circ} + \frac{\phi}{2}\right)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathP0Vary() =>
            //    Tex(@"\P_{0} = \tfrac{1}{2}\gamma B \left(z_{b}^{2} - z_{a}^{2}\right) K_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPpVary() =>
            //    Tex(@"\P_{p} = 2c(z_{b}-z_{a})B\sqrt{K_{p}}+\tfrac{1}{2}\gamma B \left(z_{b}^{2} - z_{a}^{2}\right) K_{p}");
            AddText(body, "土圧合力ばね", "left");

            AddInlineMathParagraph(body, [Tex(@"y < \Delta_{p}"), "の場合:　 ", Tex(@"\P_{Et} = \frac{(P_{p}-P_{0}) \left(\Delta_{p}-y_{sp}\right) y}{2\Delta_{p}y_{sp} + (\Delta_{p}-3)y_{sp} + \left|y\right|}")]);
            AddInlineMathParagraph(body, [Tex(@"y \ge \Delta_{p}"), "の場合:　 ", Tex(@"\P_{Et} = P_{p} - P_{0}")]);
            AddInlineMathParagraph(body, [Tex(@"\P_{0} = \tfrac{1}{2}\gamma B z^{2} K_{0}")]);
            AddInlineMathParagraph(body, [Tex(@"\P_{p} = 2czB\sqrt{K_{p}} + \tfrac{1}{2}\gamma B z^{2} K_{0}")]);

            AddInlineMathParagraph(body, [Tex(@"\P_{0} = \tfrac{1}{2}\gamma B \left(z_{b}^{2} - z_{a}^{2}\right) K_{0}")]);
            AddInlineMathParagraph(body, [Tex(@"\P_{p} = 2c(z_{b}-z_{a})B\sqrt{K_{p}}+\tfrac{1}{2}\gamma B \left(z_{b}^{2} - z_{a}^{2}\right) K_{p}")]);

            AddInlineMathParagraph(body, [Tex(@"\K_{p} = \tan^{2}\left(45^{\circ} + \frac{\phi}{2}\right)")]);
        }

        // 部材の性能 (internal: オプション分岐の出力内容をテストで検証するため)
        internal static void AddSectionMemberCapacities(Body body)
        {
            AddHeader1(body, "基礎部材の強度と変形性能", 1);

            AddText(body, ConcreteModelOptions.MapLimitStateText(
                "杭体の曲げ・せん断耐力は、日本建築学会「基礎部材の強度と変形性能」（第1版、2022年）に基づき、" +
                "使用限界・損傷限界・安全限界の 3 つの限界状態について算定する。" +
                "式中の ξ は施工品質管理係数、Fc はコンクリートの設計基準強度 [N/mm²]、" +
                "σ0 は軸方向応力度、β1・β2 は同書 解説表2.6・2.7 の低減係数（下表）である。"));

            AddBetaFactorTables(body);

            AddText(body, "コンクリートのヤング係数");
            // 基本設定「Ec の算定で ξ=1.0」ON 時は σB = Fc で算定される（強度側には実際の ξ を使用）。
            if (ConcreteModelOptions.UseUnitGsiForConcreteE)
            {
                AddEq(body, @"E_{c} = 3.35\times 10^{4} \left(\frac{\gamma}{24}\right)^{2} \left(\frac{F_{c}}{60}\right)^{\frac{1}{3}}");
                AddTableNote(body, "※ Ec の算定では ξ = 1.0 とする。強度側（ξ·Fc 等）には実際の ξ を用いる。");
            }
            else
            {
                AddEq(body, @"E_{c} = 3.35\times 10^{4} \left(\frac{\gamma}{24}\right)^{2} \left(\frac{\xi\cdot F_{c}}{60}\right)^{\frac{1}{3}}");
            }

            // 圧縮: 基本設定「場所打ち杭の許容圧縮応力度を告示1113(第8)による」ON時は、使用/損傷限界の
            // 許容圧縮応力度を告示（長期・短期）で評価する旨を注記（Ms/Md の Msi/Mdi に反映される）。
            if (ConcreteModelOptions.UseNotification1113Compression)
                AddTableNote(body, "※ 使用限界・損傷限界の許容圧縮応力度は告示 平13国交告第1113号(第8) による（使用限界=長期、損傷限界=短期）。");

            AddText(body, ConcreteModelOptions.MapLimitStateText("場所打ち鉄筋コンクリート杭の使用限界曲げモーメントMs"));
            //AddEquation_InsituReinforcedPileMs(body);
            AddEq(body, @"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2},M_{s3}\right)");

            AddText(body, ConcreteModelOptions.MapLimitStateText("場所打ち鉄筋コンクリート杭の損傷限界曲げモーメントMd"));
            //AddEquation_InsituReinforcedPileMd(body);
            AddEq(body, @"M_{d} = \beta_{1}\cdot \min\left(M_{d1},M_{d2},M_{d3}\right)");

            // e関数法 ON 時は断面解析値をそのまま用い、β1・β2 低減および軸力適用範囲の制限は課さない
            // (InsituReinforcedConcreteSection: FactoredUltimateNM = UnfactoredUltimateNM)
            if (ConcreteModelOptions.UseInsituUltimateEFunction)
            {
                AddText(body, "場所打ち鉄筋コンクリート杭の安全限界曲げモーメントMu（e関数法、耐震設計指針(案)5.4.1 準拠）");
                AddEq(body, @"M_{u} = M_{u0}");
                AddTableNote(body, "※ e関数法による断面解析値をそのまま用い、低減係数 β1・β2 および軸力適用範囲の制限は課さない。");
            }
            else
            {
                AddText(body, "場所打ち鉄筋コンクリート杭の安全限界曲げモーメントMu");
                AddEq(body, @"M_{u} = \beta_{1}\cdot \beta_{2}\cdot M_{u0}");
            }

            // せん断: 基本設定「場所打ちRC杭の許容せん断応力度を告示1113(第8)による」ON時は
            // 告示の許容せん断力 Q=fs·b·j（使用限界=長期、損傷限界=短期=長期×1.5）。既定は基礎部材式。
            if (ConcreteModelOptions.UseNotification1113Shear)
            {
                // 選択された区分の式のみを出力する (区分1: Fc/40、区分2: Fc/45。いずれもアーチ項と比較)
                bool isCase2 = ConcreteModelOptions.Notification1113CompressionCase == 2;
                string caseLabel = isCase2 ? "区分2" : "区分1";
                string fsBase = isCase2 ? @"\frac{F_{c}}{45}" : @"\frac{F_{c}}{40}";

                AddText(body, ConcreteModelOptions.MapLimitStateText(
                    $"場所打ち鉄筋コンクリート杭の使用限界せん断力Qs（告示1113(第8) 長期・{caseLabel}を適用）"));
                AddEq(body, $@"Q_{{s}} = f_{{s,L}}\,b\,j,\quad
                    f_{{s,L}} = \min\left({fsBase},\ \frac{{3}}{{4}}\left(0.49+\frac{{F_{{c}}}}{{100}}\right)\right)");

                AddText(body, ConcreteModelOptions.MapLimitStateText("場所打ち鉄筋コンクリート杭の損傷限界せん断力Qd（告示1113(第8) 短期）"));
                AddEq(body, @"Q_{d} = 1.5\,f_{s,L}\,b\,j");
            }
            else
            {
                AddText(body, ConcreteModelOptions.MapLimitStateText("場所打ち鉄筋コンクリート杭の使用限界せん断力Qs"));
                AddEq(body, @"
                    Q_{s} = \beta_{1}\cdot \frac{2}{3}\cdot
                    \frac{0.065k_{c}\left(49.0+\xi F_{c}\right)}
                    {\dfrac{M}{Qd}+1.7}
                    \left(1+\frac{\sigma_{0}}{14.7}\right)bj
                    ");

                AddText(body, ConcreteModelOptions.MapLimitStateText("場所打ち鉄筋コンクリート杭の損傷限界せん断力Qd"));
                AddEq(body, @"
                    Q_{d} = \beta_{1}\cdot
                    \frac{0.065k_{c}\left(49.0+\xi F_{c}\right)}
                    {\dfrac{M}{Qd}+1.7}
                    \left(1+\frac{\sigma_{0}}{14.7}\right)bj
                    ");
            }

            AddText(body, "場所打ち鉄筋コンクリート杭の安全限界せん断力Qu");
            //AddEquation_InsituReinforcedPileQu(body);
            AddEq(body, @"
                Q_{u} = \beta_{1}\cdot \beta_{2}\cdot
                \left\{
                \frac{0.053p_{t}^{0.23}\left(18+\xi F_{c}\right)}
                {\dfrac{M}{Qd}+0.12}
                +0.85\sqrt{p_{w}\cdot\sigma_{wy}}+0.1\sigma_{0}\right\}
                bj
                ");

            AddText(body, ConcreteModelOptions.MapLimitStateText("場所打ち鋼管コンクリート杭の使用限界曲げモーメントMs"));
            //AddEquation_InsituSteelReinforcedPileMs(body);
            AddEq(body, @"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2},M_{s3},M_{s4},M_{s5}\right)");

            AddText(body, ConcreteModelOptions.MapLimitStateText("場所打ち鋼管コンクリート杭の損傷限界曲げモーメントMd"));
            //AddEquation_InsituSteelReinforcedPileMd(body);
            AddEq(body, @"M_{d} = \beta_{1}\cdot \min\left(M_{d1},M_{d2},M_{d3},M_{d4},M_{d5}\right)");


            // 場所打ちRC杭と同様、e関数法 ON 時は β1・β2 低減・軸力制限を課さない
            if (ConcreteModelOptions.UseInsituUltimateEFunction)
            {
                AddText(body, "場所打ち鋼管コンクリート杭の安全限界曲げモーメントMu（e関数法、耐震設計指針(案)5.4.1 準拠・低減なし）");
                AddEq(body, @"M_{u} = M_{u0}");
            }
            else
            {
                AddText(body, "場所打ち鋼管コンクリート杭の安全限界曲げモーメントMu");
                AddEq(body, @"M_{u} = \beta_{1}\cdot \beta_{2}\cdot M_{u0}");
            }


            AddText(body, ConcreteModelOptions.MapLimitStateText("場所打ち鋼管コンクリート杭の使用限界せん断力Qs"));
            //AddEquation_InsituSteelReinforcedPileQs(body);
            AddEq(body, @"Q_{s} = \beta_{1}\frac{A_{s}}{\kappa}f_{s,ss}");

            AddText(body, ConcreteModelOptions.MapLimitStateText("場所打ち鋼管コンクリート杭の損傷限界せん断力Qd"));
            //AddEquation_InsituSteelReinforcedPileQd(body);
            AddEq(body, @"Q_{d} = \beta_{1}\frac{A_{s}}{\kappa}f_{s,sd}");

            AddText(body, "場所打ち鋼管コンクリート杭の安全限界せん断力Qu");
            //AddEquation_InsituSteelReinforcedPileQu(body);
            // 基本設定「安全限界を耐震設計指針(案)で算定する」ON時は指針式（合成断面の曲げ寄与を考慮）。
            if (ConcreteModelOptions.UseInsituUltimateEFunction)
            {
                AddEq(body, @"Q_{u} = sQ_{0}\sqrt{1-\eta^{2}}\,\frac{{}_{sc}M_{u}}{{}_{s}M_{u}},\quad
                    sQ_{0} = \frac{2\,t_{s}(D - t_{s})\,{}_{s}\sigma_{y}}{\sqrt{3}},\quad
                    \eta = \frac{N}{{}_{s}\sigma_{y}\,{}_{s}A}");
            }
            else
            {
                AddEq(body, @"
                    Q_{u} = \beta_{1}\beta_{2}
                    \frac{2}{3}\pi t_{s}(D - t_{s})
                    \frac{f_{cy}}{\sqrt{3}}
                    \sqrt{1 - p^{2}}
                ");
            }

            // 場所打ち系の安全限界曲げに用いるコンクリートの応力ひずみ関係。
            // 既定はバイリニア型。基本設定「安全限界を耐震設計指針(案)で算定する」ONのときのみ e関数法。
            if (ConcreteModelOptions.UseInsituUltimateEFunction)
            {
                AddText(body, "コンクリートの応力ひずみ関係（e関数法、耐震設計指針(案)5.4.1 準拠）");
                AddEq(body, @"
                    \frac{\sigma}{\xi\cdot F_{c}} = 6.75 \left\{
                      e^{-0.812\left(\frac{\varepsilon}{\varepsilon_{m}}\right)}
                      - e^{-1.218\left(\frac{\varepsilon}{\varepsilon_{m}}\right)}
                    \right\}
                ");
            }
            else
            {
                string plateau = ConcreteModelOptions.UseReducedCompression ? @"0.85 F_{c}" : @"\xi F_{c}";
                AddText(body, "コンクリートの応力ひずみ関係（バイリニア型）");
                AddEq(body, $@"\sigma = \min\left(E_{{c}}\varepsilon,\ {plateau}\right)\quad(0 \le \varepsilon \le \varepsilon_{{cu}})");
                // εcu は杭種・オプションで変わるので、値は本文で明示する（式に埋め込まない）。
                AddText(body,
                    "ここで εcu は終局（安全限界）の圧縮縁ひずみであり、"
                    + (ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete
                        ? "場所打ち鋼管コンクリート杭の鋼管コンクリート部は εcu = 5,000μ、その他の場所打ち系は εcu = 3,000μ とする。"
                        : "場所打ち系は εcu = 3,000μ とする。"));
            }

            // 解析用 M-φ の算定方式（基本設定「M-φ関係をファイバーモデルで算定する」ON時のみ明記）
            if (ConcreteModelOptions.UseFiberMPhi)
            {
                AddText(body, "コンクリート系杭の解析用 M-φ 関係（ファイバーモデル）");
                AddText(body,
                    "解析用 M-φ 関係は、指針の折線（Mcr-My-β1・Mu0 等）に代えて、断面の平面保持を仮定した" +
                    "ファイバーモデル（断面分割積分）により算定する。各曲率 φ に対し軸力つり合いを満たす" +
                    "断面ひずみ状態を解き、曲げモーメント M を断面積分で直接求める" +
                    "（対象: 場所打ちRC杭・場所打ち鋼管コンクリート杭・PHC/PRC/SC杭・コンクリート充填鋼管部。" +
                    "掃引終点は各杭種の終局状態（圧縮縁ひずみが終局ひずみ εcu に達した状態）。" +
                    "低減係数 β1・β2 は乗じない。曲線は解析の安定のため単調非減少化して用いる。" +
                    "鋼管杭の鋼管部は対象外で従来の折線による）。");
                AddEq(body, @"N = \int_{A} \sigma\left(\varepsilon_{0}-\phi\,z\right)dA,\quad
                    M = -\int_{A} \sigma\left(\varepsilon_{0}-\phi\,z\right)z\,dA");
            }
            else
            {
                // 既定 (指針ポリリニア) 時にも M-φ の作り方を明記する (ファイバー ON 時のみ説明が
                // 付く非対称の解消)
                AddText(body, "コンクリート系杭の解析用 M-φ 関係（指針ポリリニア）");
                AddText(body,
                    "解析用 M-φ 関係は、(0, 0)-(φcr, Mcr)-(φy, My)-(φc, β1・Mu0) 等を結ぶ折線で" +
                    "モデル化する（「基礎部材の強度と変形性能」。折線構成は杭種による）。" +
                    "Mcr は曲げひび割れモーメント、My は主筋（鋼材）降伏時モーメント、" +
                    "Mu0 は安全限界曲げモーメントで、対応する曲率は断面曲げ解析により定める。" +
                    "解析（FEM）で負勾配のばねとならないよう、曲線は単調非減少化して用いる。");
            }

            AddSectionKctbMethod(body);
        }

        /// <summary>
        /// KCTB 場所打ち鋼管コンクリート杭（TB工法、BCJ評定-FD0356-08）の設計法の節。
        /// オプション ON のときだけ出力する。
        /// </summary>
        private static void AddSectionKctbMethod(Body body)
        {
            // 評定に従う設定でも、評定範囲外のオプションだけでも、この節を出す。
            if (!ConcreteModelOptions.FollowsKctbEvaluation
                && !ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete
                && !ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete) return;

            AddText(body, "KCTB 場所打ち鋼管コンクリート杭（TB工法）の設計法");
            if (ConcreteModelOptions.FollowsKctbEvaluation)
            AddText(body,
                "場所打ち鋼管コンクリート杭の鋼管コンクリート部は、KCTB 場所打ち鋼管コンクリート杭" +
                "（BCJ評定-FD0356-08）による。コンクリートの許容応力度は平成13年国土交通省告示第1113号" +
                "第8第1項第一号の表中のくい体の打設の方法（一）に該当するものとし、" +
                "設計基準強度 Fc の範囲は 18 N/mm² 以上 45 N/mm² 以下、鋼管の腐食しろは 1 mm とする。");

            // 評定申込事項は 7 項目で、終局（安全限界）の規定は含まれない。
            // 評定範囲外の設定を用いた場合は出典を明記する。
            if (ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete
                || ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete)
            {
                AddText(body,
                    "次の項目は BCJ評定-FD0356-08 の評定範囲外であり、" +
                    "ジャパンパイル株式会社 Technical Note Vol.1-5 および" +
                    "建設省総合技術開発プロジェクト「新建築構造体系の開発」性能評価分科会" +
                    "基礎WG 最終報告書（平成12年3月、建設省建築研究所）資料4-7 による。");
            }

            if (ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete)
            {
            AddText(body, "許容時の限界状態の定義");
            AddText(body,
                "許容時（使用限界・損傷限界）は、圧縮側のコンクリートの応力度が許容圧縮応力度 σca に達した時、もしくは" +
                "圧縮側または引張側の鋼管の応力度が許容応力度 σsa（= 基準強度 Fs）に達した時とする。" +
                "鉄筋は限界状態の判定には用いず、断面の耐力への寄与としてのみ考慮する。");
            }

            if (ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete)
            {
            AddText(body, "コンクリートの終局ひずみ");
            AddEq(body, @"\varepsilon_{cu} = 5{,}000\,\mu");
            AddText(body,
                "鋼管によるコンクリートの拘束効果を考慮した値である。建設省総合技術開発プロジェクト" +
                "「新建築構造体系の開発」性能評価分科会 基礎WG 最終報告書（平成12年3月、建設省建築研究所）" +
                "資料4-7 において、φ800×t9（SKK400）の内面リブ・突起付き鋼管に現場施工でコンクリートを充填した" +
                "実大試験体の正負交番繰り返し曲げ試験と断面解析の照合により、" +
                "3,000μ では限界曲率を実験値に比べ過小に評価し、7,000μ では実験値を上回るため、" +
                "5,000μ であれば十分安全であると結論されている。");
            }

            if (!ConcreteModelOptions.UseFiberNMForSteelPipeConcrete)
            {
                AddText(body, "許容時の軸方向力と曲げモーメントの算定（単純累加）");
                AddText(body,
                    "鋼管コンクリート部の本体部は、日本建築学会「鉄骨鉄筋コンクリート構造計算規準・同解説」" +
                    "（2014）4章 許容応力度に基づく設計 2節 構造各部の算定 に準拠し、" +
                    "鋼管部と鉄筋コンクリート部の許容耐力を累加して算定する。");
                AddEq(body, @"{}_{r}N_{t} \le N \le {}_{r}N_{c}\ \text{のとき}\quad N = {}_{r}N,\quad
                    M \le {}_{s}M_{0} + {}_{r}M");
                AddEq(body, @"N > {}_{r}N_{c}\ \text{のとき}\quad N \le {}_{r}N_{c} + {}_{s}N,\quad M = {}_{s}M");
                AddEq(body, @"N < {}_{r}N_{t}\ \text{のとき}\quad N \ge {}_{r}N_{t} + {}_{s}N,\quad M = {}_{s}M");
                AddEq(body, @"{}_{s}M_{0} = {}_{s}Z\cdot{}_{s}f_{t}");
                AddEq(body, @"\frac{{}_{s}N}{{}_{s}A} + \frac{{}_{s}M}{{}_{s}Z} = {}_{s}f_{c}
                    \quad({}_{s}N\ \text{が圧縮})");
                AddEq(body, @"\frac{{}_{s}N}{{}_{s}A} - \frac{{}_{s}M}{{}_{s}Z} = -{}_{s}f_{t}
                    \quad({}_{s}N\ \text{が引張})");
                AddEq(body, @"{}_{r}N_{c} = \min\left(A_{e}f_{c},\ \frac{A_{e}\,{}_{m}f_{c}}{n}\right),\quad
                    {}_{r}N_{t} = -{}_{m}A\cdot{}_{m}f_{t}");
                AddText(body,
                    "ここで、sA は鋼管部分の断面積、sZ は鋼管の断面係数、sfc・sft は鋼管の許容圧縮・許容引張応力度、" +
                    "mA は全主筋断面積、mfc・mft は主筋の許容圧縮・許容引張応力度、fc はコンクリートの許容圧縮応力度、" +
                    "Ae はコンクリート断面に主筋の断面積を n 倍して加算した換算断面積、n はヤング係数比である。" +
                    "rN・rM は鉄筋コンクリート部の許容軸方向力・許容曲げモーメントで、" +
                    "「鉄筋コンクリート構造計算規準・同解説」（2018）による。");
                AddText(body, "せん断力に対する算定");
                AddEq(body, @"{}_{s}Q = \frac{{}_{s}A}{2}\,{}_{s}f_{s}");
            }
            else
            {
                AddText(body, "許容時の軸方向力と曲げモーメントの算定（ファイバーモデル）");
                AddText(body,
                    "杭断面を中立軸に平行な微小要素に分割し、断面の平面保持を仮定して、" +
                    "各要素のひずみと構成材料の応力度～ひずみ度関係から応力分布を作成し、" +
                    "軸力と曲げモーメントを累加して N-M 関係を求める。" +
                    "鉄筋は等価な断面を持つ薄肉鋼管に置き換える。" +
                    "コンクリートの応力度～ひずみ度関係は σB（= 0.85Fc）を折れ点とするバイリニア型とし、" +
                    "引張応力度は無視する。鋼材の応力度～ひずみ度関係は材料強度を用いたバイリニア型とする。" +
                    "算定手順はジャパンパイル株式会社 Technical Note Vol.1-5 による" +
                    "（BCJ評定-FD0356-08 の本体部の設計法は単純累加であり、本方法は評定範囲外である）。");
                AddEq(body, @"N_{i} = \sigma_{ci}A_{ci} + \sigma_{ri}A_{ri} + \sigma_{si}A_{si},\quad
                    M_{i} = N_{i}L_{i}");
                AddEq(body, @"N = \sum_{i} N_{i},\quad M = \sum_{i} M_{i}");
                AddText(body,
                    "ここで、Aci・Ari・Asi は各要素のコンクリート・鉄筋・鋼管の断面積、" +
                    "σci・σri・σsi はそれぞれの応力度、Li は図心から要素中心までの距離である。");
            }
        }

        /// <summary>
        /// 低減係数 β1・β2 の一覧表（「基礎部材の強度と変形性能」解説表2.6・2.7）。
        /// 耐力式に β1・β2 が現れるのに定義がなかったため追加。
        /// </summary>
        internal static void AddBetaFactorTables(Body body)
        {
            AddIntroText(body, ConcreteModelOptions.MapLimitStateText(
                "レベル1地震時の損傷限界検定では β2 を乗じない（β2 = 1.0 と扱う）。" +
                "レベル2地震時の損傷限界検定では β2 を乗じる。" +
                "使用限界・安全限界は β1×β2 を用いる（レベル差なし）。"));

            const double fs = 8;

            AddText(body, "低減係数 β1（解説表2.6）", "left", 9);
            int w0 = 2800, w1 = 1200, w2 = 2000;
            Table t1 = CreateTableWithBordersAndWidths(w0, w1, w2, w2, w2);
            t1.Append(CreateHeaderRow(
                CreateTableCellWithWidth("杭種別", "center", w0, fs),
                CreateTableCellWithWidth("応力", "center", w1, fs),
                CreateTableCellWithWidth(ConcreteModelOptions.MapLimitStateText("使用限界"), "center", w2, fs),
                CreateTableCellWithWidth(ConcreteModelOptions.MapLimitStateText("損傷限界"), "center", w2, fs),
                CreateTableCellWithWidth("安全限界", "center", w2, fs)));
            (string Type, string Force, string S, string D, string U)[] beta1Rows =
            [
                ("場所打ち鉄筋コンクリート杭", "曲げ", "1.0", "1.0", "σ0≤(1/3)ξFc: 0.95\nσ0>(1/3)ξFc: 0.8"),
                ("場所打ち鉄筋コンクリート杭", "せん断", "0.9", "0.9", "0.8"),
                ("場所打ち鋼管コンクリート杭", "曲げ", "1.0", "1.0", "1.0"),
                ("場所打ち鋼管コンクリート杭", "せん断", "1.0", "1.0", "1.0"),
                ("PHC杭・PHC節杭", "曲げ", "0.9", "1.0", "0.8"),
                ("PHC杭・PHC節杭", "せん断", "1.0", "1.0", "1.0"),
                ("PRC杭", "曲げ", "0.8", "0.8", "0.8"),
                ("PRC杭", "せん断", "1.0", "1.0", "1.0"),
                ("SC杭", "曲げ", "1.0", "1.0", "1.0"),
                ("SC杭", "せん断", "1.0", "1.0", "1.0"),
            ];
            foreach (var r in beta1Rows)
            {
                TableRow row = new();
                row.Append(CreateTableCellWithWidth(r.Type, "left", w0, fs));
                row.Append(CreateTableCellWithWidth(r.Force, "center", w1, fs));
                row.Append(CreateTableCellWithWidth(r.S, "center", w2, fs));
                row.Append(CreateTableCellWithWidth(r.D, "center", w2, fs));
                row.Append(CreateTableCellWithWidth(r.U, "center", w2, fs));
                t1.Append(row);
            }
            body.Append(t1);

            AddText(body, "低減係数 β2（解説表2.7）", "left", 9);
            int v0 = 2800, v1 = 1200, v2 = 6000;
            Table t2 = CreateTableWithBordersAndWidths(v0, v1, v2);
            t2.Append(CreateHeaderRow(
                CreateTableCellWithWidth("杭種別", "center", v0, fs),
                CreateTableCellWithWidth("応力", "center", v1, fs),
                CreateTableCellWithWidth("β2", "center", v2, fs)));
            (string Type, string Force, string B2)[] beta2Rows =
            [
                ("場所打ち鉄筋コンクリート杭", "曲げ", "σ0≤(1/3)ξFc: 1.0、σ0>(1/3)ξFc: 0.65"),
                ("場所打ち鉄筋コンクリート杭", "せん断", "σ0≤(1/3)ξFc: 0.75、σ0>(1/3)ξFc: 0.65"),
                ("場所打ち鋼管コンクリート杭", "曲げ", "1.0"),
                ("場所打ち鋼管コンクリート杭", "せん断", "1.0"),
                ("PHC杭・PHC節杭", "曲げ", "σe+σ0e≤10 N/mm²: 0.75、σe+σ0e>10 N/mm²: 0.65"),
                ("PHC杭・PHC節杭", "せん断", "0.65"),
                ("PRC杭", "曲げ", "σe+σ0e≤10 N/mm²: 0.75、σe+σ0e>10 N/mm²: 0.65"),
                ("PRC杭", "せん断", "0.65"),
                ("SC杭", "曲げ", "1.0"),
                ("SC杭", "せん断", "1.0"),
            ];
            foreach (var r in beta2Rows)
            {
                TableRow row = new();
                row.Append(CreateTableCellWithWidth(r.Type, "left", v0, fs));
                row.Append(CreateTableCellWithWidth(r.Force, "center", v1, fs));
                row.Append(CreateTableCellWithWidth(r.B2, "left", v2, fs));
                t2.Append(row);
            }
            body.Append(t2);

            AddText(body, "出典: 日本建築学会「基礎部材の強度と変形性能」（第1版、2022年）解説表2.6・2.7", "left", 8);
        }

        // 荷重条件の表を追加するメソッド
        private static void AddTrilinearCircumResistanceTable(Body body)
        {
            double fontSize = 8.0;
            Table table = CreateTableWithBorders();

            //static DocumentFormat.OpenXml.Math.OfficeMath mathTau1() =>
            //    GetCombinedRunToMath([
            //        GetSubscript(GetRun("τ"), GetRun("1")),
            //        ]);

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["地盤種別"], fontSize, "center"),
            CreateTableCell(["τ<_1>", "[kN/m<^2>]"], fontSize, "center"),
            CreateTableCell(["S<_1>", "[mm]"], fontSize, "center"),
            CreateTableCell(["τ<_2>", "[kN/m<^2>]"], fontSize, "center"),
            CreateTableCell(["S<_2>", "[mm]"], fontSize, "center")
            );
            table.Append(headerRow);

            // データ行を追加
            TableRow dataRowSand = new();
            dataRowSand.Append(CreateTableCell(["砂質土"], fontSize, "right"));
            dataRowSand.Append(CreateTableCell(["0.8τ<_max>"], fontSize, "right"));
            dataRowSand.Append(CreateTableCell(["5"], fontSize, "right"));
            dataRowSand.Append(CreateTableCell(["τ<_max>"], fontSize, "right"));
            dataRowSand.Append(CreateTableCell(["20"], fontSize, "right"));
            table.Append(dataRowSand);

            TableRow dataRowGravel = new();
            dataRowGravel.Append(CreateTableCell(["礫質土"], fontSize, "right"));
            dataRowGravel.Append(CreateTableCell(["0.7τ<_max>"], fontSize, "right"));
            dataRowGravel.Append(CreateTableCell(["10"], fontSize, "right"));
            dataRowGravel.Append(CreateTableCell(["τ<_max>"], fontSize, "right"));
            dataRowGravel.Append(CreateTableCell(["30"], fontSize, "right"));
            table.Append(dataRowGravel);

            TableRow dataRowClay = new();
            dataRowClay.Append(CreateTableCell(["粘性土"], fontSize, "right"));
            dataRowClay.Append(CreateTableCell(["0.8τ<_max>"], fontSize, "right"));
            dataRowClay.Append(CreateTableCell(["3"], fontSize, "right"));
            dataRowClay.Append(CreateTableCell(["τ<_max>"], fontSize, "right"));
            dataRowClay.Append(CreateTableCell(["10"], fontSize, "right"));
            table.Append(dataRowClay);

            body.Append(table);
        }

        // 地盤変位の節
        private void AddGroundDisplacementSection(Body body)
        {
            AddHeader1(body, "地盤の水平変位", 1);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDmaxA2Eq() =>
            //    Tex(@"\D_{max} = C_{1}\left(\alpha^{2}-1\right)f_{A}\sum {H_{i}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDmax() =>
            //    Tex(@"\D_{max}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDmaxA1Eq() =>
            //    Tex(@"\D_{max} = C_{1}\left(\alpha^{2}-1\right)f_{A}\sum {H_{i}} \left{ C_{2}\left(1 - \frac{1}{\alpha^{2}}\right) + \frac{2R_{z0}}{\alpha}\right}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathC1() =>
            //    Tex(@"\C_{1}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathC2() =>
            //    Tex(@"\C_{2}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathAlpha() =>
            //    Tex(@"\alpha");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFa() =>
            //    Tex(@"\f_{A}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRz0() =>
            //    Tex(@"\R_{Z0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathL() =>
            //    Tex(@"L");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathZ() =>
            //    Tex(@"Z");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathCAlpha() =>
            //    Tex(@"\C_{\alpha}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathT0() =>
            //    Tex(@"\T_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathT0Eq() =>
            //    Tex(@"\T_{0} = 4 \sum \frac{H_{i}}{V_{S0i}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathAlphaEq() =>
            //    Tex(@"\alpha = 1 + \frac{L Z C_{\alpha} T_{0}}{\sum {H_{i}}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFaEq() =>
            //    Tex(@"\f_{A} = \min\left(1.6\alpha T_{0},1\right)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRz0Eq() =>
            //    Tex(@"\R_{Z0} = \frac{\sum {\gamma_{i}V_{S0i}H_{i}}}{\gamma_{B}V_{SB}\sum {H_{i}}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathUEq() =>
            //    Tex(@"\u_{i+1} = u_{i} - \frac{40}{k_{i}\left(\alpha T_{0}\right)^{2}}\sum_{j=1}^{i} {m_{j}u_{j}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKi() =>
            //    Tex(@"\k_{i}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKiEq() =>
            //    Tex(@"\k_{i} = \frac{\gamma_{i}}{g}\cdot\frac{V_{SEi}^{2}}{H_{i}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathVsei() =>
            //    Tex(@"\V_{SEi}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathVseiEq() =>
            //    Tex(@"\V_{SEi} = \left(\frac{\gamma_{i}V_{S0i}}{\gamma_{B}V_{SB}}\right)^{\beta}V_{S0i}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathBeta() =>
            //    Tex(@"\beta");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathBetaEq() =>
            //    Tex(@"\beta = \frac{3}{4}\left(1 - \frac{1}{2^{\alpha-1}}\right)\frac{1}{1 - R_{Z0}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathUStar() =>
            //    Tex(@"\u_{i}^{*}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathUStarEq() =>
            //    Tex(@"\u_{i}^{*} = \frac{u_{i} - u_{N+1}}{1 - u_{N+1}}");
            AddInlineMathParagraph(body, ["a) 地表変位", Tex(@"\D_{max}"), "の算定"]);

            AddInlineMathParagraph(body, [Tex(@"\D_{max} = C_{1}\left(\alpha^{2}-1\right)f_{A}\sum {H_{i}} \left{ C_{2}\left(1 - \frac{1}{\alpha^{2}}\right) + \frac{2R_{z0}}{\alpha}\right}")]);
            AddInlineMathParagraph(body, [Tex(@"\D_{max} = C_{1}\left(\alpha^{2}-1\right)f_{A}\sum {H_{i}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\C_{1}"), ": 表層の土質のG-γ関係から決まる定数（粘性土で0.0028、砂質土で0.0015）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\C_{2}"), ": 表層の土質の減衰特性から決まる定数（粘性土で0.53、砂質土で0.66）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\alpha"), ": 地盤の地震時の固有周期ののび"]);
            AddInlineMathParagraph(body, [Tex(@"\alpha = 1 + \frac{L Z C_{\alpha} T_{0}}{\sum {H_{i}}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\f_{A}"), ": 地震荷重の加速度一定領域の影響を考慮する補正係数"]);
            AddInlineMathParagraph(body, [Tex(@"\f_{A} = \min\left(1.6\alpha T_{0},1\right)")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\R_{Z0}"), ": 地盤の表層と工学的基盤の初期インピーダンス比"]);
            AddInlineMathParagraph(body, [Tex(@"\R_{Z0} = \frac{\sum {\gamma_{i}V_{S0i}H_{i}}}{\gamma_{B}V_{SB}\sum {H_{i}}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L"), ": 地震荷重レベルにより決まる定数（レベル１では0.2、レベル２で1.0）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Z"), ": 地域係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\C_{\alpha}"), ": 表層の土質の動的変形特性から決まる定数（粘性土で25、砂質土で40）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\T_{0}"), ": 地盤の初期固有周期"]);

            AddInlineMathParagraph(body, ["b) 地盤の水平変位の深さ方向分布の算定"]);

            //AddInlineMathParagraph(body, mathUi());
            AddInlineMathParagraph(body, [Tex(@"\u_{i+1} = u_{i} - \frac{40}{k_{i}\left(\alpha T_{0}\right)^{2}}\sum_{j=1}^{i} {m_{j}u_{j}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\k_{i}"), ": 地表から第i番目の等価せん断ばね剛性"]);

            AddInlineMathParagraph(body, [Tex(@"\k_{i} = \frac{\gamma_{i}}{g}\cdot\frac{V_{SEi}^{2}}{H_{i}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\V_{SEi}"), ": 地震時の等価S波速度"]);

            AddInlineMathParagraph(body, [Tex(@"\V_{SEi} = \left(\frac{\gamma_{i}V_{S0i}}{\gamma_{B}V_{SB}}\right)^{\beta}V_{S0i}")]);

            AddInlineMathParagraph(body, [Tex(@"\beta = \frac{3}{4}\left(1 - \frac{1}{2^{\alpha-1}}\right)\frac{1}{1 - R_{Z0}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\u_{i}^{*}"), ": 地盤の水平変位の深さ方向分布"]);

            AddInlineMathParagraph(body, [Tex(@"\u_{i}^{*} = \frac{u_{i} - u_{N+1}}{1 - u_{N+1}}")]);
        }

        // 液状化の節
        private void AddLiquefactionSection(Body body)
        {
            AddHeader1(body, "液状化の検討", 1);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathTauDonSigmaZPrimeEq() =>
            //    Tex(@"\frac{\tau_{d}}{\sigma_{z}'} = r_{n}\frac{\alpha_{max}}{g}\frac{\sigma_{z}}{\sigma_{z}'}r_{d}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRnEq() =>
            //    Tex(@"\r_{n} = 0.1(M-1)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRdEq() =>
            //    Tex(@"\r_{d} = 1 - 0.015z");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathNaEq() =>
            //    Tex(@"\N_{a} = N_{1} + \Delta N_{f}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathN1Eq() =>
            //    Tex(@"\N_{1} = C_{N}N");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathCnEq() =>
            //    Tex(@"\C_{N} = \sqrt{\frac{100}{\sigma_{z}'}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDcyEq() =>
            //    Tex(@"\sum {\frac{\gamma_{cyi}H_{i}}{100}}");

            AddInlineMathParagraph(body, ["① 検討地点の地盤内の各深さに発生する等価なせん断応力比"]);

            AddInlineMathParagraph(body, [Tex(@"\frac{\tau_{d}}{\sigma_{z}'} = r_{n}\frac{\alpha_{max}}{g}\frac{\sigma_{z}}{\sigma_{z}'}r_{d}")]);

            AddText(body, "液状化時の土のせん断応力度比");

            AddInlineMathParagraph(body, [Tex(@"\r_{n} = 0.1(M-1)")]);

            AddInlineMathParagraph(body, [Tex(@"\r_{d} = 1 - 0.015z")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathTauD() =>
            //    Tex(@"\tau_{d}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\tau_{d}"), ": 水平面に生じる等価な一定繰返しせん断応力振幅"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathSigmaZPrime() =>
            //    Tex(@"\sigma_{z}'");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{z}'"), ": 検討深さにおける有効土被り圧（鉛直有効応力）"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRn() =>
            //    Tex(@"\r_{n}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\r_{n}"), ": 等価な繰返し回数に関する補正係数"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathM() =>
            //    Tex(@"M");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M"), ": 地震のマグニチュードで通常は7.5"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathAlphaMax() =>
            //    Tex(@"\alpha_{max}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\alpha_{max}"), ": 地表面における設計用水平加速度（m/s^2）"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathG() =>
            //    Tex(@"g");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"g"), ": 重力加速度（9.8m/s^2）"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathSigmaZ() =>
            //    Tex(@"\sigma_{z}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{z}"), ": 検討深さにおける全土被り圧（鉛直全応力）"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRd() =>
            //    Tex(@"\r_{d}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\r_{d}"), ": 地盤が剛体でないことによる低減係数"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathZ() =>
            //    Tex(@"z");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"z"), ": 地表面からの検討深さ[m]"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathN1() =>
            //    Tex(@"\N_{1}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathCn() =>
            //    Tex(@"\C_{N}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDeltaNf() =>
            //    Tex(@"\Delta N_{f}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathN() =>
            //    Tex(@"N");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFc() =>
            //    Tex(@"F_{c}");
            AddInlineMathParagraph(body, ["② 対応する深度の補正", Tex(@"N"), "値"]);

            AddInlineMathParagraph(body, [Tex(@"\N_{1} = C_{N}N")]);

            AddInlineMathParagraph(body, [Tex(@"\N_{a} = N_{1} + \Delta N_{f}")]);

            AddInlineMathParagraph(body, [Tex(@"\C_{N} = \sqrt{\frac{100}{\sigma_{z}'}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\N_{1}"), ": 換算N値"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\Delta N_{f}"), ": 細粒分含有率", Tex(@"F_{c}"), "に応じた補正", Tex(@"N"), "値成分"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\C_{N}"), ": 拘束圧に関する換算係数"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathREq() =>
            //    Tex(@"\R = \frac{\tau_{L}}{\sigma_{z}'}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathR() =>
            //    Tex(@"R");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFlEq() =>
            //    Tex(@"\F_{L} = \frac{\dfrac{\tau_{L}}{\sigma_{z}'}}{\dfrac{\tau_{d}}{\sigma_{z}'}}");

            AddInlineMathParagraph(body, ["③ 補正", Tex(@"\C_{N}"), "値に対応する飽和土層の液状化抵抗比", Tex(@"R")]);

            AddInlineMathParagraph(body, [Tex(@"\R = \frac{\tau_{L}}{\sigma_{z}'}")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFl() =>
            //    Tex(@"\F_{L}");

            AddInlineMathParagraph(body, ["④ 各深さにおける液状化発生に対する安全率", Tex(@"\F_{L}")]);

            AddInlineMathParagraph(body, [Tex(@"\F_{L} = \frac{\dfrac{\tau_{L}}{\sigma_{z}'}}{\dfrac{\tau_{d}}{\sigma_{z}'}}")]);

            AddInlineMathParagraph(body, ["液状化の程度と地盤変位の予測"]);

            AddInlineMathParagraph(body, [Tex(@"\sum {\frac{\gamma_{cyi}H_{i}}{100}}")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathGammaCyi() =>
            //    Tex(@"\gamma_{cyi}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathHi() =>
            //    Tex(@"\H_{i}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\gamma_{cyi}"), ": i層の繰返しせん断ひずみ(%)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\H_{i}"), ": i層の層厚[m]"]);

            // 液状化判定結果テーブル
            AddLiquefactionResultTable(body);
        }

        /// <summary>各地盤の土質点ごとのFL値を一覧表で出力</summary>
        private void AddLiquefactionResultTable(Body body)
        {
            if (inputModel?.GroundsInput == null) return;

            for (int gIdx = 0; gIdx < inputModel.GroundsInput.Count; gIdx++)
            {
                var ground = inputModel.GroundsInput[gIdx];
                if (ground?.GroundMassesData == null || ground.GroundMassesData.Count == 0) continue;

                // FL値を持つ土質点が存在するか確認
                bool hasFL = ground.GroundMassesData.Any(m => m.FL != null && m.FL.Count > 0 && m.FL.Any(f => f.HasValue));
                if (!hasFL) continue;

                AddText(body, $"地盤No.{gIdx + 1} 液状化判定結果");

                double fontSize = 8;
                int flCount = ground.GroundMassesData.Max(m => m.FL?.Count ?? 0);
                int colCount = 5 + flCount; // 標高, Z, N値, Fc, 土質 + FL×n
                Table table = CreateTableWithBordersAndWidths(GetEqualColumnWidths(colCount));

                // ヘッダ行
                var headerCells = new List<TableCell>
                {
                    CreateTableCell(["標高", "[m]"], fontSize, "center"),
                    CreateTableCell(["Z", "[m]"], fontSize, "center"),
                    CreateTableCell(["N値"], fontSize, "center"),
                    CreateTableCell(["F<_c>", "(%)"], fontSize, "center"),
                    CreateTableCell(["土質"], fontSize, "center"),
                };
                for (int f = 0; f < flCount; f++)
                    headerCells.Add(CreateTableCell([$"F<_L{f + 1}>"], fontSize, "center"));

                table.Append(CreateHeaderRow([.. headerCells]));

                // データ行
                foreach (var mass in ground.GroundMassesData)
                {
                    TableRow row = new();
                    row.Append(CreateTableCell([$"{inputModel.FundamentalInput.ToAbsolute(mass.AltitudeDepth):N2}"], fontSize, "right"));
                    row.Append(CreateTableCell([$"{mass.AltitudeDepth:N2}"], fontSize, "right"));
                    row.Append(CreateTableCell([$"{mass.NValue:N1}"], fontSize, "right"));
                    row.Append(CreateTableCell([$"{mass.Fc:N0}"], fontSize, "right"));
                    row.Append(CreateTableCell([$"{mass.Name}"], fontSize, "center"));
                    for (int f = 0; f < flCount; f++)
                    {
                        double? fl = (mass.FL != null && f < mass.FL.Count) ? mass.FL[f] : null;
                        string flText = fl.HasValue ? $"{fl.Value:N2}" : "-";
                        row.Append(CreateTableCell([flText], fontSize, "right"));
                    }
                    table.Append(row);
                }
                body.Append(table);
                AddLineBreak(body);
            }
        }
    }
}
