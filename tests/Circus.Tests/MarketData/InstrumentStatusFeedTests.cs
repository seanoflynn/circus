using Circus.Actions;
using Circus.Events;
using Circus.MarketData;
using Circus.Time;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.MarketData;

// What a channel carrying only FeedProducts.Status publishes, driven through a real feed rather
// than a producer of its own: assembling the instrument's state is a projection now, and the feed
// is where it happens.
public class InstrumentStatusFeedTests
{
    private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
    private static readonly TimeSpan PauseFor = TimeSpan.FromMinutes(2);

    private ManualClock Clock;
    private InstrumentFeed Feed;

    [SetUp]
    public void SetUp()
    {
        Clock = new ManualClock(Now1);
        Feed = ProductFeed.Carrying(FeedProducts.Status);
    }

    // A status-only feed publishes nothing else, so everything it returns is one of these.
    private IList<InstrumentStatusDataEvent> Publish(IOrderBook book, IReadOnlyList<OrderBookEvent> bookEvents) =>
        Feed.Publish<InstrumentStatusDataEvent>(bookEvents);

    private IOrderBook PlainBook() =>
        new TimestampingOrderBook(new Instrument("GCZ6", 10, 10), Clock);

    // A 5-tick volatility range on a reference of 100, pausing for two minutes.
    private IOrderBook PausingBook() =>
        new TimestampingOrderBook(
            new Instrument("GCZ6", 10, 10,
                PriceRestrictions: new PriceRestriction[] {new VolatilityBand(5, PauseFor)}),
            Clock);

    [Test]
    public void OrdinaryOpen_PublishesTheStatusAndNothingPending()
    {
        // arrange
        var book = PlainBook();

        // act
        var events = Publish(book, book.UpdateStatus(OrderBookStatus.Open));

        // assert
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(Now1, events[0].Time);
        Assert.AreEqual(OrderBookStatus.Open, events[0].Status);
        Assert.AreEqual(OrderBookStatusChangeReason.Requested, events[0].Reason);
        Assert.IsNull(events[0].ResumesAt);
        Assert.IsNull(events[0].LimitState);
    }

    [Test]
    public void OrderFlowChangingNeitherStatusNorLimit_PublishesNothing()
    {
        // arrange
        var book = PlainBook();
        Publish(book, book.UpdateStatus(OrderBookStatus.Open));

        // act
        var events = Publish(book,
            book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Buy, 5, 100));

