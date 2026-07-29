using Circus.OrderBook;
using Circus.SessionProviders;
using NUnit.Framework;

namespace Circus.Tests;

[TestFixture]
public class SessionProviderTests
{
    [Test]
    public void Constructor_Valid_Success()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);

        // assert
        new SessionProvider(preOpen, open, close);
    }

    [Test]
    public void Constructor_OpenBeforePreOpen_ArgumentException()
    {
        // arrange
        var preOpen = new TimeSpan(1, 20, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);

        // assert
        Assert.Catch<ArgumentException>(
            () => new SessionProvider(preOpen, open, close)
        );
    }

    [Test]
    public void Constructor_CloseBeforeOpen_ArgumentException()
    {
        // arrange
        var preOpen = new TimeSpan(1, 00, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(1, 5, 0);

        // assert
        Assert.Catch<ArgumentException>(
            () => new SessionProvider(preOpen, open, close)
        );
    }

    [Test]
    public void Update_BeforePreOpen_Closed()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now = new DateTime(2000, 1, 1, 0, 0, 0);

        // act
        sessionProvider.Update(now);

        // assert
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.AreEqual(now, statuses[0].Time);
    }

    [Test]
    public void Update_AfterClosed_PreOpen()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);
        sessionProvider.Update(new DateTime(2000, 1, 1, 0, 0, 0));

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now = new DateTime(2000, 1, 1, 1, 0, 0);

        // act
        sessionProvider.Update(now);

        // assert
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(OrderBookStatus.PreOpen, statuses[0].Status);
        Assert.AreEqual(now, statuses[0].Time);
    }

    [Test]
    public void Update_AfterPreOpen_Open()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);
        sessionProvider.Update(new DateTime(2000, 1, 1, 0, 0, 0));
        sessionProvider.Update(new DateTime(2000, 1, 1, 1, 0, 0));

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now = new DateTime(2000, 1, 1, 1, 10, 0);

        // act
        sessionProvider.Update(now);

        // assert
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Open, statuses[0].Status);
        Assert.AreEqual(now, statuses[0].Time);
    }

    [Test]
    public void Update_AfterOpen_Closed()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);
        sessionProvider.Update(new DateTime(2000, 1, 1, 0, 0, 0));
        sessionProvider.Update(new DateTime(2000, 1, 1, 1, 0, 0));
        sessionProvider.Update(new DateTime(2000, 1, 1, 1, 10, 0));

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now = new DateTime(2000, 1, 1, 22, 10, 0);

        // act
        sessionProvider.Update(now);

        // assert
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.AreEqual(now, statuses[0].Time);
    }

    [Test]
    public void Update_SkipToAfterPreOpen_ClosedPreOpen()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now = new DateTime(2000, 1, 1, 1, 0, 0);

        // act
        sessionProvider.Update(now);

        // assert
        Assert.AreEqual(2, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.AreEqual(now, statuses[0].Time);
        Assert.AreEqual(OrderBookStatus.PreOpen, statuses[1].Status);
        Assert.AreEqual(now, statuses[0].Time);
    }

    [Test]
    public void Update_SkipToAfterOpen_ClosedPreOpenOpen()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now = new DateTime(2000, 1, 1, 1, 10, 0);

        // act
        sessionProvider.Update(now);

        // assert
        Assert.AreEqual(3, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.AreEqual(now, statuses[0].Time);
        Assert.AreEqual(OrderBookStatus.PreOpen, statuses[1].Status);
        Assert.AreEqual(now.Date.Add(preOpen), statuses[1].Time);
        Assert.AreEqual(OrderBookStatus.Open, statuses[2].Status);
        Assert.AreEqual(now.Date.Add(open), statuses[2].Time);
    }

    [Test]
    public void Update_Closed_SkipToAfterClosed_Closed()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now = new DateTime(2000, 1, 1, 22, 10, 0);

        // act
        sessionProvider.Update(now);

        // assert
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
    }

    [Test]
    public void Update_PreOpen_SkipToAfterClosed_OpenClosed()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);
        var now1 = new DateTime(2000, 1, 1, 1, 0, 0);
        sessionProvider.Update(now1);

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now2 = new DateTime(2000, 1, 1, 22, 11, 0);

        // act
        sessionProvider.Update(now2);

        // assert
        Assert.AreEqual(2, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Open, statuses[0].Status);
        Assert.AreEqual(now1.Date.Add(open), statuses[0].Time);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[1].Status);
        Assert.AreEqual(now2.Date.Add(close), statuses[1].Time);
    }

    [Test]
    public void Update_OpenNextDay_ClosedOpen()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);
        var now1 = new DateTime(2000, 1, 1, 1, 10, 0);
        sessionProvider.Update(now1);

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now2 = new DateTime(2000, 1, 2, 1, 10, 0);

        // act
        sessionProvider.Update(now2);

        // assert
        Assert.AreEqual(3, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.AreEqual(now1.Date.Add(close), statuses[0].Time);
        Assert.AreEqual(OrderBookStatus.PreOpen, statuses[1].Status);
        Assert.AreEqual(now2.Date.Add(preOpen), statuses[1].Time);
        Assert.AreEqual(OrderBookStatus.Open, statuses[2].Status);
        Assert.AreEqual(now2.Date.Add(open), statuses[2].Time);
    }

    // A day with a morning and an afternoon session, closing for a break in between.
    private static readonly TradingSession Morning =
        new(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), new TimeSpan(11, 0, 0));

    private static readonly TradingSession Afternoon =
        new(new TimeSpan(13, 0, 0), new TimeSpan(13, 30, 0), new TimeSpan(16, 0, 0));

    private static SessionProvider TwoSessionProvider() =>
        new(new[] {Morning, Afternoon});

    [Test]
    public void Constructor_NoSessions_ArgumentException()
    {
        // assert
        Assert.Catch<ArgumentException>(
            () => new SessionProvider(Array.Empty<TradingSession>())
        );
    }

    [Test]
    public void Constructor_OverlappingSessions_ArgumentException()
    {
        // arrange - the afternoon pre-opens before the morning has closed
        var overlapping = new TradingSession(new TimeSpan(10, 0, 0), new TimeSpan(13, 30, 0),
            new TimeSpan(16, 0, 0));

        // assert
        Assert.Catch<ArgumentException>(
            () => new SessionProvider(new[] {Morning, overlapping})
        );
    }

    [Test]
    public void Constructor_UnorderedSessions_ArgumentException()
    {
        // assert
        Assert.Catch<ArgumentException>(
            () => new SessionProvider(new[] {Afternoon, Morning})
        );
    }

    [Test]
    public void Constructor_TouchingSessions_Success()
    {
        // arrange - a session may begin the moment the previous one closes
        var touching = new TradingSession(new TimeSpan(11, 0, 0), new TimeSpan(11, 30, 0),
            new TimeSpan(16, 0, 0));

        // assert
        new SessionProvider(new[] {Morning, touching});
    }

    [Test]
    public void Update_TwoSessions_FullDayCycle()
    {
        // arrange
        var sessionProvider = TwoSessionProvider();

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var day = new DateTime(2000, 1, 1);

        // act - walk the whole day past the last close
        sessionProvider.Update(day.Add(new TimeSpan(7, 0, 0)));
        sessionProvider.Update(day.Add(new TimeSpan(8, 30, 0)));
        sessionProvider.Update(day.Add(new TimeSpan(11, 0, 0)));
        sessionProvider.Update(day.Add(new TimeSpan(13, 30, 0)));
        sessionProvider.Update(day.Add(new TimeSpan(16, 0, 0)));

        // assert
        Assert.AreEqual(7, statuses.Count);

        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);

        Assert.AreEqual(OrderBookStatus.PreOpen, statuses[1].Status);
        Assert.AreEqual(day.Add(Morning.PreOpen), statuses[1].Time);
        Assert.AreEqual(OrderBookStatus.Open, statuses[2].Status);
        Assert.AreEqual(day.Add(Morning.Open), statuses[2].Time);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[3].Status);
        Assert.AreEqual(day.Add(Morning.Close), statuses[3].Time);

        Assert.AreEqual(OrderBookStatus.PreOpen, statuses[4].Status);
        Assert.AreEqual(day.Add(Afternoon.PreOpen), statuses[4].Time);
        Assert.AreEqual(OrderBookStatus.Open, statuses[5].Status);
        Assert.AreEqual(day.Add(Afternoon.Open), statuses[5].Time);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[6].Status);
        Assert.AreEqual(day.Add(Afternoon.Close), statuses[6].Time);
    }

    [Test]
    public void Update_IntradayClose_DoesNotEndTradingDay()
    {
        // arrange
        var sessionProvider = TwoSessionProvider();
        var day = new DateTime(2000, 1, 1);
        sessionProvider.Update(day.Add(new TimeSpan(8, 30, 0)));

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        // act
        sessionProvider.Update(day.Add(new TimeSpan(11, 0, 0)));

        // assert - the afternoon is still to come, so day orders must survive this close
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.IsFalse(statuses[0].EndsTradingDay);
    }

    [Test]
    public void Update_FinalClose_EndsTradingDay()
    {
        // arrange
        var sessionProvider = TwoSessionProvider();
        var day = new DateTime(2000, 1, 1);
        sessionProvider.Update(day.Add(new TimeSpan(13, 30, 0)));

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        // act
        sessionProvider.Update(day.Add(new TimeSpan(16, 0, 0)));

        // assert
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.IsTrue(statuses[0].EndsTradingDay);
    }

    [Test]
    public void Update_SingleSession_CloseEndsTradingDay()
    {
        // arrange - a lone session is also the day's last
        var sessionProvider = new SessionProvider(new TimeSpan(1, 0, 0), new TimeSpan(1, 10, 0),
            new TimeSpan(22, 10, 0));
        sessionProvider.Update(new DateTime(2000, 1, 1, 1, 10, 0));

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        // act
        sessionProvider.Update(new DateTime(2000, 1, 1, 22, 10, 0));

        // assert
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.IsTrue(statuses[0].EndsTradingDay);
    }

    [Test]
    public void Update_BetweenSessions_StaysClosed()
    {
        // arrange
        var sessionProvider = TwoSessionProvider();
        var day = new DateTime(2000, 1, 1);
        sessionProvider.Update(day.Add(new TimeSpan(11, 0, 0)));

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        // act - the middle of the break
        sessionProvider.Update(day.Add(new TimeSpan(12, 0, 0)));

        // assert - nothing happens until the afternoon pre-opens
        Assert.AreEqual(0, statuses.Count);
    }

    [Test]
    public void Update_CatchUpIntoSecondSession_SkipsStraightToOpen()
    {
        // arrange - the first Update lands mid-afternoon, having missed the whole morning
        var sessionProvider = TwoSessionProvider();

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now = new DateTime(2000, 1, 1, 14, 0, 0);

        // act
        sessionProvider.Update(now);

        // assert - the session in progress wins; the morning is not replayed
        Assert.AreEqual(3, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.AreEqual(OrderBookStatus.PreOpen, statuses[1].Status);
        Assert.AreEqual(now.Date.Add(Afternoon.PreOpen), statuses[1].Time);
        Assert.AreEqual(OrderBookStatus.Open, statuses[2].Status);
        Assert.AreEqual(now.Date.Add(Afternoon.Open), statuses[2].Time);
    }

    [Test]
    public void Update_TwoSessions_NextDay_WrapsToFirstSession()
    {
        // arrange - closed after the afternoon of day 1
        var sessionProvider = TwoSessionProvider();
        var day1 = new DateTime(2000, 1, 1);
        sessionProvider.Update(day1.Add(new TimeSpan(16, 0, 0)));

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var day2 = new DateTime(2000, 1, 2);

        // act
        sessionProvider.Update(day2.Add(new TimeSpan(8, 30, 0)));

        // assert - day 2 begins with the morning session again
        Assert.AreEqual(2, statuses.Count);
        Assert.AreEqual(OrderBookStatus.PreOpen, statuses[0].Status);
        Assert.AreEqual(day2.Add(Morning.PreOpen), statuses[0].Time);
        Assert.AreEqual(OrderBookStatus.Open, statuses[1].Status);
        Assert.AreEqual(day2.Add(Morning.Open), statuses[1].Time);
    }

    [Test]
    public void Update_OpenTwoDaysLater_SkipsEmptyDay()
    {
        // arrange
        var preOpen = new TimeSpan(1, 0, 0);
        var open = new TimeSpan(1, 10, 0);
        var close = new TimeSpan(22, 10, 0);
        var sessionProvider = new SessionProvider(preOpen, open, close);
        var now1 = new DateTime(2000, 1, 1, 1, 10, 0);
        sessionProvider.Update(now1);

        var statuses = new List<SessionStatusChangedArgs>();
        sessionProvider.Changed += (_, status) => statuses.Add(status);

        var now2 = new DateTime(2000, 1, 3, 1, 10, 0);

        // act
        sessionProvider.Update(now2);

        // assert
        Assert.AreEqual(3, statuses.Count);
        Assert.AreEqual(OrderBookStatus.Closed, statuses[0].Status);
        Assert.AreEqual(now1.Date.Add(close), statuses[0].Time);
        Assert.AreEqual(OrderBookStatus.PreOpen, statuses[1].Status);
        Assert.AreEqual(now2.Date.Add(preOpen), statuses[1].Time);
        Assert.AreEqual(OrderBookStatus.Open, statuses[2].Status);
        Assert.AreEqual(now2.Date.Add(open), statuses[2].Time);
    }
}
