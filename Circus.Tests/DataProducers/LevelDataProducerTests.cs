using System;
using System.Collections.Generic;
using Circus.DataProducers;
using Circus.OrderBook;
using Circus.TimeProviders;
using NUnit.Framework;

namespace Circus.Tests.DataProducers
{
    // LevelDataProducer maintains its own state purely from the OrderConfirmedEvent stream (it
    // no longer queries IOrderBook.GetLevels), so every test must route every book action
    // through the same producer instance, in order - never feed it only the final action's
    // events after driving earlier actions directly through the book.
    public class LevelDataProducerTests
    {
        private static readonly Security Sec = new("GCZ6", SecurityType.Future, 10, 10);

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

        private static TestTimeProvider TimeProvider;
        private static IOrderBook Book;

        [SetUp]
        public void SetUp()
        {
            TimeProvider = new TestTimeProvider(Now1);
            Book = new InMemoryOrderBook(Sec, TimeProvider);
        }

        private static LevelsDataEvent Publish(LevelDataProducer producer, IReadOnlyList<OrderBookEvent> bookEvents)
        {
            var events = producer.Process(Book, bookEvents);
            Assert.AreEqual(1, events.Count);
            return events[0];
        }

        [Test]
        public void SingleOrder_AppearsOnItsSide()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));

            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 3, 100));

            Assert.IsEmpty(level.Bids);
            Assert.AreEqual(1, level.Offers.Count);
            Assert.AreEqual(100, level.Offers[0].Price);
            Assert.AreEqual(3, level.Offers[0].Quantity);
            Assert.AreEqual(1, level.Offers[0].Count);
        }

        [Test]
        public void MultipleOrders_SamePrice_Aggregates()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 100));

            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 100));

            Assert.AreEqual(1, level.Offers.Count);
            Assert.AreEqual(100, level.Offers[0].Price);
            Assert.AreEqual(8, level.Offers[0].Quantity);
            Assert.AreEqual(2, level.Offers[0].Count);
        }

        [Test]
        public void MultipleOffers_DifferentPrice_OrderedBestFirst()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 5, 110));

            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 100));

            Assert.AreEqual(2, level.Offers.Count);
            Assert.AreEqual(100, level.Offers[0].Price);
            Assert.AreEqual(3, level.Offers[0].Quantity);
            Assert.AreEqual(110, level.Offers[1].Price);
            Assert.AreEqual(5, level.Offers[1].Quantity);
        }

        [Test]
        public void MultipleBids_DifferentPrice_OrderedBestFirst()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100));

            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 3, 110));

            Assert.AreEqual(2, level.Bids.Count);
            Assert.AreEqual(110, level.Bids[0].Price);
            Assert.AreEqual(3, level.Bids[0].Quantity);
            Assert.AreEqual(100, level.Bids[1].Price);
            Assert.AreEqual(5, level.Bids[1].Quantity);
        }

        [Test]
        public void OppositeSides_TrackedIndependently()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100));

            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 110));

            Assert.AreEqual(1, level.Bids.Count);
            Assert.AreEqual(100, level.Bids[0].Price);
            Assert.AreEqual(1, level.Offers.Count);
            Assert.AreEqual(110, level.Offers[0].Price);
        }

        [Test]
        public void MoreLevelsThanMax_LimitedToMaxLevels()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 110));
            Publish(producer, Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 4, 120));

            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Buy, 5, 130));

            Assert.AreEqual(2, level.Bids.Count);
            Assert.AreEqual(130, level.Bids[0].Price);
            Assert.AreEqual(120, level.Bids[1].Price);
        }

        [Test]
        public void PartialFill_ReducesQuantity_KeepsLevel()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100));

            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 2, 100));

            Assert.AreEqual(1, level.Bids.Count);
            Assert.AreEqual(100, level.Bids[0].Price);
            Assert.AreEqual(3, level.Bids[0].Quantity);
            Assert.AreEqual(1, level.Bids[0].Count);
            Assert.IsEmpty(level.Offers);
        }

        [Test]
        public void FullFill_RemovesLevel()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 110));
            Publish(producer, Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 4, 120));

            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 5, 100));

            // the 120 bid (best) fully fills against 4 of the sell order's 5; the remaining 1
            // partially fills the 110 bid down to 2
            Assert.AreEqual(1, level.Bids.Count);
            Assert.AreEqual(110, level.Bids[0].Price);
            Assert.AreEqual(2, level.Bids[0].Quantity);
            Assert.IsEmpty(level.Offers);
        }

        [Test]
        public void Cancel_RemovesFromLevel()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100));
            Publish(producer, Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Buy, 3, 100));

            var level = Publish(producer, Book.CancelOrder(CompanyId1, OrderId4, OrderId1));

            Assert.AreEqual(1, level.Bids.Count);
            Assert.AreEqual(100, level.Bids[0].Price);
            Assert.AreEqual(3, level.Bids[0].Quantity);
            Assert.AreEqual(1, level.Bids[0].Count);
        }

        [Test]
        public void Cancel_LastOrderAtLevel_RemovesLevelEntirely()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100));

            var level = Publish(producer, Book.CancelOrder(CompanyId1, OrderId4, OrderId1));

            Assert.IsEmpty(level.Bids);
        }

        [Test]
        public void Reprice_MovesBetweenLevels()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100));

            var level = Publish(producer, Book.UpdateOrder(CompanyId1, OrderId2, OrderId1, price: 110));

            Assert.IsEmpty(level.Offers);
            Assert.AreEqual(1, level.Bids.Count);
            Assert.AreEqual(110, level.Bids[0].Price);
            Assert.AreEqual(5, level.Bids[0].Quantity);
        }

        [Test]
        public void QuantityOnlyUpdate_SameLevel_QuantityChangesInPlace()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 5, 100));

            var level = Publish(producer, Book.UpdateOrder(CompanyId1, OrderId2, OrderId1, newTotalQuantity: 8));

            Assert.AreEqual(1, level.Bids.Count);
            Assert.AreEqual(100, level.Bids[0].Price);
            Assert.AreEqual(8, level.Bids[0].Quantity);
            Assert.AreEqual(1, level.Bids[0].Count);
        }

        [Test]
        public void Iceberg_ShowsOnlyDisplayedPeak_NotHiddenReserve()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));

            // total 20, only 5 displayed at a time
            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 20, 100,
                    maxVisibleQuantity: 5));

            Assert.AreEqual(1, level.Offers.Count);
            Assert.AreEqual(100, level.Offers[0].Price);
            Assert.AreEqual(5, level.Offers[0].Quantity, "only the displayed peak, never the hidden reserve");
            Assert.AreEqual(1, level.Offers[0].Count);
        }

        [Test]
        public void Iceberg_Reprice_MovesOnlyDisplayedPeak()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer,
                Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Sell, 20, 100,
                    maxVisibleQuantity: 5));

            var level = Publish(producer, Book.UpdateOrder(CompanyId1, OrderId2, OrderId1, price: 110));

            Assert.IsEmpty(level.Bids);
            Assert.AreEqual(1, level.Offers.Count);
            Assert.AreEqual(110, level.Offers[0].Price);
            Assert.AreEqual(5, level.Offers[0].Quantity, "still only the displayed peak after moving levels");
        }

        [Test]
        public void StopOrderActivation_ReportedAsArrival_NotDoubleCounted()
        {
            var producer = new LevelDataProducer(2);
            Publish(producer, Book.UpdateStatus(OrderBookStatus.Open));
            Publish(producer, Book.CreateLimitOrder(CompanyId1, OrderId1, new OrderValidity.Day(), Side.Buy, 3, 500));
            Publish(producer, Book.CreateLimitOrder(CompanyId2, OrderId2, new OrderValidity.Day(), Side.Sell, 3, 500));

            TimeProvider.SetCurrentTime(Now3);
            Publish(producer, Book.CreateLimitOrder(CompanyId3, OrderId3, new OrderValidity.Day(), Side.Sell, 5, 520));

            // still-Hidden stop order: must not appear in the working-book levels yet
            TimeProvider.SetCurrentTime(Now4);
            var afterStopCreate = Publish(producer,
                Book.CreateStopLimitOrder(CompanyId4, OrderId4, new OrderValidity.Day(), Side.Buy, 5, 530, 510));
            Assert.IsFalse(afterStopCreate.Bids.Count > 0 && afterStopCreate.Bids[0].Price == 530);

            TimeProvider.SetCurrentTime(Now5);
            Publish(producer, Book.CreateLimitOrder(CompanyId5, OrderId5, new OrderValidity.Day(), Side.Sell, 2, 510));

            // act - trade at the trigger price, converting the stop into a working limit order
            // that immediately matches the resting 520 offer
            TimeProvider.SetCurrentTime(Now6);
            var level = Publish(producer,
                Book.CreateLimitOrder(CompanyId6, OrderId6, new OrderValidity.Day(), Side.Buy, 2, 510));

            // the triggered order (530, qty 5) fully fills against the 520 offer (qty 5) and
            // leaves the book - it should never have appeared as a resting level at all
            Assert.IsFalse(level.Bids.Count > 0 && level.Bids[0].Price == 530);
            Assert.IsEmpty(level.Offers);
        }
    }
}
