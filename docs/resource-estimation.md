# Migrify Resource Estimation Guide

## API Rate Limits

### Microsoft Graph API (Mail Operations)
| Limit | Value |
|-------|-------|
| Requests per 10 minutes | 10,000 per app per tenant |
| Safe sustained rate | ~14 requests/second |
| Mailbox concurrency (batch) | 4 concurrent requests per mailbox |
| Throttling response | HTTP 429 with Retry-After header |
| Message size limit | 4 MB for direct JSON upload |

**Migrify rate limiting:** 150ms delay between messages (~7 msg/sec with duplicate check, ~14 msg/sec without). This provides a safe margin below the 10K/10min limit.

### Google Workspace IMAP (Service Account)
| Limit | Value |
|-------|-------|
| Download bandwidth | 2,500 MB/day per user |
| Upload bandwidth | 500 MB/day per user |
| Concurrent IMAP connections | 15 per user |
| Session duration | ~24 hours (or OAuth token validity) |

**Migrify usage:** 1 IMAP connection per active job (sequential folder processing).

---

## Throughput Estimates

| Scenario | Messages/Hour | Notes |
|----------|---------------|-------|
| Copy mode (no duplicate check) | ~50,000 | Limited by Graph API rate (14/sec) |
| Copy mode + skip duplicates | ~25,000 | 2 API calls per message (check + upload) |
| Incremental mode | ~25,000 | Always checks duplicates |
| Large attachments (>1MB avg) | ~5,000-10,000 | Limited by upload bandwidth |

---

## Server Resource Requirements

### Memory (RAM) per Job

| Component | Memory Usage |
|-----------|-------------|
| MailKit IMAP connection | ~10-20 MB base |
| Single email (no attachments) | 10-100 KB |
| Single email (with attachments) | up to 4 MB |
| MIME serialization peak | ~2x message size |
| Base64 encoding overhead | +33% for attachments |
| **Working set per job** | **~50-250 MB** |

### Server RAM Recommendations

| Concurrent Jobs | Recommended RAM | Notes |
|-----------------|-----------------|-------|
| 1 job | 512 MB | Minimum for single migration |
| 2-5 jobs | 1-2 GB | Comfortable for small batches |
| 5-10 jobs | 2-4 GB | Medium workload |
| 10+ jobs | 4+ GB | Large-scale migrations |

> **Note:** Since v0.0.14, jobs run in parallel with smart concurrency limits. The system automatically calculates the max concurrent jobs based on available RAM (250 MB/job, 512 MB reserved for OS+app). Manual override is available via Settings page.

### CPU
- Migration is primarily I/O-bound (network: IMAP download + Graph API upload)
- CPU usage is low: MIME parsing, JSON serialization, base64 encoding
- 1-2 vCPUs is sufficient for most workloads
- CPU spikes during MIME parsing of large messages with many attachments

### Disk
| Component | Disk Usage |
|-----------|-----------|
| PostgreSQL database | ~1 KB per job, ~100 bytes per folder mapping |
| Log files | ~1-10 MB per migration run |
| Application | ~50-100 MB (Docker image) |
| **No local message caching** | Messages stream IMAP → Graph API |

---

## Network Bandwidth

| Direction | Volume |
|-----------|--------|
| Download from IMAP | = total mailbox size |
| Upload to Graph API | ~1.33x mailbox size (base64 attachment overhead) |
| **Total per mailbox** | **~2.33x mailbox size** |

Example: 10 GB mailbox = ~10 GB download + ~13 GB upload = ~23 GB total network transfer.

---

## Migration Time Estimates

| Mailbox Size | Messages (est.) | Copy Mode | Incremental Mode |
|-------------|-----------------|-----------|------------------|
| 1 GB / 5,000 msgs | 5,000 | ~6 min | ~12 min |
| 5 GB / 25,000 msgs | 25,000 | ~30 min | ~60 min |
| 10 GB / 50,000 msgs | 50,000 | ~60 min | ~120 min |
| 50 GB / 250,000 msgs | 250,000 | ~5 hours | ~10 hours |

Assumes:
- Average message size ~200 KB
- No throttling events (HTTP 429)
- Stable network connection
- Single job (multiply throughput by concurrent jobs, max 2 per M365 tenant)

---

## Concurrency Limits (v0.0.14b)

Migrify enforces a 3-layer concurrency limit model. The lowest limit across all three layers determines how many jobs can run simultaneously.

### Layer 1: System (global)
| Resource | Calculation |
|----------|-------------|
| Memory | (Total RAM - 512 MB) / 250 MB per job |
| CPU | Processor count × 3 (I/O-bound) |
| **Effective** | **min(memory, CPU)** |

### Layer 2: Destination (per M365 tenant)
| Limit | Value | Reason |
|-------|-------|--------|
| Max concurrent jobs per tenant | 2 | 10K req/10min shared across all jobs using same ClientId; each job uses ~4,200 req/10min at 150ms rate |

### Layer 3: Source (per IMAP server / Google domain)
| Provider | Max Concurrent Jobs |
|----------|-------------------|
| Gmail / Google Workspace | 15 |
| Outlook.com | 10 |
| iCloud | 10 |
| Fastmail | 10 |
| Yahoo | 5 |
| Zoho | 5 |
| Unknown IMAP servers | 3 (conservative default) |

All limits can be manually overridden via the Settings page (with safety warnings when exceeding calculated limits).

---

## Sources
- [Microsoft Graph throttling limits](https://learn.microsoft.com/en-us/graph/throttling-limits)
- [Gmail bandwidth limits](https://support.google.com/a/answer/1071518)
- [Gmail IMAP limits](https://support.google.com/a/answer/2751577)
