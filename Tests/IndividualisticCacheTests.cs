using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrganicCache;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Tests
{
    [TestClass]
    public class IndividualisticCacheTests
    {
        DateTime nowTime, thenTime;

        public void then() { thenTime = DateTime.Now; }
        public void now() { nowTime = DateTime.Now; }

        [TestMethod]
        public void ShouldWaitForBlockingGetMethod()
        {
            then();

            var cache = new IndividualisticCache<int, TestEntity>(
                (entity) => entity.Id,
                (id) =>
                {
                    Thread.Sleep(5000);

                    if (id == 2)
                    {
                        return new TestEntity { Id = 2, Name = "Bjorn" };
                    }
                    if (id == 4)
                    {
                        return new TestEntity { Id = 4, Name = "Ali" };
                    }
                    if (id == 6)
                    {
                        return new TestEntity { Id = 6, Name = "Hamdi" };
                    }

                    throw new Exception("No such id");
                },
                TimeSpan.FromSeconds(10)
            );

            var x = cache.Get(2);

            now();

            Assert.IsTrue(nowTime > thenTime.AddSeconds(5), "Only " + (nowTime - thenTime).Seconds + " seconds had elapsed, GetterFunction alone should've taken 5 seconds");

            then();

            var y = cache.Get(2);

            now();

            Assert.IsFalse(nowTime > thenTime.AddSeconds(5), (nowTime - thenTime).Seconds + " seconds had elapsed, should've been immediate");
        }

        [TestMethod]
        public void ShouldRefreshAsynchronously()
        {
            var numberOfGets = 0;

            var cache = new IndividualisticCache<int, TestEntity>(
                (entity) => entity.Id,
                (id) =>
                {
                    numberOfGets++;

                    if (numberOfGets == 1)
                    {
                        return new TestEntity { Id = 2, Name = "Ali" };
                    }
                    if (numberOfGets == 2)
                    {
                        return new TestEntity { Id = 2, Name = "Bjorn Ali" };
                    }

                    throw new Exception("No such id");
                },
                TimeSpan.FromSeconds(2)
            );

            var a = cache.Get(2);

            Assert.AreEqual("Ali", a.Name);

            Thread.Sleep(3 * 1000);

            var b = cache.Get(2);

            Assert.AreEqual("Bjorn Ali", b.Name);
        }

        [TestMethod]
        public void ShouldLetTwoConcurrentGetCallsWaitForFirstGetterFunctionRun()
        {
            then();

            var getterFunctionRuns = 0;

            var cache = new IndividualisticCache<int, TestEntity>(i => i.Id, (id) =>
            {
                getterFunctionRuns++;
                Thread.Sleep(10 * 1000);
                return new TestEntity { Id = 2 };
            });

            var t1 = Task.Run(() =>
                cache.Get(2)
            );
            var t2 = Task.Run(() =>
            {
                Thread.Sleep(5 * 1000);
                return cache.Get(2);
            });

            Task.WaitAll(t1, t2);

            now();

            Assert.AreEqual(1, getterFunctionRuns, "Getter function should only have run once");
            Assert.IsTrue(nowTime > thenTime.AddSeconds(10), "Only " + (nowTime - thenTime).Seconds + " seconds had elapsed, GetterFunction alone should've taken 10 seconds");
            Assert.IsNotNull(t1.Result);
            Assert.IsNotNull(t2.Result);
        }

        [TestMethod]
        public void ShouldFollowMaximumConcurrentCallsToGetterFunction()
        {
            var getterFunctionRuns = 0;

            var cacheA = new IndividualisticCache<int, object>(
                idFunction: i => -1,
                getterFunction: (id) =>
                {
                    getterFunctionRuns++;
                    Thread.Sleep(1 * 1000);
                    return new object();
                },
                getterFunctionWaitPeriod: TimeSpan.FromSeconds(10),
                maximumConcurrentCallsToGetterFunction: 2
            );

            var i1 = Task.Run(() => cacheA.Get(1));
            var i2 = Task.Run(() => cacheA.Get(2));
            var i3 = Task.Run(() => cacheA.Get(3));

            Thread.Sleep(1 * 1000); // now all getter functions should be completed
            
            Thread.Sleep((int)(0.1 * 1000)); // (error margin)
            
            Assert.IsTrue(i1.IsCompleted, "Instance 1 should have completed by now");
            Assert.IsTrue(i2.IsCompleted, "Instance 2 should have completed by now");
            Assert.IsTrue(i3.IsCompleted, "Instance 3 should have completed by now");

            Thread.Sleep(10 * 1000); // now getterFunctionWaitPeriod is over; 3 getter functions should be queued and 2 getter functions should run (since maximumConcurrentCallsToGetterFunction is 2)

            Thread.Sleep(1 * 1000); // now 1 last getter function should run

            Thread.Sleep(1 * 1000); // now last one should be done

            Assert.AreEqual(6, getterFunctionRuns, "3 + 2 + 1 getter functions should have run");
        }

        public class TestEntity
        {
            public int Id;
            public string Name;
        }
    }
}
