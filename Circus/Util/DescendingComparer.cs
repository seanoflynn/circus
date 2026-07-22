using System;
using System.Collections.Generic;

namespace Circus.Util
{
    internal class DescendingComparer<T> : IComparer<T> where T : notnull, IComparable<T>
    {
        // CS8767: BCL's IComparer<T>.Compare is annotated T? regardless of T's own nullability,
        // so implementing it with a notnull-constrained T always trips this warning even though
        // T is only ever instantiated here with `long`, which can never be null.
        #pragma warning disable CS8767
        public int Compare(T x, T y)
        {
            return y.CompareTo(x);
        }
        #pragma warning restore CS8767
    }
}
