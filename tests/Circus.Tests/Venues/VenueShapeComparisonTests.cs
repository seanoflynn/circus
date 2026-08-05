using Circus.MarketData;
using Circus.Sequencing;
using Circus.Tests.Helpers;
using NUnit.Framework;

namespace Circus.Tests.Venues;

// The claim the other two classes are only half of: a venue shape decides how a market is
// packaged, not what the market is. Two shapes over one session must agree about the book, the
// prints and the state, or one of them is publishing something that did not happen.
//
// This is also where the two by-price and by-order products are held against each other. They are
// not two feeds of two books - EMDI is EOBI added up - and a subscriber to the order-by-order
// interface can arrive at the depth interface's ladder without being sent it. That relationship
// is what makes a venue able to sell them separately, and nothing else here asserts it.
//
// What no shape here models, so it is findable rather than discovered: packet framing and A/B
// line arbitration, instrument definition messages, end-of-event markers, implied and spread
// books, statistics and banding messages, and EMDI's netted throttling. Each of those is a real
// part of the venues being imitated and none of them is a channel's configuration, which is why
// the shapes stop where they do.
public class VenueShapeComparisonTests
{
    private const string CmeChannel = "310";
    private const string EurexByOrder = "EOBI-GC";
    private const string EurexByPrice = "EMDI-GC";

    // One channel carrying every product about every instrument.
    private static InstrumentGroup CmeShaped(TimeSpan? snapshotInterval = null)
    {
        var group = new InstrumentGroup(VenueSession.Day, snapshotInterval);

        group.AddChannel(CmeChannel, FeedProducts.All, depth: VenueSession.Depth);
        group.Add(VenueSession.Gold, VenueSession.Schedule);
        group.Add(VenueSession.Silver, VenueSession.Schedule);

        return group;
    }

    // The same book split across two interfaces carrying different products.
    private static InstrumentGroup EurexShaped(TimeSpan? snapshotInterval = null)
    {
        var group = new InstrumentGroup(VenueSession.Day, snapshotInterval);

        group.AddChannel(EurexByOrder, FeedProducts.ByOrder | FeedProducts.Status);
        group.AddChannel(EurexByPrice,
            FeedProducts.ByPrice | FeedProducts.Trades | FeedProducts.Status | FeedProducts.Indicative,
            depth: VenueSession.Depth);
        group.Add(VenueSession.Gold, VenueSession.Schedule, new[] {EurexByOrder, EurexByPrice});
        group.Add(VenueSession.Silver, VenueSession.Schedule, new[] {EurexByOrder, EurexByPrice});

        return group;
    }

    private static LevelBook LadderFrom(IEnumerable<ChannelMessage> messages, string symbol)
    {
        var book = new LevelBook();

        foreach (var message in messages)
        {
            if (message.Data.Symbol == symbol && message.Data is MarketByPriceDeltaEvent delta)
                book.Apply(delta);
        }

        return book;
    }

    private static OrderBookMirror MirrorFrom(IEnumerable<ChannelMessage> messages, string symbol)
    {
        var mirror = new OrderBookMirror();

        foreach (var message in messages)
        {
            if (message.Data.Symbol == symbol && message.Data is MarketByOrderDeltaEvent delta)
                mirror.Apply(delta);
        }

        return mirror;
    }

    // The id included, so two shapes agreeing about the prints agree about which trades they were
    // and not merely about the tape's shape.
    private static (string TradeId, decimal Price, int Quantity)[] Prints(
        IEnumerable<ChannelMessage> messages, string symbol) =>
        messages.Select(m => m.Data).OfType<TradeDataEvent>()
            .Where(t => t.Symbol == symbol)
            .Select(t => (t.TradeId, t.Price, t.Quantity))
            .ToArray();

    // The headline. One session, two packagings, one market.
    [Test]
    public void TwoShapes_AgreeAboutTheLadder()
    {
        var cme = VenueSession.Run(CmeShaped())[CmeChannel];
        var eurex = VenueSession.Run(EurexShaped())[EurexByPrice];

        var fromCme = LadderFrom(cme, VenueSession.Gold.Symbol);
        var fromEurex = LadderFrom(eurex, VenueSession.Gold.Symbol);

        Assert.IsNotEmpty(fromCme.Bids, "the session should have left a book behind");
        Assert.AreEqual(fromCme.Bids, fromEurex.Bids);
        Assert.AreEqual(fromCme.Offers, fromEurex.Offers);
    }

    [Test]
    public void TwoShapes_AgreeAboutThePrints()
    {
        var cme = VenueSession.Run(CmeShaped())[CmeChannel];
        var eurex = VenueSession.Run(EurexShaped())[EurexByPrice];

        var fromCme = Prints(cme, VenueSession.Gold.Symbol);

        Assert.IsNotEmpty(fromCme);
        Assert.AreEqual(fromCme, Prints(eurex, VenueSession.Gold.Symbol));
    }

