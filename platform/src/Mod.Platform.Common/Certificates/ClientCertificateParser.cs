using System.Security.Cryptography.X509Certificates;

namespace Mod.Platform.Common.Certificates;

/// <summary>
/// Parses the X-Forwarded-Client-Cert header injected by Azure Container Apps.
/// Format: Hash=&lt;sha256&gt;;Cert=&lt;url-encoded-pem&gt;;Chain=&lt;url-encoded-pem&gt;
/// </summary>
public static class ClientCertificateParser
{
    public static X509Certificate2? Parse(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return null;

        foreach (var segment in headerValue.Split(';'))
        {
            var eq = segment.IndexOf('=');
            if (eq < 0) continue;

            var key = segment[..eq].Trim();
            if (!key.Equals("Cert", StringComparison.OrdinalIgnoreCase))
                continue;

            var encoded = segment[(eq + 1)..].Trim().Trim('"');
            try
            {
                var pem = Uri.UnescapeDataString(encoded);
                return X509Certificate2.CreateFromPem(pem);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public static string ComputeThumbprint(X509Certificate2 cert)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(cert.RawData);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
