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
    using System.Threading.Tasks;

    static class TaskExtensions
    {
        public static bool HasRunToCompletion(this Task task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            return task.Status == TaskStatus.RanToCompletion;
        }

        public static Task<bool> OrTimeout(this Task task, TimeSpan timeout) =>
            OrTimeout(task, timeout, false, _ => true);

        public static Task<T> OrTimeout<T>(this Task<T> task, TimeSpan timeout) =>
            task.OrTimeout(timeout, default(T), t => t.Result);

        public static async Task<TResult> OrTimeout<T, TResult>(this T task,
            TimeSpan timeout, TResult timeoutResult, Func<T, TResult> resultSelector)
            where T : Task
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (resultSelector == null) throw new ArgumentNullException(nameof(resultSelector));

            var timeoutTask = Task.Delay(timeout);
            var winner = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);
            if (winner == timeoutTask)
            {
                task.IgnoreFault();
                return timeoutResult;
            }
            return resultSelector(task);
        }
    }
}