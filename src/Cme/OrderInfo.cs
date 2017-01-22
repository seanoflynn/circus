using System;

namespace Circus.Cme
{
	public class OrderInfo
	{
		public string OrderId { get; set; }
		public string ClientId { get; set; }
		public string PreviousClientId { get; set; }
		public string CorrelationClientId { get; set; }
		public string Account { get; set; }

		public bool IsManual { get; set; }
		public bool? PreTradeAnonymity { get; set; }

		public OrderInfo(string orderId, string clientId, string correlationClientId, string account, 
		                 bool isManual, bool? preTradeAnonymity)
		{
			OrderId = orderId;
			ClientId = clientId;
			CorrelationClientId = correlationClientId;
			Account = account;
			IsManual = isManual;
			PreTradeAnonymity = preTradeAnonymity;
		}
	}	
}