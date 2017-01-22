using System;
using System.Threading;
using System.Linq;
using System.Net;
using System.Collections.Generic;
using System.Net.Sockets;

using Circus.Common;
using Circus.Server;
                  
namespace Circus.Cme
{
	public class TradingEngine
	{
		private TcpListener tcpListener;

		private string senderCompId;
		private string senderTraderId;
		private string senderLocationId;

		private Dictionary<string, OrderBook> books = new Dictionary<string, OrderBook>();
		private List<Security> securities = new List<Security>();

		private Dictionary<int, FixTcpClient> ordersToClients = new Dictionary<int, FixTcpClient>();
		private Dictionary<int, OrderInfo> orderInfos = new Dictionary<int, OrderInfo>();
		private Dictionary<string, int> orderIds = new Dictionary<string, int>();

		private MarketDataChannel channels;

		private FixUdpServer snapshotServer;
		private MarketDataChannelConnection snapshotConnection;
		private Timer snapshotTimer;
		private int snapshotUpdateInterval = 50; // ms
		private int snapshotSecurityIterator;
		private DateTime snapshotTime;
		private Dictionary<Security, AggregateBook> aggregateBooks = new Dictionary<Security, AggregateBook>();

		private FixUdpServer incrementalServer;
		private MarketDataChannelConnection incrementalConnection;
		private Dictionary<Security, int> incrementalSeqNums = new Dictionary<Security, int>();

		private TradingSession session;

		private static int nextOrderId;

		public TradingEngine(MarketDataChannel chnnls, TradingSession sess, string companyId, string traderId, string locationId = null)
		{
			senderCompId = companyId;
			senderTraderId = traderId;
			senderLocationId = locationId;

			channels = chnnls;
            if (channels != null)
            {
                snapshotConnection = channels.Connections.Find(x => x.Type == MarketDataChannelConnectionType.Snapshot && x.Feed == "A");
                incrementalConnection = channels.Connections.Find(x => x.Type == MarketDataChannelConnectionType.Incremental && x.Feed == "A");

                snapshotServer = new FixUdpServer(snapshotConnection);
                incrementalServer = new FixUdpServer(incrementalConnection);
            }
            else
            {
                snapshotServer = new FixUdpServer(new MarketDataChannelConnection() { IPAddress = IPAddress.Loopback, Port = 7998 });
                incrementalServer = new FixUdpServer(new MarketDataChannelConnection() { IPAddress = IPAddress.Loopback, Port = 7999 });
            }

			session = sess;
			session.Changed += HandleSessionChanged;			
		}

		public void Dispose()
		{
			if (tcpListener != null)
				tcpListener.Stop();
		}

		public async void Start(IPAddress address, int port)
		{
			tcpListener = new TcpListener(address, port);
			tcpListener.Start();
			Console.WriteLine($"globex server listening at {tcpListener.LocalEndpoint}");

			snapshotTimer = new Timer(HandleSnapshotTimerInterval, null, 0, snapshotUpdateInterval);

			while (true)
			{
				TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();

				FixTcpClient client = new FixTcpClient(senderCompId, senderTraderId, null, tcpClient);
				client.LogonReceived += HandleLogon;
				client.LogoutReceived += HandleLogout;
				client.NewOrderReceived += HandleCreateRequest;
				client.CancelReplaceRequestReceived += HandleUpdateRequest;
				client.CancelRequestReceived += HandleDeleteRequest;

				Console.WriteLine("client connected");
				client.Listen();
			}
		}

		public void Stop()
		{
			// TODO: how to kill an await loop safely?
			tcpListener.Stop();
			snapshotTimer.Dispose();
		}

