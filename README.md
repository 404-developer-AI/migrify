# Migrify

**A universal mailbox migration tool that moves any mailbox type to Microsoft 365, fast and hopefully reliable.**

> Built by a non-developer with zero coding skills, mass amounts of coffee, and an mass amount of AI prompts.
> This is vibe coding at its finest. If it works, don't ask why. If it doesn't, yeah... that tracks.

## What does it do?

Migrify migrates email from IMAP mailboxes (Gmail, Outlook, Yahoo, your uncle's self-hosted mail server from 2003) to Microsoft 365 Exchange Online.

**Features so far:**
- Project-based migration management
- IMAP connection testing & mailbox exploration
- IMAP OAuth2 authentication (Gmail / Google Workspace)
- Microsoft 365 connection testing via Graph API
- M365 mailbox exploration
- Migration job management per project
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

Current version: `v0.0.5`

The version numbering starts at 0.0.1 because even 1.0 feels too optimistic right now.

## Can I use this?

Technically? Yes. Should you trust your production mailboxes to software built by someone who learned what a `DbContext` is last week? That's between you and your backup strategy.

## Roadmap

There is one. It's ambitious. It involves actual email migration at some point. Stay tuned.

## License

Not yet decided. For now: look, laugh, learn.

---

*Built with vibes, not skills. Powered by caffeine, AI, and the unshakable belief that "it works on my machine" counts as QA.*
