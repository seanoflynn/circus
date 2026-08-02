using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

public class FullBookDataProducerTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
    private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);
    private static readonly DateTime Now4 = new(2000, 1, 1, 12, 3, 0);
    private static readonly DateTime Now5 = new(2000, 1, 1, 12, 4, 0);
    private static readonly DateTime Now6 = new(2000, 1, 1, 12, 5, 0);

    private static readonly string CompanyId1 = "Company1";
    private static readonly string CompanyId2 = "Company2";
    private static readonly string CompanyId3 = "Company3";
    private static readonly string CompanyId4 = "Company4";
    private static readonly string CompanyId5 = "Company5";
    private static readonly string CompanyId6 = "Company6";

    private static readonly string OrderId1 = "Order1";
    private static readonly string OrderId2 = "Order2";
    private static readonly string OrderId3 = "Order3";
    private static readonly string OrderId4 = "Order4";
    private static readonly string OrderId5 = "Order5";
    private static readonly string OrderId6 = "Order6";

    private static ManualClock Clock;
    private static IOrderBook Book;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
        Book = new TimestampingOrderBook(Gold, Clock);
    }

    [Test]
    public void Create_ProducesAddedDelta()
    {
        var producer = new FullBookDataProducer();
        Book.UpdateStatus(OrderBookStatus.Open);

        var bookEvents = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);
        var deltas = producer.Process(bookEvents);

        Assert.AreEqual(1, deltas.Count);
        Assert.AreEqual(Side.Buy, deltas[0].Side);
        Assert.AreEqual(100, deltas[0].Price);
        Assert.AreEqual(3, deltas[0].Quantity);
        Assert.AreEqual(OrderBookDeltaAction.Added, deltas[0].Action);
    }

    [Test]
    public void Iceberg_AddedDelta_ShowsOnlyDisplayedPeak_NotHiddenReserve()
    {
        var producer = new FullBookDataProducer();
        Book.UpdateStatus(OrderBookStatus.Open);

        // total 20, only 5 displayed at a time
        var bookEvents = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 20, 100,
            maxVisibleQuantity: 5);
        var deltas = producer.Process(bookEvents);

        Assert.AreEqual(1, deltas.Count);
        Assert.AreEqual(5, deltas[0].Quantity, "only the displayed peak, never the hidden reserve");
        Assert.AreEqual(OrderBookDeltaAction.Added, deltas[0].Action);
    }

    [Test]
    public void IcebergReplenish_ProducesRemovedThenAddedWithNewId()
    {
        var producer = new FullBookDataProducer();
        Book.UpdateStatus(OrderBookStatus.Open);
        var created = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 12, 100,
                maxVisibleQuantity: 5)
            .OfType<CreateOrderConfirmed>().Single();
        producer.Process(new OrderBookEvent[] {created});

        // aggressor larger than the peak - exhausts and replenishes the iceberg mid-match
        var bookEvents = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 100);
        var deltas = producer.Process(bookEvents);

        // one Filled delta per leg of the match (the iceberg resting, and the aggressor)
        var filled = deltas.Where(d => d.Action == OrderBookDeltaAction.Filled).ToList();
        Assert.AreEqual(2, filled.Count);
        Assert.IsTrue(filled.Any(d => d.ExchangeOrderId == created.Order.ExchangeOrderId),
            "the iceberg's fill happened against its pre-replenish id");

        var removed = deltas.Single(d => d.Action == OrderBookDeltaAction.Removed);
        Assert.AreEqual(created.Order.ExchangeOrderId, removed.ExchangeOrderId);

        // Side.Sell, not Side.Buy - the aggressor itself also produces its own Added delta
        // (its CreateOrderConfirmed), distinct from the iceberg's replenish arrival
        var added = deltas.Single(d => d.Action == OrderBookDeltaAction.Added && d.Side == Side.Sell);
        Assert.AreNotEqual(created.Order.ExchangeOrderId, added.ExchangeOrderId);
        Assert.AreEqual(5, added.Quantity, "replenished back to the full peak");
    }

    [Test]
    public void Reprice_LosesPriority_ProducesRemovedThenAddedWithNewId()
    {
        var producer = new FullBookDataProducer();
        Book.UpdateStatus(OrderBookStatus.Open);
        var created = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100)
            .OfType<CreateOrderConfirmed>().Single();

        var bookEvents = Book.UpdateOrder(CompanyId1, OrderId2, OrderId1, price: 110);
        var deltas = producer.Process(bookEvents);

        Assert.AreEqual(2, deltas.Count);
        Assert.AreEqual(created.Order.ExchangeOrderId, deltas[0].ExchangeOrderId);
        Assert.AreEqual(100, deltas[0].Price);
        Assert.AreEqual(OrderBookDeltaAction.Removed, deltas[0].Action);

        Assert.AreNotEqual(created.Order.ExchangeOrderId, deltas[1].ExchangeOrderId);
        Assert.AreEqual(110, deltas[1].Price);
        Assert.AreEqual(OrderBookDeltaAction.Added, deltas[1].Action);
    }

    [Test]
    public void QuantityDecrease_PreservesPriority_ProducesModifiedWithSameId()
    {
        var producer = new FullBookDataProducer();
        Book.UpdateStatus(OrderBookStatus.Open);
        var created = Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100)
            .OfType<CreateOrderConfirmed>().Single();

        var bookEvents = Book.UpdateOrder(CompanyId1, OrderId2, OrderId1, newTotalQuantity: 3);
        var deltas = producer.Process(bookEvents);

        Assert.AreEqual(1, deltas.Count);
        Assert.AreEqual(created.Order.ExchangeOrderId, deltas[0].ExchangeOrderId);
        Assert.AreEqual(3, deltas[0].Quantity);
        Assert.AreEqual(OrderBookDeltaAction.Modified, deltas[0].Action);
    }

    [Test]
    public void Cancel_ProducesRemovedDelta_WithPreCancelQuantity()
    {
        var producer = new FullBookDataProducer();
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 100);

        var bookEvents = Book.CancelOrder(CompanyId1, OrderId2, OrderId1);
        var deltas = producer.Process(bookEvents);

        Assert.AreEqual(1, deltas.Count);
        Assert.AreEqual(100, deltas[0].Price);
        Assert.AreEqual(3, deltas[0].Quantity);
        Assert.AreEqual(OrderBookDeltaAction.Removed, deltas[0].Action);
    }

    [Test]
    public void PartialFill_ProducesFilledDeltaPerLeg_WithFillQuantityNotRemaining()
    {
        var producer = new FullBookDataProducer();
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100);

        var bookEvents = Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 100);
        var deltas = producer.Process(bookEvents);

        var filled = deltas.Where(d => d.Action == OrderBookDeltaAction.Filled).ToList();
        Assert.AreEqual(2, filled.Count, "expected one Filled delta per leg of the match");
        Assert.IsTrue(filled.All(d => d.Quantity == 3), "fill quantity is the traded amount, not the resting order's total or remaining size");
    }

    [Test]
    public void CompanyAndClientOrderIdNeverAppearOnOrderBookDeltaEvent()
    {
        var properties = typeof(OrderBookDeltaEvent).GetProperties().Select(p => p.Name).ToList();

        Assert.IsFalse(properties.Contains("CompanyId"), "a public depth feed must never carry the originating client's CompanyId");
        Assert.IsFalse(properties.Contains("ClientOrderId"), "a public depth feed must never carry the originating client's ClientOrderId");
    }

    [Test]
    public void StillHiddenStopOrder_Create_ProducesNoDelta()
    {
        var producer = new FullBookDataProducer();
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 500);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 500);

        var bookEvents =
            Book.CreateStopLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 5, 530, 510);
        var deltas = producer.Process(bookEvents);

        Assert.IsEmpty(deltas, "a stop order that hasn't triggered yet isn't part of the displayed order book");
    }

    [Test]
    public void StopOrderActivation_ProducesAddedDelta_NotModified()
    {
        var producer = new FullBookDataProducer();
        Book.UpdateStatus(OrderBookStatus.Open);
        Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 500);
        Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 500);

        Clock.SetCurrentTime(Now3);
        Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 5, 520);

        Clock.SetCurrentTime(Now4);
        Book.CreateStopLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Buy, 5, 530, 510);

        Clock.SetCurrentTime(Now5);
        Book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Sell, 2, 510);

        // act - trade at the trigger price, converting the stop into a working limit order
        Clock.SetCurrentTime(Now6);
        var bookEvents = Book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Buy, 2, 510);
        var deltas = producer.Process(bookEvents);

        var activation = deltas.SingleOrDefault(d => d.Price == 530 && d.Action != OrderBookDeltaAction.Filled);
        Assert.IsNotNull(activation, "expected the triggered order's arrival into the working book");
        Assert.AreEqual(OrderBookDeltaAction.Added, activation.Action,
            "it has no prior working-book presence, so it's an arrival, not a move");
    }
}
