namespace Circus.MarketData;

public record RestingOrder(Side Side, string ExchangeOrderId, decimal Price, int Quantity);
