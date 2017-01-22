using System;

using Circus.Common;

namespace Circus.Client
{
	public interface Client
	{
		event EventHandler OrderCreated;
		event EventHandler OrderUpdated;
		event EventHandler OrderDeleted;

		event EventHandler OrderCreateRejected;
		event EventHandler OrderUpdateRejected;
		event EventHandler OrderDeleteRejected;

		event EventHandler OrderFilled;
		event EventHandler OrderExpired;

		void Connect();
		void Disconnect();

		void Logon();
		void Logout();

		// TODO: decide on how this is going to work???
		void Create(ClientOrder order);

		ClientOrder CreateOrder(Security security, OrderType type, Side side, int price, int stopPrice, 
		                        int? quantity, TimeInForce tif = TimeInForce.Day, DateTime? expiry = null, 
		                        int? minQuantity = null, int? maxShow = null);

		ClientOrder CreateLimit(Security security, Side side, int quantity, int price, TimeInForce tif = TimeInForce.Day,
						 		DateTime? expiry = null, int? minQuantity = null, int? maxShow = null);

		ClientOrder CreateMarketOrder(Security security, Side side, int quantity, TimeInForce tif = TimeInForce.Day,
									  DateTime? expiry = null, int? minQuantity = null, int? maxShow = null);

		ClientOrder CreateMarketLimitOrder(Security security, Side side, int quantity, TimeInForce tif = TimeInForce.Day,
										   DateTime? expiry = null, int? minQuantity = null, int? maxShow = null);

		ClientOrder CreateStopOrder(Security security, Side side, int quantity, int stopPrice, TimeInForce tif = TimeInForce.Day,
								    DateTime? expiry = null, int? minQuantity = null, int? maxShow = null);

		ClientOrder CreateStopLimitOrder(Security security, Side side, int quantity, int price, int stopPrice, TimeInForce tif = TimeInForce.Day,
								   		 DateTime? expiry = null, int? minQuantity = null, int? maxShow = null);
	}
}