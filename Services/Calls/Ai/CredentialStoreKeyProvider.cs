using BetterWinTab.Services.Calls;

namespace BetterWinTab.Services.Calls.Ai;

/// <summary>Adapts the Windows Credential Locker store to the AI credential interface.</summary>
public sealed class CredentialStoreKeyProvider(CredentialStore credentialStore) : IApiKeyProvider
{
    private readonly CredentialStore _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));

    public string? GetApiKey(string providerId) => _credentialStore.GetApiKey(providerId);
}
