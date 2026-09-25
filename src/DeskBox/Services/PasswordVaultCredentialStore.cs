using Windows.Security.Credentials;

namespace DeskBox.Services;

/// <summary>
/// Credential store backed by the Windows Credential Locker
/// (<see cref="PasswordVault"/>) — the OS-managed secret surface available
/// to a packaged MSIX app. Secrets are keyed per provider so the same
/// store can later carry the official-cloud session token as well.
/// </summary>
internal sealed class PasswordVaultCredentialStore : ICredentialStore
{
    private const string Resource = "DeskBox/CloudBackup";

    /// <summary>
    /// The PasswordVault surface this store uses. Production resolves to the
    /// real WinRT vault; tests substitute an in-memory vault that mirrors the
    /// Add-throws-on-duplicate contract the update path below must handle.
    /// </summary>
    internal interface IVault
    {
        PasswordCredential Retrieve(string resource, string userName);

        IReadOnlyList<PasswordCredential> RetrieveAll();

        void Add(PasswordCredential credential);

        void Remove(PasswordCredential credential);
    }

    private readonly IVault _vault;

    public PasswordVaultCredentialStore()
        : this(new SystemVault())
    {
    }

    internal PasswordVaultCredentialStore(IVault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
    }

    /// <summary>Thin adapter over the real WinRT vault.</summary>
    private sealed class SystemVault : IVault
    {
        private readonly PasswordVault _vault = new();

        public PasswordCredential Retrieve(string resource, string userName) =>
            _vault.Retrieve(resource, userName);

        public IReadOnlyList<PasswordCredential> RetrieveAll() => _vault.RetrieveAll();

        public void Add(PasswordCredential credential) => _vault.Add(credential);

        public void Remove(PasswordCredential credential) => _vault.Remove(credential);
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            PasswordCredential credential = _vault.Retrieve(Resource, key);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException ||
                                     ex is System.IO.FileNotFoundException)
        {
            // PasswordVault.Retrieve throws (not null) when the key is absent.
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);

        // PasswordVault.Add throws when the resource/key pair already exists,
        // so an update has to be expressed as remove-then-add. Retrieve
        // (which throws on absence — same filter as GetSecretAsync) locates
        // the existing credential so Remove retires exactly that entry; the
        // not-found case falls through to a plain first-time Add. Failures
        // other than "absent" still propagate unchanged.
        try
        {
            _vault.Remove(_vault.Retrieve(Resource, key));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException ||
                                     ex is System.IO.FileNotFoundException)
        {
            // Absent key: nothing to retire before the Add.
        }

        _vault.Add(new PasswordCredential(Resource, key, secret));
        return Task.CompletedTask;
    }

    public Task RemoveSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            _vault.Remove(new PasswordCredential(Resource, key, string.Empty));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException ||
                                     ex is System.IO.FileNotFoundException)
        {
            // Removing an absent key is a no-op.
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<string> keys = _vault
                .RetrieveAll()
                .Where(credential => string.Equals(
                    credential.Resource, Resource, StringComparison.Ordinal))
                .Select(credential => credential.UserName)
                .ToList();
            return Task.FromResult(keys);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException ||
                                     ex is System.IO.FileNotFoundException)
        {
            // An empty vault throws on RetrieveAll.
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }
}
