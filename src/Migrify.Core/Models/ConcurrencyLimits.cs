namespace Migrify.Core.Models;

public record ConcurrencyLimits(
    int DestinationLimit,
    int SourceLimit,
    int SystemLimit,
    int EffectiveLimit,
    bool IsOverridden,
    int? OverrideValue
);
