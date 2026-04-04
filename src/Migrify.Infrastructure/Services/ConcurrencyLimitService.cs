using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class ConcurrencyLimitService : IConcurrencyLimitService
{
    private readonly MigrationQueueService _queueService;
    private readonly SystemResourceMonitor _resourceMonitor;
    private readonly SourceLimitProvider _sourceLimitProvider;
    private readonly DestinationLimitProvider _destinationLimitProvider;
    private readonly IAppSettingsService _appSettings;
    private readonly ILogger<ConcurrencyLimitService> _logger;

    public ConcurrencyLimitService(
        MigrationQueueService queueService,
        SystemResourceMonitor resourceMonitor,
        SourceLimitProvider sourceLimitProvider,
        DestinationLimitProvider destinationLimitProvider,
        IAppSettingsService appSettings,
        ILogger<ConcurrencyLimitService> logger)
    {
        _queueService = queueService;
        _resourceMonitor = resourceMonitor;
        _sourceLimitProvider = sourceLimitProvider;
        _destinationLimitProvider = destinationLimitProvider;
        _appSettings = appSettings;
        _logger = logger;
    }

    public bool CanStartJob(string tenantId, string sourceKey, SourceConnectorType sourceType)
    {
        var runningJobs = _queueService.GetRunningJobInfos();
        var totalRunning = runningJobs.Count;

        // Layer 1: System limit (global)
        var systemLimit = GetEffectiveSystemLimit();
        if (totalRunning >= systemLimit)
        {
            _logger.LogDebug("System limit reached: {Running}/{Limit} jobs running", totalRunning, systemLimit);
            return false;
        }

        // Layer 2: Destination limit (per tenant)
        var destLimit = GetEffectiveDestinationLimit(tenantId);
        var runningForTenant = _queueService.CountRunningByTenant(tenantId);
        if (runningForTenant >= destLimit)
        {
            _logger.LogDebug("Destination limit reached for tenant {TenantId}: {Running}/{Limit}",
                tenantId, runningForTenant, destLimit);
            return false;
        }

        // Layer 3: Source limit (per server/domain)
        var srcLimit = GetEffectiveSourceLimit(sourceKey, sourceType);
        var runningForSource = _queueService.CountRunningBySource(sourceKey);
        if (runningForSource >= srcLimit)
        {
            _logger.LogDebug("Source limit reached for {SourceKey}: {Running}/{Limit}",
                sourceKey, runningForSource, srcLimit);
            return false;
        }

        return true;
    }

    public ConcurrencyLimits GetCurrentLimits()
    {
        var systemLimit = _resourceMonitor.GetSystemLimit();
        var destLimit = _destinationLimitProvider.GetDestinationLimit("");
        var srcLimit = _sourceLimitProvider.GetSourceLimit("", SourceConnectorType.ManualImap); // default for unknown

        var overrideMax = _appSettings.GetConcurrencyOverrideMax();
        var effectiveSystem = overrideMax ?? systemLimit;

        return new ConcurrencyLimits(
            DestinationLimit: destLimit,
            SourceLimit: srcLimit,
            SystemLimit: systemLimit,
            EffectiveLimit: effectiveSystem,
            IsOverridden: overrideMax.HasValue,
            OverrideValue: overrideMax
        );
    }

    public WaitReason DetermineWaitReason(string tenantId, string sourceKey, SourceConnectorType sourceType)
    {
        var runningJobs = _queueService.GetRunningJobInfos();
        var totalRunning = runningJobs.Count;

        // Check in same order as CanStartJob
        var systemLimit = GetEffectiveSystemLimit();
        if (totalRunning >= systemLimit)
            return WaitReason.SystemLimitReached;

        var destLimit = GetEffectiveDestinationLimit(tenantId);
        var runningForTenant = _queueService.CountRunningByTenant(tenantId);
        if (runningForTenant >= destLimit)
            return WaitReason.DestinationSlotFull;

        var srcLimit = GetEffectiveSourceLimit(sourceKey, sourceType);
        var runningForSource = _queueService.CountRunningBySource(sourceKey);
        if (runningForSource >= srcLimit)
            return WaitReason.SourceSlotFull;

        return WaitReason.None;
    }

    public ConcurrencyOverview GetConcurrencyOverview(string? tenantId, string? sourceKey, SourceConnectorType? sourceType)
    {
        var runningJobs = _queueService.GetRunningJobInfos();
        var totalRunning = runningJobs.Count;

        // System layer
        var systemLimit = GetEffectiveSystemLimit();
        var systemLayer = new ConcurrencyLayerStatus(
            Label: "System",
            Limit: systemLimit,
            Occupancy: totalRunning,
            Confidence: LimitConfidence.Calculated
        );

        // Destination layer
        var effectiveTenantId = tenantId ?? "";
        var destLimit = GetEffectiveDestinationLimit(effectiveTenantId);
        var destOccupancy = string.IsNullOrEmpty(tenantId)
            ? totalRunning
            : _queueService.CountRunningByTenant(effectiveTenantId);
        var destLayer = new ConcurrencyLayerStatus(
            Label: string.IsNullOrEmpty(tenantId) ? "Destination (all tenants)" : $"Destination ({tenantId})",
            Limit: destLimit,
            Occupancy: destOccupancy,
            Confidence: LimitConfidence.Estimated
        );

        // Source layer
        var effectiveSourceKey = sourceKey ?? "";
        var effectiveSourceType = sourceType ?? SourceConnectorType.ManualImap;
        var srcLimit = GetEffectiveSourceLimit(effectiveSourceKey, effectiveSourceType);
        var srcOccupancy = string.IsNullOrEmpty(sourceKey)
            ? totalRunning
            : _queueService.CountRunningBySource(effectiveSourceKey);
        var isKnown = !string.IsNullOrEmpty(sourceKey) && _sourceLimitProvider.IsKnownProvider(effectiveSourceKey, effectiveSourceType);
        var srcLayer = new ConcurrencyLayerStatus(
            Label: string.IsNullOrEmpty(sourceKey) ? "Source (all sources)" : $"Source ({sourceKey})",
            Limit: srcLimit,
            Occupancy: srcOccupancy,
            Confidence: isKnown ? LimitConfidence.Known : LimitConfidence.Estimated
        );

        return new ConcurrencyOverview(destLayer, srcLayer, systemLayer);
    }

    public void RefreshSystemLimits()
    {
        _resourceMonitor.Refresh();
    }

    private int GetEffectiveSystemLimit()
    {
        var overrideMax = _appSettings.GetConcurrencyOverrideMax();
        return overrideMax ?? _resourceMonitor.GetSystemLimit();
    }

    private int GetEffectiveDestinationLimit(string tenantId)
    {
        var overridePerTenant = _appSettings.GetConcurrencyOverridePerTenant();
        return overridePerTenant ?? _destinationLimitProvider.GetDestinationLimit(tenantId);
    }

    private int GetEffectiveSourceLimit(string sourceKey, SourceConnectorType sourceType)
    {
        var overridePerSource = _appSettings.GetConcurrencyOverridePerSource();
        return overridePerSource ?? _sourceLimitProvider.GetSourceLimit(sourceKey, sourceType);
    }
}
