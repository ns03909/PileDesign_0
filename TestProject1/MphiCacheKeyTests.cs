using PileDesign.Models.InputData;
using System;

namespace TestProject1
{
    /// <summary>
    /// M-φ キャッシュキー（PileSection.GetMPhiCacheKey）の回帰検査。
    ///
    /// キーは断面諸元を手書きで連結した文字列のため、断面プロパティを追加した際に
    /// キーへの追加を忘れると「異なる断面が同じ M-φ を共有する」サイレントなキャッシュ衝突が
    /// 起きる（鋼管杭系 OTHER キーで実例あり → 13270a4 以前に修正）。
    /// 本テストは杭種ごとに「同一諸元→同一キー」「キーに含まれるべき諸元 1 個の変更→キー変化」
    /// 「軸力・材料オプションの変更→キー変化」を検証する。
    /// </summary>
    [TestClass]
    public class MphiCacheKeyTests
    {
        private static void ResetOptions()
        {
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
            ConcreteModelOptions.UseNotification1113Compression = false;
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.IgnoreTensileStrength = false;
            ConcreteModelOptions.UseReducedCompression = false;
            ConcreteModelOptions.UseFiberMPhi = false;
        }

        [TestInitialize]
        public void Init() => ResetOptions();

        [TestCleanup]
        public void Cleanup() => ResetOptions();

        /// <summary>
        /// 共通検査: 同一諸元→同一キー、各諸元変更→キー変化、軸力変更→キー変化。
        /// 変更ごとに新しいインスタンスを生成し、プロパティ setter の副作用連鎖の影響を避ける。
        /// </summary>
        private static void AssertKeyReflectsParameters(
            string typeName, Func<PileSection> make,
            params (string Name, Action<PileSection> Mutate)[] mutations)
        {
            string baseKey = make().GetMPhiCacheKey(1000.0);

            Assert.AreEqual(baseKey, make().GetMPhiCacheKey(1000.0),
                $"{typeName}: 同一諸元でキーが一致しない");
            Assert.AreNotEqual(baseKey, make().GetMPhiCacheKey(1500.0),
                $"{typeName}: 軸力の変更がキーに反映されない");

            foreach (var (name, mutate) in mutations)
            {
                var s = make();
                mutate(s);
                Assert.AreNotEqual(baseKey, s.GetMPhiCacheKey(1000.0),
                    $"{typeName}: {name} の変更がキャッシュキーに反映されない（キー更新漏れ＝衝突の危険）");
            }
        }

        private static PileSection MakeRc() => new()
        {
            PileBodyType = "場所打ち鉄筋コンクリート杭",
            PileSectionType = "鉄筋コンクリート部",
            ConcreteOutDia = 1500.0,
            ConcreteGsi = 1.0,
            ConcreteFc = 27.0,
            MainBarNum = 30,
            MainBarSize = "D29",
            MainBarSpec = "SD390",
            MainBarDr = 200.0,
            PileDiameter = 1500.0,
        };

        [TestMethod]
        public void CacheKey_InsituRc()
        {
            AssertKeyReflectsParameters("場所打ちRC", MakeRc,
                ("ConcreteOutDia", s => s.ConcreteOutDia = 1600.0),
                ("ConcreteGsi", s => s.ConcreteGsi = 0.75),
                ("ConcreteFc", s => s.ConcreteFc = 30.0),
                ("MainBarDr", s => s.MainBarDr = 250.0),
                ("MainBarNum", s => s.MainBarNum = 24),
                ("MainBarSpec", s => s.MainBarSpec = "SD345"),
                ("MainBarSize", s => s.MainBarSize = "D32"));
        }

        private static PileSection MakeSprcSp() => new()
        {
            PileBodyType = "場所打ち鋼管コンクリート杭",
            PileSectionType = "鋼管コンクリート部",
            PipeGrade = "SKK400",
            PipeDia = 1000.0,
            PipeTs = 12.0,
            CorrosionDepth = 1.0,
            ConcreteOutDia = 1000.0,
            ConcreteGsi = 1.0,
            ConcreteFc = 27.0,
            MainBarNum = 20,
            MainBarSize = "D25",
            MainBarSpec = "SD390",
            MainBarDr = 800.0,
            PileDiameter = 1000.0,
        };

        [TestMethod]
        public void CacheKey_InsituSteelPipeConcrete()
        {
            // 注: ConcreteOutDia・MainBarDr は鋼管諸元から自動導出される（手動設定は上書きされる）ため
            //     変異対象にしない。PipeDia/PipeTs の変異が導出値の変化も兼ねる。
            AssertKeyReflectsParameters("場所打ち鋼管コンクリート杭（鋼管コンクリート部）", MakeSprcSp,
                ("PipeGrade", s => s.PipeGrade = "SKK490"),
                ("PipeDia", s => s.PipeDia = 1100.0),
                ("PipeTs", s => s.PipeTs = 16.0),
                ("CorrosionDepth", s => s.CorrosionDepth = 2.0),
                ("ConcreteGsi", s => s.ConcreteGsi = 0.75),
                ("ConcreteFc", s => s.ConcreteFc = 30.0),
                ("MainBarNum", s => s.MainBarNum = 24));
        }

