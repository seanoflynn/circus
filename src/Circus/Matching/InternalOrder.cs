namespace Circus.Matching;

internal class InternalOrder
{
    public long SequenceNumber { get; private set; }

    public long InternalId { get; }

    public string CompanyId { get; }

    public string ExchangeOrderId => SequenceNumber.ToString();

    public string ClientOrderId { get; private set; }
    public Instrument Instrument { get; }
    public DateTime CreatedTime { get; }
    public DateTime ModifiedTime { get; private set; }
    public DateTime? CompletedTime { get; private set; }
    public OrderStatus Status { get; private set; }
    public OrderType Type { get; private set; }
    public OrderValidity Validity { get; }
    public Side Side { get; }
    public int Quantity { get; private set; }
    public int RemainingQuantity { get; private set; }
    public int FilledQuantity { get; private set; }
    public long? Price { get; private set; }
    public long? TriggerPrice { get; private set; }
    public string? SelfMatchPreventionId { get; }
    public SelfMatchPreventionInstruction? SelfMatchPreventionInstruction { get; }
    public int? MaxVisibleQuantity { get; }

    public int DisplayedQuantity { get; private set; }

    internal InternalOrder? LevelNext { get; set; }
    internal InternalOrder? LevelPrev { get; set; }

    internal long RestingTick { get; set; }

    public InternalOrder(long sequenceNumber, string companyId, string clientOrderId, Instrument instrument, DateTime time,
        OrderStatus status, OrderType type, OrderValidity validity, Side side, int quantity, long? price,
        long? triggerPrice, string? selfMatchPreventionId = null,
        SelfMatchPreventionInstruction? selfMatchPreventionInstruction = null, int? maxVisibleQuantity = null)
    {
        SequenceNumber = sequenceNumber;
        InternalId = sequenceNumber;
        CompanyId = companyId;
        ClientOrderId = clientOrderId;
        Instrument = instrument;
        CreatedTime = time;
        ModifiedTime = time;
        Status = status;
        Type = type;
        Validity = validity;
        Side = side;
        Quantity = quantity;
        RemainingQuantity = Quantity;
        FilledQuantity = 0;
        Price = price;
        TriggerPrice = triggerPrice;
        SelfMatchPreventionId = selfMatchPreventionId;
        SelfMatchPreventionInstruction = selfMatchPreventionInstruction;
        MaxVisibleQuantity = maxVisibleQuantity;
        DisplayedQuantity = Math.Min(maxVisibleQuantity ?? quantity, quantity);
    }

    public override string ToString() =>
        $"[Order #{ExchangeOrderId} company={CompanyId} clientOrder={ClientOrderId} {Status} {ModifiedTime:HH:mm:ss} {Side} {Quantity}@{ToDecimal(Price)}]";

    public Order ToOrder()
    {
        return new(CompanyId, ExchangeOrderId, ClientOrderId, Instrument, CreatedTime, ModifiedTime, CompletedTime,
            Status, Type, Validity, Side, Quantity, FilledQuantity, RemainingQuantity, DisplayedQuantity,
            ToDecimal(Price), ToDecimal(TriggerPrice), SelfMatchPreventionId, SelfMatchPreventionInstruction,
            MaxVisibleQuantity);
    }

    private decimal? ToDecimal(long? ticks) => ticks.HasValue ? ticks.Value * Instrument.TickSize : null;

    public void Cancel(DateTime time, string? clientOrderId = null)
    {
        if (clientOrderId != null)
        {
            ClientOrderId = clientOrderId;
        }

        RemainingQuantity = 0;
        CompletedTime = time;
        Status = OrderStatus.Cancelled;
    }

    public void Expire(DateTime time)
    {
        RemainingQuantity = 0;
        CompletedTime = time;
        Status = OrderStatus.Expired;
    }

    public void Update(long sequenceNumber, DateTime time, int? quantity, long? triggerPrice, long? price,
        string clientOrderId)
    {
        SequenceNumber = sequenceNumber;
        ModifiedTime = time;
        ClientOrderId = clientOrderId;
        if (quantity.HasValue)
        {
            RemainingQuantity -= (Quantity - quantity.Value);
            Quantity = quantity.Value;

            DisplayedQuantity = Math.Min(MaxVisibleQuantity ?? RemainingQuantity, RemainingQuantity);
        }

        if (triggerPrice.HasValue)
        {
            TriggerPrice = triggerPrice;
        }

        if (price.HasValue)
        {
            Price = price;
        }
    }

    public void Fill(DateTime time, int quantity)
    {
        // TODO: validate quantity

        FilledQuantity += quantity;
        RemainingQuantity -= quantity;
        DisplayedQuantity -= quantity;

        if (RemainingQuantity == 0)
        {
            Status = OrderStatus.Filled;
            CompletedTime = time;
        }
    }

    public void FillFullSize(DateTime time, int quantity)
    {
        FilledQuantity += quantity;
        RemainingQuantity -= quantity;
        DisplayedQuantity = Math.Min(MaxVisibleQuantity ?? RemainingQuantity, RemainingQuantity);

        if (RemainingQuantity == 0)
        {
            Status = OrderStatus.Filled;
            CompletedTime = time;
        }
    }

    public void Replenish(long sequenceNumber, DateTime time)
    {
        DisplayedQuantity = Math.Min(MaxVisibleQuantity!.Value, RemainingQuantity);
        SequenceNumber = sequenceNumber;
        ModifiedTime = time;
    }

    public void ConvertToLimit(DateTime time, long sequenceNumber, long? price = null)
    {
        if (price.HasValue)
        {
            Price = price;
        }

        SequenceNumber = sequenceNumber;
        ModifiedTime = time;
        Type = OrderType.Limit;
        Status = OrderStatus.Working;
    }
}
