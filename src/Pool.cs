#region Copyright (c) 2015 Atif Aziz. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

namespace Hydrah
{
    #region Imports

    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Reactive.Concurrency;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;
    using System.Reactive.Threading.Tasks;
    using System.Threading;
    using System.Threading.Tasks;
    using AngryArrays.Push;
    using AngryArrays.Shift;
    using AngryArrays.Splice;
    using Interlocker;
    using Mannex.Collections.Generic;
    using Mannex.Threading.Tasks;
    using MoreLinq;
    using Interlocked = Interlocker.Interlocked;
    using Semaphore = Worms.Semaphore;
    using AutoResetEvent = Worms.AutoResetEvent;

    #endregion

    public abstract class Controller<TSetup, TService>
    {
        public Task<TResult> Start<TResult>(TSetup setup, Func<TService, Task, TResult> selector) =>
            Start(setup, CancellationToken.None, selector);
        public abstract Task<TResult> Start<TResult>(TSetup setup, CancellationToken cancellationToken, Func<TService, Task, TResult> selector);
        public abstract Task Shutdown(TService service, bool force = false);
        public virtual string Format(TSetup setup) => $"{setup}";
        public virtual string Format(TService service) => $"{service}";
    }

    public sealed class Stat
    {
        public DateTime?  LastLeaseTime     { get; }
        public TimeSpan?  LastLeaseDuration { get; }
        public long       LeaseCount        { get; }
        public TimeSpan   UsageDuration     { get; }

        public Stat() : this(null, null, 0, TimeSpan.Zero) {}

        public Stat(DateTime? lastLeaseTime, TimeSpan? lastLeaseDuration, long leaseCount, TimeSpan usageDuration)
        {
            LastLeaseTime     = lastLeaseTime;
            LastLeaseDuration = lastLeaseDuration;
            LeaseCount        = leaseCount;
            UsageDuration     = usageDuration;
        }

        public Stat<TSetup, TService> For<TSetup, TService>(int poolId,
                    TService service, string formattedService,
                    TSetup setup, string formattedSetup,
                    bool isLeased, object user) =>
            new Stat<TSetup,TService>(poolId,
                                      service, formattedService,
                                      setup, formattedSetup, isLeased,
                                      user, this);
    }

    [DebuggerDisplay("Service = {_formattedService}, IsLeased = {IsLeased}, PoolId = {PoolId}, Setup = {_formattedSetup}")]
    public sealed class Stat<TSetup, TService>
    {
        public TService  Service           { get; }
        public int       PoolId            { get; }
        public bool      IsLeased          { get; }
        public object    User              { get; }
        public TSetup    Setup             { get; }
        public long      LeaseCount        { get; }
        public DateTime? LastLeaseTime     { get; }
        public TimeSpan? LastLeaseDuration { get; }
        public TimeSpan  UsageDuration     { get; }

        // ReSharper disable PrivateFieldCanBeConvertedToLocalVariable

        readonly string _formattedSetup;
        readonly string _formattedService;

        // ReSharper restore PrivateFieldCanBeConvertedToLocalVariable

        public Stat(int poolId, TService service, string formattedService,
                    TSetup setup, string formattedSetup, bool isLeased,
                    object user, Stat stat)
        {
            PoolId            = poolId;
            Setup             = setup;
            _formattedSetup   = formattedSetup;
            Service           = service;
            _formattedService = formattedService;
            LeaseCount        = stat.LeaseCount;
            LastLeaseTime     = stat.LastLeaseTime;
            LastLeaseDuration = stat.LastLeaseDuration;
            UsageDuration     = stat.UsageDuration;
            IsLeased          = isLeased;
            User              = user;
        }
    }

    public static class Pool
    {
        public static Pool<TSetup, TService> Start<TSetup, TService>(
            IEnumerable<TSetup> configs, Controller<TSetup, TService> controller,
            CancellationToken cancellationToken) =>
            Pool<TSetup, TService>.Start(configs, controller, cancellationToken);
    }

