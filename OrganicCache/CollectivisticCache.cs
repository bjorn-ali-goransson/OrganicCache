using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OrganicCache
{
    public class CollectivisticCache<TInstance> : CollectivisticCache<string, TInstance> {
        public CollectivisticCache(Func<TInstance, string> idFunction, Func<IEnumerable<TInstance>> getterFunction, TimeSpan getterFunctionWaitPeriod) : base(idFunction, getterFunction, getterFunctionWaitPeriod) { }
        public CollectivisticCache(Func<TInstance, string> idFunction, Func<IEnumerable<TInstance>> getterFunction) : base(idFunction, getterFunction) { }
    }

    public class CollectivisticCache<TId, TInstance>
    {
        public CollectivisticCache(Func<TInstance, TId> idFunction, Func<IEnumerable<TInstance>> getterFunction, TimeSpan getterFunctionWaitPeriod)
            : this(idFunction, getterFunction)
        {
            _getterFunctionWaitPeriod = getterFunctionWaitPeriod;
        }

        public CollectivisticCache(Func<TInstance, TId> idFunction, Func<IEnumerable<TInstance>> getterFunction)
        {
            _getterFunction = getterFunction;
            _idFunction = idFunction;
        }

        #region First run of getter function

        private bool _hasRunGetterFunctionFirstTime;
        private readonly object _hasRunGetterFunctionFirstTimeLock = new object();

        private bool _isRunningGetterFunctionFirstTime;
        private readonly object _isRunningGetterFunctionFirstTimeLock = new object();

        private Task _getterFunctionFirstRunTask;

        #endregion

        private readonly Func<IEnumerable<TInstance>> _getterFunction;
        private readonly Nullable<TimeSpan> _getterFunctionWaitPeriod;

        private bool _isWaitingToRunGetterFunction;
        private readonly object _isWaitingToRunGetterFunctionLock = new object();

        private readonly Func<TInstance, TId> _idFunction;

        private readonly ConcurrentDictionary<TId, TInstance> _instances = new ConcurrentDictionary<TId, TInstance>();
        private readonly object _instancesLock = new object();

        public ICollection<TInstance> GetAll()
        {
            AssertGetterFunctionHasRunFirstTime();

            return _instances.Values;
        }

        public TInstance Get(TId id)
        {
            AssertGetterFunctionHasRunFirstTime();

            TInstance instance;

            _instances.TryGetValue(id, out instance);

            return instance;
        }

        private void AssertGetterFunctionHasRunFirstTime()
        {
            if (_hasRunGetterFunctionFirstTime) {
                return;
            }

            lock (_hasRunGetterFunctionFirstTimeLock)
            {
                if (_hasRunGetterFunctionFirstTime) {
                    return;
                }

                if (_isRunningGetterFunctionFirstTime) {
                    return;
                }

                lock (_isRunningGetterFunctionFirstTimeLock)
                {
                    if (_isRunningGetterFunctionFirstTime) {
                        return;
                    }

                    _isRunningGetterFunctionFirstTime = true;
                    _getterFunctionFirstRunTask = Task.Run(() => RunGetterFunction());
                    Task.WaitAll(_getterFunctionFirstRunTask);
                    _isRunningGetterFunctionFirstTime = false;
                }
                
                _hasRunGetterFunctionFirstTime = true;
            }
        }

        private void RunGetterFunction()
        {
            var instances = _getterFunction();

            foreach (var instance in instances)
            {
                var id = _idFunction(instance);

                lock (_instancesLock)
                {
                    _instances.AddOrUpdate(id, instance, (_, __) => instance);
                }
            }

            RunGetterFunctionAfterWaitPeriod();
        }

        private void RunGetterFunctionAfterWaitPeriod()
        {
            if (_getterFunctionWaitPeriod == null) {
                return;
            }

            if (_isWaitingToRunGetterFunction) {
                return;
            }

            lock (_isWaitingToRunGetterFunctionLock)
            {
                if (_isWaitingToRunGetterFunction) {
                    return;
                }

                _isWaitingToRunGetterFunction = true;

                Task.Run(() =>
                {
                    Thread.Sleep(_getterFunctionWaitPeriod.Value);

                    RunGetterFunction();

                    _isWaitingToRunGetterFunction = false;
                });
            }
        }
    }
}
