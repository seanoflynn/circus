namespace Circus.Matching;

internal interface IReadOnlyPriceLadder
{
    bool TryGetBest(out long tick, out InternalOrder? firstOrder);

    IEnumerable<(long Tick, InternalOrder First, int Count)> EnumerateFromBest();

    void CopyLevelsFromBest(int maxLevels, List<(long Tick, int Quantity, int Count)> into);
}
