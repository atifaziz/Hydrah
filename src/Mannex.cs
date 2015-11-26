#region License, Terms and Author(s)
//
// Mannex - Extension methods for .NET
// Copyright (c) 2009 Atif Aziz. All rights reserved.
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

// ReSharper disable PartialTypeWithSinglePart

namespace Mannex.Collections.Generic
{
    #region Imports

    using System;
    using System.Collections.Generic;

    #endregion

    /// <summary>
    /// Extension methods for types implementing <see cref="IEnumerator{T}"/>.
    /// </summary>

    static partial class IEnumeratorExtensions
    {
        /// <summary>
        /// Reads the next value from the enumerator otherwise throws
        /// <see cref="InvalidOperationException"/>.
        /// </summary>

        public static T Read<T>(this IEnumerator<T> enumerator)
        {
            if (enumerator == null) throw new ArgumentNullException("enumerator");
            if (!enumerator.MoveNext()) throw new InvalidOperationException();
            return enumerator.Current;
        }
    }
}

namespace Mannex.Threading.Tasks
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Extension methods for <see cref="CancellationToken"/>.
    /// </summary>

    static partial class CancellationTokenExtensions
    {
        /// <summary>
        /// Creates a task the completes when the cancellation token enters
        /// the cancelled state.
        /// </summary>

        public static Task AsTask(this CancellationToken cancellationToken)
        {
            return cancellationToken.AsTask(0);
        }

        /// <summary>
        /// Creates a task the completes when the cancellation token enters
        /// the cancelled state. An additional parameter specifies the result
        /// to return for the task.
        /// </summary>

        public static Task<T> AsTask<T>(this CancellationToken cancellationToken, T result)
        {
            #if NET45
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(result);
            #endif

            var tcs = new TaskCompletionSource<T>();

            #if !NET45
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.SetResult(result);
                return tcs.Task;
            }
            #endif

            var registration = new CancellationTokenRegistration[1];
            registration[0] = cancellationToken.Register(() =>
            {
                tcs.TrySetResult(result);
                registration[0].Dispose();
            });
            return tcs.Task;
        }
    }
}

namespace Mannex
{
    using System;

    /// <summary>
    /// Extension methods for <see cref="Delegate"/>.
    /// </summary>

    static partial class DelegateExtensions
    {
        /// <summary>
        /// Sequentially invokes each delegate in the invocation list as
        /// <see cref="EventHandler{TEventArgs}"/> and ignores exceptions
        /// thrown during the invocation of any one handler (continuing
        /// with the next handler in the list).
        /// </summary>

        public static void InvokeAsEventHandlerWhileIgnoringErrors<T>(this Delegate del, object sender, T args)
            where T : EventArgs // constraint removed post-.NET 4
        {
            if (del == null) throw new ArgumentNullException("del");
            // ReSharper disable once PossibleInvalidCastExceptionInForeachLoop
            foreach (EventHandler<T> handler in del.GetInvocationList())
                try { handler(sender, args); } catch { /* ignored */ }
        }
    }
}

namespace Mannex.Collections.Generic
{
    #region Imports

    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;

    #endregion

    /// <summary>
    /// Extension methods for <see cref="List{T}"/>.
    /// </summary>

    static partial class ListExtensions
    {
        /// <summary>
        /// Removes and returns the first value of the list.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if list is empty.
        /// </exception>

        [DebuggerStepThrough]
        public static T Shift<T>(this IList<T> list)
        {
            if (list == null) throw new ArgumentNullException("list");
            var value = list.First();
            list.RemoveAt(0);
            return value;
        }
    }
}