    // The relationship between the two products, which is the reason a venue can sell them
    // separately: the depth interface is the order-by-order interface added up. A subscriber to
    // EOBI alone arrives at EMDI's ladder by aggregating what it was already sent.
    [Test]
    public void TheDepthLadder_IsTheOrderByOrderBookAddedUp()
    {
        var published = VenueSession.Run(EurexShaped());

        var ladder = LadderFrom(published[EurexByPrice], VenueSession.Gold.Symbol);
        var mirror = MirrorFrom(published[EurexByOrder], VenueSession.Gold.Symbol);

        Assert.IsNotEmpty(ladder.Bids);
        Assert.AreEqual(ladder.Bids, mirror.Levels(Side.Buy, VenueSession.Depth));
        Assert.AreEqual(ladder.Offers, mirror.Levels(Side.Sell, VenueSession.Depth));
    }

    // The other join between the two products, and the one a shape can most easily break: every
    // print on the depth interface names a trade the order-by-order interface reported the two
    // sides of. A participant reading both is exactly the case this is for - it sees the market's
    // print and its own two order events and can say they are the same trade.
    //
    // Over the whole session rather than one trade, because the sweep is where matching on time
    // and price stops working: one action prints at three prices, and an aggressor taking two
    // resting orders at one price would print twice with nothing but the id to tell them apart.
    //
    // Keyed on the instrument as well as the id, because a trade id counts within a book rather
    // than across the venue - Gold's first trade and Silver's are both "1". That is how CME and
    // Eurex scope theirs too, and a channel carrying a whole product group has to join on both.
    [Test]
    public void EveryPrint_NamesATradeTheOrderByOrderInterfaceReportedBothSidesOf()
    {
        var published = VenueSession.Run(EurexShaped());

        var sidesOfTrade = published[EurexByOrder].Select(m => m.Data).OfType<MarketByOrderDeltaEvent>()
            .SelectMany(message => message.Changes
                .Where(change => change.TradeId != null)
                .Select(change => (Trade: (message.Symbol, change.TradeId!), Change: change)))
            .GroupBy(entry => entry.Trade)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Change).ToList());

        var prints = published[EurexByPrice].Select(m => m.Data).OfType<TradeDataEvent>().ToList();

        Assert.IsNotEmpty(prints);
        Assert.AreEqual(prints.Count, prints.Select(p => (p.Symbol, p.TradeId)).Distinct().Count(),
            "one print per trade, so one id per print");

        foreach (var print in prints)
        {
            Assert.IsTrue(sidesOfTrade.TryGetValue((print.Symbol, print.TradeId), out var sides),
                $"{print.Symbol} trade {print.TradeId} printed with no order events naming it");
            Assert.AreEqual(new[] {Side.Buy, Side.Sell}, sides!.Select(s => s.Side).OrderBy(s => s).ToArray());
            Assert.IsTrue(sides.All(s => s.Action == MarketByOrderDeltaAction.Filled));
            Assert.IsTrue(sides.All(s => s.Price == print.Price && s.Quantity == print.Quantity),
                "the two sides of a trade are the trade, at its price and its size");
        }

        Assert.AreEqual(prints.Select(p => (p.Symbol, p.TradeId)).OrderBy(t => t).ToArray(),
            sidesOfTrade.Keys.OrderBy(t => t).ToArray(),
            "and nothing filled that no print named");
    }

    // And the same thing against the images rather than the updates, which is the path a
    // subscriber joining mid-session takes. A shape whose two interfaces agree on the deltas but
    // not on what they restate would strand every joiner on one of them.
    [Test]
    public void TheTwoImages_DescribeTheSameBook()
    {
        var published = VenueSession.Run(EurexShaped(TimeSpan.FromMinutes(10)));

        var ladder = new LevelBook();
        ladder.Reset(LastImage<LevelsDataEvent>(published[EurexByPrice]));

        var mirror = new OrderBookMirror();
        mirror.Reset(LastImage<OrdersDataEvent>(published[EurexByOrder]));

        Assert.IsNotEmpty(ladder.Bids);
        Assert.AreEqual(ladder.Bids, mirror.Levels(Side.Buy, VenueSession.Depth));
        Assert.AreEqual(ladder.Offers, mirror.Levels(Side.Sell, VenueSession.Depth));
    }

    // A subscriber that joins late and takes the image lands where one that heard everything is.
    // That is what the snapshot stream is for, and it has to hold whatever shape the venue wears.
    [Test]
    public void AJoinerTakingTheImage_LandsWhereTheStreamLeftIt()
    {
        var published = VenueSession.Run(EurexShaped(TimeSpan.FromMinutes(10)));

        var fromStream = LadderFrom(published[EurexByPrice], VenueSession.Gold.Symbol);

        var joiner = new LevelBook();
        joiner.Reset(LastImage<LevelsDataEvent>(published[EurexByPrice]));

        Assert.AreEqual(fromStream.Bids, joiner.Bids);
        Assert.AreEqual(fromStream.Offers, joiner.Offers);
    }

    private static T LastImage<T>(IEnumerable<ChannelMessage> messages) where T : MarketDataEvent =>
        messages.Where(m => m.Stream == ChannelStream.Snapshot && m.Data.Symbol == VenueSession.Gold.Symbol)
            .Select(m => m.Data)
            .OfType<T>()
            .Last();
}
