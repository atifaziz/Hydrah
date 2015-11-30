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

namespace Interlocker
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    // ReSharper disable PartialTypeWithSinglePart

    static partial class Interlocked
    {
        public static Interlocked<T> Create<T>(T value = default(T))
            where T : class =>
            new Interlocked<T>(value);
    }

    [DebuggerDisplay("Value")]
    partial class Interlocked<T> where T : class
    {
        T _value;

        public Interlocked() : this(default(T)) { }
        public Interlocked(T value) { _value = value; }

        public T Value => _value;

        public T Update(Func<T, T> updater) =>
            Update(s => Tuple.Create(s = updater(s), s));

        public TResult Update<TResult>(Func<T, Tuple<T, TResult>> updater) =>
            Update(updater, t => t.Item1, t => t.Item2);

        public TResult Update<TResult>(Func<T, int, Tuple<T, TResult>> updater) =>
            Update(updater, t => t.Item1, t => t.Item2);

        public TResult Update<TUpdate, TResult>(
            Func<T, TUpdate> updater,
            Func<TUpdate, T> stateSelector,
            Func<TUpdate, TResult> resultSelector) =>
            Update((curr, i, attempt) => updater(curr), stateSelector, resultSelector);

        public TResult Update<TUpdate, TResult>(
            Func<T, int, TUpdate> updater,
            Func<TUpdate, T> stateSelector,
            Func<TUpdate, TResult> resultSelector) =>
            Update((curr, i, _) => updater(curr, i), stateSelector, resultSelector);

        public TResult Update<TUpdate, TResult>(
            Func<T, int, TUpdate, TUpdate> updater,
            Func<TUpdate, T> replacementSelector,
            Func<TUpdate, TResult> resultSelector)
        {
            if (updater == null) throw new ArgumentNullException(nameof(updater));
            if (replacementSelector == null) throw new ArgumentNullException(nameof(replacementSelector));
            if (resultSelector == null) throw new ArgumentNullException(nameof(resultSelector));

            var attempt = default(TUpdate);
            var i = 0;
            for (var sw = new SpinWait(); ; sw.SpinOnce(), i++)
            {
                var current = _value;
                var update = updater(current, i, attempt);
                var replacement = replacementSelector(update);
                if (replacement == null || current == System.Threading.Interlocked.CompareExchange(ref _value, replacement, current))
                    return resultSelector(update);
                attempt = update;
            }
        }
    }
}