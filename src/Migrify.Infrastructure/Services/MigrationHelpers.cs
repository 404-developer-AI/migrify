using System.Net.Sockets;
using Google.Apis.Auth.OAuth2;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Services;

public static class MigrationHelpers
{
    public const int MaxMimeSize = 4 * 1024 * 1024; // 4MB Graph API limit

    public static ImapClient CreateImapClient()
    {
        var client = new ImapClient();
        client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        client.CheckCertificateRevocation = false;
        client.Timeout = 120_000; // 2 minute I/O timeout — prevents hangs when server drops connection
        client.SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                            | System.Security.Authentication.SslProtocols.Tls13;
        return client;
    }

    public static SecureSocketOptions MapEncryption(ImapEncryption encryption) => encryption switch
    {
        ImapEncryption.SSL => SecureSocketOptions.SslOnConnect,
        ImapEncryption.TLS => SecureSocketOptions.SslOnConnect,
        ImapEncryption.STARTTLS => SecureSocketOptions.StartTls,
        ImapEncryption.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };

    public static async Task ConnectAndAuthenticate(
        ImapClient client, MigrationJob job, Project project,
        ICredentialEncryptor encryptor, IImapOAuthCredentialProvider oauthProvider,
        ILogger logger, CancellationToken ct)
    {
        if (project.SourceConnectorType == SourceConnectorType.GoogleWorkspace && !job.HasImapOverride)
        {
            var gws = project.GoogleWorkspaceSettings!;
            var privateKey = encryptor.Decrypt(gws.EncryptedPrivateKey!);
            var credential = new ServiceAccountCredential(
                new ServiceAccountCredential.Initializer(gws.ServiceAccountEmail)
                {
                    Scopes = ["https://mail.google.com/"],
                    User = job.SourceEmail
                }.FromPrivateKey(privateKey));

            await credential.RequestAccessTokenAsync(ct);
            var accessToken = credential.Token.AccessToken;

            await ConnectImapClient(client, "imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, logger, ct);

            var oauth2 = new SaslMechanismOAuth2(job.SourceEmail, accessToken);
            await client.AuthenticateAsync(oauth2, ct);
        }
        else
        {
            var imap = job.ImapSettings!;
            var socketOptions = MapEncryption(imap.Encryption);

            await ConnectImapClient(client, imap.Host, imap.Port, socketOptions, logger, ct);

            if (imap.AuthType == ImapAuthType.OAuth2)
            {
                var accessToken = await oauthProvider.GetAccessTokenAsync(imap);
                var oauth2 = new SaslMechanismOAuth2(imap.Username ?? string.Empty, accessToken);
                await client.AuthenticateAsync(oauth2, ct);
            }
            else
            {
                var password = encryptor.Decrypt(imap.EncryptedPassword!);
                await client.AuthenticateAsync(imap.Username ?? string.Empty, password, ct);
            }
        }
    }

    public static async Task ConnectImapClient(
        ImapClient client, string host, int port, SecureSocketOptions socketOptions,
        ILogger logger, CancellationToken ct)
    {
        try
        {
            await client.ConnectAsync(host, port, socketOptions, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Primary IMAP connection failed to {Host}:{Port}, attempting IPv4 fallback", host, port);

            var addresses = await System.Net.Dns.GetHostAddressesAsync(host, ct);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new InvalidOperationException($"No IPv4 address found for {host}");

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(ipv4, port, ct);
            await client.ConnectAsync(socket, host, port, socketOptions, ct);
        }
    }

    public static Dictionary<string, object?> ConvertToGraphMessage(MimeKit.MimeMessage mime)
    {
        var msg = new Dictionary<string, object?>
        {
            ["subject"] = mime.Subject ?? "(no subject)",
            ["internetMessageId"] = mime.MessageId,
        };

        // Body
        var htmlBody = mime.HtmlBody;
        var textBody = mime.TextBody;
        if (!string.IsNullOrEmpty(htmlBody))
            msg["body"] = new { contentType = "html", content = htmlBody };
        else if (!string.IsNullOrEmpty(textBody))
            msg["body"] = new { contentType = "text", content = textBody };

        // Dates
        if (mime.Date != DateTimeOffset.MinValue)
        {
            msg["sentDateTime"] = mime.Date.UtcDateTime.ToString("o");
            msg["receivedDateTime"] = mime.Date.UtcDateTime.ToString("o");
        }

        // From
        if (mime.From.Mailboxes.Any())
        {
            var from = mime.From.Mailboxes.First();
            msg["from"] = new { emailAddress = new { name = from.Name ?? from.Address, address = from.Address } };
            msg["sender"] = msg["from"];
        }

        // To
        if (mime.To.Mailboxes.Any())
            msg["toRecipients"] = mime.To.Mailboxes.Select(m =>
                new { emailAddress = new { name = m.Name ?? m.Address, address = m.Address } }).ToArray();

        // Cc
        if (mime.Cc.Mailboxes.Any())
            msg["ccRecipients"] = mime.Cc.Mailboxes.Select(m =>
                new { emailAddress = new { name = m.Name ?? m.Address, address = m.Address } }).ToArray();

        // Bcc
        if (mime.Bcc.Mailboxes.Any())
            msg["bccRecipients"] = mime.Bcc.Mailboxes.Select(m =>
                new { emailAddress = new { name = m.Name ?? m.Address, address = m.Address } }).ToArray();

        // Importance
        msg["importance"] = mime.Importance switch
        {
            MimeKit.MessageImportance.High => "high",
            MimeKit.MessageImportance.Low => "low",
            _ => "normal"
        };

        // MAPI extended properties
        var extendedProps = new List<object>
        {
            new { id = "Integer 0x0E07", value = "1" }
        };

        if (mime.Date != DateTimeOffset.MinValue)
        {
            var isoDate = mime.Date.UtcDateTime.ToString("o");
            extendedProps.Add(new { id = "SystemTime 0x0039", value = isoDate });
            extendedProps.Add(new { id = "SystemTime 0x0E06", value = isoDate });
        }

        msg["singleValueExtendedProperties"] = extendedProps;

        // Attachments
        var attachments = new List<Dictionary<string, object?>>();
        foreach (var attachment in mime.Attachments)
        {
            if (attachment is MimeKit.MimePart part)
            {
                using var ms = new MemoryStream();
                part.Content.DecodeTo(ms);
                var bytes = ms.ToArray();

                attachments.Add(new Dictionary<string, object?>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = part.FileName ?? "attachment",
                    ["contentType"] = part.ContentType.MimeType,
                    ["contentBytes"] = Convert.ToBase64String(bytes)
                });
            }
        }

        if (attachments.Count > 0)
            msg["attachments"] = attachments;

        return msg;
    }
}
