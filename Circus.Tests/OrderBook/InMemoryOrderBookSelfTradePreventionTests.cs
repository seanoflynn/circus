using System;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.OrderBook
{
    [TestFixture]
    public class InMemoryOrderBookSelfTradePreventionTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

        private static readonly DateTime Now1 = new(2000, 1, 1, 12, 0, 0);
        private static readonly DateTime Now2 = new(2000, 1, 1, 12, 1, 0);
        private static readonly DateTime Now3 = new(2000, 1, 1, 12, 2, 0);

        private static readonly string CompanyId1 = "Company1";
        private static readonly string CompanyId2 = "Company2";

        private static readonly string OrderId1 = "Order1";
        private static readonly string OrderId2 = "Order2";
        private static readonly string OrderId3 = "Order3";

        private static readonly string Smp1 = "Smp1";
        private static readonly string Smp2 = "Smp2";

        private static TestTimeProvider TimeProvider;
        private static IOrderBook Book;

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
            Book = new InMemoryOrderBook(Sec, TimeProvider);
        }

        [Test]
        public void CancelResting_CancelsRestingOrder_AggressorContinuesMatching()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100,
                selfMatchPreventionId: Smp1);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 100);
            TimeProvider.SetCurrentTime(Now3);

            // act - same SMP id as the first (older) resting sell, so that one is prevented and
            // the aggressor should fall through to the second (different company) resting sell
            var events = Book.CreateOrder(CompanyId1, OrderId3, OrderValidity.Day, Side.Buy, 5, 100,
                selfMatchPreventionId: Smp1,
                selfMatchPreventionInstruction: SelfMatchPreventionInstruction.CancelResting);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);

            var cancelled = events[1] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.SelfMatchPrevention, cancelled.Reason);
            Assert.AreEqual(OrderId1, cancelled.Order.ClientOrderId);
            Assert.AreEqual(CompanyId1, cancelled.Order.CompanyId);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);

            var matched = events[2] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(100, matched.Price);
            Assert.AreEqual(5, matched.Quantity);
            Assert.AreEqual(CompanyId2, matched.Fills[0].CompanyId);
            Assert.AreEqual(OrderId2, matched.Fills[0].ClientOrderId);
            Assert.AreEqual(true, matched.Fills[0].IsResting);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
            Assert.AreEqual(CompanyId1, matched.Fills[1].CompanyId);
            Assert.AreEqual(OrderId3, matched.Fills[1].ClientOrderId);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);

            Assert.AreEqual(0, Book.GetLevels(Side.Sell, 10).Count);
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void CancelAggressor_CancelsIncomingOrder_RestingOrderUntouched()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100,
                selfMatchPreventionId: Smp1);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(CompanyId1, OrderId2, OrderValidity.Day, Side.Buy, 5, 100,
                selfMatchPreventionId: Smp1,
                selfMatchPreventionInstruction: SelfMatchPreventionInstruction.CancelAggressor);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);

            var cancelled = events[1] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.SelfMatchPrevention, cancelled.Reason);
            Assert.AreEqual(OrderId2, cancelled.Order.ClientOrderId);
            Assert.AreEqual(OrderStatus.Cancelled, cancelled.Order.Status);

            // the resting sell was never touched
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
            var sellLevels = Book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, sellLevels.Count);
            Assert.AreEqual(100, sellLevels[0].Price);
            Assert.AreEqual(5, sellLevels[0].Quantity);
        }

        [Test]
        public void CancelBoth_CancelsBothOrders()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100,
                selfMatchPreventionId: Smp1);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(CompanyId1, OrderId2, OrderValidity.Day, Side.Buy, 5, 100,
                selfMatchPreventionId: Smp1,
                selfMatchPreventionInstruction: SelfMatchPreventionInstruction.CancelBoth);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);

            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);

            var cancelledResting = events[1] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelledResting);
            Assert.AreEqual(OrderCancelledReason.SelfMatchPrevention, cancelledResting.Reason);
            Assert.AreEqual(OrderId1, cancelledResting.Order.ClientOrderId);

            var cancelledAggressor = events[2] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelledAggressor);
            Assert.AreEqual(OrderCancelledReason.SelfMatchPrevention, cancelledAggressor.Reason);
            Assert.AreEqual(OrderId2, cancelledAggressor.Order.ClientOrderId);

            Assert.AreEqual(0, Book.GetLevels(Side.Sell, 10).Count);
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void DifferentSelfMatchPreventionId_SameCompany_MatchesNormally()
        {
            // arrange - proves the check is id-based, not company-based: same CompanyId on both
            // sides, but different SMP ids, so it should NOT be prevented
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100,
                selfMatchPreventionId: Smp1);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(CompanyId1, OrderId2, OrderValidity.Day, Side.Buy, 5, 100,
                selfMatchPreventionId: Smp2);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);
            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(5, matched.Quantity);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        }

        [Test]
        public void NeitherSideSetsId_MatchesNormally()
        {
            // arrange - same company, neither order opts into STP at all - confirms it's opt-in,
            // not automatic for same-company orders
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(CompanyId1, OrderId2, OrderValidity.Day, Side.Buy, 5, 100);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);
            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);

            var matched = events[1] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(5, matched.Quantity);
        }

        [Test]
        public void IdSetOnBothSides_InstructionOmitted_DefaultsToCancelResting()
        {
            // arrange
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100,
                selfMatchPreventionId: Smp1);
            TimeProvider.SetCurrentTime(Now2);

            // act - neither side specifies an instruction
            var events = Book.CreateOrder(CompanyId1, OrderId2, OrderValidity.Day, Side.Buy, 5, 100,
                selfMatchPreventionId: Smp1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(2, events.Count);

            var cancelled = events[1] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.SelfMatchPrevention, cancelled.Reason);
            Assert.AreEqual(OrderId1, cancelled.Order.ClientOrderId);

            // the aggressor survives (CancelResting), still resting on the buy side
            var buyLevels = Book.GetLevels(Side.Buy, 10);
            Assert.AreEqual(1, buyLevels.Count);
            Assert.AreEqual(100, buyLevels[0].Price);
            Assert.AreEqual(5, buyLevels[0].Quantity);
        }

        [Test]
        public void OnlyRestingSetsInstruction_UsedAsFallback()
        {
            // arrange - the aggressor shares the SMP id but doesn't specify an instruction, so
            // the resting order's instruction should govern
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100,
                selfMatchPreventionId: Smp1,
                selfMatchPreventionInstruction: SelfMatchPreventionInstruction.CancelBoth);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(CompanyId1, OrderId2, OrderValidity.Day, Side.Buy, 5, 100,
                selfMatchPreventionId: Smp1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);
            Assert.IsInstanceOf<CancelOrderConfirmed>(events[1]);
            Assert.IsInstanceOf<CancelOrderConfirmed>(events[2]);
            Assert.AreEqual(0, Book.GetLevels(Side.Sell, 10).Count);
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void FillOrKill_OnlyLiquidityIsSelfMatchPrevented_Rejected()
        {
            // arrange - the only resting liquidity shares the incoming order's SMP id, so it
            // must be excluded from the upfront liquidity check rather than partially filled
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 5, 100,
                selfMatchPreventionId: Smp1);
            TimeProvider.SetCurrentTime(Now2);

            // act
            var events = Book.CreateOrder(CompanyId1, OrderId2, OrderValidity.FillOrKill, Side.Buy, 5, 100,
                selfMatchPreventionId: Smp1);

            // assert
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);

            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InsufficientLiquidityForFillOrKill, rejected.Reason);

            // book is untouched - no partial fill leaked through
            var sellLevels = Book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, sellLevels.Count);
            Assert.AreEqual(5, sellLevels[0].Quantity);
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void FillOrKill_CancelResting_SkipsSelfMatchedOrder_CountsLiquidityBeyondIt()
        {
            // arrange - a self-matched order at the front of the queue, with real liquidity
            // from a different company right behind it. CancelResting only kills the resting
            // order, so the incoming order should keep going and see the liquidity behind it.
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 2, 100,
                selfMatchPreventionId: Smp1);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Sell, 5, 100);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(CompanyId1, OrderId3, OrderValidity.FillOrKill, Side.Buy, 5, 100,
                selfMatchPreventionId: Smp1,
                selfMatchPreventionInstruction: SelfMatchPreventionInstruction.CancelResting);

            // assert - accepted: the 2@100 self-match is skipped, the 5@100 behind it is enough
            Assert.IsNotNull(events);
            Assert.AreEqual(3, events.Count);
            Assert.IsInstanceOf<CreateOrderConfirmed>(events[0]);

            var cancelled = events[1] as CancelOrderConfirmed;
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(OrderCancelledReason.SelfMatchPrevention, cancelled.Reason);
            Assert.AreEqual(OrderId1, cancelled.Order.ClientOrderId);

            var matched = events[2] as OrdersMatched;
            Assert.IsNotNull(matched);
            Assert.AreEqual(5, matched.Quantity);
            Assert.AreEqual(OrderId2, matched.Fills[0].ClientOrderId);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[0].Order.Status);
            Assert.AreEqual(OrderId3, matched.Fills[1].ClientOrderId);
            Assert.AreEqual(OrderStatus.Filled, matched.Fills[1].Order.Status);
        }

        [Test]
        public void FillOrKill_CancelAggressor_StopsCountingAtSelfMatch_RejectedDespiteLaterLiquidity()
        {
            // arrange - same shape as above, but with CancelAggressor/CancelBoth the incoming
            // order itself would be cancelled the instant it reaches the self-matched order, so
            // it can never actually reach the 10@100 sitting right behind it. The liquidity
            // check must reflect that and reject upfront, not just skip the self-matched
            // quantity and keep counting past it.
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 2, 100,
                selfMatchPreventionId: Smp1);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Sell, 10, 100);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(CompanyId1, OrderId3, OrderValidity.FillOrKill, Side.Buy, 5, 100,
                selfMatchPreventionId: Smp1,
                selfMatchPreventionInstruction: SelfMatchPreventionInstruction.CancelAggressor);

            // assert - rejected: reachable liquidity before the self-match is 0, well short of 5,
            // even though 10 more sits just behind it in the queue
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);

            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InsufficientLiquidityForFillOrKill, rejected.Reason);

            // book is completely untouched - both resting orders survive, nothing was cancelled
            var sellLevels = Book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(1, sellLevels.Count);
            Assert.AreEqual(100, sellLevels[0].Price);
            Assert.AreEqual(12, sellLevels[0].Quantity);
            Assert.AreEqual(2, sellLevels[0].Count);
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }

        [Test]
        public void FillOrKill_CancelAggressor_StopsAtSelfMatch_AcrossPriceLevels_Rejected()
        {
            // arrange - the self-match sits at the best price level; genuine liquidity sits at
            // the next (worse but still-crossing) price level. CancelAggressor kills the
            // incoming order the instant it reaches the self-match, so it never gets to the
            // second price level at all, no matter how much liquidity is sitting there.
            Book.UpdateStatus(OrderBookStatus.Open);
            Book.CreateOrder(CompanyId1, OrderId1, OrderValidity.Day, Side.Sell, 2, 100,
                selfMatchPreventionId: Smp1);
            TimeProvider.SetCurrentTime(Now2);
            Book.CreateOrder(CompanyId2, OrderId2, OrderValidity.Day, Side.Sell, 20, 110);
            TimeProvider.SetCurrentTime(Now3);

            // act
            var events = Book.CreateOrder(CompanyId1, OrderId3, OrderValidity.FillOrKill, Side.Buy, 5, 110,
                selfMatchPreventionId: Smp1,
                selfMatchPreventionInstruction: SelfMatchPreventionInstruction.CancelAggressor);

            // assert - rejected: the self-match at 100 stops the search cold, so the 20 sitting
            // at 110 is never even considered
            Assert.IsNotNull(events);
            Assert.AreEqual(1, events.Count);

            var rejected = events[0] as CreateOrderRejected;
            Assert.IsNotNull(rejected);
            Assert.AreEqual(OrderRejectedReason.InsufficientLiquidityForFillOrKill, rejected.Reason);

            // book is completely untouched - both price levels survive, nothing was cancelled
            var sellLevels = Book.GetLevels(Side.Sell, 10);
            Assert.AreEqual(2, sellLevels.Count);
            Assert.AreEqual(100, sellLevels[0].Price);
            Assert.AreEqual(2, sellLevels[0].Quantity);
            Assert.AreEqual(110, sellLevels[1].Price);
            Assert.AreEqual(20, sellLevels[1].Quantity);
            Assert.AreEqual(0, Book.GetLevels(Side.Buy, 10).Count);
        }
    }
}
