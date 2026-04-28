using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// GroundInput の数値プロパティに導入した INotifyDataErrorInfo 連動の
    /// 入力検証 (BaseModel.SetFiniteDouble / SetFiniteClampedDouble 経由) を検証する。
    /// </summary>
    [TestClass]
    public class GroundInputValidationTests
    {
        // --- GroundTopAltitude (有限値必須) ---

        [TestMethod]
        public void GroundTopAltitude_FiniteValue_NoError()
        {
            var g = new GroundInput { GroundTopAltitude = 12.5 };
            Assert.IsFalse(g.HasErrors);
            Assert.AreEqual(12.5, g.GroundTopAltitude);
        }

        [DataTestMethod]
        [DataRow(double.NaN)]
        [DataRow(double.PositiveInfinity)]
        [DataRow(double.NegativeInfinity)]
        public void GroundTopAltitude_NonFinite_FallsBackAndRecordsError(double bad)
        {
            var g = new GroundInput();
            g.GroundTopAltitude = bad;

            // 値はフォールバック (0.0)
            Assert.AreEqual(0.0, g.GroundTopAltitude);
            // エラーが記録される
            Assert.IsTrue(g.HasErrors, "HasErrors=true が期待される");
            var errors = g.GetErrors(nameof(GroundInput.GroundTopAltitude))?.Cast<string>().ToList();
            Assert.IsNotNull(errors);
            Assert.IsTrue(errors.Count > 0, "エラーメッセージが空");
        }

        [TestMethod]
        public void GroundTopAltitude_RecoversAfterValidValue()
        {
            var g = new GroundInput();
            g.GroundTopAltitude = double.NaN;
            Assert.IsTrue(g.HasErrors);

            g.GroundTopAltitude = 5.0;
            Assert.AreEqual(5.0, g.GroundTopAltitude);
            Assert.IsFalse(g.HasErrors, "有効値セット後は HasErrors=false");
        }

        // --- GroundAcceleration1 / 2 (範囲チェック付き) ---

        [TestMethod]
        public void GroundAcceleration1_OutOfRange_ClampsAndRecordsError()
        {
            var g = new GroundInput();
            g.GroundAcceleration1 = -1.0;  // min=0 を下回る
            Assert.AreEqual(0.0, g.GroundAcceleration1, "下限クランプ");
            Assert.IsTrue(g.HasErrors);

            g.GroundAcceleration1 = 200.0;  // max=100 を上回る
            Assert.AreEqual(100.0, g.GroundAcceleration1, "上限クランプ");
            Assert.IsTrue(g.HasErrors);
        }

        [TestMethod]
        public void GroundAcceleration1_NaN_FallsBackToDefault()
        {
            var g = new GroundInput();
            g.GroundAcceleration1 = double.NaN;
            Assert.AreEqual(1.5, g.GroundAcceleration1, "NaN 時は fallback (1.5) に置換される");
            Assert.IsTrue(g.HasErrors);
        }

        [TestMethod]
        public void GroundAcceleration2_NaN_FallsBackToDefault()
        {
            var g = new GroundInput();
            g.GroundAcceleration2 = double.NaN;
            Assert.AreEqual(3.5, g.GroundAcceleration2);
            Assert.IsTrue(g.HasErrors);
        }

        // --- ErrorsChanged イベント ---

        [TestMethod]
        public void GroundTopAltitude_NaN_FiresErrorsChangedEvent()
        {
            var g = new GroundInput();
            var captured = new List<string>();
            g.ErrorsChanged += (s, e) => captured.Add(e.PropertyName);

            g.GroundTopAltitude = double.NaN;

            Assert.IsTrue(captured.Contains(nameof(GroundInput.GroundTopAltitude)),
                $"ErrorsChanged イベントに GroundTopAltitude が含まれない: [{string.Join(", ", captured)}]");
        }
    }
}
