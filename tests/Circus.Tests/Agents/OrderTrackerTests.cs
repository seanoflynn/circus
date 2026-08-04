using Circus.Agents;
using Circus.Events;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Agents;

// The tracker is what replaces a participant's private copy of the book, so what matters is that
// it agrees with a real one. Every test here drives an actual OrderBook and feeds the tracker only
// the events a venue would route to that company - which is also the point: if the two ever
// disagree, one of them is wrong about what is resting, and the tracker has no way to be right on
// its own.
[TestFixture]
public class OrderTrackerTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Now = new(2000, 1, 1, 12, 0, 0);

    private const string Company = "Company1";
    private const string Counterparty = "Company2";

    private ManualClock _clock;
    private IOrderBook _book;
    private OrderTracker _tracker;

    [SetUp]
    public void SetUp()
    {
        _clock = new ManualClock(Now);
        _book = new TimestampingOrderBook(Gold, _clock);
        _tracker = new OrderTracker();

        Feed(_book.UpdateStatus(OrderBookStatus.Open));
    }

    // What AgentVenue routes: order events carrying this company's id, and nothing else.
    private void Feed(IReadOnlyList<OrderBookEvent> events)
    {
        foreach (var ev in events)
        {
            if (ev is OrderEvent order && order.CompanyId == Company)
                _tracker.Apply(ev);
        }
    }

    private void Rest(string clientOrderId, Side side, int quantity, decimal price) =>
        Feed(_book.CreateLimitOrder(Company, clientOrderId, new OrderValidity.Day(), side, quantity, price));

    [Test]
    public void CreateConfirmed_IsTracked()
    {
        Rest("Order1", Side.Buy, 3, 100);

        Assert.That(_tracker.LiveCount, Is.EqualTo(1));
        Assert.That(_tracker.HasLive, Is.True);

        var order = _tracker["Order1"];
        Assert.That(order.Symbol, Is.EqualTo(Gold.Symbol));
        Assert.That(order.CompanyId, Is.EqualTo(Company));
        Assert.That(order.Side, Is.EqualTo(Side.Buy));
        Assert.That(order.Status, Is.EqualTo(OrderStatus.Working));
        Assert.That(order.Quantity, Is.EqualTo(3));
        Assert.That(order.RemainingQuantity, Is.EqualTo(3));
        Assert.That(order.Price, Is.EqualTo(100));
        Assert.That(order.TriggerPrice, Is.Null);
    }

    [Test]
    public void Update_RenamesTheEntryRatherThanAddingOne()
    {
        Rest("Order1", Side.Buy, 3, 100);

        Feed(_book.UpdateOrder(Company, "Order2", "Order1", price: 90));

        // one logical resting order, however many client order ids it has been through
        Assert.That(_tracker.LiveCount, Is.EqualTo(1));
        Assert.That(_tracker.TryGet("Order1", out _), Is.False);
        Assert.That(_tracker["Order2"].Price, Is.EqualTo(90));
    }

    [Test]
    public void Cancel_DropsIt()
    {
        Rest("Order1", Side.Buy, 3, 100);

        Feed(_book.CancelOrder(Company, "Order2", "Order1"));

        Assert.That(_tracker.LiveCount, Is.EqualTo(0));
        Assert.That(_tracker.HasLive, Is.False);
    }

    [Test]
    public void PartialFill_KeepsWhatIsLeftAndMovesThePosition()
    {
        Rest("Order1", Side.Buy, 5, 100);

        Feed(_book.CreateLimitOrder(Counterparty, "Sell1", new OrderValidity.Day(), Side.Sell, 2, 100));

        Assert.That(_tracker.LiveCount, Is.EqualTo(1));
        Assert.That(_tracker["Order1"].RemainingQuantity, Is.EqualTo(3));
        Assert.That(_tracker.Position(Gold.Symbol), Is.EqualTo(2));
    }

    [Test]
    public void FullFill_DropsItAndMovesThePosition()
    {
        Rest("Order1", Side.Buy, 5, 100);

        Feed(_book.CreateLimitOrder(Counterparty, "Sell1", new OrderValidity.Day(), Side.Sell, 5, 100));

        Assert.That(_tracker.LiveCount, Is.EqualTo(0));
        Assert.That(_tracker.Position(Gold.Symbol), Is.EqualTo(5));
    }

    [Test]
    public void SellingBack_NetsThePositionOffAgain()
    {
        Rest("Order1", Side.Buy, 5, 100);
        Feed(_book.CreateLimitOrder(Counterparty, "Sell1", new OrderValidity.Day(), Side.Sell, 5, 100));

        Rest("Order2", Side.Sell, 5, 100);
        Feed(_book.CreateLimitOrder(Counterparty, "Buy1", new OrderValidity.Day(), Side.Buy, 5, 100));

        Assert.That(_tracker.Position(Gold.Symbol), Is.EqualTo(0));
        Assert.That(_tracker.LiveCount, Is.EqualTo(0));
    }

    [Test]
    public void CounterpartyFlow_IsNeverTracked()
    {
        Rest("Order1", Side.Buy, 5, 100);

        // the other side's create, and the fill it takes, both carry its own company id
        Feed(_book.CreateLimitOrder(Counterparty, "Sell1", new OrderValidity.Day(), Side.Sell, 2, 100));

        Assert.That(_tracker.LiveOrderIds, Is.EqualTo(new[] {"Order1"}));
    }

    [Test]
    public void RejectedCreate_TracksNothing()
    {
        // no such price on a 10-tick instrument
        Feed(_book.CreateLimitOrder(Company, "Order1", new OrderValidity.Day(), Side.Buy, 3, 105));

        Assert.That(_tracker.LiveCount, Is.EqualTo(0));
    }

    [Test]
    public void RejectedUpdate_LeavesTheOrderUnderTheIdItAlreadyHad()
    {
        Rest("Order1", Side.Buy, 3, 100);

        // naming an order that was never created: refused, and the rename never happens
        Feed(_book.UpdateOrder(Company, "Order3", "Order2", price: 90));

        Assert.That(_tracker.LiveOrderIds, Is.EqualTo(new[] {"Order1"}));
        Assert.That(_tracker["Order1"].Price, Is.EqualTo(100));
    }

    [Test]
    public void DayOrder_ExpiringAtTheClose_IsDropped()
    {
        Rest("Order1", Side.Buy, 3, 100);

        _clock.SetCurrentTime(Now.AddHours(5));
        Feed(_book.CloseTrading());

        Assert.That(_tracker.LiveCount, Is.EqualTo(0));
    }

    [Test]
    public void StopOrder_IsLiveButNotWorking()
    {
        // a stop needs a last traded price to be measured against
        Rest("Order1", Side.Buy, 1, 100);
        Feed(_book.CreateLimitOrder(Counterparty, "Sell1", new OrderValidity.Day(), Side.Sell, 1, 100));

        Feed(_book.CreateStopLimitOrder(Company, "Stop1", new OrderValidity.Day(), Side.Buy, 2,
            price: 120, triggerPrice: 110));

        var stop = _tracker["Stop1"];
        Assert.That(stop.Status, Is.EqualTo(OrderStatus.Hidden));
        Assert.That(stop.TriggerPrice, Is.EqualTo(110));
    }

    [Test]
    public void LiveOrders_ComeBackOldestFirst()
    {
        Rest("Order1", Side.Buy, 1, 100);
        Rest("Order2", Side.Buy, 1, 90);
        Rest("Order3", Side.Buy, 1, 80);

        Feed(_book.CancelOrder(Company, "Cancel2", "Order2"));

        // the tail reindexes rather than the list reordering, so age order survives a removal
        Assert.That(_tracker.LiveOrderIds, Is.EqualTo(new[] {"Order1", "Order3"}));
    }

    [Test]
    public void Pick_DrawsOnlyFromLiveOrders()
    {
        Rest("Order1", Side.Buy, 1, 100);
        Rest("Order2", Side.Buy, 1, 90);
        Feed(_book.CancelOrder(Company, "Cancel1", "Order1"));

        var random = new Random(1);
        for (var i = 0; i < 20; i++)
            Assert.That(_tracker.Pick(random).ClientOrderId, Is.EqualTo("Order2"));
    }

    [Test]
    public void Pick_NothingLive_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _tracker.Pick(new Random(1)));
    }
}
