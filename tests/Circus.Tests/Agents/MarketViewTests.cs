using Circus.Agents;
using Circus.Events;
using Circus.MarketData;
using Circus.Time;
using NUnit.Framework;

namespace Circus.Tests.Agents;

// The public half of what an agent knows. Fed through a real InstrumentFeed rather than from
// hand-built messages, because the thing worth pinning is that an agent watching the feed a
// subscriber actually receives ends up believing the right things about the market.
[TestFixture]
public class MarketViewTests
{
    private static readonly Instrument Gold = new("GCZ6", 10, 10);

    private static readonly DateTime Now = new(2000, 1, 1, 12, 0, 0);

    private ManualClock _clock;
    private IOrderBook _book;
    private InstrumentFeed _feed;
    private MarketView _view;

    [SetUp]
    public void SetUp()
    {
        _clock = new ManualClock(Now);
        _book = new TimestampingOrderBook(Gold, _clock);
        _feed = new InstrumentFeed(Gold.Symbol, maxLevels: 10);
        _view = new MarketView();
    }

    private void Publish(IReadOnlyList<OrderBookEvent> events)
    {
        foreach (var data in _feed.Process(events))
            _view.Apply(data);
    }

    private InstrumentView View => _view.Of(Gold.Symbol);

    [Test]
    public void HavingHeardNothing_KnowsNothing()
    {
        var view = _view.Of(Gold.Symbol);

        Assert.That(view.Symbol, Is.EqualTo(Gold.Symbol));
        Assert.That(view.Bids, Is.Empty);
        Assert.That(view.Offers, Is.Empty);
        Assert.That(view.BestBid, Is.Null);
        Assert.That(view.BestOffer, Is.Null);
        Assert.That(view.Mid, Is.Null);
        Assert.That(view.LastTradePrice, Is.Null);

        // closed is where a book starts, so it is where a subscriber to one starts too
        Assert.That(view.Status, Is.EqualTo(OrderBookStatus.Closed));
        Assert.That(view.IsOpen, Is.False);
        Assert.That(view.AcceptsOrders, Is.False);
    }

    [Test]
    public void Levels_GiveTheTouch()
    {
        Publish(_book.UpdateStatus(OrderBookStatus.Open));
        Publish(_book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 90));
        Publish(_book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 2, 110));

        Assert.That(View.BestBid, Is.EqualTo(90));
        Assert.That(View.BestOffer, Is.EqualTo(110));
        Assert.That(View.Mid, Is.EqualTo(100));
        Assert.That(View.Bids[0].Quantity, Is.EqualTo(3));
        Assert.That(View.Offers[0].Quantity, Is.EqualTo(2));
        Assert.That(View.Time, Is.EqualTo(Now));
    }

    [Test]
    public void OneSidedBook_HasNoMid()
    {
        Publish(_book.UpdateStatus(OrderBookStatus.Open));
        Publish(_book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 90));

        Assert.That(View.BestBid, Is.EqualTo(90));
        Assert.That(View.BestOffer, Is.Null);
        Assert.That(View.Mid, Is.Null);
    }

    [Test]
    public void Trade_SetsTheLastPrice()
    {
        Publish(_book.UpdateStatus(OrderBookStatus.Open));
        Publish(_book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 5, 100));
        Publish(_book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 2, 100));

        Assert.That(View.LastTradePrice, Is.EqualTo(100));
        Assert.That(View.LastTradeQuantity, Is.EqualTo(2));
    }

    [Test]
    public void Status_FollowsTheVenueThroughTheDay()
    {
        Publish(_book.UpdateStatus(OrderBookStatus.PreOpen));
        Assert.That(View.Status, Is.EqualTo(OrderBookStatus.PreOpen));
        Assert.That(View.IsOpen, Is.False);

        // pre-open takes orders even though it does not trade continuously, which is the
        // distinction an agent has to be able to make
        Assert.That(View.AcceptsOrders, Is.True);

        Publish(_book.UpdateStatus(OrderBookStatus.Open));
        Assert.That(View.IsOpen, Is.True);

        Publish(_book.UpdateStatus(OrderBookStatus.Halted));
        Assert.That(View.Status, Is.EqualTo(OrderBookStatus.Halted));
        Assert.That(View.IsOpen, Is.False);
        Assert.That(View.AcceptsOrders, Is.True);

        _clock.SetCurrentTime(Now.AddHours(5));
        Publish(_book.UpdateStatus(OrderBookStatus.Closed));
        Assert.That(View.Status, Is.EqualTo(OrderBookStatus.Closed));
        Assert.That(View.AcceptsOrders, Is.False);
    }

    [Test]
    public void IndicativePrice_FromAPreOpenAuction()
    {
        Publish(_book.UpdateStatus(OrderBookStatus.PreOpen));
        Publish(_book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 5, 100));

        Assert.That(View.IndicativePrice, Is.Null);

        Publish(_book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Sell, 3, 100));

        Assert.That(View.IndicativePrice, Is.EqualTo(100));
    }

    [Test]
    public void OneInstrumentsMessagesNeverReachAnothersView()
    {
        Publish(_book.UpdateStatus(OrderBookStatus.Open));
        Publish(_book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 3, 90));

        // asked about an instrument nothing has been published for: empty and closed, not the
        // last thing that happened to be applied
        var silver = _view.Of("SIZ6");
        Assert.That(silver.BestBid, Is.Null);
        Assert.That(silver.Status, Is.EqualTo(OrderBookStatus.Closed));
    }
}
