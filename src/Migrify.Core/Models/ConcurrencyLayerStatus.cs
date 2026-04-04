namespace Migrify.Core.Models;

public enum LimitConfidence
{
    Known,
    Estimated,
    Calculated
}

public record ConcurrencyLayerStatus(
    string Label,
    int Limit,
    int Occupancy,
    LimitConfidence Confidence
);
