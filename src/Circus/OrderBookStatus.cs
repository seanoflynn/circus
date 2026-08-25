namespace Circus;

// Appended rather than reordered: no existing status's numeric value may move.
public enum OrderBookStatus
{
    PreOpen,
    Open,
    Closed,

    Paused,

    Halted
}
