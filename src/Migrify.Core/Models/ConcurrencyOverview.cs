namespace Migrify.Core.Models;

public record ConcurrencyOverview(
    ConcurrencyLayerStatus Destination,
    ConcurrencyLayerStatus Source,
    ConcurrencyLayerStatus System
);
