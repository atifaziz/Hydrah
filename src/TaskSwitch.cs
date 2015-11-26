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
    using System.Linq;
    using System.Threading.Tasks;

    #endregion

    static class TaskSwitch
    {
        public static TaskSwitch<TResult> When<T, TResult>(T task, Func<T, Task<TResult>> then)
            where T : Task =>
            new TaskSwitch<TResult>().When(task, then);

        public static TaskSwitch<TResult> WhenAny<T, TResult>(IEnumerable<T> tasks, Func<T, Task<TResult>> then)
            where T : Task =>
            new TaskSwitch<TResult>().WhenAny(tasks, then);

        public static TaskSwitch<TResult> WhenAny<T, TTask, TResult>(IEnumerable<T> source, Func<T, TTask> taskSelector, Func<T, Task<TResult>> then)
            where TTask : Task =>
            new TaskSwitch<TResult>().WhenAny(source, taskSelector, then);

        public static TaskSwitch<TResult> WhenAny<T, TTask, TResult>(IEnumerable<T> source, Func<T, TTask> taskSelector, Func<TTask, T, Task<TResult>> then)
            where TTask : Task =>
            new TaskSwitch<TResult>().WhenAny(source, taskSelector, then);
    }

    class TaskSwitch<TResult>
    {
        readonly Dictionary<Task, Func<Task<TResult>>> _cases = new Dictionary<Task, Func<Task<TResult>>>();

        TaskSwitch<TResult> Adding(Task task, Func<Task<TResult>> then)
        {
            _cases.Add(task, then);
            return this;
        }

        public TaskSwitch<TResult> When<T>(T task, Func<T, Task<TResult>> then) where T : Task =>
            Adding(task, () => then(task));

        public TaskSwitch<TResult> WhenAny<T>(IEnumerable<T> tasks, Func<T, Task<TResult>> then)
            where T : Task =>
            tasks.Aggregate(this, (me, task) => me.When(task, then));

        public TaskSwitch<TResult> WhenAny<T, TTask>(IEnumerable<T> source, Func<T, TTask> taskSelector, Func<T, Task<TResult>> then)
            where TTask : Task =>
            WhenAny(source, taskSelector, (_, e) => then(e));

        public TaskSwitch<TResult> WhenAny<T, TTask>(IEnumerable<T> source, Func<T, TTask> taskSelector, Func<TTask, T, Task<TResult>> then)
            where TTask : Task =>
            source.Aggregate(this, (me, e) => me.When(taskSelector(e), t => then(t, e)));

        public int CaseCount => _cases.Count;

        public async Task<TResult> Switch() =>
            await _cases[await Task.WhenAny(_cases.Keys).ConfigureAwait(false)]().ConfigureAwait(false);
    }
}