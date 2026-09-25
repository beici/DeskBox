using System.Runtime.InteropServices;
using DeskBox.Services;
using Windows.Security.Credentials;

namespace DeskBox.Tests;

/// <summary>
/// FCFG-04: PasswordVault.Add throws when the resource/key pair already
/// exists, so SetSecretAsync must express an update as retrieve-then-remove-
/// then-add. The real WinRT vault is never touched here — an in-memory vault
/// that mirrors the Add-throws-on-duplicate / Retrieve-throws-when-absent
/// contract stands in for it.
/// </summary>
public sealed class PasswordVaultCredentialStoreTests
{
    [Fact]
    public async Task SetSecretAsync_UpdatesExistingCredentialInsteadOfThrowing()
    {
        var vault = new InMemoryVault();
        var store = new PasswordVaultCredentialStore(vault);

        await store.SetSecretAsync("webdav:alice", "first-secret");
        // Pre-fix behavior: the second Add on the same key threw COMException.
        await store.SetSecretAsync("webdav:alice", "second-secret");

        Assert.Equal("second-secret", await store.GetSecretAsync("webdav:alice"));
        Assert.Equal(2, vault.AddCalls);
        Assert.Equal(1, vault.RemoveCalls);
        Assert.Equal(new[] { "webdav:alice" }, await store.ListKeysAsync());
    }

    [Fact]
    public async Task SetSecretAsync_FirstWriteTakesThePlainAddPath()
    {
        var vault = new InMemoryVault();
        var store = new PasswordVaultCredentialStore(vault);

        await store.SetSecretAsync("webdav:alice", "secret");

        Assert.Equal("secret", await store.GetSecretAsync("webdav:alice"));
        Assert.Equal(1, vault.AddCalls);
        Assert.Equal(0, vault.RemoveCalls);
    }

    [Fact]
    public async Task SetSecretAsync_StillPropagatesVaultFailures()
    {
        var store = new PasswordVaultCredentialStore(new ThrowingVault());

        await Assert.ThrowsAsync<COMException>(
            () => store.SetSecretAsync("webdav:alice", "secret"));
    }

    /// <summary>In-memory vault mirroring the real PasswordVault contracts.</summary>
    private sealed class InMemoryVault : PasswordVaultCredentialStore.IVault
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public int AddCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public PasswordCredential Retrieve(string resource, string userName)
        {
            if (!_secrets.TryGetValue(userName, out string? password))
            {
                // PasswordVault.Retrieve throws (not null) when the key is absent.
                throw new COMException($"Element not found: {userName}");
            }

            return new PasswordCredential(resource, userName, password);
        }

        public IReadOnlyList<PasswordCredential> RetrieveAll() =>
            _secrets.Select(pair => new PasswordCredential(
                "DeskBox/CloudBackup", pair.Key, pair.Value)).ToList();

        public void Add(PasswordCredential credential)
        {
            if (!_secrets.TryAdd(credential.UserName, credential.Password))
            {
                // PasswordVault.Add throws when the credential already exists.
                throw new COMException($"Credential already exists: {credential.UserName}");
            }

            AddCalls++;
        }

        public void Remove(PasswordCredential credential)
        {
            RemoveCalls++;
            _secrets.Remove(credential.UserName);
        }
    }

    private sealed class ThrowingVault : PasswordVaultCredentialStore.IVault
    {
        public PasswordCredential Retrieve(string resource, string userName) =>
            throw new COMException("vault unavailable");

        public IReadOnlyList<PasswordCredential> RetrieveAll() =>
            throw new COMException("vault unavailable");

        public void Add(PasswordCredential credential) =>
            throw new COMException("vault unavailable");

        public void Remove(PasswordCredential credential) =>
            throw new COMException("vault unavailable");
    }
}
