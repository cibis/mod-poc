namespace Mod.Collector.Identity;

// Injects X-Collector-Id on every outbound HTTP request so platform APIs (ingest, management)
// can identify the calling collector from the header after CA-chain cert validation.
// PoC: header is trusted because the cert proves the caller is an authorised collector.
// Production: collectorId would be embedded in the per-collector cert CN; no header needed.
internal sealed class CollectorIdHandler(IdentityStore identity) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(identity.CollectorId))
            request.Headers.TryAddWithoutValidation("X-Collector-Id", identity.CollectorId);
        return base.SendAsync(request, ct);
    }
}