        // assert
        Assert.AreEqual(0, events.Count);
    }

    [Test]
    public void VolatilityPause_PublishesTheReasonAndWhenItIsDueBack()
    {
        // arrange - referenced at 100, so a trade at 200 breaches the range
        var book = PausingBook();
        Publish(book, book.UpdateStatus(OrderBookStatus.Open, 100));
        Publish(book, book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Sell, 5, 200));

        // act
        var events = Publish(book,
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Buy, 5, 200));

        // assert - the moment it is due back is the thing a halt notification is for, and it
        // could not be published at all before this product existed
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(OrderBookStatus.Paused, events[0].Status);
        Assert.AreEqual(OrderBookStatusChangeReason.PriceRestriction, events[0].Reason);
        Assert.AreEqual(Now1 + PauseFor, events[0].ResumesAt);
    }

    [Test]
    public void PauseElapsing_PublishesTheResumptionWithNothingPending()
    {
        // arrange - paused, due back in two minutes
        var book = PausingBook();
        Publish(book, book.UpdateStatus(OrderBookStatus.Open, 100));
        book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Sell, 5, 200);
        Publish(book, book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Buy, 5, 200));

        // act
        Clock.SetCurrentTime(Now1 + PauseFor);
        var events = Publish(book, book.AdvanceTime());

        // assert
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(OrderBookStatus.Open, events[0].Status);
        Assert.AreEqual(OrderBookStatusChangeReason.InterruptionElapsed, events[0].Reason);
        Assert.IsNull(events[0].ResumesAt, "back to trading, so nothing is pending");
    }

    [Test]
    public void OpenEndedInterruption_PublishesNoResumeTime()
    {
        // arrange - a range with no duration configured stands until something ends it
        var book = new TimestampingOrderBook(
            new Instrument("GCZ6", 10, 10,
                PriceRestrictions: new PriceRestriction[] {new VolatilityBand(5)}),
            Clock);
        Publish(book, book.UpdateStatus(OrderBookStatus.Open, 100));
        book.CreateLimitOrder("Company1", "Order1", new OrderValidity.Day(), Side.Sell, 5, 200);

        // act
        var events = Publish(book,
            book.CreateLimitOrder("Company2", "Order2", new OrderValidity.Day(), Side.Buy, 5, 200));

        // assert
        Assert.AreEqual(OrderBookStatus.Paused, events[0].Status);
        Assert.IsNull(events[0].ResumesAt);
    }

    [Test]
    public void LimitLock_PublishesTheSideWhileTheStatusStaysOpen()
    {
        // arrange - a book holding a buy above the ceiling, reached by resting it while the
        // limit was wider and then moving the reference so the limit narrows under it. The
        // clock moves on so that buy is genuinely the older order when the sell arrives.
        var book = new TimestampingOrderBook(
            new Instrument("GCZ6", 10, 10,
                PriceRestrictions: new PriceRestriction[]
                {
                    new DailyPriceLimit(new PriceLimitWidth.Ticks(5))
                }),
            Clock);

        Publish(book, book.UpdateStatus(OrderBookStatus.Open, 200));
        book.CreateLimitOrder("Company1", "Sell1", new OrderValidity.Day(), Side.Sell, 5, 200);
        book.CreateLimitOrder("Company2", "Buy1", new OrderValidity.Day(), Side.Buy, 5, 200);
        book.CreateLimitOrder("Company2", "Buy2", new OrderValidity.Day(), Side.Buy, 5, 240);

        Clock.SetCurrentTime(Now1.AddMinutes(1));
        Publish(book, book.UpdateStatus(OrderBookStatus.Open, 100));

        // act - a sell inside the limit, crossing into the buy that is not
        var events = Publish(book,
            book.CreateLimitOrder("Company1", "Sell2", new OrderValidity.Day(), Side.Sell, 5, 150));

        // assert - stuck, and still open. That a limit is not a status is the whole reason it
        // needs assembling with one.
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(Side.Buy, events[0].LimitState);
        Assert.AreEqual(OrderBookStatus.Open, events[0].Status);
        Assert.AreEqual(OrderBookStatusChangeReason.Requested, events[0].Reason,
            "the last status change is still what put it here");

        // act - the order out beyond the ceiling is pulled and the book trades inside the limit
        book.CancelOrder("Company2", "Cancel1", "Buy2");
        var released = Publish(book,
            book.CreateLimitOrder("Company2", "Buy3", new OrderValidity.Day(), Side.Buy, 5, 150));

        // assert
        Assert.AreEqual(1, released.Count);
        Assert.IsNull(released[0].LimitState);
        Assert.AreEqual(OrderBookStatus.Open, released[0].Status);
    }

    [Test]
    public void ALimitChangeAlone_PublishesTheWholeComposite()
    {
        // act - a limit event reaching a feed that has heard nothing before it. Assembling the
        // composite used to need a status event to have arrived first, because the status half was
        // remembered rather than carried; that this answers from a standing start is the whole of
        // what changed, and is what makes a missed message cost an update rather than the truth.
        var events = Feed.Process(
            new OrderBookEvent[]
            {
                new LimitStateChanged(Feed.Symbol, Now1, Side.Sell, 90, OrderBookStatus.Paused,
                    OrderBookStatusChangeReason.Requested, null)
            });

        // assert
        var status = events.Cast<InstrumentStatusDataEvent>().Single();
        Assert.AreEqual(OrderBookStatus.Paused, status.Status);
        Assert.AreEqual(Side.Sell, status.LimitState);
        Assert.AreEqual(Now1, status.Time);
    }
}
