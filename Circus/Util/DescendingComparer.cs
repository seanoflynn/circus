using System.Collections.Generic;

namespace Circus.Util
{
    internal class DescendingComparer : IComparer<decimal>
    {
        public int Compare(decimal x, decimal y)
        {
            return y.CompareTo(x);
        }
    }
}