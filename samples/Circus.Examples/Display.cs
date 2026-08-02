using Circus.MarketData;

namespace Circus.Examples;

// A record's generated ToString renders a list as its type name, so a depth message prints as
// "Bids = System.Collections.Generic.List`1[...]" unless something describes it. Only
// LevelsDataEvent carries lists, so only it needs the help.
internal static class Display
{
    public static void Print(IEnumerable<ChannelMessage> messages)
    {
        foreach (var message in messages)
            Console.WriteLine($"  {message.Sequence,3} {message.Data.Symbol} {Describe(message.Data)}");
    }

    private static string Describe(MarketDataEvent data) => data switch
    {
        LevelsDataEvent levels =>
            $"LevelsDataEvent {{ Bids = [{string.Join(", ", levels.Bids)}], " +
            $"Offers = [{string.Join(", ", levels.Offers)}] }}",
        _ => data.ToString()!
    };
}
