using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace OrganicCache
{
    public class IndividualisticCache<TInstance> : IndividualisticCache<string, TInstance> {
        public IndividualisticCache(Func<string, TInstance> getterFunction, TimeSpan getterFunctionWaitPeriod) : base(getterFunction, getterFunctionWaitPeriod) { }
        public IndividualisticCache(Func<string, TInstance> getterFunction) : base(getterFunction) { }
    }

    public class IndividualisticCache<TId, TInstance>
    {
        public IndividualisticCache(Func<TId, TInstance> getterFunction, Nullable<TimeSpan> getterFunctionWaitPeriod, int maximumConcurrentCallsToGetterFunction)
        {
            _getterFunction = getterFunction;
            _getterFunctionWaitPeriod = getterFunctionWaitPeriod;
            _getterFunctionExecutor = new ActionBlock<ExecutorItem>(
                action: (item) => item.Run(),
                dataflowBlockOptions: new ExecutionDataflowBlockOptions {
                    MaxDegreeOfParallelism = maximumConcurrentCallsToGetterFunction,
                }
            );
        }

        public IndividualisticCache(Func<TId, TInstance> getterFunction, Nullable<TimeSpan> getterFunctionWaitPeriod)
            : this(getterFunction, getterFunctionWaitPeriod, ExecutionDataflowBlockOptions.Unbounded) 
        {
        }

        public IndividualisticCache(Func<TId, TInstance> getterFunction)
            : this(getterFunction, null, ExecutionDataflowBlockOptions.Unbounded) 
        {
        }

        private readonly Func<TId, TInstance> _getterFunction;
        private readonly Nullable<TimeSpan> _getterFunctionWaitPeriod;

        private readonly Dictionary<TId, bool> _hasRunGetterFunctionFirstTimeForInstance = new Dictionary<TId, bool>();
        private readonly Dictionary<TId, object> _hasRunGetterFunctionFirstTimeForInstanceLock = new Dictionary<TId, object>();

        private readonly ConcurrentDictionary<TId, TInstance> _instances = new ConcurrentDictionary<TId, TInstance>();
        private readonly object _lockCreationLock = new object();

        private readonly CancellationTokenSource _schedulerCancellationToken = new CancellationTokenSource();
        private readonly ActionBlock<SchedulerItem> _scheduler = new ActionBlock<SchedulerItem>(
            action: async (item) => await item.RunAsync(),
            dataflowBlockOptions: new ExecutionDataflowBlockOptions {
                MaxDegreeOfParallelism = ExecutionDataflowBlockOptions.Unbounded,
            }
        );
        private readonly ActionBlock<ExecutorItem> _getterFunctionExecutor;

        public TInstance Get(TId id)
        {
            AssertGetterFunctionHasRunFirstTimeForInstance(id);

            return _instances[id];
        }

        private void AssertGetterFunctionHasRunFirstTimeForInstance(TId id)
        {
            if (_hasRunGetterFunctionFirstTimeForInstance.ContainsKey(id)) {
                return;
            }

            if(!_hasRunGetterFunctionFirstTimeForInstanceLock.ContainsKey(id)){
                lock(_lockCreationLock){
                    if (!_hasRunGetterFunctionFirstTimeForInstanceLock.ContainsKey(id))
                    {
                        _hasRunGetterFunctionFirstTimeForInstanceLock[id] = new object();
                    }
                }
            }

            lock (_hasRunGetterFunctionFirstTimeForInstanceLock[id])
            {
                if (_hasRunGetterFunctionFirstTimeForInstance.ContainsKey(id)) {
                    return;
                }

                RunGetterFunction(id);

                _hasRunGetterFunctionFirstTimeForInstance[id] = true;
            }
        }

        private void RunGetterFunction(TId id)
        {
            var instance = _getterFunction(id);

            _instances.AddOrUpdate(id, instance, (_, __) => instance);

            ScheduleGetterFunction(id);
        }

        private void ScheduleGetterFunction(TId id)
        {
            if (_getterFunctionWaitPeriod == null)
            {
                return;
            }

            _scheduler.Post(new SchedulerItem(this, id));
        }

        protected class SchedulerItem
        {
            public SchedulerItem(IndividualisticCache<TId, TInstance> cache, TId id)
            {
                Cache = cache;
                Id = id;
            }

            private readonly IndividualisticCache<TId, TInstance> Cache;
            private readonly TId Id;

            public async Task RunAsync()
            {
                await Task.Delay(Cache._getterFunctionWaitPeriod.Value, Cache._schedulerCancellationToken.Token).ConfigureAwait(false);

                Cache._getterFunctionExecutor.Post(new ExecutorItem(Cache, Id));
            }
        }

        protected class ExecutorItem
        {
            public ExecutorItem(IndividualisticCache<TId, TInstance> cache, TId id)
            {
                Cache = cache;
                Id = id;
            }

            private readonly IndividualisticCache<TId, TInstance> Cache;
            private readonly TId Id;

            public void Run()
            {
                Cache.RunGetterFunction(Id);
            }
        }
    }
}
