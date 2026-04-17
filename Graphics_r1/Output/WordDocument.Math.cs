using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Collections.Generic;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;

namespace PileDesign.Output
{
    // OfficeMath の構築ヘルパー、TeX による定型式、シンボル説明段落を出力する partial。
    internal partial class WordDocument
    {
        //数式
        public static DocumentFormat.OpenXml.Math.Run GetRun(string text)
        {
            // Create the base (mc).
            return new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Text(text));
        }

        // 数式　上付き文字
        public static DocumentFormat.OpenXml.Math.Run GetSuperscript(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run superscriptRun)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            DocumentFormat.OpenXml.Math.SuperArgument superArgument = new(superscriptRun);
            return new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Superscript(baseElement, superArgument));
        }

        // 数式　下付き文字
        public static DocumentFormat.OpenXml.Math.Run GetSubscript(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subscriptRun)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            DocumentFormat.OpenXml.Math.SubArgument subArgument = new(subscriptRun);
            return new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Subscript(baseElement, subArgument));
        }

        // 数式　上付き・下付き文字
        public static DocumentFormat.OpenXml.Math.Run GetSubSuperscript(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subscriptRun,
            DocumentFormat.OpenXml.Math.Run superscriptRun)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            DocumentFormat.OpenXml.Math.SubArgument subArgument = new(subscriptRun);
            DocumentFormat.OpenXml.Math.SuperArgument superArgument = new(superscriptRun);
            return new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.SubSuperscript(baseElement, subArgument, superArgument));
        }

        // 左下と右下に下付き文字をもつ文字
        public static DocumentFormat.OpenXml.Math.Run GetDoubleSubscript(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run leftSubscriptRun,
            DocumentFormat.OpenXml.Math.Run rightSubscriptRun)
        {
            DocumentFormat.OpenXml.Math.Subscript leftSubscript = new(
                new DocumentFormat.OpenXml.Math.Base(baseRun),
                new DocumentFormat.OpenXml.Math.SubArgument(leftSubscriptRun)
            );
            DocumentFormat.OpenXml.Math.Subscript rightSubscript = new(
                new DocumentFormat.OpenXml.Math.Base(leftSubscript),
                new DocumentFormat.OpenXml.Math.SubArgument(rightSubscriptRun)
            );
            return new DocumentFormat.OpenXml.Math.Run(rightSubscript);
        }

        // 数式　分数
        public static DocumentFormat.OpenXml.Math.Run GetFraction(
            DocumentFormat.OpenXml.Math.Run numerator, DocumentFormat.OpenXml.Math.Run denominator)
        {
            return new DocumentFormat.OpenXml.Math.Run(
                    new DocumentFormat.OpenXml.Math.Fraction(
                        new DocumentFormat.OpenXml.Math.FractionProperties(),
                        new DocumentFormat.OpenXml.Math.Numerator(numerator),
                        new DocumentFormat.OpenXml.Math.Denominator(denominator)));
        }

        // 数式　分数
        public static DocumentFormat.OpenXml.Math.OfficeMath GetFraction(
            DocumentFormat.OpenXml.Math.OfficeMath numerator, DocumentFormat.OpenXml.Math.OfficeMath denominator)
        {
            return new DocumentFormat.OpenXml.Math.OfficeMath(
                new DocumentFormat.OpenXml.Math.Fraction(
                    new DocumentFormat.OpenXml.Math.FractionProperties(),
                    new DocumentFormat.OpenXml.Math.Numerator(numerator),
                    new DocumentFormat.OpenXml.Math.Denominator(denominator)));
        }

        // 数式　平方根
        public static DocumentFormat.OpenXml.Math.Run GetRadicalRun(DocumentFormat.OpenXml.Math.Run run)
        {
            return new DocumentFormat.OpenXml.Math.Run(
                new DocumentFormat.OpenXml.Math.Radical(new DocumentFormat.OpenXml.Math.Base(run)));
        }

        // 数式　上バー付
        public static DocumentFormat.OpenXml.Math.Run GetTopBarredRun(DocumentFormat.OpenXml.Math.Run baseRun)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            DocumentFormat.OpenXml.Math.AccentProperties accentProperties = new(new DocumentFormat.OpenXml.Math.AccentChar { Val = "¯" });
            DocumentFormat.OpenXml.Math.Accent accent = new(accentProperties, baseElement);
            return new DocumentFormat.OpenXml.Math.Run(accent);
        }

        // 数式　下バー付
        public static DocumentFormat.OpenXml.Math.Run GetBottomBarredRun(DocumentFormat.OpenXml.Math.Run baseRun)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            return new DocumentFormat.OpenXml.Math.Run(
                new DocumentFormat.OpenXml.Math.Bar(baseElement));
        }

        // 数式　()
        public static DocumentFormat.OpenXml.Math.Run GetDelimiteredRun(
            DocumentFormat.OpenXml.Math.Run baseRun, string beginChar, string endChar)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            DocumentFormat.OpenXml.Math.DelimiterProperties delimiterProperties = new()
            {
                BeginChar = new DocumentFormat.OpenXml.Math.BeginChar { Val = beginChar },
                EndChar = new DocumentFormat.OpenXml.Math.EndChar { Val = endChar }
            };
            return new DocumentFormat.OpenXml.Math.Run(
               new DocumentFormat.OpenXml.Math.Delimiter(delimiterProperties, baseElement));
        }

        // 数式　()
        public static DocumentFormat.OpenXml.Math.OfficeMath GetDelimiteredMath(
            DocumentFormat.OpenXml.Math.OfficeMath baseRun, string beginChar, string endChar)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            DocumentFormat.OpenXml.Math.DelimiterProperties delimiterProperties = new()
            {
                BeginChar = new DocumentFormat.OpenXml.Math.BeginChar { Val = beginChar },
                EndChar = new DocumentFormat.OpenXml.Math.EndChar { Val = endChar }
            };
            return new DocumentFormat.OpenXml.Math.OfficeMath(
               new DocumentFormat.OpenXml.Math.Delimiter(delimiterProperties, baseElement));
        }

        // 数式　総和∑
        public static DocumentFormat.OpenXml.Math.Run GetSummationRun(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subRun,
            DocumentFormat.OpenXml.Math.Run superRun)
        {
            DocumentFormat.OpenXml.Math.NaryProperties naryProperties = new(
                new DocumentFormat.OpenXml.Math.AccentChar { Val = "∑" }
            );
            DocumentFormat.OpenXml.Math.Nary nary = new(
                naryProperties,
                new DocumentFormat.OpenXml.Math.SubArgument(subRun),
                new DocumentFormat.OpenXml.Math.SuperArgument(superRun),
                new DocumentFormat.OpenXml.Math.Base(baseRun)
            );
            return new DocumentFormat.OpenXml.Math.Run(nary);
        }

        // 積分記号
        public static DocumentFormat.OpenXml.Math.Run GetIntegralRun(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subRun,
            DocumentFormat.OpenXml.Math.Run superRun)
        {
            var naryProperties = new DocumentFormat.OpenXml.Math.NaryProperties(
                new DocumentFormat.OpenXml.Math.AccentChar { Val = "∫" }
            );
            var nary = new DocumentFormat.OpenXml.Math.Nary(
                naryProperties,
                new DocumentFormat.OpenXml.Math.SubArgument(subRun),
                new DocumentFormat.OpenXml.Math.SuperArgument(superRun),
                new DocumentFormat.OpenXml.Math.Base(baseRun)
            );
            return new DocumentFormat.OpenXml.Math.Run(nary);
        }

        // 数式　総和∑
        public static DocumentFormat.OpenXml.Math.OfficeMath GetSummationMath(
            DocumentFormat.OpenXml.Math.OfficeMath basemath,
            DocumentFormat.OpenXml.Math.Run subRun,
            DocumentFormat.OpenXml.Math.Run superRun)
        {
            DocumentFormat.OpenXml.Math.NaryProperties naryProperties = new(
                new DocumentFormat.OpenXml.Math.AccentChar { Val = "∑" }
            );
            DocumentFormat.OpenXml.Math.Nary nary = new(
                naryProperties,
                new DocumentFormat.OpenXml.Math.SubArgument(subRun),
                new DocumentFormat.OpenXml.Math.SuperArgument(superRun),
                new DocumentFormat.OpenXml.Math.Base(basemath)
            );
            DocumentFormat.OpenXml.Math.OfficeMath officeMath = new();
            officeMath.Append(nary);
            return officeMath;
        }


        // 数式　積分記号
        public static DocumentFormat.OpenXml.Math.Run GetSekibunRun(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subRun,
            DocumentFormat.OpenXml.Math.Run superRun)
        {
            DocumentFormat.OpenXml.Math.Nary nary = new(
                new DocumentFormat.OpenXml.Math.SubArgument(subRun),
                new DocumentFormat.OpenXml.Math.SuperArgument(superRun),
                new DocumentFormat.OpenXml.Math.Base(baseRun)
            );
            return new DocumentFormat.OpenXml.Math.Run(nary);
        }

        // CombineRun to Math
        public static DocumentFormat.OpenXml.Math.OfficeMath GetCombinedRunToMath(
            List<DocumentFormat.OpenXml.Math.Run> runs)
        {
            DocumentFormat.OpenXml.Math.OfficeMath officeMath = new();
            foreach (DocumentFormat.OpenXml.Math.Run run in runs)
            {
                officeMath.Append(run);
            }
            return officeMath;
        }

        // CombineRun
        public static DocumentFormat.OpenXml.Math.Run GetCombinedRun(
            List<DocumentFormat.OpenXml.Math.Run> runs)
        {
            DocumentFormat.OpenXml.Math.Run combinedRun = new();
            foreach (DocumentFormat.OpenXml.Math.Run run in runs)
            {
                combinedRun.Append(run);
            }
            return combinedRun;
        }

        // Mathをbodyに挿入するメソッド
        public static void PutMathInBody(Body body, DocumentFormat.OpenXml.Math.OfficeMath math)
        {
            Paragraph paragraph = new();
            paragraph.Append(math);
            body.Append(paragraph);
        }

        // タブ付きシンボル説明
        public static void AddSymbolDescriptionWithTab(
            Body body,
            double tabPositionsMm,
            object[] parts,
            double fontSize = 10.5,
            int leftIndentMm = 15,
            int firstLineIndentMm = -10
        )
        {
            if (body == null) return;

            parts ??= [];

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Indentation = CreateIndentation(leftIndentMm, firstLineIndentMm),
                    Tabs = tabPositionsMm > 0
                        ? new Tabs(new TabStop
                        {
                            Val = TabStopValues.Left,
                            Position = (int)(tabPositionsMm * TwipsPerMm)
                        })
                        : null
                }
            };

            if (parts.Length == 0)
            {
                body.Append(paragraph);
                return;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == null) continue;

                void AppendString(string s)
                {
                    if (string.IsNullOrEmpty(s)) return;
                    var lines = s.Replace("\r\n", "\n").Split('\n');
                    for (int li = 0; li < lines.Length; li++)
                    {
                        foreach (var e in BuildInlineMixedRuns(lines[li], fontSize))
                            paragraph.Append(e);
                        if (li < lines.Length - 1)
                            paragraph.Append(new Run(new Break()));
                    }
                }

                switch (part)
                {
                    case string s:
                        AppendString(s);
                        break;
                    case Run r:
                        paragraph.Append(r);
                        break;
                    case DocumentFormat.OpenXml.Math.OfficeMath math:
                        paragraph.Append(math);
                        break;
                    case OpenXmlElement oxEl:
                        paragraph.Append(oxEl);
                        break;
                    default:
                        AppendString(part.ToString());
                        break;
                }

                // 1つ目の要素の後にのみタブを挿入
                if (i == 0 && parts.Length > 1)
                {
                    paragraph.Append(new Run(new TabChar()));
                }
            }
            body.Append(paragraph);
        }

        // p = k_h y（TeX形式で記述してパーサ経由でOfficeMathに変換）
        public static void AddEquationP(Body body)
        {
            var math = Tex("p = k_{h} y");
            PutMathInBody(body, math);
        }

        // kh = 3.16 kh0
        public static void AddEquationKh_1(Body body)
        {
            var math = Tex("k_{h}=3.16k_{h0}");
            PutMathInBody(body, math);
        }

        // kh = kh0 / sqrt(ybar)
        public static void AddEquationKh_2(Body body)
        {
            var math = Tex(@"k_{h} = \frac{k_{h0}}{\sqrt{\overline{y}}} = \frac{k_{h0}}{\sqrt{\frac{y}{0.01}}}");
            PutMathInBody(body, math);
        }

        // kh0
        public static void AddEquation_kh0(Body body)
        {
            var math = Tex(@"k_{h0} = \alpha \xi E_{0} \left(\frac{B}{B_{0}}\right)^{-3/4}");
            PutMathInBody(body, math);
        }

        // Kp
        public static void AddEquation_Kp(Body body)
        {
            var math = Tex(@"k_{p} = \frac{1+\sin\phi}{1-\sin\phi}");
            PutMathInBody(body, math);
        }

        private static void AddEq(Body body, string tex)
        {
            if (body == null) return;
            PutMathInBody(body, Tex(tex));
        }

        // PHC杭の使用限界曲げモーメント
        public static void AddEquation_PHCPileMs(Body body)
        {
            var math = Tex(@"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2}\right)");
            PutMathInBody(body, math);
        }

        // PHC杭の損傷限界曲げモーメント
        public static void AddEquation_PHCPileMd(Body body)
        {
            var math = Tex(@"M_{d} = \beta_{1}\cdot \min\left(M_{d1},M_{d2}\right)");
            PutMathInBody(body, math);
        }

        // PHC杭の安全限界曲げモーメント
        public static void AddEquation_PHCPileMu(Body body)
        {
            var math = Tex(@"M_{u} = \beta_{1}\cdot \beta_{2}\cdot M_{u0}");
            PutMathInBody(body, math);
        }

        // PHC杭の使用限界せん断力
        public static void AddEquation_PHCPileQs(Body body)
        {
            var math = Tex(@"Q_{s} = \beta_{1}\cdot \min\left(Q_{s1},Q_{s2}\right)");
            PutMathInBody(body, math);
        }

        // PHC杭の損傷限界せん断力
        public static void AddEquation_PHCPileQd(Body body)
        {
            var math = Tex(@"Q_{d} = \beta_{1}\cdot \min\left(Q_{d1},Q_{d2}\right)");
            PutMathInBody(body, math);
        }

        // PHC杭の安全限界せん断力
        public static void AddEquation_PHCPileQu(Body body)
        {
            var math = Tex(@"Q_{u} = \beta_{1}\cdot \beta_{2}\cdot \min\left(Q_{u1},Q_{u2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の使用限界曲げモーメント
        public static void AddEquation_PRCPileMs(Body body)
        {
            var math = Tex(@"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の損傷限界曲げモーメント
        public static void AddEquation_PRCPileMd(Body body)
        {
            var math = Tex(@"M_{d} = \beta_{1}\cdot \beta_{2}\cdot \min\left(M_{d1},M_{d2},M_{d3}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の安全限界曲げモーメント
        public static void AddEquation_PRCPileMu(Body body)
        {
            var math = Tex(@"M_{u} = \beta_{1}\cdot \beta_{2}\cdot \min\left(M_{u1},M_{u2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の使用限界せん断力
        public static void AddEquation_PRCPileQs(Body body)
        {
            var math = Tex(@"Q_{s} = \beta_{1}\cdot \min\left(Q_{s1},Q_{s2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の損傷限界せん断力
        public static void AddEquation_PRCPileQd(Body body)
        {
            var math = Tex(@"Q_{d} = \beta_{1}\cdot \min\left(Q_{d1},Q_{d2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の安全限界せん断力
        public static void AddEquation_PRCPileQu(Body body)
        {
            var math = Tex(@"Q_{u} = \beta_{1}\cdot \beta_{2}\cdot \min\left(Q_{u1},Q_{u2}\right)");
            PutMathInBody(body, math);
        }

        // SC杭の使用限界曲げモーメント
        public static void AddEquation_SCPileMs(Body body)
        {
            var math = Tex(@"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2},M_{s3}\right)");
            PutMathInBody(body, math);
        }

        // SC杭の損傷限界曲げモーメント
        public static void AddEquation_SCPileMd(Body body)
        {
            var math = Tex(@"M_{d} = \beta_{1}\cdot \beta_{2}\cdot \min\left(M_{d1},M_{d2},M_{d3}\right)");
            PutMathInBody(body, math);
        }

        // SC杭の安全限界曲げモーメント
        public static void AddEquation_SCPileMu(Body body)
        {
            var math = Tex(@"M_{u} = \beta_{1}\cdot \beta_{2}\cdot \min\left(M_{u1},M_{u2}\right)");
            PutMathInBody(body, math);
        }

        // SC杭の使用限界せん断力
        public static void AddEquation_SCPileQs(Body body)
        {
            var math = Tex(@"Q_{s} = \beta_{1}\cdot \frac{1}{\kappa_{s}}\cdot f_{s}\cdot A_{s}");
            PutMathInBody(body, math);
        }

        // SC杭の損傷限界せん断力
        public static void AddEquation_SCPileQd(Body body)
        {
            var math = Tex(@"Q_{d} = \beta_{1}\cdot \beta_{2}\cdot \frac{1}{\kappa_{s}}\cdot f_{d}\cdot A_{s}");
            PutMathInBody(body, math);
        }

        // SC杭の安全限界せん断力
        public static void AddEquation_SCPileQu(Body body)
        {
            var math = Tex(@"Q_{u} = \beta_{1}\cdot \beta_{2}\cdot Q_{s,un}");
            PutMathInBody(body, math);
        }
    }
}
