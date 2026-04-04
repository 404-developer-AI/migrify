# Migrify

**A universal mailbox migration tool that moves any mailbox type to Microsoft 365. Built to actually work.**

> Built by a non-developer with zero coding skills, mass amounts of coffee, and a mass amount of AI prompts.
> This is vibe coding at its finest. But somehow — it actually works. Emails go in, emails come out. Every time.

## What does it do?

Migrify migrates email from IMAP mailboxes (Gmail, Google Workspace, Outlook, Yahoo, your uncle's self-hosted mail server from 2003) to Microsoft 365 Exchange Online.

What started as "let's see if this is even possible" is now a fully functional migration tool with parallel execution, smart queuing, real-time progress tracking, and one-command deployment.

### Features

**Source connectors**
- Manual IMAP with password or OAuth2 authentication
- Google Workspace with service account & domain-wide delegation
- Bulk mailbox discovery via Google Admin SDK and Graph API

**Destination connector**
- Microsoft 365 via Graph SDK (app-only auth)
- Per-job folder mapping with auto-map, mirror folders (auto-create missing folders on M365), manual mapping, and inline folder creation

**Migration engine**
- Full copy and incremental migration modes
- Date range filtering, duplicate detection (Message-ID), and rate limiting (Graph API compliant)
- Parallel execution: multiple jobs run simultaneously with a global FIFO queue
- Smart concurrency limits: 3-layer model (system resources, per M365 tenant, per source server)
- Resume interrupted migrations from checkpoint (re-evaluates skipped/failed messages)
- Automatic retry with exponential backoff for transient errors (429/503/504/408)
- Per-mail retry and bulk retry for failed messages

**Monitoring & UI**
- Real-time progress tracking via SignalR (live progress bars, status chips, folder updates)
- Dashboard with cross-project job overview, queue positions, and wait reasons
- Concurrency limit panels with per-layer occupancy and confidence indicators
- Searchable migration logs at project and job level with type filtering
- Job diagnostics: connection timing, cancel reasons, throughput metrics, full error context in DB logs
- Premium dark/light UI with MudBlazor

**Deployment**
- One-command install on a fresh Ubuntu VPS — sets up everything
- Interactive installer: domain, passwords, SSL, all configuration generated automatically
- Automatic SSL via Let's Encrypt with auto-renewal
- One-command updates: pull latest version, restart, data intact
- Docker containers with persistent volumes (database, logs, certificates)
- CI/CD via GitHub Actions to GitHub Container Registry

## Screenshots

![Dashboard](docs/screenshots/Dashboard.jpg)
*Dashboard — project overview, concurrency limits, and cross-project job status*

![Migration Jobs](docs/screenshots/Migration%20Jobs.jpg)
*Project detail — connector configuration, concurrency limits, and migration jobs with bulk start*

![Logs](docs/screenshots/Logs.jpg)
*Migration logs — filterable by type, searchable, with per-mail details*

![Settings](docs/screenshots/Settings.jpg)
*Settings — system resources, provider limits, and manual concurrency overrides*

## Tech Stack

| Layer | Tech |
|-------|------|
| Frontend | Blazor Server + MudBlazor |
| Backend | C# / ASP.NET Core 10 |
| Database | PostgreSQL + Entity Framework Core |
| IMAP | MailKit |
| M365 | Microsoft Graph SDK |
| Real-time | SignalR |
| Deployment | Docker + Nginx + Let's Encrypt |

## Quick Start

```bash
# Install on a fresh Ubuntu VPS (as root):
curl -fsSL https://raw.githubusercontent.com/404-developer-AI/migrify/main/deploy/scripts/install.sh | bash

# Update to latest version:
curl -fsSL https://raw.githubusercontent.com/404-developer-AI/migrify/main/deploy/scripts/update.sh | bash
```

The installer walks you through everything: domain, database password, admin credentials, SSL — and spins up the whole stack in under a minute.

## Can I use this?

Yes. That's no longer a joke — it actually works. Small migrations, large migrations, parallel batch jobs across multiple projects. Emails arrive with original dates, folder structure intact, duplicates detected.

Should you trust your 50,000-email production mailbox to software built by someone who learned what a `DbContext` is a few months ago? Make a backup first, but honestly — it handles it.

## Status

Current version: `v0.1.1`

The migration engine is solid, the deployment is automated, and the UI doesn't look like it was built in 2003. v0.1.1 adds comprehensive job diagnostics — connection timing, cancel reasons, throughput metrics, and full error context — so you actually know what happened when something goes wrong.

## License

Not yet decided. For now: look, learn, use at your own risk.

---

*Built with vibes, not skills. Powered by caffeine, AI, and the unshakable belief that shipping beats perfection.*
