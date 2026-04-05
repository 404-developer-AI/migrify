# Migrify - Versiegeschiedenis

Afgeronde versies — referentie voor context wanneer nodig.

---

## Versie 0.0.1 — Project setup & basis webapplicatie
- Solution aanmaken (ASP.NET Core 8 + Blazor Server)
- Projectstructuur: Migrify.Web, Migrify.Core, Migrify.Infrastructure
- Git init + .gitignore
- CLAUDE.md aanmaken
- Basis layout/design: sidebar navigatie
- Donker + licht thema met toggle-knop
- Kleurenpalet: donker (#1a1a2e/#16213e), licht (#ffffff/#f4f6f8), accent (#0078d4), error (#e94560)
- Placeholder pagina's: Dashboard, Projecten, Instellingen
- Basis admin login (ASP.NET Identity, 1 admin user)

## Versie 0.0.2 — Database & projectbeheer
- PostgreSQL setup (Docker Compose voor development)
- Entity Framework Core + migraties
- Datamodel: Project, ImapSettings, M365Settings, ImapProviderPreset
- CRUD pagina's voor Projecten
- Per project: bron-server instellingen (IMAP host, poort, auth type)
- Per project: M365 doel-instellingen (tenant, app registration, client ID/secret)
- Connectie-gegevens zichtbaar en bewerkbaar in UI

## Versie 0.0.3 — IMAP bronconnector (basis)
- IMAP verbinding maken via MailKit
- Authenticatie: wachtwoord
- Mailbox verkennen: folders ophalen met aantal mails
- Eerste en laatste maildatum detecteren per folder
- Test UI: verbinding testen, folders tonen
- Serveradres + resolved IP opslaan bij connectie

## Versie 0.0.4 — IMAP OAuth2
- OAuth2 authenticatie voor Gmail/Google Workspace
- OAuth2 flow implementatie (authorization code + token exchange)
- Token opslag (encrypted) en automatische refresh
- Google Cloud Console app setup documenteren
- Test UI: OAuth2 verbinding testen

## Versie 0.0.5 — Project/Job herstructurering + UI redesign

### Project/Job herstructurering
- Project wordt een container/folder voor meerdere migratie-jobs (geen auth op project-niveau)
- Nieuwe entity: MigrationJob (SourceEmail, DestinationEmail, Status, etc.)
- ImapSettings + M365Settings verplaatst naar job-niveau (alle auth per job)
- Project 1:N MigrationJob relatie
- Nieuwe IMigrationJobRepository + implementatie
- UI: Project detail pagina met jobs-overzicht
- UI: Job aanmaken/bewerken/verwijderen (bron mailbox + doel mailbox)
- EF migratie met backward compatibility (bestaande projecten krijgen automatisch 1 job)
- Enkele mailbox-migratie = 1 project met 1 job

### UI redesign — Premium SaaS-look
- Volledige visuele redesign in Linear/Vercel/Notion-stijl
- Font swap: Roboto → Inter (Google Fonts CDN)
- Kleurenpalet: indigo accent (#6366F1 light / #818CF8 dark met glow)
- Layout: AppBar verwijderd, full-height sidebar met indigo gradient, logo bovenaan, nav pills, dark mode toggle + logout onderaan
- Glassmorphism: login pagina, cards, dialogen (backdrop-filter blur, semi-transparante achtergronden)
- Dark mode als standaard, handmatige toggle in sidebar (geen OS-detectie)
- Alle pagina's, dialogen en componenten restyled (clean tables, rounded cards, soft shadows)
- CSS design system in `app.css` (19 secties)

## Versie 0.0.6 — M365 doelconnector
- Microsoft Graph SDK integratie
- Per-project Azure AD (Entra ID) app registration configuratie
- App-level permissions (Mail.ReadWrite, User.Read.All) — nodig voor bulk discovery later
- OAuth2 authenticatie flow naar M365 tenant
- Mailbox verkennen: M365 folders ophalen per job via Graph API
- Test UI: verbinding testen, M365 folders tonen
- Documentatie: hoe Azure AD app registration aan te maken per tenant

## Versie 0.0.7 — Connector model + Bulk mailbox discovery

### Connector model (herstructurering)
- Source Connector op project-niveau: Manual IMAP of Google Workspace
- Destination Connector op project-niveau: M365 (TenantId/ClientId/Secret verhuisd van job naar project)
- M365Settings verplaatst van MigrationJob naar Project (1:1)
- Nieuwe entity: GoogleWorkspaceSettings (service account, domain-wide delegation)
- Per-job IMAP override: HasImapOverride flag + eigen ImapSettings voor uitzonderingen
- MigrationJobDialog vereenvoudigd: M365 tab verwijderd, IMAP tab conditioneel
- Connector configuratie via aparte dialogen op project-detail pagina
- DB migratie met data-migratie (bestaande M365Settings automatisch naar project)

### Bulk mailbox discovery
- M365: Graph API User.Read.All → alle mailboxen in tenant ophalen
- Google Workspace: Google Admin SDK met domain-wide delegation → alle mailboxen ophalen
- Nieuwe entity: DiscoveredMailbox (gecached per project + side)
- UI: "Discover Mailboxes" knoppen op project-niveau (source + destination)
- Zoekbare autocomplete dropdowns voor bron- en doelmailbox selectie bij job aanmaken
- Handmatig invoeren van mailboxadressen blijft ook werken (free text fallback)
- Service account JSON key: secure file upload, alleen benodigde velden geëxtraheerd (private_key, client_email, token_uri)

## Versie 0.0.8 — Folder mapping
- UI voor handmatige folder mapping per MigrationJob (bron folder <-> M365 folder)
- Ik moet meerdere map mappings kunnen maken per job.
- Als ik een verkeerde mapping maak moet ik die ook kunnen verwijderen.
- Dropdown-selectie: source folders via IMAP explore (of Google Workspace), destination folders via Graph API
- Auto-suggest voor veelvoorkomende mappings (Inbox, Sent, Drafts, etc.)
- Wanneer mappen ontbreken op de ontvangende mailbox moet ik deze manueel kunnen aanmaken en dan mappen
- Mapping opslaan per migratie-job
- Validatie: waarschuwing bij niet-gemapte folders, maar niet voor lege folders.
- Niet gemapte mappen met mails niet overnemen naar de nieuwe mailbox

## Versie 0.0.9 — Migratie-engine (basis)
- Mails ophalen via IMAP per folder (volgens mapping)
- Mails schrijven naar M365 via Graph API (volgens mapping)
- Per-job uitvoering: elke MigrationJob draait onafhankelijk
- Basis progress tracking: percentage in database, pagina toont voortgang
- Simpele batch-verwerking (kleine mailboxen)
- Bugfix: UI refresh — AsNoTracking() voor progress polling (EF change tracker cachete verouderde data)
- Bugfix: EF tracking conflict bij StartMigration opgelost (detach bestaande tracked entity)
- UI: Completed-met-fouten toont oranje chip i.p.v. groen, details zichtbaar via tooltip

## Versie 0.0.10 — Migratie-engine (uitbreiding)
- Real-time progress via SignalR (vervangt 2s timer polling): live progress bar, status-chips, huidige folder, error meldingen
- Job-opties tab in MigrationJobDialog: copy/incremental modus, datumbereik (van/tot), duplicaten skippen/toestaan
- Migration options moeten geconfigureerd zijn voordat migratie gestart kan worden (net als folder mappings)
- Options kolom in job tabel: toont modus (Full Copy/Incremental) + datumrange indicator, details via tooltip
- Duplicaat-detectie via Graph API (internetMessageId filter) — apart geteld als "already present", geen effect op warning status
- Datumfiltering via IMAP SEARCH (server-side) — Sent Items gebruikt verzenddatum als fallback
- Rate limiting: 150ms delay tussen berichten (respecteert Graph API 10K req/10min limiet)
- Completed jobs kunnen opnieuw gestart worden (bijv. voor incremental re-run)
- Start-knop altijd zichtbaar met tooltip die toont welke voorwaarde ontbreekt
- Resource estimation document (docs/resource-estimation.md): API limieten, throughput, server resources
- Bugfix: IPv4 fallback socket werd te vroeg disposed (using var verwijderd)
- Bugfix: DateTime conversie voor Npgsql (MudDatePicker → UTC voor timestamptz kolommen)

## Versie 0.0.11 — Logging & foutoverzicht
- Per-mail logging: fout met details (subject, datum, foutmelding)
- Logging voor succes mag summier, moet niet per mail.
- Migratie-log pagina met filtering en zoeken
- Migratie-log pagina per project en per job, duidelijk gescheiden.
- Log pagina moet ook geopend kunnen worden vanuit de job bar
- Gefaalde mails overzicht

## Versie 0.0.12 — Retry & hervatten
- Retry-knop per gefaalde mail
- Automatische retry met exponential backoff bij tijdelijke fouten
- Hervatten vanaf laatst geslaagd punt (checkpoint systeem)

## Versie 0.0.13 — Incremental sync & job-info

### Incremental sync
- `LastCompletedAt` timestamp op MigrationJob: bijhouden wanneer de laatste succesvolle migratie is afgerond
- Bij re-run in incremental modus: `DateFrom` automatisch vullen met `LastCompletedAt` (alleen nieuwe mails ophalen)
- Datumbereik blijft handmatig aanpasbaar: gebruiker kan `DateFrom`/`DateTo` overschrijven vóór het starten
- Checkpoints na succesvolle afronding behouden (nodig voor incremental re-runs)
- Bij starten van een job met bestaande checkpoints: gebruiker kiest "Hervatten vanaf checkpoint" of "Opnieuw beginnen" (geldt voor zowel Incremental als Full Copy)
- Message-ID tracking / duplicaat-detectie: reeds geïmplementeerd (v0.0.10)

### Job-info in UI
- Job bar: "Completed" chip toont via tooltip: starttijd, eindtijd en duur van de laatste run
- `StartedAt` en `CompletedAt` timestamps opslaan op MigrationJob
- Duur berekenen en leesbaar tonen (bijv. "2u 15m 30s")

## Versie 0.0.14 — Parallel migraties & smart queue

### v0.0.14a — Parallel engine + basis queue
- Background service die meerdere jobs tegelijk kan draaien
- Queue systeem: jobs komen in een wachtrij, worden automatisch opgepakt zodra een slot vrijkomt
- Voorlopig hardcoded limiet (bijv. max 3 gelijktijdig) om de queue te testen
- Bestaande SignalR progress werkt voor meerdere jobs tegelijk
- Jobs starten vanuit project-detail pagina — alle geselecteerde jobs gaan naar de queue

### v0.0.14b — Smart limietbepaling (3 lagen)
- Drie-lagen limietmodel vervangt de hardcoded limiet:
  - **Destination (per M365 tenant):** berekend op basis van Graph API limiet (10K req/10min), gedeeld over alle jobs naar dezelfde tenant — als twee projecten naar dezelfde tenant migreren, delen ze de limiet
  - **Source (per bronserver):** Google Workspace op basis van API quotas, bekende IMAP-providers (Gmail, Outlook.com) met bekende limieten, onbekende IMAP-servers conservatieve default (bijv. 3 connecties) — als twee projecten van dezelfde server lezen, delen ze de limiet
  - **Systeem (globaal over alle projecten):** beschikbaar geheugen en CPU uitlezen via .NET (cross-platform: Windows, Linux, Docker) — één VPS, één limiet
- De laagste limiet van de drie bepaalt hoeveel jobs tegelijk draaien — de rest wacht in de queue
- Handmatige override mogelijk via instellingen (met waarschuwing bij overschrijding geschatte veilige limiet)

### v0.0.14c — Dashboard & limiet-overzicht UI
- Dashboard pagina: overzicht van alle lopende, in-queue en afgeronde jobs (cross-project)
- Per job duidelijke status: Running / Queued / Completed / Failed
- Per job live voortgang (hergebruik bestaande SignalR progress)
- Per project een overzichtspanel met de drie limietlagen:
  - Destination limiet (geschat/berekend) + huidige bezetting
  - Source limiet (geschat/berekend) + huidige bezetting
  - Systeem limiet (geschat) + huidige bezetting
- Per laag: of het een bekende of geschatte limiet is
- Aantal jobs in queue + per job de reden waarom die in de queue staat (bijv. "Wacht op destination slot — M365 tenant limiet bereikt" of "Wacht op systeem resources")

### v0.0.14d — Security update & bugfixes
- MailKit geüpdatet van 4.12.1 naar 4.15.1 (security fix: CRLF injection vulnerability in MimeKit, CVE-2026-30227)
- Bugfix: EF Core tracking conflict bij FolderMapping/ImapSettings entities bij job update
- Bugfix: IMAP verbinding wordt nu hergebruikt per job (was: per folder, veroorzaakte herhaalde SSL fallback warnings)
- Bugfix: Checkpoints werden niet opgeslagen na de laatste batch — nu bij elke message bijgewerkt in memory
- Bugfix: Resume from checkpoint verwerkt nu ook skipped/failed messages opnieuw voor accuraat eindresultaat

## Versie 0.1.0 — Docker, deployment & one-string install

### v0.1.0a — Dockerfile, Docker Compose & basis deployment
- Multi-stage Dockerfile voor de applicatie (.NET 10 SDK build → runtime image)
- Docker Compose: app + PostgreSQL (geen Nginx nog)
- `.env.example` template met alle configureerbare variabelen
- Environment-based configuratie: connection string, encryption key, admin credentials via env vars
- Health check endpoint (`/health`)
- EF Core Migrations draaien automatisch bij opstarten (al geïmplementeerd in Program.cs)
- PostgreSQL data op named Docker volume (databehoud bij updates)
- App logs op Docker volume (databehoud bij updates)

### v0.1.0b — Nginx reverse proxy + SSL
- Nginx container toegevoegd aan Docker Compose
- Nginx reverse proxy configuratie (proxy_pass naar app container)
- Let's Encrypt / Certbot integratie voor automatisch SSL-certificaat
- HTTP → HTTPS redirect
- SSL auto-renewal via Certbot timer/cron in container
- Certbot certificaten op Docker volume (databehoud bij updates)

### v0.1.0c — One-string install & update scripts
- **Install script** (`install.sh`): één commando installeert alles op een verse Ubuntu VPS
  - Installeert Docker + Docker Compose als die ontbreken
  - Interactieve vragen:
    - FQDN / URL van de webapp (bijv. `migrify.steaan.com`)
    - Email voor Let's Encrypt certificaat
    - PostgreSQL wachtwoord (of automatisch genereren)
    - Admin wachtwoord voor de webapp (of automatisch genereren)
    - Encryption key (automatisch genereren)
  - Genereert `.env` bestand met alle configuratie
  - Draait `docker compose up -d`
  - Wacht tot health check OK is
  - **Installatieresultaat wordt weggeschreven naar logbestand** (`/opt/migrify/install-report.log`):
    - Alle configuratie (inclusief wachtwoorden)
    - Welke componenten geïnstalleerd zijn
    - URLs en endpoints
    - Status van alle containers
  - Na afloop: toont samenvatting + waarschuwing dat het logbestand wachtwoorden bevat en verwijderd moet worden
- **Update script** (`update.sh`): één commando updatet naar nieuwste versie
  - Pulled nieuwste Docker image(s)
  - Stopt containers, herstart met nieuwe images
  - EF Migrations draait automatisch bij opstarten
  - Data intact via Docker volumes
  - Toont update-rapport met versie-info

### v0.1.0d — CI/CD pipeline (GitHub Actions)
- GitHub Actions workflow: bouwt Docker image bij push naar main
- Pusht image naar GitHub Container Registry (ghcr.io)
- Install/update scripts pullen image van ghcr.io (i.p.v. lokaal bouwen)
- Optioneel: automatische deployment naar VPS via SSH

## Versie 0.1.1 — Job logging & diagnostiek

Uitgebreide logging zodat bij falen, cancelen of trage jobs direct duidelijk is wat er mis ging.

- Nieuwe `MigrationLogType`: `Info` — voor job-level events in de DB (naast bestaande Error/Skipped/Summary/Retried)
- **Connectie-logging** (naar zowel file log als DB MigrationLog):
  - IMAP: host, poort, encryptie, auth type, IPv4/IPv6, connectie-duur
  - Google Workspace: service account, impersonation email, token request duur
  - M365: tenant ID, token acquisitie duur
  - Succesvolle connectie bevestiging met timing
  - Gedetailleerde foutmelding bij connectie-falen (DNS, auth, timeout, cert)
- **Job lifecycle logging** (DB records):
  - Job gestart: configuratie samenvatting (modus, datumrange, folder count)
  - Job geslaagd: duur, totalen, throughput (msg/min)
  - Job gefaald: volledige error context + stack trace samenvatting
  - Job gecanceld: reden (user request, app shutdown, of vanuit queue)
- **Cancellation reden tracking**: onderscheid tussen user-cancel, shutdown-cancel, en queue-cancel
- Log pagina (bestaand) toont nu ook Info-entries — filterable
- Geen UI-wijzigingen buiten het tonen van Info-logs in de bestaande log pagina

## Versie 0.1.2 — Agenda-migratie (CalDAV → M365)

### v0.1.2a — CalDAV discovery & verbinding
- CalDAV support detectie per job/mailbox: automatisch tijdens IMAP explore
- 4-staps discovery: bekende non-CalDAV providers (Gmail, Outlook, iCloud) → hardcoded provider presets (Fastmail, Yahoo, GMX, Mailbox.org, Posteo, Zoho) → well-known URL (.well-known/caldav, RFC 5785) → SRV records (_caldavs/_caldav._tcp) → MX pattern matching
- CalDAV status kolom in jobs-tabel: Supported (groen), Not Supported (grijs), N/A (info, Google Workspace), Unknown
- CalDavSupportStatus enum + CalDavBaseUrl op MigrationJob entity (EF migratie)
- CalDAV Explore dialog: verbindt met CalDAV server, toont kalenders (naam, kleur, event count)
- Explorer met 3 fallback-strategieën: standaard RFC 4791 discovery (principal → calendar-home → calendars), common path patterns (Nextcloud, SOGo, generiek), direct listing
- Event counting: REPORT calendar-query (RFC 4791), fallback naar PROPFIND Depth:1 (telt .ics resources)
- Alleen password-auth voor CalDAV (OAuth2 toont melding "not yet supported")
- Google Workspace projecten: CalDAV status = NotApplicable (Google Calendar API is apart)
- CalDAV discovery is non-blocking: fouten breken IMAP explore niet
- Library: eigen HttpClient voor WebDAV (PROPFIND/REPORT) + System.Xml.Linq voor XML, geen extra NuGet packages
