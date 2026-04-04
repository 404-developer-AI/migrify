using Migrify.Core.Entities;

namespace Migrify.Core.Models;

public record DashboardJobDto(
    Guid JobId,
    Guid ProjectId,
    string ProjectName,
    string SourceEmail,
    string DestinationEmail,
    MigrationJobStatus Status,
    double ProgressPercent,
    string? CurrentFolder,
    int? QueuePosition,
    WaitReason WaitReason
);
