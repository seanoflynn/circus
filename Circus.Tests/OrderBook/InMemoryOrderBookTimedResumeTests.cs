using System;
using System.Linq;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    // An interruption that ends on its own, and the reason a consumer reads off the status change.
    [TestFixture]
    public class InMemoryOrderBookTimedResumeTests
    {
        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly TimeSpan PauseFor = TimeSpan.FromMinutes(2);

        private static readonly string CompanyId1 = "Company1";
        private static readonly string CompanyId2 = "Company2";
        private static readonly string CompanyId3 = "Company3";

        private static readonly string OrderId1 = "Order1";
        private static readonly string OrderId2 = "Order2";
        private static readonly string OrderId3 = "Order3";

        private TestTimeProvider TimeProvider;

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
        }

        // A 5-tick volatility band on a reference of 100, so a trade at 200 breaches it.
        private IOrderBook PausingBook(TimeSpan? duration)
        {
            var security = new Security("GCZ6", SecurityType.Future, 10, 10,
                PriceRestrictions: new PriceRestrictionConfig[] {new VolatilityBand(5, duration)});
            var book = new InMemoryOrderBook(security, TimeProvider);
            book.UpdateStatus(OrderBookStatus.Open, 100);
            return book;
        }

        // Drives the book into a volatility pause, leaving the two orders that caused it crossed
        // and unfilled - the trade is prevented, not executed.
        private void Breach(IOrderBook book)
        {
            book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 200);
            book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 200);
        }

        [Test]
        public void PauseWithDuration_ResumesOnceElapsed()
        {
            // arrange
            var book = PausingBook(PauseFor);
            Breach(book);
            Assert.AreEqual(OrderBookStatus.Paused, book.Status);

            // act
            TimeProvider.SetCurrentTime(Now1 + PauseFor);
            var events = book.AdvanceTime();

            // assert
            Assert.AreEqual(OrderBookStatus.Open, book.Status);
            var resumed = events.OfType<StatusChanged>().Single();
            Assert.AreEqual(OrderBookStatus.Open, resumed.Status);
            Assert.AreEqual(StatusChangeReason.InterruptionElapsed, resumed.Reason);
        }

        [Test]
        public void PauseWithDuration_NotYetElapsed_StaysPaused()
        {
            // arrange
            var book = PausingBook(PauseFor);
            Breach(book);

            // act - one second short
            TimeProvider.SetCurrentTime(Now1 + PauseFor - TimeSpan.FromSeconds(1));
            var events = book.AdvanceTime();

            // assert
            Assert.AreEqual(OrderBookStatus.Paused, book.Status);
            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void PauseWithoutDuration_StandsUntilEndedExplicitly()
        {
            // arrange - no duration configured, which is the older open-ended behaviour
            var book = PausingBook(null);
            Breach(book);

            // act - however long passes
            TimeProvider.SetCurrentTime(Now1.AddDays(1));
            var events = book.AdvanceTime();

            // assert
            Assert.AreEqual(OrderBookStatus.Paused, book.Status);
            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void Resuming_PrintsWhatAccumulatedDuringThePause()
        {
            // arrange - the orders that caused the breach are still crossed and unfilled
            var book = PausingBook(PauseFor);
            Breach(book);

            // act
            TimeProvider.SetCurrentTime(Now1 + PauseFor);
            var events = book.AdvanceTime();

            // assert - the pause resolves into one uncrossing print rather than resuming mid-sweep
            var matched = events.OfType<OrdersMatched>().Single();
            Assert.AreEqual(200, matched.Price);
            Assert.AreEqual(5, matched.Quantity);
        }

        [Test]
        public void ResumeAlsoFiresOnOrdinaryOrderFlow_NotOnlyAdvanceTime()
        {
            // arrange - a book being traded should not need poking to notice its pause has run out
            var book = PausingBook(PauseFor);
            Breach(book);

            // act
            TimeProvider.SetCurrentTime(Now1 + PauseFor);
            var events = book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 5, 90);

            // assert - resumed first, so the order arrived into an open book
            Assert.AreEqual(OrderBookStatus.Open, book.Status);
            Assert.AreEqual(StatusChangeReason.InterruptionElapsed,
                events.OfType<StatusChanged>().Single().Reason);
            Assert.AreEqual(1, events.OfType<CreateOrderConfirmed>().Count());
        }

        [Test]
        public void ExplicitTransitionDuringPause_CancelsThePendingResume()
        {
            // arrange - the session closes while a pause is still running
            var book = PausingBook(PauseFor);
            Breach(book);
            book.CloseTrading();

            // act - the deadline the pause had set comes and goes
            TimeProvider.SetCurrentTime(Now1 + PauseFor);
            var events = book.AdvanceTime();

            // assert - a closed book must not spring back open
            Assert.AreEqual(OrderBookStatus.Closed, book.Status);
            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void AdvanceTime_WithNothingPending_DoesNothing()
        {
            // arrange
            var book = PausingBook(PauseFor);

            // act
            TimeProvider.SetCurrentTime(Now1.AddDays(1));
            var events = book.AdvanceTime();

            // assert
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(OrderBookStatus.Open, book.Status);
        }

        [Test]
        public void BreachReportsPriceRestriction_ExplicitTransitionReportsRequested()
        {
            // arrange
            var book = PausingBook(PauseFor);

            // act
            var opened = book.UpdateStatus(OrderBookStatus.Open);
            book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 200);
            var breached = book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 200);

            // assert
            Assert.AreEqual(StatusChangeReason.Requested, opened.OfType<StatusChanged>().Single().Reason);
            Assert.AreEqual(StatusChangeReason.PriceRestriction,
                breached.OfType<StatusChanged>().Single().Reason);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SeverestBreachWins_WhicheverOrderTheRestrictionsAreDeclaredIn(bool haltingFirst)
        {
            // arrange - two trade-scoped restrictions breached by the same price, disagreeing about
            // what it costs. The severer must win either way round, so declaration order cannot be
            // what decides whether a halt is served or shadowed by a pause.
            var pausing = new AlwaysBreaches(RestrictionBreachAction.Pause, PauseFor);
            var halting = new AlwaysBreaches(RestrictionBreachAction.Halt, null);
            var restrictions = haltingFirst
                ? new IPriceRestriction[] {halting, pausing}
                : new IPriceRestriction[] {pausing, halting};

            var security = new Security("GCZ6", SecurityType.Future, 10, 10);
            var book = new InMemoryOrderBook(security, TimeProvider, restrictions);
            book.UpdateStatus(OrderBookStatus.Open);

            // act
            book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 100);
            book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 5, 100);

            // assert
            Assert.AreEqual(OrderBookStatus.Halted, book.Status);

            // and the halting restriction's own (absent) duration is the one that applies, so
            // nothing resumes when the pausing restriction's would have elapsed
            TimeProvider.SetCurrentTime(Now1 + PauseFor);
            book.AdvanceTime();
            Assert.AreEqual(OrderBookStatus.Halted, book.Status);
        }

        private sealed class AlwaysBreaches : IPriceRestriction
        {
            private readonly RestrictionBreachAction _action;
            private readonly TimeSpan? _resumeAfter;

            public AlwaysBreaches(RestrictionBreachAction action, TimeSpan? resumeAfter)
            {
                _action = action;
                _resumeAfter = resumeAfter;
            }

            public RestrictionScope Scope => RestrictionScope.Trade;
            public RestrictionBreachAction OnBreach => _action;
            public OrderRejectedReason EntryRejectionReason => OrderRejectedReason.PriceOutsideBands;
            public TimeSpan? ResumeAfter => _resumeAfter;
            public bool Allows(long priceTicks, DateTime time) => false;
            public bool AllowsStopSpread(long spreadTicks) => true;
            public bool AllowsResumption(long priceTicks, DateTime time) => true;
            public void OnTrade(long priceTicks, DateTime time) { }
            public void OnSessionChange(long? referencePriceTicks) { }
            public void OnIndicativePrice(long? priceTicks) { }
        }
    }
}
