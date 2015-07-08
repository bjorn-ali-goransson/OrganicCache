using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrganicCache;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tests
{
    [TestClass]
    public class CollectivisticCacheTests
    {
        DateTime nowTime, thenTime;

        public void then() { thenTime = DateTime.Now; }
        public void now() { nowTime = DateTime.Now; }

        [TestMethod]
        public void ShouldWaitForBlockingGetAllMethod()
        {
            then();

            var cache = new CollectivisticCache<int, TestEntity>(
                (entity) => entity.Id,
                () =>
                {
                    Thread.Sleep(5000);

                    return new List<TestEntity> {
                        new TestEntity { Id = 2, Name = "Bjorn" },
                        new TestEntity { Id = 4, Name = "Ali" },
                        new TestEntity { Id = 6, Name = "Hamdi" },
                    };
                },
                TimeSpan.FromSeconds(10)
            );

            var x = cache.GetAll();

            now();

            Assert.IsTrue(nowTime > thenTime.AddSeconds(5), "Only " + (nowTime - thenTime).Seconds + " seconds had elapsed, BulkGetterFunction alone should've taken 5 seconds");

            then();

            var y = cache.GetAll();

            now();

            Assert.IsFalse(nowTime > thenTime.AddSeconds(5), (nowTime - thenTime).Seconds + " seconds had elapsed, should've been immediate");
        }

        [TestMethod]
        public void ShouldWaitForBlockingGetMethod()
        {
            then();
            
            var cache = new CollectivisticCache<int, TestEntity>(
                (entity) => entity.Id,
                () =>
                {
                    Thread.Sleep(5000);

                    return new List<TestEntity> {
                        new TestEntity { Id = 2, Name = "Bjorn" },
                        new TestEntity { Id = 4, Name = "Ali" },
                        new TestEntity { Id = 6, Name = "Hamdi" },
                    };
                },
                TimeSpan.FromSeconds(10)
            );

            var x = cache.Get(2);

            now();

            Assert.IsTrue(nowTime > thenTime.AddSeconds(5), "Only " + (nowTime - thenTime).Seconds + " seconds had elapsed, BulkGetterFunction alone should've taken 5 seconds");

            then();

            var y = cache.Get(4);

            now();

            Assert.IsFalse(nowTime > thenTime.AddSeconds(5), (nowTime - thenTime).Seconds + " seconds had elapsed, should've been immediate");
        }

        [TestMethod]
        public void ShouldRefreshAsynchronously()
        {
            var numberOfGets = 0;

            var cache = new CollectivisticCache<int, TestEntity>(
                i => i.Id,
                () =>
                {
                    Thread.Sleep(2000);

                    numberOfGets++;

                    return new List<TestEntity>
                    {
                        new TestEntity {
                            Id = 2,
                            Name =
                                numberOfGets == 1 ?
                                "Ali" :
                                numberOfGets == 2 ?
                                "Bjorn Ali" :
                                null
                        },
                    };
                },
                TimeSpan.FromSeconds(2)
            );

            var a = cache.Get(2);

            Assert.AreEqual("Ali", a.Name);

            Thread.Sleep(4000 + 1000);

            var b = cache.Get(2);

            Assert.AreEqual("Bjorn Ali", b.Name);
        }

        [TestMethod]
        public void ShouldLetTwoConcurrentGetCallsWaitForFirstGetterFunctionRun()
        {
            then();

            var getterFunctionRuns = 0;

            var cache = new CollectivisticCache<int, TestEntity>(
            i => i.Id,
            () => {
                getterFunctionRuns++;
                Thread.Sleep(10 * 1000);
                return new List<TestEntity> {
                    new TestEntity { Id = 2 },
                    new TestEntity { Id = 4 }
                };
            });

            var t1 = Task.Run(() => 
                cache.Get(2)
            );
            var t2 = Task.Run(() =>
            {
                Thread.Sleep(2 * 1000);
                return cache.Get(4);
            });

            Task.WaitAll(t1, t2);

            now();

            Assert.AreEqual(1, getterFunctionRuns, "Getter function has run more than once");
            Assert.IsTrue(nowTime > thenTime.AddSeconds(5), "Only " + (nowTime - thenTime).Seconds + " seconds had elapsed, GetterFunction alone should've taken 5 seconds");
            Assert.IsNotNull(t1.Result);
            Assert.IsNotNull(t2.Result);
        }

        public class TestEntity
        {
            public int Id;
            public string Name;
        }
    }
}
