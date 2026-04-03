# Migrify

**A universal mailbox migration tool that moves any mailbox type to Microsoft 365, fast and hopefully reliable.**

> Built by a non-developer with zero coding skills, mass amounts of coffee, and an mass amount of AI prompts.
> This is vibe coding at its finest. If it works, don't ask why. If it doesn't, yeah... that tracks.

## What does it do?

Migrify migrates email from IMAP mailboxes (Gmail, Outlook, Yahoo, your uncle's self-hosted mail server from 2003) to Microsoft 365 Exchange Online. And yes — it actually works now. Emails go in on one side and come out on the other. Most of the time.

**Features so far:**
- Project-based migration management with connector model (source + destination per project)
- IMAP connection testing & mailbox exploration (password + OAuth2)
- IMAP OAuth2 authentication (Gmail / Google Workspace)
- Google Workspace source connector with service account & domain-wide delegation
- Microsoft 365 destination connector via Graph SDK (app-only auth)
- M365 mailbox exploration (including subfolders)
- Bulk mailbox discovery: Google Admin SDK (source) + Graph API User.Read.All (destination)
- Migration job management per project with autocomplete mailbox selection
- Per-job folder mapping: source (IMAP) to destination (M365) folder mapping
- Auto-map for common folders (Inbox, Sent, Drafts, etc.)
- M365 folder & subfolder creation from within the mapping dialog
- Validation warnings for unmapped folders containing emails
- Migration engine: IMAP → M365 email migration per folder mapping
- Real-time progress tracking via SignalR (live progress bar, status, folder updates)
- Job options: date range filter, duplicate detection (Message-ID), full copy & incremental mode
- Rate limiting for Graph API compliance (10K req/10min)
- Memory-efficient streaming with IMAP SEARCH-based date filtering
- Per-job start/cancel controls with live progress updates
- Per-mail error & skip logging with searchable log pages
- Project-level and job-level log views with type filtering
- Real-time log updates via SignalR during active migrations
- Per-mail retry and bulk retry for failed messages
- Automatic retry with exponential backoff for transient errors (429/503/504/408)
- Resume interrupted migrations from checkpoint (skips already-processed messages)
- Incremental sync: auto-fill date range from last successful run for re-runs
- Resume or start fresh dialog when re-running jobs with checkpoints
- Job timing info: start time, end time, and duration visible on status chips
- Parallel migration engine: multiple jobs run simultaneously
- Global FIFO queue with Queued status and cancel-from-queue support
- Smart concurrency limits: 3-layer model (system resources, per M365 tenant, per source server)
- Known provider detection (Gmail, Outlook, Yahoo, etc.) with provider-specific connection limits
- Manual override for concurrency limits via Settings page with safety warnings
- Bulk start: checkboxes with "Start Selected" and "Start All" buttons
- Clean admin dashboard with premium UI (thanks MudBlazor)

## Tech Stack

| Layer | Tech |
|-------|------|
| Frontend | Blazor Server + MudBlazor |
| Backend | C# / ASP.NET Core 10 |
| Database | PostgreSQL + Entity Framework Core |
| IMAP | MailKit |
| M365 | Microsoft Graph SDK |
| Real-time | SignalR |
| Deployment | Docker + Nginx |

## Status

**Work in progress.** But the kind of progress where emails actually migrate now. The foundation is there, the walls are up, and the roof is... getting there. Still wouldn't host a dinner party though.

Current version: `v0.0.14`

The version numbering starts at 0.0.1 because even 1.0 feels too optimistic right now.

## Can I use this?

Technically? Yes. Small migrations are actually working. Should you trust your 50,000-email production mailbox to software built by someone who learned what a `DbContext` is last week? That's between you and your backup strategy.

## Roadmap

There is one. It's ambitious. It currently involves smart queue management and eventually Docker deployment. The email migration part? That's actually done. Failed mails can be retried, interrupted migrations can be resumed, and incremental sync re-runs only fetch new emails. Wild.

## License

Not yet decided. For now: look, laugh, learn.

---

*Built with vibes, not skills. Powered by caffeine, AI, and the unshakable belief that "it works on my machine" counts as QA.*
