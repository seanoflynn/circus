using System.Collections.Generic;

namespace Circus.Util
{
    internal class DescendingComparer : IComparer<long>
    {
        public int Compare(long x, long y)
        {
            return y.CompareTo(x);
        }
    }
}
