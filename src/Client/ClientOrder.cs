using System;

using Circus.Common;

namespace Circus.Client
{
	public abstract class ClientOrder : Order
	{
		protected ClientOrder(int id, Security security, OrderType type, TimeInForce tif, DateTime? expiry, Side side,
							int? price, int? stopPrice, int quantity, int minQuantity, int? maxVisibleQuantity,
							string selfMatchId, SelfMatchPreventionInstruction selfMatchMode)
			: base(id, security, type, tif, expiry, side, price, stopPrice, quantity, minQuantity, maxVisibleQuantity, 
			       selfMatchId, selfMatchMode)
		{ }

		public abstract void Update();
		public abstract void Delete();
	}
}