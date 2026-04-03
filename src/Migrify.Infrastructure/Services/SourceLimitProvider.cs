using Migrify.Core.Entities;

namespace Migrify.Infrastructure.Services;

public class SourceLimitProvider
{
    private const int DefaultUnknownLimit = 3;
    private const int GoogleWorkspaceLimit = 15;

    private static readonly Dictionary<string, int> KnownProviderLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["imap.gmail.com"] = 15,
        ["imap.googlemail.com"] = 15,
        ["outlook.office365.com"] = 10,
        ["imap.mail.yahoo.com"] = 5,
        ["imap.fastmail.com"] = 10,
        ["imap.mail.me.com"] = 10,
        ["imap.zoho.com"] = 5,
        ["imap.zoho.eu"] = 5,
        ["imap.one.com"] = 5,
        ["imap.gmx.net"] = 5,
        ["imap.web.de"] = 5,
    };

    public int GetSourceLimit(string sourceKey, SourceConnectorType sourceType)
    {
        if (sourceType == SourceConnectorType.GoogleWorkspace)
            return GoogleWorkspaceLimit;

        if (KnownProviderLimits.TryGetValue(sourceKey, out var limit))
            return limit;

        return DefaultUnknownLimit;
    }

    public bool IsKnownProvider(string sourceKey, SourceConnectorType sourceType)
    {
        return sourceType == SourceConnectorType.GoogleWorkspace
            || KnownProviderLimits.ContainsKey(sourceKey);
    }
}