		public void AddSecurity(Security sec)
		{
			securities.Add(sec);

			var book = new OrderBook(sec);
			book.OrderCreated += HandleOrderCreateAccepted;
			book.OrderUpdated += HandleOrderUpdateAccepted;
			book.OrderDeleted += HandleOrderDeleteAccepted;
			book.OrderCreateRejected += HandleOrderCreateRejected;
			book.OrderUpdateRejected += HandleOrderUpdateRejected;
			book.OrderDeleteRejected += HandleOrderDeleteRejected;
			book.OrderFilled += HandleOrderFilled;
			book.OrderExpired += HandleOrderExpired;

			book.Traded += HandleTraded;
			//book.BookUpdated += HandleBookUpdated;

			books.Add(sec.Contract, book);

			incrementalSeqNums.Add(sec, 0);
			aggregateBooks.Add(sec, new AggregateBook(10));
		}

		private void HandleSessionChanged(object sender, SecurityTradingStatus e)
		{
			foreach (var book in books)
				book.Value.SetStatus(e);
		}

		private void HandleLogon(object sender, Logon logon)
		{
			var client = (FixTcpClient)sender;

			// check deets

			if (client.IsLoggedOn)
			{
				client.Send(new BusinessLevelReject(logon, BusinessLevelRejectReason.Other));
				return;
			}
			client.IsLoggedOn = true;

			client.TargetCompanyId = logon.Header.SenderCompanyId;
			client.TargetTraderId = logon.Header.SenderTraderId;
			client.TargetLocationId = logon.Header.SenderLocationId;

			client.Send(new Logon()
			{
				HeartbeatInterval = logon.HeartbeatInterval,
				ApplicationSystemName = logon.ApplicationSystemName,
				ApplicationSystemVendor = logon.ApplicationSystemVendor,
				TradingSystemVersion = logon.TradingSystemVersion
			});

			// start heartbeat
			client.StartHeartbeat(logon.HeartbeatInterval, true);
		}

		private void HandleLogout(object sender, Logout logout)
		{
			var client = (FixTcpClient)sender;

			if (!client.IsLoggedOn)
			{
				client.Send(new BusinessLevelReject(logout, BusinessLevelRejectReason.Other));
				return;
			}
			client.IsLoggedOn = false;

			client.Send(new Logout());

			client.StopHeartbeat();
		}

		private void HandleCreateRequest(object sender, NewOrder request)
		{
			if (!books.ContainsKey(request.Contract))
			{
				((FixTcpClient)sender).Send(new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownSecurity));
				return;
			}

			int id = nextOrderId++;
			string globexId = id.ToString();
			ordersToClients[id] = (FixTcpClient)sender;
			orderInfos[id] = new OrderInfo(globexId, request.ClientOrderId,
										  request.CorrelationClientOrderId, request.Account,
			                               request.IsManualOrder, request.PreTradeAnonymity);
			orderIds[globexId] = id;

			var book = books[request.Contract];

			int minQty = request.MinQuantity ?? 0;
			int maxVisibleQty = request.MaxVisibleQuantity ?? int.MaxValue;
			SelfMatchPreventionInstruction smpMode = request.SelfMatchPreventionInstruction ?? SelfMatchPreventionInstruction.CancelResting;

			switch (request.OrderType)
			{
				case OrderType.Limit:
					book.CreateLimitOrder(id, request.TimeInForce, request.ExpireDate, 
					                      request.Side, request.Price, request.Quantity,
					                      minQty, maxVisibleQty,
										  request.SelfMatchPreventionId, smpMode);
					break;
				case OrderType.Market:
					book.CreateMarketOrder(id, request.TimeInForce, request.ExpireDate, 
					                       request.Side, request.Quantity,
										   minQty, maxVisibleQty,
										   request.SelfMatchPreventionId, smpMode);
					break;
				case OrderType.MarketLimit:
					book.CreateMarketLimitOrder(id, request.TimeInForce, request.ExpireDate, 
					                            request.Side, request.Quantity,
												minQty, maxVisibleQty,
												request.SelfMatchPreventionId, smpMode);
					break;
				case OrderType.Stop:
					book.CreateStopOrder(id, request.TimeInForce, request.ExpireDate, 
					                     request.Side, request.StopPrice, request.Quantity,
										 minQty, maxVisibleQty,
										 request.SelfMatchPreventionId, smpMode);
					break;
				case OrderType.StopLimit:
					book.CreateStopLimitOrder(id, request.TimeInForce, request.ExpireDate, 
					                          request.Side, request.Price, request.StopPrice, request.Quantity,
											  minQty, maxVisibleQty,
											  request.SelfMatchPreventionId, smpMode);
					break;
			}
		}

