namespace Circus.Examples;

// Runs one sample, or all of them in order when given no argument.
//
// Every sample here is deterministic: not one of them reads a system clock, so running any of
// them twice prints the same thing twice. That is the property the whole library is built on,
// and the samples are the wrong place to quietly depend on the opposite. It is also what lets
// CI run them as a smoke test, which is what keeps them from rotting the way the last set did.
internal static class Program
{
    private static readonly (string Name, Action Run)[] Examples =
    {
        ("order-book", OrderBookExample.Run),
        ("market-data", MarketDataExample.Run),
        ("replay", ReplayExample.Run),
        ("live-venue", LiveVenueExample.Run),
        ("agent-swarm", AgentSwarmExample.Run)
    };

    private static int Main(string[] args)
    {
        var wanted = args.Length == 0 ? Examples.Select(e => e.Name).ToArray() : args;

        foreach (var name in wanted)
        {
            var example = Examples.FirstOrDefault(e => e.Name == name);
            if (example.Run == null)
            {
                Console.Error.WriteLine($"unknown example '{name}'");
                Console.Error.WriteLine($"known: {string.Join(", ", Examples.Select(e => e.Name))}");
                return 1;
            }

            Console.WriteLine($"=== {name} ===");
            example.Run();
            Console.WriteLine();
        }

        return 0;
    }
}
