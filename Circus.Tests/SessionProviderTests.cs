using System;
using System.Collections.Generic;
using Circus.OrderBook;
using Circus.SessionProviders;
using NUnit.Framework;

namespace Circus.Tests
{
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
}