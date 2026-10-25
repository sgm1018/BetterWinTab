using Windows.Security.Credentials;

namespace BetterWinTab.Services.Calls;

/// <summary>Stores provider API keys in the current user's Windows Credential Locker.</summary>
public sealed class CredentialStore
{
    private const string ResourcePrefix = "BetterWinTab.AI.";

    public void SetApiKey(string providerId, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var vault = new PasswordVault();
        var resource = GetResource(providerId);
        RemoveExisting(vault, resource);
        vault.Add(new PasswordCredential(resource, "api-key", apiKey));
    }

    public string? GetApiKey(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        try
        {
            var credential = new PasswordVault().Retrieve(GetResource(providerId), "api-key");
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public void RemoveApiKey(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        RemoveExisting(new PasswordVault(), GetResource(providerId));
    }

    private static string GetResource(string providerId) => ResourcePrefix + providerId.Trim().ToLowerInvariant();

    private static void RemoveExisting(PasswordVault vault, string resource)
    {
        try
        {
            vault.Remove(vault.Retrieve(resource, "api-key"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No prior credential exists.
        }
    }
}
