namespace Circus.DataProducers;

public record LevelsDataEvent(DateTime Time, IReadOnlyList<Level> Bids, IReadOnlyList<Level> Offers);
