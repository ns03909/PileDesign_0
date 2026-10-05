using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 例題の断面の値が、ファイルに書いたとおりにモデルへ入ること。
    ///
    /// 主筋の配置直径は、入れた時点のコンクリート径から重心かぶり厚を逆算して持つ (以後は配置直径をかぶり厚から導く)。
    /// 例題の読み込みは配置直径を鋼管の寸法より先に入れていたので、既定の鋼管 (1200×16、コンクリート径 1168) から
    /// かぶり厚を逆算していた。設計例集 3.8 の鋼管コンクリート部は、ファイルでは配置直径 1900 mm なのに
    /// かぶり厚 −366 mm・配置直径 2894 mm (コンクリート径 2162 mm の外) で解析されていた。
    /// ファイルとモデルを突き合わせていなかったので、どの検査にも掛からなかった。
    /// </summary>
    [TestClass]
    public class ExampleSectionFidelityTests
    {
        [TestMethod]
        public void EveryExampleSection_GetsTheMainBarPlacementWrittenInTheFile()
        {
            var problems = new List<string>();
            int compared = 0;
            foreach (var path in TestSource.ExampleFiles("PileExample*.json", atLeast: 10))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                PileExampleData data;
                try { data = PileExampleLoader.LoadFromFile(name); }
                catch (System.Text.Json.JsonException) { continue; }   // 別形式のファイル
                if (data.PileBodies == null) continue;
                // 断面の値は地盤に依らないので、地盤は計算例9 のものを使う (実機と同じローダーで組む)
                var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", name);
                if (model == null) { problems.Add($"{name}: 読み込めません ({error})"); continue; }

                for (int b = 0; b < data.PileBodies.Count && b < model.PileBodies.Count; b++)
                {
                    var segDtos = data.PileBodies[b].Segments;
                    var segs = model.PileBodies[b].PileBodySegments;
                    for (int s = 0; s < segDtos.Count && s < segs.Count; s++)
                    {
                        var dto = segDtos[s];
                        var sec = segs[s].PileSection;
                        if (!dto.MainBarDr.HasValue || sec.MainBarNum <= 0) continue;
                        compared++;
                        string where = $"{name} 杭体{b + 1} 区間{s + 1} ({sec.PileSectionType})";
                        if (System.Math.Abs(sec.MainBarDr - dto.MainBarDr.Value) > 1e-9)
                            problems.Add($"{where}: 配置直径 ファイル {dto.MainBarDr.Value} → モデル {sec.MainBarDr}");
                        if (!(sec.MainBarCenterCover > 0) || !(sec.MainBarDr < sec.ConcreteOutDia))
                            problems.Add($"{where}: 主筋がコンクリートの内側にありません (かぶり厚 {sec.MainBarCenterCover}, 配置直径 {sec.MainBarDr}, コンクリート径 {sec.ConcreteOutDia})");
                    }
                }
            }
            TestSource.AssertScanned(compared, 10, "主筋の配置直径を書いた例題の断面");
            Assert.AreEqual(0, problems.Count, "例題の断面がファイルどおりに入っていません:\n" + string.Join("\n", problems));
        }
    }
}
