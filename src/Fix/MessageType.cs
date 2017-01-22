using System;

namespace Circus.Fix
{
	public enum MessageType
	{
		Unknown = 0,
		Heartbeat = '0',
		TestRequest = '1',
		ResendRequest = '2',
		SessionLevelReject = '3',
		SequenceReset = '4',
		Logout = '5',
		ExecutionReport = '8',
		OrderCancelReject = '9',
		Logon = 'A',
		OrderSingle = 'D',
		OrderList = 'E',
		OrderCancelRequest = 'F',
		OrderCancelReplaceRequest = 'G',
		OrderStatusRequest = 'H',
		Quote = 'S',
		MarketDataRequest = 'V',
		MarketDataSnapshotFullRefresh = 'W',
		MarketDataIncrementalRefresh = 'X',
		MarketDataRequestReject = 'Y',
		QuoteCancel = 'Z',
		QuoteStatusRequest = 'a',
		QuoteAcknowledgement = 'b',
		SecurityDefinitionRequest = 'c',
		SecurityDefinition = 'd',
		SecurityStatusRequest = 'e',
		SecurityStatus = 'f',
		TradingSessionStatusRequest = 'g',
		TradingSessionStatus = 'h',
		BusinessLevelReject = 'j',
	}	
}
