using Domain.Trading;

namespace Domain.Services;

public interface ISeriesReader
{
    IReadOnlyList<double> GetCloses(Symbol symbol, Timeframe tf, int lastN);
    void AppendClose(Symbol symbol, Timeframe tf, double close);
}