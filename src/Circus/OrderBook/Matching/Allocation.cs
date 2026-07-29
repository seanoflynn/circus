namespace Circus.OrderBook.Matching;

internal readonly record struct Allocation(InternalOrder Resting, int Quantity, long PriceTicks);