    public sealed class Pool<TSetup, TService>
    {
        [DebuggerDisplay("Pool = {PoolId}, Service = {FormattedService}, Setup = {FormattedSetup}")]
        sealed class Member
        {
            public int      PoolId     { get; }
            public TSetup   Setup      { get; }
            public TService Service    { get; }
            public Task     StopSignal { get; }

            public Stat Stat { get; set; }

            public string FormattedSetup   { get; }
            public string FormattedService { get; }

            public Member(int poolId,
                          TSetup setup, string formattedSetup,
                          TService service, string formattedService,
                          Task stopSignal)
            {
                PoolId           = poolId;
                Setup            = setup;
                FormattedSetup   = formattedSetup;
                Service          = service;
                FormattedService = formattedService;
                StopSignal       = stopSignal;
                Stat             = new Stat();
            }
        }

        [DebuggerDisplay("Server = {Lease.Member.FormattedService}, Setup = {Lease.Member.FormattedSetup}")]
        sealed class LeaseReturn
        {
            public readonly Lease Lease;
            public readonly TimeSpan Duration;

            public LeaseReturn(Lease lease, TimeSpan duration)
            {
                Lease    = lease;
                Duration = duration;
            }
        }

        [DebuggerDisplay("Server = {Member.FormattedService}, Setup = {Member.FormattedSetup}")]
        sealed class Lease : IDisposable
        {
            public Member Member { get; }
            public object User   { get; }
            public DateTime Time { get; }

            readonly TaskCompletionSource<LeaseReturn> _tcs;

            public Lease(Member member, object user = null, DateTime? time = null)
            {
                Member = member;
                User = user;
                _tcs = new TaskCompletionSource<LeaseReturn>();
                Time = time ?? DateTime.Now;
            }

            public void Dispose() =>
                _tcs.TrySetResult(new LeaseReturn(this, DateTime.Now - Time));

            public Task<LeaseReturn> Task => _tcs.Task;
        }

        [DebuggerDisplay("Servers = {Servers.Length}, Leases = {Leases.Length}")]
        sealed class State
        {
            public Member[] Servers { get; }
            public Lease[]  Leases  { get; }

            public State(Member[] servers = null, Lease[] leases = null)
            {
                Servers = servers ?? EmptyArray<Member>.Value;
                Leases  = leases  ?? EmptyArray<Lease>.Value;
            }

            public State WithServers(Member[] servers) => new State(servers, Leases);
            public State WithLeases(Lease[] leases) => new State(Servers, leases);
            public Tuple<State, T> AndWith<T>(T value) => Tuple.Create(this, value);
            public static Tuple<State, T> NullAndWith<T>(T value) => Tuple.Create((State)null, value);
        }

        readonly Semaphore _semaphore = new Semaphore(0);
        readonly IEnumerator<int> _poolIds = MoreEnumerable.Generate(1, id => id + 1).GetEnumerator();
        readonly Interlocked<State> _state;
        readonly AutoResetEvent _leaseEvent = new AutoResetEvent();
        readonly AutoResetEvent _restartEvent = new AutoResetEvent();
        readonly AutoResetEvent _event = new AutoResetEvent();

        Pool()
        {
            _state = Interlocked.Create(new State());

            ObservableStats = Observable
                .Create<Stat<TSetup, TService>[]>(o =>
                {
                    var tcs = new CancellationTokenSource();
                    var events =
                        _event.WaitAsync(Timeout.InfiniteTimeSpan, tcs.Token)
                              .ToObservable()
                              .Select(_ => Stats());
                    return new CompositeDisposable(
                        Disposable.Create(tcs.Cancel),
                        events.Subscribe(o));
                })
                .Repeat()
                .Publish()
                .RefCount()
                .ObserveOn(new EventLoopScheduler());
        }

