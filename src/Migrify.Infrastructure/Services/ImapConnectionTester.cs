using System.Net;
using System.Net.Sockets;
using MailKit.Net.Imap;
using MailKit.Security;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class ImapConnectionTester : IImapConnectionTester
{
    public Task<ConnectionTestResult> TestAsync(
        string host, int port, ImapEncryption encryption, string? username, string? password)
    {
        return TestAsync(host, port, encryption, ImapAuthType.Password, username, password, null);
    }

    public async Task<ConnectionTestResult> TestAsync(
        string host, int port, ImapEncryption encryption,
        ImapAuthType authType, string? username, string? password, string? oauthAccessToken)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            var socketOptions = MapEncryption(encryption);

            // Try normal hostname connection first (MailKit handles DNS + SSL/TLS properly)
            try
            {
                return await ConnectAndTestAsync(host, port, socketOptions, authType, username, password, oauthAccessToken, cts.Token);
            }
            catch (Exception) when (!cts.IsCancellationRequested)
            {
                // Fall back to forced IPv4 via raw socket (workaround for broken IPv6 on some providers)
                var addresses = await Dns.GetHostAddressesAsync(host, cts.Token);
                var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

                if (ipv4 is null)
                    throw; // no IPv4 available, re-throw original error

                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(ipv4, port, cts.Token);

                return await ConnectAndTestAsync(socket, host, port, socketOptions, authType, username, password, oauthAccessToken, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return new ConnectionTestResult(false, "Connection timed out after 10 seconds.");
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, ex.Message);
        }
    }

    private static async Task<ConnectionTestResult> ConnectAndTestAsync(
        string host, int port, SecureSocketOptions socketOptions,
        ImapAuthType authType, string? username, string? password, string? oauthAccessToken,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        await client.ConnectAsync(host, port, socketOptions, cancellationToken);
        return await AuthenticateAndDisconnectAsync(client, authType, username, password, oauthAccessToken, cancellationToken);
    }

    private static async Task<ConnectionTestResult> ConnectAndTestAsync(
        Socket socket, string host, int port, SecureSocketOptions socketOptions,
        ImapAuthType authType, string? username, string? password, string? oauthAccessToken,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        await client.ConnectAsync(socket, host, port, socketOptions, cancellationToken);
        return await AuthenticateAndDisconnectAsync(client, authType, username, password, oauthAccessToken, cancellationToken);
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

    private static async Task<ConnectionTestResult> AuthenticateAndDisconnectAsync(
        ImapClient client, ImapAuthType authType, string? username, string? password, string? oauthAccessToken,
        CancellationToken cancellationToken)
    {
        if (authType == ImapAuthType.OAuth2 && !string.IsNullOrEmpty(oauthAccessToken))
        {
            var oauth2 = new SaslMechanismOAuth2(username ?? string.Empty, oauthAccessToken);
            await client.AuthenticateAsync(oauth2, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            await client.AuthenticateAsync(username, password, cancellationToken);
        }

        await client.DisconnectAsync(true, cancellationToken);
        return new ConnectionTestResult(true);
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