        private static PileSection MakePhc() => new()
        {
            PileBodyType = "既製コンクリート杭",
            PileSectionType = "PHC杭",
            PileDiameter = 600.0,
            ConcreteThickness = 90.0,
            ConcreteFc = 85.0,
            TendonDp = 520.0,
            TendonAp = 560.0,
            TendonSigmaPy = 1226.0,
            TendonSigmaPu = 1418.0,
            Prestress = 4.0,
        };

        [TestMethod]
        public void CacheKey_Phc()
        {
            AssertKeyReflectsParameters("PHC杭", MakePhc,
                ("PileDiameter", s => s.PileDiameter = 700.0),
                ("ConcreteThickness", s => s.ConcreteThickness = 100.0),
                ("ConcreteFc", s => s.ConcreteFc = 105.0),
                ("TendonDp", s => s.TendonDp = 540.0),
                ("TendonAp", s => s.TendonAp = 700.0),
                ("TendonSigmaPy", s => s.TendonSigmaPy = 1275.0),
                ("TendonSigmaPu", s => s.TendonSigmaPu = 1470.0),
                ("Prestress", s => s.Prestress = 8.0));
        }

        private static PileSection MakePhcNodular()
        {
            var s = MakePhc();
            s.PileSectionType = "PHC節杭";
            return s;
        }

        [TestMethod]
        public void CacheKey_PhcNodular()
        {
            AssertKeyReflectsParameters("PHC節杭", MakePhcNodular,
                ("PileDiameter", s => s.PileDiameter = 700.0),
                ("ConcreteThickness", s => s.ConcreteThickness = 100.0),
                ("ConcreteFc", s => s.ConcreteFc = 105.0),
                ("TendonDp", s => s.TendonDp = 540.0),
                ("TendonAp", s => s.TendonAp = 700.0),
                ("TendonSigmaPy", s => s.TendonSigmaPy = 1275.0),
                ("TendonSigmaPu", s => s.TendonSigmaPu = 1470.0),
                ("Prestress", s => s.Prestress = 8.0));
        }

        [TestMethod]
        public void CacheKey_PhcAndPhcNodular_DoNotCollide()
        {
            // 断面諸元が同一でもキーは別。落とすと片方のキャッシュがもう片方に返る
            string keyPhc = MakePhc().GetMPhiCacheKey(1000.0);
            string keyNodular = MakePhcNodular().GetMPhiCacheKey(1000.0);

            Assert.AreNotEqual(keyPhc, keyNodular);
            Assert.IsFalse(keyNodular.StartsWith("OTHER|"), "PHC節杭 がキー未登録で OTHER| に落ちている");
        }

        private static PileSection MakeSc() => new()
        {
            PileBodyType = "既製コンクリート杭",
            PileSectionType = "SC杭",
            PileDiameter = 600.0,
            PipeGrade = "SKK490",
            PipeDia = 600.0,
            PipeTs = 9.0,
            CorrosionDepth = 0.0,
            ConcreteThickness = 90.0,
            ConcreteFc = 105.0,
        };

        [TestMethod]
        public void CacheKey_Sc()
        {
            AssertKeyReflectsParameters("SC杭", MakeSc,
                ("PileDiameter", s => s.PileDiameter = 700.0),
                ("PipeTs", s => s.PipeTs = 12.0),
                ("ConcreteThickness", s => s.ConcreteThickness = 100.0),
                ("ConcreteFc", s => s.ConcreteFc = 85.0),
                ("PipeGrade", s => s.PipeGrade = "SKK400"),
                ("CorrosionDepth", s => s.CorrosionDepth = 1.0));
        }

        private static PileSection MakeSteelPipe() => new()
        {
            PileBodyType = "鋼管杭",
            PileSectionType = "鋼管部",
            PipeGrade = "SKK400",
            PipeDia = 800.0,
            PipeTs = 12.0,
            CorrosionDepth = 1.0,
            PileDiameter = 800.0,
        };

        /// <summary>
        /// OTHER キー（鋼管杭系）にも断面諸元が含まれること。
        /// かつては OTHER キーに諸元が無く、異なる鋼管断面同士が衝突し得た（実例修正済み）。
        /// </summary>
        [TestMethod]
        public void CacheKey_SteelPipe_OtherKeyIncludesGeometry()
        {
            AssertKeyReflectsParameters("鋼管杭（鋼管部）", MakeSteelPipe,
                ("PipeGrade", s => s.PipeGrade = "SKK490"),
                ("PipeDia", s => s.PipeDia = 900.0),
                ("PipeTs", s => s.PipeTs = 16.0),
                ("CorrosionDepth", s => s.CorrosionDepth = 2.0));
        }

        /// <summary>材料モデル化オプション（Signature）の変更がキーに反映されること。</summary>
        [TestMethod]
        public void CacheKey_ReflectsConcreteModelOptions()
        {
            string baseKey = MakeRc().GetMPhiCacheKey(1000.0);

            ConcreteModelOptions.UseFiberMPhi = true;
            Assert.AreNotEqual(baseKey, MakeRc().GetMPhiCacheKey(1000.0),
                "UseFiberMPhi の変更がキーに反映されない");

            ResetOptions();
            ConcreteModelOptions.IgnoreTensileStrength = true;
            Assert.AreNotEqual(baseKey, MakeRc().GetMPhiCacheKey(1000.0),
                "IgnoreTensileStrength の変更がキーに反映されない");
        }
    }
}
