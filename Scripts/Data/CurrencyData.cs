namespace PeakShift.Data;

/// <summary>
/// Stub currency tracker — no persistence yet.
/// </summary>
public class CurrencyData
{
    public int Coins { get; set; } = 0;
    public int Gems { get; set; } = 0;

    public bool TrySpend(int coins)
    {
        if (Coins < coins) return false;
        Coins -= coins;
        return true;
    }
}
