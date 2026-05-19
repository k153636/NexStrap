using Newtonsoft.Json;
using NexStrap.Core.Models;

namespace NexStrap.Core.Services;

public class ProfileService
{
    private readonly string _profilesDir;
    private List<Profile> _profiles = new();

    public IReadOnlyList<Profile> Profiles => _profiles.AsReadOnly();
    public event EventHandler? ProfilesChanged;

    public ProfileService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _profilesDir = Path.Combine(appData, "NexStrap", "Profiles");
        Directory.CreateDirectory(_profilesDir);
        LoadProfiles();
        EnsureDefaultProfile();
    }

    private string ProfilesFile => Path.Combine(_profilesDir, "profiles.json");

    public void LoadProfiles()
    {
        if (!File.Exists(ProfilesFile)) { _profiles = new(); return; }
        try
        {
            var json = File.ReadAllText(ProfilesFile);
            _profiles = JsonConvert.DeserializeObject<List<Profile>>(json) ?? new();
        }
        catch { _profiles = new(); }
    }

    public void SaveProfiles()
    {
        var json = JsonConvert.SerializeObject(_profiles, Formatting.Indented);
        File.WriteAllText(ProfilesFile, json);
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public Profile CreateProfile(string name, string description = "")
    {
        var profile = new Profile { Name = name, Description = description };
        _profiles.Add(profile);
        SaveProfiles();
        return profile;
    }

    public void UpdateProfile(Profile profile)
    {
        var idx = _profiles.FindIndex(p => p.Id == profile.Id);
        if (idx < 0) return;
        profile.UpdatedAt = DateTime.UtcNow;
        _profiles[idx] = profile;
        SaveProfiles();
    }

    public void DeleteProfile(string profileId)
    {
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null || profile.IsDefault) return;
        _profiles.Remove(profile);
        SaveProfiles();
    }

    public Profile? GetProfile(string profileId) =>
        _profiles.FirstOrDefault(p => p.Id == profileId);

    public Profile? GetDefaultProfile() =>
        _profiles.FirstOrDefault(p => p.IsDefault) ?? _profiles.FirstOrDefault();

    private void EnsureDefaultProfile()
    {
        if (_profiles.Any(p => p.IsDefault)) return;

        var defaultProfile = new Profile
        {
            Name = "デフォルト",
            Description = "標準プロファイル",
            IsDefault = true,
            FastFlags = FastFlagPresets.All.ToList()
        };
        _profiles.Insert(0, defaultProfile);
        SaveProfiles();
    }
}
