namespace Circus
{
    public class AggregateBookLevel
    {
        public int Price { get; set; }
        public int Quantity { get; set; }
        public int Count { get; set; }

        public AggregateBookLevel(int price, int quantity, int count)
        {
            Price = price;
            Quantity = quantity;
            Count = count;
        }
    }
}