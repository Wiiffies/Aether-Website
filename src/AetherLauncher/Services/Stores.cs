using AetherLauncher.Infrastructure;
using AetherLauncher.Models;

namespace AetherLauncher.Services;





public sealed class AccountStore
{
    private readonly string _path;

    public AccountStore(string? path = null) => _path = path ?? AppPaths.AccountsFile;

    public List<AccountRecord> Accounts { get; private set; } = new();

    public void Load()
    {
        Accounts = JsonFile.Load(_path, () => new List<AccountRecord>());

        
        
        
        var migrated = false;
        foreach (var account in Accounts)
        {
            if (!Dpapi.NeedsUpgrade(account.CookieProtected)) continue;

            var cookie = Dpapi.Unprotect(account.CookieProtected);
            if (string.IsNullOrEmpty(cookie)) continue;

            account.CookieProtected = Dpapi.Protect(cookie);
            migrated = true;
        }

        if (migrated) Save();
    }

    public bool Save() => JsonFile.Save(_path, Accounts);

    
    public AccountRecord Upsert(string cookie, RobloxApi.AccountInfo info)
    {
        var existing = Accounts.FirstOrDefault(a => a.UserId == info.UserId);
        if (existing is not null)
        {
            existing.Username = info.Username;
            existing.DisplayName = info.DisplayName;
            existing.CookieProtected = Dpapi.Protect(cookie);
            existing.LastValidatedAt = DateTimeOffset.Now;
            Save();
            return existing;
        }

        var record = new AccountRecord
        {
            UserId = info.UserId,
            Username = info.Username,
            DisplayName = info.DisplayName,
            CookieProtected = Dpapi.Protect(cookie),
        };

        Accounts.Add(record);
        Save();
        return record;
    }

    public bool Remove(AccountRecord record)
    {
        var removed = Accounts.RemoveAll(a => a.Id == record.Id) > 0;
        if (removed) Save();
        return removed;
    }

    
    public string? GetCookie(AccountRecord record)
    {
        var cookie = Dpapi.Unprotect(record.CookieProtected);
        return string.IsNullOrWhiteSpace(cookie) ? null : cookie;
    }
}


public sealed class PlaceStore
{
    private readonly string _path;

    public PlaceStore(string? path = null) => _path = path ?? AppPaths.PlacesFile;

    public List<PlaceRecord> Places { get; private set; } = new();

    public void Load() => Places = JsonFile.Load(_path, () => new List<PlaceRecord>());

    public bool Save() => JsonFile.Save(_path, Places);

    public PlaceRecord? Find(long placeId) => Places.FirstOrDefault(p => p.PlaceId == placeId);

    public PlaceRecord Add(long placeId, string? accessCode, string name)
    {
        var existing = Find(placeId);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(accessCode)) existing.AccessCode = accessCode;
            if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
            Save();
            return existing;
        }

        var record = new PlaceRecord { PlaceId = placeId, AccessCode = accessCode, Name = name };
        Places.Insert(0, record);
        Save();
        return record;
    }

    public bool Remove(PlaceRecord record)
    {
        var removed = Places.RemoveAll(p => p.Id == record.Id) > 0;
        if (removed) Save();
        return removed;
    }

    
    public bool SetPinned(PlaceRecord record, bool pinned)
    {
        record.IsPinned = pinned;
        record.PinnedAt = pinned ? DateTimeOffset.Now : null;
        return Save();
    }

    
    public bool SetOrder(IEnumerable<PlaceRecord> records)
    {
        var index = 0;
        foreach (var record in records) record.SortIndex = index++;
        return Save();
    }
}





public sealed class RecentStore
{
    
    public const int Capacity = 24;

    private readonly string _path;

    public RecentStore(string? path = null) => _path = path ?? AppPaths.RecentFile;

    public List<RecentRecord> Entries { get; private set; } = new();

    public void Load() => Entries = JsonFile.Load(_path, () => new List<RecentRecord>());

    public bool Save() => JsonFile.Save(_path, Entries);

    public RecentRecord? Find(long placeId) => Entries.FirstOrDefault(e => e.PlaceId == placeId);

    
    
    
    
    public RecentRecord Record(long placeId, string? accessCode, string? name, long? universeId, string? accountName)
    {
        var existing = Find(placeId);
        if (existing is not null) Entries.Remove(existing);

        var record = new RecentRecord
        {
            PlaceId = placeId,
            AccessCode = accessCode ?? existing?.AccessCode,
            UniverseId = universeId ?? existing?.UniverseId,
            Name = string.IsNullOrWhiteSpace(name) ? existing?.Name ?? "" : name!,
            AccountName = accountName,
            Sessions = (existing?.Sessions ?? 0) + 1,
            PlayedAt = DateTimeOffset.Now,
        };

        Entries.Insert(0, record);

        while (Entries.Count > Capacity) Entries.RemoveAt(Entries.Count - 1);

        Save();
        return record;
    }

    
    public bool Forget(long placeId)
    {
        var removed = Entries.RemoveAll(e => e.PlaceId == placeId) > 0;
        if (removed) Save();
        return removed;
    }

    
    
    
    
    public bool Import(long placeId, long? universeId, DateTimeOffset playedAt)
    {
        if (placeId <= 0 || Find(placeId) is not null) return false;

        Entries.Add(new RecentRecord
        {
            PlaceId = placeId,
            UniverseId = universeId,
            PlayedAt = playedAt,
            FromLog = true,
        });

        Entries.Sort((a, b) => b.PlayedAt.CompareTo(a.PlayedAt));
        while (Entries.Count > Capacity) Entries.RemoveAt(Entries.Count - 1);

        Save();
        return true;
    }

    
    
    
    
    
    
    public bool Update(long placeId, string? name, long universeId)
    {
        var entry = Find(placeId);
        if (entry is null) return false;

        var changed = false;

        if (!string.IsNullOrWhiteSpace(name) && entry.Name != name)
        {
            entry.Name = name!;
            changed = true;
        }

        if (universeId > 0 && entry.UniverseId != universeId)
        {
            entry.UniverseId = universeId;
            changed = true;
        }

        if (changed) Save();
        return changed;
    }

    public bool Clear()
    {
        if (Entries.Count == 0) return false;
        Entries.Clear();
        Save();
        return true;
    }
}


public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string? path = null) => _path = path ?? AppPaths.SettingsFile;

    private const int CurrentRevision = 2;

    public LauncherSettings Load()
    {
        var settings = JsonFile.Load(_path, () => new LauncherSettings());

        
        
        if (settings.SettingsRevision < CurrentRevision)
        {
            settings.GameOverlayAutoShow = false;
            settings.SettingsRevision = CurrentRevision;
            Save(settings);
        }

        return settings;
    }

    public bool Save(LauncherSettings settings) => JsonFile.Save(_path, settings);
}
