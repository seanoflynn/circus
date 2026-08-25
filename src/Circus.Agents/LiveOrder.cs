namespace Circus.Agents;

public sealed record LiveOrder(
    string Symbol,
    string CompanyId,
    string ClientOrderId,
    Side Side,
    OrderStatus Status,
    int Quantity,
    int RemainingQuantity,
    int DisplayedQuantity,
    decimal? Price,
    decimal? TriggerPrice
);
