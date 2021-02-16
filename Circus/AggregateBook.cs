using System.Collections.Generic;
using System.Linq;

namespace Circus
{
    public class AggregateBook
    {
        public AggregateBookLevel[] Bids { get; private set; }
        public AggregateBookLevel[] Asks { get; private set; }
        public int Depth { get; private set; }

        public AggregateBook(int depth)
        {
            Depth = depth;
            Bids = new AggregateBookLevel[depth];
            Asks = new AggregateBookLevel[depth];
        }

        public AggregateBook(int depth, IEnumerable<AggregateBookLevel> bids, IEnumerable<AggregateBookLevel> asks)
        {
            Depth = depth;
            Bids = new AggregateBookLevel[depth];
            Asks = new AggregateBookLevel[depth];

            var b = bids.ToArray();
            var a = asks.ToArray();

            for (var i = 0; i < 10; i++)
            {
                if (i < b.Length)
                    Bids[i] = b[i];

                if (i < a.Length)
                    Asks[i] = a[i];
            }
        }
    }
}