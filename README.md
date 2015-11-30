# Hydrah

[![Build Status][build-badge]][builds]
[![NuGet][nuget-badge]][nuget-pkg]
[![MyGet][myget-badge]][edge-pkgs]

Hydrah is a .NET PCL (Portable Class Library) for generic pooling of a
service. You describe how a service is configured, started and stopped and
Hydrah nearly takes care of the rest, including restarting any members of the
pool that may stop working as well as restarting the entire pool at any time.
The latter can can be useful, for example, when a configuration file
changes.

Hydrah uses an asynchronous programming model based on the `Task`
abstraction from TPL, whether that is to launch services to be added to the
pool, lease out a member of the pool or shutdown the pool. Nearly all
operations can be aborted via `CancellationToken` objects.

A Hydrah pool works with two types: one that describes how to configure a
service (`TSetup`) and another that is the actual type of the service
(`TService`). Since both are generic, you can decide to use any type for
either without any constraints whatsoever.

A pool needs a subclass of `Controller` that requires you to implement two
methods, one to describe how to start a service and another to stop it:

    public abstract class Controller<TSetup, TService>
    {
        public abstract Task<TResult> Start<TResult>(TSetup setup,
            CancellationToken cancellationToken,
            Func<TService, Task, TResult> selector);
        public abstract Task Shutdown(TService service, bool force = false);
    }

`Start` takes an instance of `TSetup` and uses it to launch and initialize
a service asynchronously. That service then joins the pool once it is ready,
when the `Task` object returned by `Start` completes. The last parameter
of `Start` is a projection callback. Once the service is ready to be added
to the pool, `Start` supplies the callback with the instance of the service
and a `Task` object that acts as a stop signal. If this task object completes
for any reasons, then the service is considered to have stopped and the pool
will restart it in an attempt to have it join the pool again.

`Stop` receives an instance of a service and simply uses whatever method
necessary to stop the service. This could be something as light as just
freeing an in-memory object or closing a connection to remote service.

A pool is created using its static `Start` method:

    public static Pool<TSetup, TService> Start(IEnumerable<TSetup> configs,
        Controller<TSetup, TService> controller,
        CancellationToken cancellationToken)

The first argument is just a sequence of `TSetup` instances that is used to
configure and start services via the controller (the second argument). A
pool attempts a graceful shutdown when the supplied `CancellationToken`
object requests cancellation.

Once a pool has been created, a service can be leased using its
`TryGetService` method:

    public Task<TResult> TryGetService<TResult>(object user,
        TimeSpan timeout, CancellationToken cancellationToken,
        Func<IDisposable, TService, TResult> selector)

The last argument is a project callback that will be supplied an object that
can be used to dispose the lease and another that is the actual leased
service instance.

A `Pool` object itself has a `Task` property that can be used, for example,
to await for a complete shutdown of the pool.


  [build-badge]: https://img.shields.io/appveyor/ci/raboof/hydrah.svg
  [myget-badge]: https://img.shields.io/myget/raboof/v/Hydrah.svg?label=myget
  [edge-pkgs]: https://www.myget.org/feed/raboof/package/nuget/Hydrah
  [nuget-badge]: https://img.shields.io/nuget/v/Hydrah.svg
  [nuget-pkg]: https://www.nuget.org/packages/Hydrah
  [builds]: https://ci.appveyor.com/project/raboof/hydrah
