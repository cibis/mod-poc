using System.Text.Json;

namespace Mod.Collector.Identity;

internal sealed class EnrolmentClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IdentityStore _identity;
    private readonly CollectorOptions _options;
    private readonly ILogger<EnrolmentClient> _logger;

    public EnrolmentClient(
        IHttpClientFactory httpFactory,
        IdentityStore identity,
        CollectorOptions options,
        ILogger<EnrolmentClient> logger)
    {
        _httpFactory = httpFactory;
        _identity = identity;
        _options = options;
        _logger = logger;
    }

    // Posts /v1/enrol, saves identity, and returns the raw config JSON from the response.
    // Throws on all failures; caller retries as needed.
    public async Task<string> EnrolAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.EnrolmentToken))
            throw new InvalidOperationException("ENROLMENT_TOKEN is not set");

        var csrPem = _identity.GetCsrPem();
        var requestBody = JsonSerializer.Serialize(new
        {
            enrolmentToken = _options.EnrolmentToken,
            csrPem,
            softwareVersion = _options.CollectorSoftwareVersion,
        });

        var client = _httpFactory.CreateClient("noClientCert");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.ManagementUrl.TrimEnd('/')}/v1/enrol")
        {
            Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Enrolment failed: {(int)response.StatusCode} — {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        _identity.SaveIdentity(
            collectorId: root.GetProperty("collectorId").GetString()!,
            tenantId: root.GetProperty("tenantId").GetString()!,
            siteId: root.GetProperty("siteId").GetString()!,
            certPem: root.GetProperty("certificatePem").GetString()!,
            caCertPem: root.GetProperty("caCertificatePem").GetString()!,
            expiresAt: root.GetProperty("certificateExpiresAt").GetDateTimeOffset());

        _logger.LogInformation("Enrolled as {CollectorId}", _identity.CollectorId);
        return root.GetProperty("config").GetRawText();
    }

    // Renews the certificate via the authenticated client (uses the client cert).
    public async Task<bool> TryRenewAsync(string managementUrl, CancellationToken ct)
    {
        try
        {
            var csrPem = _identity.GetCsrPem();
            var body = JsonSerializer.Serialize(new { csrPem });

            var client = _httpFactory.CreateClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{managementUrl.TrimEnd('/')}/v1/certificate/renew")
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Certificate renewal returned {Status}", response.StatusCode);
                return false;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            _identity.UpdateCertificate(
                root.GetProperty("certificatePem").GetString()!,
                root.GetProperty("certificateExpiresAt").GetDateTimeOffset());

            _logger.LogInformation("Certificate renewed, expires {At}", _identity.CertExpiresAt);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Certificate renewal failed");
            return false;
        }
    }
}
