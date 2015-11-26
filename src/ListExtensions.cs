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
    using System.Collections.Generic;
    using System.Linq;

    static class ListExtensions
    {
        public static T[] Shift<T>(this List<T> list, int count)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (count == 0)
                return EmptyArray<T>.Value;
            var removals = list.Take(count).ToArray();
            list.RemoveRange(0, removals.Length);
            return removals;
        }
    }
}