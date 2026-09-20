// Azure.Core 1.60+ defines credential types; Azure.Identity 1.11+ type-forwards them back.
// Azure.Identity is aliased in the csproj to remove it from the global namespace,
// leaving only Azure.Core's definitions and resolving the CS0433 ambiguity.
using Azure.Identity;

namespace Mod.PortalApi.Infrastructure;

internal static class CredentialFactory
{
    internal static Azure.Core.TokenCredential CreateDefault()
    {
        var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        return clientId != null
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
            : new AzureCliCredential();
    }
}
