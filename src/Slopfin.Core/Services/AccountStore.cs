using System.Text.Json;
using Slopfin.Core.Json;
using Slopfin.Core.Models;
using Slopfin.Core.Platform;

namespace Slopfin.Core.Services;

/// <summary>
/// Remembered accounts. The whole vault (server URLs, user ids, access tokens) is kept in the
/// platform secure store. Passwords are never stored: Jellyfin issues a long-lived access token
/// on first sign-in and that token is what enables auto-login.
/// </summary>
public sealed class AccountStore
{
    private const string Key = "accounts.v1";
    private readonly ISecureStore _secure;
    private AccountVault _vault = new();

    public AccountStore(ISecureStore secure) => _secure = secure;

    public IReadOnlyList<SavedAccount> Accounts => _vault.Accounts.OrderByDescending(a => a.LastUsed).ToList();

    public async Task LoadAsync()
    {
        try
        {
            var json = await _secure.GetAsync(Key);
            _vault = string.IsNullOrEmpty(json) ? new() : JsonSerializer.Deserialize(json, LocalJson.Default.AccountVault) ?? new();
        }
        catch
        {
            _vault = new();
        }
    }

    public SavedAccount? Find(string? id) => id == null ? null : _vault.Accounts.FirstOrDefault(a => a.Id == id);

    public SavedAccount? FindByUser(string serverId, string userId) =>
        _vault.Accounts.FirstOrDefault(a => a.ServerId == serverId && a.UserId == userId);

    public async Task UpsertAsync(SavedAccount account)
    {
        _vault.Accounts.RemoveAll(a => a.Id == account.Id);
        _vault.Accounts.Add(account);
        await PersistAsync();
    }

    public async Task RemoveAsync(string id)
    {
        _vault.Accounts.RemoveAll(a => a.Id == id);
        await PersistAsync();
    }

    private Task PersistAsync() =>
        _secure.SetAsync(Key, JsonSerializer.Serialize(_vault, LocalJson.Default.AccountVault));
}
