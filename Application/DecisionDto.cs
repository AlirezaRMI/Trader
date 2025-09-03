namespace Application;


public record DecisionDto(
    string Action,
    double PositionSizeLots,
    double StopLossPrice,
    double TakeProfitPrice,
    string Note
);