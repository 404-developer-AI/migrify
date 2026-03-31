using System.Net;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class ImapMailboxExplorer : IImapMailboxExplorer
{
    private readonly ILogger<ImapMailboxExplorer> _logger;

    public ImapMailboxExplorer(ILogger<ImapMailboxExplorer> logger)
    {
        _logger = logger;
    }

    public Task<ImapExploreResult> ExploreAsync(
        string host, int port, ImapEncryption encryption,
        string username, string password,
        CancellationToken cancellationToken = default)
    {
        return ExploreAsync(host, port, encryption, ImapAuthType.Password, username, password, null, cancellationToken);
    }

    public async Task<ImapExploreResult> ExploreAsync(
        string host, int port, ImapEncryption encryption,
        ImapAuthType authType, string username, string? password, string? oauthAccessToken,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        var ct = cts.Token;

        try
        {
            var socketOptions = MapEncryption(encryption);
            string? resolvedIp = null;

            // Resolve IP address
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, ct);
                _logger.LogDebug("DNS resolved {Host} to: {Addresses}",
                    host, string.Join(", ", addresses.Select(a => $"{a} ({a.AddressFamily})")));
                resolvedIp = addresses.FirstOrDefault()?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DNS resolution failed for {Host}", host);
            }

            ImapClient client;
            Socket? fallbackSocket = null;

            // Try normal connection first, fall back to IPv4 raw socket
            try
            {
                _logger.LogDebug("Attempting IMAP connect to {Host}:{Port} ({Encryption})", host, port, socketOptions);
                client = CreateClient();
                await client.ConnectAsync(host, port, socketOptions, ct);
                _logger.LogDebug("IMAP connected successfully via hostname");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Primary IMAP connection failed to {Host}:{Port}, attempting IPv4 fallback", host, port);

                var addresses = await Dns.GetHostAddressesAsync(host, ct);
                var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

                if (ipv4 is null)
                {
                    _logger.LogError("No IPv4 address found for {Host}, cannot fallback", host);
                    throw;
                }

                resolvedIp = ipv4.ToString();
                _logger.LogDebug("Falling back to IPv4: {Ip}:{Port}", ipv4, port);

                client = CreateClient();
                fallbackSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await fallbackSocket.ConnectAsync(ipv4, port, ct);
                await client.ConnectAsync(fallbackSocket, host, port, socketOptions, ct);
                _logger.LogDebug("IMAP connected successfully via IPv4 fallback");
            }

            try
            {
                _logger.LogDebug("Authenticating as {Username} (AuthType: {AuthType})", username, authType);

                if (authType == ImapAuthType.OAuth2 && !string.IsNullOrEmpty(oauthAccessToken))
                {
                    var oauth2 = new SaslMechanismOAuth2(username, oauthAccessToken);
                    await client.AuthenticateAsync(oauth2, ct);
                }
                else
                {
                    await client.AuthenticateAsync(username, password ?? string.Empty, ct);
                }

                _logger.LogDebug("Authenticated, exploring folders...");

                var folders = await GetFolderInfoAsync(client, ct);
                _logger.LogDebug("Explored {Count} folders", folders.Count);

                await client.DisconnectAsync(true, ct);

                return new ImapExploreResult(true, null, host, resolvedIp, folders);
            }
            finally
            {
                client.Dispose();
                fallbackSocket?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("IMAP explore timed out after 60 seconds for {Host}", host);
            return new ImapExploreResult(false, "Connection timed out after 60 seconds.", host, null, []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IMAP explore failed for {Host}", host);
            return new ImapExploreResult(false, ex.Message, host, null, []);
        }
    }

    private static async Task<List<ImapFolderInfo>> GetFolderInfoAsync(ImapClient client, CancellationToken ct)
    {
        var result = new List<ImapFolderInfo>();
        var personal = client.PersonalNamespaces;

        if (personal.Count == 0)
            return result;

        var folders = await client.GetFoldersAsync(personal[0], cancellationToken: ct);

        foreach (var folder in folders)
        {
            try
            {
                await folder.OpenAsync(FolderAccess.ReadOnly, ct);

                var count = folder.Count;
                DateTime? firstDate = null;
                DateTime? lastDate = null;

                if (count > 0)
                {
                    // Fetch first message date
                    var firstSummary = await folder.FetchAsync(0, 0, MessageSummaryItems.InternalDate, ct);
                    if (firstSummary.Count > 0)
                        firstDate = firstSummary[0].InternalDate?.DateTime;

                    // Fetch last message date
                    if (count > 1)
                    {
                        var lastSummary = await folder.FetchAsync(count - 1, count - 1, MessageSummaryItems.InternalDate, ct);
                        if (lastSummary.Count > 0)
                            lastDate = lastSummary[0].InternalDate?.DateTime;
                    }
                    else
                    {
                        lastDate = firstDate;
                    }
                }

                await folder.CloseAsync(false, ct);

                result.Add(new ImapFolderInfo(
                    folder.FullName,
                    folder.Name,
                    count,
                    firstDate,
                    lastDate
                ));
            }
            catch
            {
                // Some folders can't be opened (e.g. \Noselect); skip them
            }
        }

        return result;
    }

    private static ImapClient CreateClient()
    {
        var client = new ImapClient();
        client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        client.CheckCertificateRevocation = false;
        client.SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                            | System.Security.Authentication.SslProtocols.Tls13;
        return client;
    }

    private static SecureSocketOptions MapEncryption(ImapEncryption encryption) => encryption switch
    {
        ImapEncryption.SSL => SecureSocketOptions.SslOnConnect,
        ImapEncryption.TLS => SecureSocketOptions.SslOnConnect,
        ImapEncryption.STARTTLS => SecureSocketOptions.StartTls,
        ImapEncryption.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };
}