        public IObservable<Stat<TSetup, TService>[]> ObservableStats { get; }

        public IObservable<TResult> GetObservableStats<TResult>(Func<Stat<TSetup, TService>[], int, TResult> selector) =>
            ObservableStats.Select(stats => selector(stats, _semaphore.FreeCount));

        public Stat<TSetup, TService>[] Stats()
        {
            var state = _state.Value;
            return
                state.Servers.Select(s => s.Stat.For(s.PoolId,
                                                     s.Service, s.FormattedService,
                                                     s.Setup, s.FormattedSetup,
                                                     isLeased: false, user: null))
                     .Concat(state.Leases.Select(l => l.Member.Stat.For(l.Member.PoolId,
                                                                        l.Member.Service, l.Member.FormattedService,
                                                                        l.Member.Setup, l.Member.FormattedSetup,
                                                                        isLeased: true, user: l.User)))
                     .ToArray();
        }

        public Task Task { get; private set; }

        public static Pool<TSetup, TService> Start(IEnumerable<TSetup> configs, Controller<TSetup, TService> controller,
            CancellationToken cancellationToken)
        {
            var pool = new Pool<TSetup, TService>();
            pool.Task = pool.StartCore(configs, controller, cancellationToken);
            return pool;
        }

        async Task StartCore(IEnumerable<TSetup> configs, Controller<TSetup, TService> controller,
            CancellationToken cancellationToken)
        {
            const int hydrationRate = 2;
            var setupList = configs.ToList();
            var refreshTask = _leaseEvent.WaitAsync();
            var poolId = _poolIds.Read();
            var rsts = setupList.Shift(hydrationRate)
                                .Select(setup => new { PoolId  = poolId,
                                                       Setup   = setup,
                                                       // TODO Handle Start throwing
                                                       Task    = controller.Start(setup, cancellationToken, (s, t) => new { Service = s, StopSignal = t }) })
                                .ToList();
            var defaultFailure = new { Setup = default(TSetup), Exception = default(AggregateException) };
            var failures = Enumerable.Repeat(defaultFailure, 0).ToList();
            var cont = Task.FromResult(false);
            var breakerTask = cancellationToken.AsTask();

            restart:

            var st = _state.Update(state => state.WithServers(EmptyArray<Member>.Value));
            var pool = st.Servers;
            var leases = st.Leases;
            var restartTask = _restartEvent.WaitAsync();
            var halting = false;

            while (true)
            {
                var tsw = new TaskSwitch<bool>();
                tsw
                .WhenAny(rsts, e => e.Task, async rst =>
                {
                    // TODO Consolidate (some eager) service starts

                    rsts.Remove(rst);
                    if (!halting && setupList.Any())
                    {
                        TSetup setup;
                        rsts.Add(new { PoolId = poolId,
                                       Setup  = setup = setupList.Shift(),
                                       Task   = controller.Start(setup, cancellationToken, (s, ss) => new { Service = s, StopSignal = ss }) });
                    }

                    if (rst.Task.IsFaulted)
                    {
                        if (rst.PoolId != poolId)
                            rsts.Add(new { PoolId = poolId, rst.Setup,
                                           Task = controller.Start(rst.Setup, cancellationToken, (s, ss) => new { Service = s, StopSignal = ss }) });
                        else
                            failures.Add(new { rst.Setup, rst.Task.Exception });
                    }
                    else if (rst.Task.HasRunToCompletion())
                    {
                        // While a server is still firing up, the pool could
                        // have been restarted. In that event, the instance
                        // that just be re-started.

                        if (rst.PoolId == poolId)
                        {
                            pool = _state.Update(s => s.WithServers(s.Servers.Concat(new Member(poolId, rst.Setup, controller.Format(rst.Setup), rst.Task.Result.Service, controller.Format(rst.Task.Result.Service), rst.Task.Result.StopSignal)).ToArray())).Servers;
                            if (!halting)
                                _semaphore.Signal();
                        }
                        else
                        {
                            await controller.Shutdown(rst.Task.Result.Service).ConfigureAwait(false);
                            setupList.Add(rst.Setup);
                            rsts.AddRange(from setup in setupList.Shift(hydrationRate - rsts.Count)
                                          select new { PoolId = poolId,
                                                       Setup  = setup,
                                                       Task   = controller.Start(setup, cancellationToken, (s, ss) => new { Service = s, StopSignal = ss }) });
                        }
                    }
                    return cont.Result;
                });

                if (!halting)
                    tsw
                    .When(refreshTask, _ =>
                    {
                        st = _state.Value;
                        pool = st.Servers;
                        leases = st.Leases;
                        refreshTask = _leaseEvent.WaitAsync();
                        return cont;
                    })
                    .When(breakerTask, _ =>
                    {
                        halting = true;
                        _semaphore.Block();
                        return cont;
                    })
                    .When(restartTask, async _ =>
                    {
                        _semaphore.Block();
                        var servers = _state.Update(state => state.WithServers(EmptyArray<Member>.Value).AndWith(state.Servers));
                        poolId = _poolIds.Read();
                        _event.Set();
                        // Some servers may be leased out and occupying ports so
                        // don't just go and fire up new servers against the
                        // full set of ports. Re-start those that are free now
                        // followed by those that are leased out and as and when
                        // they return.
                        await Task.WhenAll(servers.Select(rsv => controller.Shutdown(rsv.Service))).ConfigureAwait(false);
                        setupList.AddRange(servers.Select(s => s.Setup)
                                              .Concat(failures.Select(f => f.Setup)));
                        rsts.AddRange(from setup in setupList.Shift(hydrationRate - rsts.Count)
                                      select new { PoolId = poolId,
                                                   Setup  = setup,
                                                   Task   = controller.Start(setup, cancellationToken, (s, ss) => new { Service = s, StopSignal = ss }) });
                        return true;
                    })
                    .WhenAny(leases.Select(l => l.Task), async leaseReturnTask =>
                    {
                        var @return = leaseReturnTask.Result;
                        var lease = @return.Lease;
                        var stat = lease.Member.Stat;
                        lease.Member.Stat = new Stat(@return.Lease.Time,
                                                     @return.Duration,
                                                     stat.LeaseCount + 1,
                                                     stat.UsageDuration + @return.Duration);

                        var ns = _state.Update(state =>
                        {
                            var i = state.Leases.IndexOf(lease);
                            // TODO Handle lease not found on return
                            // This can happen if the server process died and
                            // the lease was removed before it was returned.
                            Debug.Assert(i >= 0);
                            // Return to the pool if the pool Id hasn't changed!
                            var doesBelongToPool = lease.Member.PoolId == poolId;
                            var update = state.WithLeases(state.Leases.Splice(i, 1))
                                              .WithServers(doesBelongToPool
                                                           ? state.Servers.Push(lease.Member)
                                                           : state.Servers);
                            return update.AndWith(new { update.Leases, update.Servers, NeedsRestarting = !doesBelongToPool });
                        });

                        pool = ns.Servers;
                        leases = ns.Leases;

                        // TODO did the server disappear/die?

                        if (ns.NeedsRestarting)
                        {
                            // NOTE! During this restart, no one is being serviced!
                            // Could to better at the cost of complexity.

                            await controller.Shutdown(lease.Member.Service).ConfigureAwait(false);
                            setupList.Add(lease.Member.Setup);
                            rsts.AddRange(from setup in setupList.Shift(hydrationRate - rsts.Count)
                                          select new { PoolId = poolId,
                                                       Setup  = setup,
                                                       Task   = controller.Start(setup, cancellationToken, (s, ss) => new { Service = s, StopSignal = ss }) });
                        }
                        else
                        {
                            _semaphore.Signal();
                        }
                        return cont.Result;
                    })
                    // TODO Look for leased resources that may have stopped
                    .WhenAny(pool.Select(s => s.StopSignal), sig =>
                    {
                        var update = _state.Update(state =>
                        {
                            // A free or leased server could have died so search
                            // both lists.

                            var xq =
                                from ss in new[]
                                {
                                    from s in state.Servers.Index()
                                    select new
                                    {
                                        Index = s.Key, WasLeased = false,
                                        s.Value.StopSignal,
                                    },
                                    from l in state.Leases.Index()
                                    select new
                                    {
                                        Index = l.Key, WasLeased = true,
                                        l.Value.Member.StopSignal,
                                    },
                                }
                                from s in ss
                                where s.StopSignal == sig
                                select s;

                            var x = xq.First();

                            return x.WasLeased
                                 ? state.Leases.Splice(x.Index, 1, (l, d) => state.WithLeases(l).AndWith(new { state.Servers, Leases = l, x.WasLeased, Dead = d[0].Member }))
                                 : state.Servers.Splice(x.Index, 1, (s, d) => state.WithServers(s).AndWith(new { Servers = s, state.Leases, x.WasLeased, Dead = d[0] }));
                        });

                        pool = update.Servers;
                        leases = update.Leases;
                        // TODO if (update.Removed.User == null)
                        {
                            // If the server was free (not leased) then withdraw
                            // from the semaphore to mark availability of one less.

                            if (!update.WasLeased)
                                _semaphore.Withdraw(1);

                            // Restart the server so it joins back the pool.

                            setupList.Add(update.Dead.Setup);
                            rsts.AddRange(from setup in setupList.Shift(hydrationRate - rsts.Count)
                                          select new { PoolId = poolId,
                                                       Setup  = setup,
                                                       Task   = controller.Start(setup, cancellationToken, (s, ss) => new { Service = s, StopSignal = ss }) });
                        }

                        return cont;
                    });

                if (tsw.CaseCount == 0)
                    break;

                if (await tsw.Switch().ConfigureAwait(false))
                    goto restart;

                _event.Set();
            }

            var fst = _state.Update(s => new State().AndWith(s));
            await Task.WhenAll(fst.Servers.Concat(fst.Leases.Select(l => l.Member))
                                          .Select(rsv => controller.Shutdown(rsv.Service)))
                      .ConfigureAwait(false);
        }

