namespace Circus;

// ExchangeOrderId identifies this order within its Instrument and not beyond it. Each book issues
// ids from its own counter, seeded from the session date, so two instruments opening on the same
// day issue the same run of ids - the venue-wide identity of an order is the pair
// (Instrument, ExchangeOrderId), which is why Instrument travels alongside it here and on every
// event carrying one.
//
// Per book on purpose. A shared counter would be tidier to look at and would make each book's ids
// depend on every other book's traffic, so a book would stop being reproducible from its own
// actions and replaying one instrument alone would stop working. Anything keying a venue-wide
// store, index or drop copy off the id must key off the pair.
public record Order(
    string CompanyId,
    string ExchangeOrderId,
    string ClientOrderId,
    Instrument Instrument,
    DateTime CreatedTime,
    DateTime ModifiedTime,
    DateTime? CompletedTime,
    OrderStatus Status,
    OrderType Type,
    OrderValidity OrderValidity,
    Side Side,
    int Quantity,
    int FilledQuantity,
    int RemainingQuantity,
    int DisplayedQuantity,
    decimal? Price,
    decimal? TriggerPrice,
    string? SelfMatchPreventionId = null,
    SelfMatchPreventionInstruction? SelfMatchPreventionInstruction = null,
    int? MaxVisibleQuantity = null
);
