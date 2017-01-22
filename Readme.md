# Circus

A futures exchange simulator built with .NET Core (C#). Still under development.

## Purpose

The main purpose of this project is to gain a better understanding of the features of the CME exchange (other exchanges afterwards) as well as the communication protocols between clients and the exchange (FIX over TCP and UDP). It is still very much in it's early stages but it could be used for testing purposes in the future.

## Usage

Download and open in [Visual Studio Code](https://code.visualstudio.com/download). Make sure you have [.NET Core](https://www.microsoft.com/net/download/core) installed. There are no dependencies.

## Examples

The exchange order book is a separate module that can be used on it's own.

```C#
var sec = new Security() { Id = 1, Type = SecurityType.Future, Group = "GC", Product = "GC", Contract = "GCZ6" };
var book = new OrderBook(sec);
book.SetStatus(SecurityTradingStatus.Open);

book.OrderCreated += (sender, e) => { Console.WriteLine("created " + e.Order.Id); };
book.OrderUpdated += (sender, e) => { Console.WriteLine("updated " + e.Order.Id); };
book.OrderDeleted += (sender, e) => { Console.WriteLine("deleted " + e.Order.Id); };

book.CreateLimitOrder(1, TimeInForce.Day, null, Side.Buy, 100, 3);
book.UpdateLimitOrder(1, 105, 5);
book.DeleteOrder(1);
```

You can also set up a fake CME server with accurate contract specifications, trading sessions and data channels. First you need to download three files from the CME FTP site (http://ftp.cmegroup.com)

- Trading Sessions from ```/SBEFix/Production/TradingSessionList.dat```
- Market Feeds from ```/SBEFix/Production/Configuration/config.xml```
- Security Definitions from ```/SBEFix/Production/secdef.dat.gz``` (Note this file is large and I recommend slicing out only the required definitions. It is also empty for most of the weekend, it is updated on Sunday afternoon before the start of trading at 6pm Chicago time.)

```C#
var contract = "GCG7";

// load CME information
var sec = SecurityDefinitionImporter.Load("Cme/Resources/secdef.dat", contract);
var ts = TradingSessionImporter.Load("Cme/Resources/TradingSessionList.dat", sec.Group);
var channels = MarketDataChannelImporter.Load("Cme/Resources/config.xml", sec.Product, true);

// start trading engine
var te = new TradingEngine(channels, ts, "CME", "G");
te.AddSecurity(sec);
te.Start(IPAddress.Loopback, 7821);

// open for trading
ts.Update(SecurityTradingStatus.Open);

// listen and output incoming market data
var ch = channels.Connections.Find(x => x.Type == Circus.Cme.MarketDataChannelConnectionType.Incremental && x.Feed == "A");
var dataClient = new FixUdpClient(ch.IPAddress, ch.Port);
dataClient.IncrementalUpdateReceived += (sender, e) =>
{
	Console.WriteLine("*** Incremental Update: " + e);
	foreach (var u in e.MDEntries)
	{
		Console.WriteLine("*** - " + u);
	}
};
dataClient.Listen();

Thread.Sleep(100);

// connect a new client and generate some order activity
var client = new Client("ABC123N", "Operator1", "IE", "CME", "G", null, "Acc1");
client.Connect(IPAddress.Loopback, port);
client.Logon("pass");
Thread.Sleep(100);
client.CreateLimitOrder(sec, Side.Buy, 2, 105);
Thread.Sleep(100);
client.CreateLimitOrder(sec, Side.Buy, 2, 104);
Thread.Sleep(100);
client.CreateLimitOrder(sec, Side.Sell, 5, 104);

Thread.Sleep(10000);
```

## Known Issues

- Exchange order book cannot handle multiple simultaneous requests yet. 
- CME MDP does not implement Simple Binary Encoding.

## Features
### Core
- [x] Limit orders
- [x] Market orders
- [x] Stop orders
- [x] Day orders
- [x] FAK/FOK orders
- [ ] GTC/GTD orders
- [x] Sessions
- [ ] Banding
- [ ] Limits
- [ ] Circuit breakers
- [ ] Stop & velocity logic
- [x] Self match trade prevention
- [x] FIFO matching algorithm
- [ ] Allocation matching algorithm
- [ ] Pro-Rata matching algorithm
- [ ] Indicative open, open algorithm
- [ ] Calendars/spread contracts
- [ ] Options
- [x] Market Statistics

### FIX
- [x] Encoder
- [x] Decoder
- [x] Sections
- [x] Groups
- [x] Messages

### CME
- [x] MarketDataChannel (config.xml) importer
- [x] SecurityDefinition (secdef.dat) importer
- [x] TradingSession (TradingSessionList.dat) importer
- [x] CME order management (Globex) – most
- [ ] CME market data (MDS) – a small amount
