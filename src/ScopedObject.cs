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
    using System;
    using Interlocker;

    // ReSharper disable PartialTypeWithSinglePart

    public abstract class ScopedObject<T> : IDisposable
    {
        public abstract T Object { get; }
        public abstract void Dispose();
    }

    public static partial class ScopedObject
    {
        public static ScopedObject<T> Create<T>(IDisposable disposable, T obj) =>
            new SimpleScopedObject<T>(disposable, obj);

        public static ScopedObject<T> CreateThreadSafe<T>(IDisposable disposable, T obj) =>
            new ThreadSafeScopedObject<T>(disposable, obj);

        sealed class SimpleScopedObject<T> : ScopedObject<T>
        {
            IDisposable _scope;
            readonly T _obj;

            public SimpleScopedObject(IDisposable scope, T obj)
            {
                _scope = scope;
                _obj = obj;
            }

            public override T Object
            {
                get
                {
                    if (_scope == null) throw new ObjectDisposedException(nameof(ScopedObject));
                    return _obj;
                }
            }

            public override void Dispose() => Cleaner.Clear(ref _scope)?.Dispose();
        }

        sealed class ThreadSafeScopedObject<T> : ScopedObject<T>
        {
            class State
            {
                public readonly IDisposable Scope;
                public readonly T Object;

                public State(IDisposable scope, T o)
                {
                    Scope = scope;
                    Object = o;
                }
            }

            readonly Interlocked<State> _state;

            public ThreadSafeScopedObject(IDisposable scope, T obj)
            {
                _state = Interlocked.Create(new State(scope, obj));
            }

            public override T Object
            {
                get
                {
                    var state = _state.Value;
                    if (state == null) throw new ObjectDisposedException(nameof(ScopedObject));
                    return state.Object;
                }
            }

            public override void Dispose()
            {
                if (_state.Value == null)
                    return;
                _state.Update(s => Tuple.Create((State) null, s))?.Scope.Dispose();
            }
        }
    }
}