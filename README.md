# Migrify

**A universal mailbox migration tool that moves any mailbox type to Microsoft 365, fast and hopefully reliable.**

> Built by a non-developer with zero coding skills, mass amounts of coffee, and an mass amount of AI prompts.
> This is vibe coding at its finest. If it works, don't ask why. If it doesn't, yeah... that tracks.

## What does it do?

Migrify migrates email from IMAP mailboxes (Gmail, Outlook, Yahoo, your uncle's self-hosted mail server from 2003) to Microsoft 365 Exchange Online.

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
- Basic migration engine: IMAP → M365 email migration per folder mapping
- Background job execution with progress tracking
- Per-job start/cancel controls with auto-refreshing progress bar
- Clean admin dashboard with premium UI (thanks MudBlazor)

## Tech Stack

| Layer | Tech |
|-------|------|
| Frontend | Blazor Server + MudBlazor |
| Backend | C# / ASP.NET Core 10 |
| Database | PostgreSQL + Entity Framework Core |
| IMAP | MailKit |
| M365 | Microsoft Graph SDK |
| Deployment | Docker + Nginx |

## Status

**Work in progress.** Very much in progress. Like, "the foundation is there but the house has no roof" kind of progress.

Current version: `v0.0.9`

The version numbering starts at 0.0.1 because even 1.0 feels too optimistic right now.

## Can I use this?

Technically? Yes. Should you trust your production mailboxes to software built by someone who learned what a `DbContext` is last week? That's between you and your backup strategy.

## Roadmap

There is one. It's ambitious. It involves actual email migration at some point. Stay tuned.

## License

Not yet decided. For now: look, laugh, learn.

---

*Built with vibes, not skills. Powered by caffeine, AI, and the unshakable belief that "it works on my machine" counts as QA.*
