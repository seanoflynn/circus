using Circus.MarketData;

namespace Circus.Examples;

// Every published message carries only scalars - depth arrives one price level at a time rather
// than as a whole ladder - so a record's generated ToString renders each of them faithfully and
// nothing here needs describing by hand.
internal static class Display
{
    public static void Print(IEnumerable<ChannelMessage> messages)
    {
        foreach (var message in messages)
            Console.WriteLine($"  {message.Sequence,3} {message.Data.Symbol} {message.Data}");
    }
}
