using System;
using System.Collections.Generic;
using System.Net;

using Circus.Common;

namespace Circus.Cme
{
	public class Client
	{
		public event EventHandler<NewOrder> NewOrderReceived;
		public event EventHandler<CancelReplaceRequest> CancelReplaceRequestReceived;
		public event EventHandler<CancelRequest> CancelRequestReceived;
		public event EventHandler<CancelReject> CancelRejectReceived;

		public event EventHandler<NewOrderAck> NewOrderAckReceived;
		public event EventHandler<CancelReplaceAck> CancelReplaceAckReceived;
		public event EventHandler<CancelAck> CancelAckReceived;
		public event EventHandler<Reject> RejectReceived;
		public event EventHandler<Fill> FillReceived;

		private FixTcpClient client;

		public string Account { get; set; }

		private int nextCorrelationClientOrderId = 0;
		private Dictionary<int, int> nextClientOrderId = new Dictionary<int, int>();
		private Dictionary<string, string> exchangeOrderId = new Dictionary<string, string>();

		public Client(string compId, string subId, string locationId,
					  string targetCompanyId, string targetTraderId, string targetLocationId,
					  string account)
		{
			client = new FixTcpClient(compId, subId, locationId, targetCompanyId, targetTraderId, targetLocationId);
			client.LogonReceived += HandleLogon;
			client.NewOrderReceived += NewOrderReceived;
			client.CancelReplaceRequestReceived += CancelReplaceRequestReceived;
			client.CancelRequestReceived += CancelRequestReceived;
			client.CancelRejectReceived += CancelRejectReceived;

			client.NewOrderAckReceived += HandleNewOrderAck;
			client.NewOrderAckReceived += NewOrderAckReceived;
			client.CancelReplaceAckReceived += CancelReplaceAckReceived;
			client.CancelAckReceived += CancelAckReceived;
			client.RejectReceived += RejectReceived;
			client.FillReceived += FillReceived;

			Account = account;
		}

		public void Connect(IPAddress address, int port)
		{
			client.Connect(address, port);
		}

		public void Logon(string password)
		{
			client.Send(new Logon(password));
		}

		private void HandleLogon(object sender, Logon e)
		{
			client.StartHeartbeat(30);
		}

		public void Logout()
		{
			client.Send(new Logout());
			client.StopHeartbeat();
		}

		private int CreateOrder(Security security, OrderType orderType, TimeInForce tif, DateTime? expiry,
		                        Side side, int? price, int? stopPrice, int quantity, int? minQuantity, int? maxVisibleQuantity)
		{
			int ccoid = nextCorrelationClientOrderId++;
			nextClientOrderId.Add(ccoid, 0);
			string clientId = DateTime.UtcNow.ToString("yyMMdd") + "." + ccoid + ".0";
			client.Send(new NewOrder(Account, clientId, security, orderType, tif, expiry,
										  side, price, stopPrice, quantity, minQuantity, maxVisibleQuantity));

			return ccoid;
		}

		public int CreateLimitOrder(Security security, Side side, int quantity, int price, TimeInForce tif = TimeInForce.Day, 
		                            DateTime? expiry = null, int? minQuantity = null, int? maxShow = null)
		{
			return CreateOrder(security, OrderType.Limit, tif, expiry, side, price, null, quantity, minQuantity, maxShow);
		}

		public int CreateMarketOrder(Security security, Side side, int quantity, TimeInForce tif = TimeInForce.Day, 
		                             DateTime? expiry = null, int? minQuantity = null, int? maxShow = null)
		{
			return CreateOrder(security, OrderType.Market, tif, expiry, side, null, null, quantity, minQuantity, maxShow);
		}

		public int CreateMarketLimitOrder(Security security, Side side, int quantity, TimeInForce tif = TimeInForce.Day, 
		                                  DateTime? expiry = null, int? minQuantity = null, int? maxShow = null)
		{
			return CreateOrder(security, OrderType.MarketLimit, tif, expiry, side, null, null, quantity, minQuantity, maxShow);
		}

		public int CreateStopOrder(Security security, Side side, int quantity, int stopPrice, TimeInForce tif = TimeInForce.Day, 
		                           DateTime? expiry = null, int? minQuantity = null, int? maxShow = null)
		{
			return CreateOrder(security, OrderType.Stop, tif, expiry, side, null, stopPrice, quantity, minQuantity, maxShow);
		}

		public int CreateStopLimitOrder(Security security, Side side, int quantity, int price, int stopPrice, TimeInForce tif = TimeInForce.Day, 
		                                DateTime? expiry = null, int? minQuantity = null, int? maxShow = null)
		{
			return CreateOrder(security, OrderType.Limit, tif, expiry, side, price, stopPrice, quantity, minQuantity, maxShow);
		}

		private void HandleNewOrderAck(object sender, NewOrderAck e)
		{
			exchangeOrderId[e.CorrelationClientOrderId] = e.OrderId;
		}

		public void UpdateOrder(int ccoid, Security security, OrderType orderType, TimeInForce tif, DateTime? expiry,
							 	Side side, int? price, int? stopPrice, int quantity, int? minQuantity, int? maxShow)
		{
			string corrClientId = DateTime.UtcNow.ToString("yyMMdd") + "." + ccoid + ".0";
			string prevClientId = DateTime.UtcNow.ToString("yyMMdd") + "." + ccoid + "." + nextClientOrderId[ccoid];
			string orderId = exchangeOrderId[corrClientId];

			nextClientOrderId[ccoid]++;
			string clientId = DateTime.UtcNow.ToString("yyMMdd") + "." + ccoid + "." + nextClientOrderId[ccoid];

			client.Send(new CancelReplaceRequest(Account, orderId, clientId, prevClientId, corrClientId, security, orderType, tif, expiry,
										  side, price, stopPrice, quantity, minQuantity, maxShow));
		}

		public void DeleteOrder(int ccoid, Security security, Side side)
		{
			string corrClientId = DateTime.UtcNow.ToString("yyMMdd") + "." + ccoid + ".0";
			string prevClientId = DateTime.UtcNow.ToString("yyMMdd") + "." + ccoid + "." + nextClientOrderId[ccoid];
			string orderId = exchangeOrderId[corrClientId];

			nextClientOrderId[ccoid]++;
			string clientId = DateTime.UtcNow.ToString("yyMMdd") + "." + ccoid + "." + nextClientOrderId[ccoid];

			client.Send(new CancelRequest(orderId, clientId, prevClientId, corrClientId, security, side, Account));
		}
	}
}