		private void HandleUpdateRequest(object sender, CancelReplaceRequest request)
		{
			if (!books.ContainsKey(request.Contract))
			{
				((FixTcpClient)sender).Send(new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownSecurity));
				return;
			}

			var book = books[request.Contract];
			var id = orderIds[request.OrderId];

			if (!book.Contains(id))
			{
				((FixTcpClient)sender).Send(new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownId));
				return;
			}

			int maxVisibleQty = request.MaxVisibleQuantity ?? int.MaxValue;

			switch (request.OrderType)
			{
				case OrderType.Limit:
					book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
					break;
				case OrderType.Market:
					book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
					break;
				case OrderType.MarketLimit:
					book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
					break;
				case OrderType.Stop:
					book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
					break;
				case OrderType.StopLimit:
					book.UpdateLimitOrder(id, request.Price, request.Quantity, maxVisibleQty);
					break;
			}
		}

		private void HandleDeleteRequest(object sender, CancelRequest request)
		{
			if (!books.ContainsKey(request.Contract))
			{
				((FixTcpClient)sender).Send(new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownSecurity));
				return;
			}

			var id = orderIds[request.OrderId];

			if (!books[request.Contract].Contains(id))
			{
				((FixTcpClient)sender).Send(new BusinessLevelReject(request, BusinessLevelRejectReason.UnknownId));
				return;
			}

			books[request.Contract].DeleteOrder(id);
		}

		#region Order Callbacks

		private void HandleOrderCreateAccepted(object sender, OrderCreatedEventArgs args)
		{
			ordersToClients[args.Order.Id].Send(new NewOrderAck(args.Order, orderInfos[args.Order.Id]));
			SendBookUpdates(args.Order.Security);
		}

		private void HandleOrderUpdateAccepted(object sender, OrderUpdateEventArgs args)
		{
			ordersToClients[args.Order.Id].Send(new CancelReplaceAck(args.Order, orderInfos[args.Order.Id]));
			SendBookUpdates(args.Order.Security);
		}

		private void HandleOrderDeleteAccepted(object sender, OrderDeletedEventArgs args)
		{
			ordersToClients[args.Order.Id].Send(new CancelAck(args.Order, orderInfos[args.Order.Id], args.Reason));
			SendBookUpdates(args.Order.Security);
		}

		private void HandleOrderCreateRejected(object sender, OrderCreateRejectedEventArgs args)
		{
			ordersToClients[args.Order.Id].Send(new Reject(args.Order, orderInfos[args.Order.Id], args.Reason));
		}

		private void HandleOrderUpdateRejected(object sender, OrderUpdateRejectedEventArgs args)
		{
			ordersToClients[args.Order.Id].Send(new CancelReject(args.Order, orderInfos[args.Order.Id], CancelRejectResponseTo.OrderCancelReplaceRequest, args.Reason));
		}

		private void HandleOrderDeleteRejected(object sender, OrderDeleteRejectedEventArgs args)
		{
			ordersToClients[args.Order.Id].Send(new CancelReject(args.Order, orderInfos[args.Order.Id], CancelRejectResponseTo.OrderCancelRequest, args.Reason));
		}

		private void HandleOrderExpired(object sender, OrderExpiredEventArgs args)
		{
			ordersToClients[args.Order.Id].Send(new Expire(args.Order, orderInfos[args.Order.Id]));
			SendBookUpdates(args.Order.Security);
		}

		private void HandleOrderFilled(object sender, OrderFilledEventArgs args)
		{
			ordersToClients[args.Order.Id].Send(new Fill(args.Order, orderInfos[args.Order.Id], args.Time, args.Price, args.Quantity,
														 args.IsAggressor, args.Order.Status.HasFlag(Common.OrderStatus.Completed)));
		}

		#endregion

		#region Market Data Callbacks

		private void HandleTraded(object sender, TradedEventArgs e)
		{
			var book = (OrderBook)sender;

			SendTradeUpdate(e.Security, e.Time, e.Fills);
			SendBookUpdate(e);
			SendVolumeUpdate(e, book);
		}

		private void SendBookUpdates(Security security)
		{
			var bookUpdate = new IncrementalUpdate(MatchEventIndicator.LastRealQuote, DateTime.UtcNow);
			foreach (var bookLevelUpdate in GenerateBookUpdateDataBlock(security))
			{
				bookLevelUpdate.RptSeq = incrementalSeqNums[security];
				incrementalSeqNums[security]++;
				bookUpdate.MDEntries.Add(bookLevelUpdate);
			}

			if (bookUpdate.MDEntries.Count < 1)
				return;

			incrementalServer.Send(bookUpdate);
		}

		private void SendBookUpdate(TradedEventArgs e)
		{
			var bookUpdate = new IncrementalUpdate(MatchEventIndicator.LastRealQuote, e.Time);
			foreach (var bookLevelUpdate in GenerateBookUpdateDataBlock(e.Security))
			{
				bookLevelUpdate.RptSeq = incrementalSeqNums[e.Security];
				incrementalSeqNums[e.Security]++;
				bookUpdate.MDEntries.Add(bookLevelUpdate);
			}
			incrementalServer.Send(bookUpdate);
		}

		private void SendVolumeUpdate(TradedEventArgs e, OrderBook book)
		{
			var volumeUpdate = new IncrementalUpdate(MatchEventIndicator.LastVolume, e.Time);
			volumeUpdate.MDEntries.Add(MarketDataUpdateDataBlock.VolumeNew(e.Security,
																			incrementalSeqNums[e.Security],
																			book.SessionVolume));
			incrementalSeqNums[e.Security]++;
			incrementalServer.Send(volumeUpdate);
		}

		private void SendRangeUpdate(TradedEventArgs e, OrderBook book)
		{
			var tradeHighUpdate = new IncrementalUpdate(MatchEventIndicator.None, e.Time);
			tradeHighUpdate.MDEntries.Add(MarketDataUpdateDataBlock.TradeHighLowNew(e.Security,
																			   incrementalSeqNums[e.Security],
																			   true,
			                                                                   book.SessionMaxTradePrice.Value));
			incrementalSeqNums[e.Security]++;
			incrementalServer.Send(tradeHighUpdate);

			var tradeLowUpdate = new IncrementalUpdate(MatchEventIndicator.None, e.Time);
			tradeLowUpdate.MDEntries.Add(MarketDataUpdateDataBlock.TradeHighLowNew(e.Security,
																			  incrementalSeqNums[e.Security],
																			  true,
			                                                                  book.SessionMinTradePrice.Value));
			incrementalSeqNums[e.Security]++;
			incrementalServer.Send(tradeLowUpdate);

			var highBidUpdate = new IncrementalUpdate(MatchEventIndicator.None, e.Time);
			highBidUpdate.MDEntries.Add(MarketDataUpdateDataBlock.TradeHighBidLowAskNew(e.Security,
																			   			incrementalSeqNums[e.Security],
			                                                                            Side.Buy,
			                                                                            book.SessionMaxBidPrice.Value));
			incrementalSeqNums[e.Security]++;
			incrementalServer.Send(highBidUpdate);

			var lowAskUpdate = new IncrementalUpdate(MatchEventIndicator.None, e.Time);
			lowAskUpdate.MDEntries.Add(MarketDataUpdateDataBlock.TradeHighBidLowAskNew(e.Security,
																			  		   incrementalSeqNums[e.Security],
			                                                                           Side.Sell,
			                                                                           book.SessionMinAskPrice.Value));
			incrementalSeqNums[e.Security]++;
			incrementalServer.Send(lowAskUpdate);
		}

		private void SendOpenPriceUpdate(TradedEventArgs e, OrderBook book)
		{
			var tradeHighUpdate = new IncrementalUpdate(MatchEventIndicator.None, e.Time);
			tradeHighUpdate.MDEntries.Add(MarketDataUpdateDataBlock.OpenPriceNew(e.Security,
																			     incrementalSeqNums[e.Security],
																				 book.SessionOpenPrice.Value,
																				 false));
			incrementalSeqNums[e.Security]++;
			incrementalServer.Send(tradeHighUpdate);
		}

		private void SendClearedVolumeUpdate(TradedEventArgs e, OrderBook book)
		{
			var volumeUpdate = new IncrementalUpdate(MatchEventIndicator.None, e.Time);
			volumeUpdate.MDEntries.Add(MarketDataUpdateDataBlock.ClearedVolumeNew(e.Security,
																				 incrementalSeqNums[e.Security],
																				 book.PreviousSessionVolume,
																				 book.PreviousSessionDate));
			incrementalSeqNums[e.Security]++;
			incrementalServer.Send(volumeUpdate);
		}

		private void SendOpenInterestUpdate(TradedEventArgs e, OrderBook book)
		{
			var interestUpdate = new IncrementalUpdate(MatchEventIndicator.None, e.Time);
			interestUpdate.MDEntries.Add(MarketDataUpdateDataBlock.OpenInterestNew(e.Security,
																				 incrementalSeqNums[e.Security],
			                                                                     book.PreviousSessionOpenInterest,
																				 book.PreviousSessionDate));
			incrementalSeqNums[e.Security]++;
			incrementalServer.Send(interestUpdate);
		}

		private void SendSettlementUpdate(TradedEventArgs e, OrderBook book)
		{
			var settlementUpdate = new IncrementalUpdate(MatchEventIndicator.None, e.Time);
			settlementUpdate.MDEntries.Add(MarketDataUpdateDataBlock.SettlePriceNew(e.Security,
																				 	incrementalSeqNums[e.Security],
																					book.PreviousSessionSettlementPrice,
																					SettlementPriceType.Final,
																					book.PreviousSessionDate));
			incrementalSeqNums[e.Security]++;
			incrementalServer.Send(settlementUpdate);
		}

		#endregion

		private void HandleSnapshotTimerInterval(object state)
		{
			return;

			if (snapshotSecurityIterator == 0)
			{
				// take snapshot of all books 
				// i.e. generate all kinds of messages
				// x Book Quotes – Bids and Offers
				// x Implied Book Quotes – Bids and Offers
				// Last Trade
				// Opening Prices
				// Session High and Low Trade Prices
				// Session High Bid and Session Low Offer
				// Fixing Price
				// Settlement Price
				// Cleared Trade Volume
				// Open Interest
				// Electronic Volume
				// Threshold Limits

				//SendRangeUpdate(e, book);
				//SendOpenPriceUpdate(e, book);
				//SendClearedVolumeUpdate(e, book);
				//SendOpenInterestUpdate();
				//SendSettlementUpdate(e, book);

				snapshotTime = DateTime.UtcNow;
			}

			var sec = securities[snapshotSecurityIterator];

			var update = new IncrementalUpdate(MatchEventIndicator.LastMessage, snapshotTime);
			update.MDEntries.Add(MarketDataUpdateDataBlock.VolumeNew(sec, incrementalSeqNums[sec], 0));
			incrementalSeqNums[sec]++;
			snapshotServer.Send(update);

			// move iterator on
			snapshotSecurityIterator++;	
			if (snapshotSecurityIterator >= securities.Count)
				snapshotSecurityIterator = 0;
		}

		private void GenerateSnapshot(Security security)
		{
			
		}

		private IEnumerable<MarketDataUpdateDataBlock> GenerateBookUpdateDataBlock(Security security)
		{
			var old = aggregateBooks[security];
			var fresh = books[security.Contract].GetAggregateBook(10);
			aggregateBooks[security] = fresh;

			var bidChanges = GenerateFromAggregateBooks(security, 10, Side.Buy, old.Bids, fresh.Bids);
			var askChanges = GenerateFromAggregateBooks(security, 10, Side.Sell, old.Asks, fresh.Asks);

			return bidChanges.Concat(askChanges);
		}

		private IEnumerable<MarketDataUpdateDataBlock> GenerateFromAggregateBooks(Security sec, int depth, Side side, 
		                                                                          AggregateBookLevel[] old, 
		                                                                          AggregateBookLevel[] fresh)
		{
			// TODO: add support for Delete From/Delete Thru
			for (int i = 0; i < depth; i++)
			{
				if (old[i] == null && fresh[i] == null)
				{
					continue;
				}
				if (old[i] != null && fresh[i] == null)
				{
					yield return MarketDataUpdateDataBlock.RealBookDelete(sec, side, 0, old[i].Price,
																		  old[i].Quantity, i, old[i].Count);
				}
				else if (old[i] == null && fresh[i] != null)
				{
					yield return MarketDataUpdateDataBlock.RealBookNew(sec, side, 0, fresh[i].Price,
																	   fresh[i].Quantity, i, fresh[i].Count);

				}
				else if (old[i].Price == fresh[i].Price && 
				         old[i].Quantity == fresh[i].Quantity && 
				         old[i].Count == fresh[i].Count)
				{
					continue;
				}
				else
				{
					yield return MarketDataUpdateDataBlock.RealBookUpdate(sec, side, 0, fresh[i].Price,
																		  fresh[i].Quantity, i, fresh[i].Count);
				}
			}
		}

		private MarketDataUpdateDataBlock GenerateImpliedBookUpdateDataBlock()
		{ return null; }

		private void SendTradeUpdate(Security security, DateTime time, List<Server.Fill> fills)
		{
			var tradeUpdate = new IncrementalUpdate(MatchEventIndicator.LastTradeSummary, time);

			// aggregate by price
			var xyz = fills.GroupBy(x => x.Price).Select(group => new { Price = group.Key, Orders = group.ToList() }).ToList();
			var infoByPrice = fills.GroupBy(x => x.Price)
									 .Select(group => new
									 {
										 Price = group.Key,
										 Quantity = group.Sum(z => z.Quantity) / 2,
										 Count = group.Select(z => z.OrderId).Distinct().Count(),
										 AggressiveSide = group.First(x => x.Side == Side.Buy).IsAggressor ? Side.Buy : Side.Sell,
									 }).ToList();

			foreach (var grpByPrice in infoByPrice)
			{
				tradeUpdate.MDEntries.Add(MarketDataUpdateDataBlock.TradeNew(security, incrementalSeqNums[security],
																	 grpByPrice.Price, grpByPrice.Quantity,
																	 grpByPrice.Quantity, grpByPrice.AggressiveSide));
				incrementalSeqNums[security]++;
			}

			// aggregate by orderid
			var qtyById = fills.GroupBy(x => new { Id = x.OrderId, Price = x.Price })
								 .Select(group => new
								 {
									 OrderId = group.Key.Id,
									 Quantity = group.Sum(z => z.Quantity),
								 }).ToList();

			foreach (var orderFillQty in qtyById)
			{
				tradeUpdate.NoOrderIDEntries.Add(new IncrementalUpdateOrderEntry() { OrderId = orderFillQty.OrderId.ToString(), FillQuantity = orderFillQty.Quantity });
			}

			incrementalServer.Send(tradeUpdate);
		}

		private MarketDataUpdateDataBlock GenerateFixingPriceDataBlock()
		{ return null; }

		private MarketDataUpdateDataBlock GenerateOpenInterestDataBlock()
		{ return null; }

		private MarketDataUpdateDataBlock GenerateVolumeDataBlock()
		{ return null; }

		private MarketDataUpdateDataBlock GenerateLimitsDataBlock()
		{ return null; }
	}
}