        public void Restart() => _restartEvent.Set();

        public Task<TResult> TryGetService<TResult>(object user, Func<IDisposable, TService, TResult> selector) =>
            TryGetService(user, CancellationToken.None, selector);

        public Task<TResult> TryGetService<TResult>(object user, CancellationToken cancellationToken, Func<IDisposable, TService, TResult> selector) =>
            TryGetService(user, Timeout.InfiniteTimeSpan, cancellationToken, selector);

        public Task<TResult> TryGetService<TResult>(object user, TimeSpan timeout, Func<IDisposable, TService, TResult> selector) =>
            TryGetService(user, timeout, CancellationToken.None, selector);

        public async Task<TResult> TryGetService<TResult>(object user, TimeSpan timeout, CancellationToken cancellationToken, Func<IDisposable, TService, TResult> selector)
        {
            while (true)
            {
                if (!await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
                    return selector(null, default(TService));

                var result = _state.Update(state =>
                {
                    var shift = state.Servers.Shift(1, (f, s) => new { Free = f.FirstOrDefault(), Servers = s });
                    // It's possible that the semaphore signaled but the server
                    // may have been removed from the pool meanwhile.
                    if (shift.Free == null)
                        return State.NullAndWith((Lease)null);
                    var lease = new Lease(shift.Free, user);
                    return new State(shift.Servers, state.Leases.Push(lease)).AndWith(lease);
                });

                if (result == null)
                    continue;

                _leaseEvent.Set();
                return selector(result, result.Member.Service);
            }
        }
    }
}
