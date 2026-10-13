using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace TestProject1
{
    /// <summary>
    /// 節点の実行時の番号 (<see cref="InputNode.Id"/>) を、複数のスレッドから同時に作っても重ねずに振ること。
    ///
    /// 以前は、ファイナライザが回収した番号をロックの無い static List に戻し、コンストラクタがそこから
    /// 使い回していた。読込・履歴の複製 (Task.Run) などワーカースレッドのコンストラクタとファイナライザの
    /// スレッドが同じ List と次の番号 (<c>_nextId++</c>) を同時に触り、同じ番号の重複・番号の取りこぼし・
    /// RemoveAt の範囲外の例外が起こり得た。ファイナライザがあるだけで全節点 (杭配置の行も) が
    /// ファイナライズの対象になり、GC の負担にもなっていた。
    /// </summary>
    [TestClass]
    public class InputNodeIdAllocationTests
    {
        [TestMethod]
        public void NodesConstructedInParallel_GetDistinctIds()
        {
            const int threads = 8;
            const int perThread = 20_000;
            var kept = new ConcurrentBag<InputNode>();
            var errors = new ConcurrentBag<Exception>();

            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
            {
                try
                {
                    for (int i = 0; i < perThread; i++)
                    {
                        // 残す節点と、すぐ捨てる節点を交互に作る (捨てた節点の番号の回収と競合させる)
                        kept.Add(t % 2 == 0 ? new InputNode() : new PileLayoutDataItem());
                        _ = new InputNode();
                        if (i % 5_000 == 0) GC.Collect(0, GCCollectionMode.Forced, blocking: false);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.AreEqual(0, errors.Count, "節点を作る途中で例外が出ました: " + errors.FirstOrDefault());
            var ids = kept.Select(n => n.Id).ToList();
            Assert.AreEqual(threads * perThread, ids.Count);
            int duplicates = ids.Count - ids.Distinct().Count();
            Assert.AreEqual(0, duplicates, $"同じ番号の節点が {duplicates} 組あります");
            Assert.IsTrue(ids.All(id => id > 0), "0 以下の番号があります");
        }

        /// <summary>節点 (と杭配置の行) にファイナライザを持たせないこと (番号の回収をしない・GC の負担を増やさない)。</summary>
        [TestMethod]
        public void InputNodeHasNoFinalizer()
        {
            foreach (var type in new[] { typeof(InputNode), typeof(PileLayoutDataItem) })
            {
                for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                {
                    var finalizer = t.GetMethod("Finalize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    Assert.IsNull(finalizer, $"{t.Name} にファイナライザがあります ({type.Name} の継承元)");
                }
            }
        }
    }
}
