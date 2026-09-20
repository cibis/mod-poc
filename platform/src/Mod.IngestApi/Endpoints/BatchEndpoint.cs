using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Threading.RateLimiting;
using Mod.Platform.Common.Certificates;
using Mod.IngestApi.EventHub;
using Mod.IngestApi.Validation;

namespace Mod.IngestApi.Endpoints;

public static class BatchEndpoint
{
    private static readonly Meter _meter = new("Mod.IngestApi");
    private static readonly Counter<long> _batches = _meter.CreateCounter<long>("mod.ingest.batches");
    private static readonly Counter<long> _events = _meter.CreateCounter<long>("mod.ingest.events");
    private static readonly Histogram<double> _duration = _meter.CreateHistogram<double>("mod.ingest.duration", "ms");

    public record Options(int MaxEvents, int MaxBytes);

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        CertificateValidator certValidator,
        PartitionedRateLimiter<string> rateLimiter,
        BatchPublisher publisher,
        Options options,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var log = loggerFactory.CreateLogger("Mod.IngestApi.Batch");

        Guid? collectorId = null;
        Guid? tenantId = null;
        Guid? batchId = null;
        var eventCount = 0;
        var bytes = 0;
        var outcome = "unknown";

        try
        {
            var request = context.Request;

            // 1. Reject non-gzip or non-JSON
            var contentType = request.ContentType ?? string.Empty;
            var contentEncoding = request.Headers.ContentEncoding.ToString();
            if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(contentEncoding, "gzip", StringComparison.OrdinalIgnoreCase))
            {
                outcome = "rejected_content_type";
                return Results.StatusCode(415);
            }

            // 2. Extract and validate client certificate
            var certHeader = request.Headers["X-Forwarded-Client-Cert"].ToString();
            var cert = ClientCertificateParser.Parse(certHeader);
            if (cert is null)
            {
                outcome = "rejected_no_cert";
                return Results.StatusCode(401);
            }

            CertValidationResult certResult;
            try
            {
                certResult = await certValidator.ValidateAsync(cert, ct);
            }
            finally
            {
                cert.Dispose();
            }

            if (!certResult.IsSuccess)
            {
                outcome = certResult.Error == CertValidationError.ForbiddenAccess
                    ? "rejected_forbidden"
                    : "rejected_invalid_cert";
                return certResult.Error == CertValidationError.ForbiddenAccess
                    ? Results.StatusCode(403)
                    : Results.StatusCode(401);
            }

            var identity = certResult.Identity!;
            collectorId = identity.CollectorId;
            tenantId = identity.TenantId;

            // 3. Apply per-collector rate limiter
            using var lease = rateLimiter.AttemptAcquire(identity.CollectorId.ToString("D"));
            if (!lease.IsAcquired)
            {
                var retryAfter = 1;
                if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterSpan))
                    retryAfter = Math.Max(1, (int)Math.Ceiling(retryAfterSpan.TotalSeconds));
                context.Response.Headers.RetryAfter = retryAfter.ToString();
                outcome = "rate_limited";
                return Results.StatusCode(429);
            }

            // 4. Read body; decompress with streaming limit (zip-bomb safe)
            using var compressedMs = new MemoryStream();
            await request.Body.CopyToAsync(compressedMs, ct);
            var compressedBytes = compressedMs.ToArray();
            bytes = compressedBytes.Length;

            byte[] decompressedBytes;
            try
            {
                using var decompMs = new MemoryStream();
                using (var gzip = new GZipStream(new MemoryStream(compressedBytes), CompressionMode.Decompress))
                {
                    var buf = new byte[4096];
                    int read;
                    var total = 0;
                    while ((read = await gzip.ReadAsync(buf, ct)) > 0)
                    {
                        total += read;
                        if (total > options.MaxBytes)
                        {
                            outcome = "rejected_too_large";
                            return Results.StatusCode(413);
                        }
                        decompMs.Write(buf, 0, read);
                    }
                }
                decompressedBytes = decompMs.ToArray();
            }
            catch (InvalidDataException)
            {
                outcome = "rejected_invalid_gzip";
                return Results.Problem(statusCode: 400, type: "EnvelopeInvalid",
                    detail: "Request body is not valid gzip");
            }

            // 5. Validate envelope
            var validation = EnvelopeValidator.Validate(decompressedBytes, identity.CollectorId, options.MaxEvents);
            if (!validation.IsSuccess)
            {
                outcome = $"rejected_{validation.ErrorCode}";
                if (validation.ErrorCode == "EnvelopeInvalid")
                {
                    return Results.Problem(
                        statusCode: 400,
                        type: "EnvelopeInvalid",
                        extensions: new Dictionary<string, object?> { ["errors"] = validation.Errors });
                }
                return Results.Problem(statusCode: 400, type: validation.ErrorCode);
            }

            batchId = validation.BatchId;
            eventCount = validation.EventCount;
            var receivedAt = DateTimeOffset.UtcNow;

            // 7. Publish to Event Hubs (10 s timeout inside BatchPublisher)
            try
            {
                await publisher.SendAsync(compressedBytes, identity, validation.BatchId,
                    validation.EventCount, receivedAt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Event Hubs publish failed for collector {CollectorId}", collectorId);
                context.Response.Headers.RetryAfter = "5";
                outcome = "eventhub_failure";
                return Results.Problem(statusCode: 503, type: "EventHubUnavailable");
            }

            // 8. 202 Accepted
            outcome = "accepted";
            _events.Add(eventCount);
            return Results.Json(new { batchId, acceptedEventCount = eventCount }, statusCode: 202);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _batches.Add(1, new TagList { { "outcome", outcome } });
            _duration.Record(elapsedMs);
            log.LogInformation(
                "Batch {Outcome}: collectorId={CollectorId} tenantId={TenantId} batchId={BatchId} " +
                "eventCount={EventCount} bytes={Bytes} durationMs={DurationMs}",
                outcome, collectorId, tenantId, batchId, eventCount, bytes, elapsedMs);
        }
    }
}
