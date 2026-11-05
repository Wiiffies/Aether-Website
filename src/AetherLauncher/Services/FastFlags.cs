using System.Text.Json;
using System.Text.RegularExpressions;
using AetherLauncher.Infrastructure;
using AetherLauncher.Models;

namespace AetherLauncher.Services;


public sealed record FastFlagPreset(
    string Name,
    string Description,
    string Group,
    IReadOnlyList<KeyValuePair<string, string>> Flags)
{
    public FastFlagPreset(string name, string description, string group, string key, string value)
        : this(name, description, group, new[] { new KeyValuePair<string, string>(key, value) })
    {
    }







    public bool Allowlisted => Flags.Count > 0 && Flags.All(flag => FastFlagAllowlist.Contains(flag.Key));


    public string Keys => string.Join(", ", Flags.Select(f => f.Key));
}








public static class FastFlags
{
    private const string FileName = "ClientAppSettings.json";
    private const string BackupSuffix = ".aetherbak";


    public const string FrameRateFlag = "DFIntTaskSchedulerTargetFps";


    public const int UncappedFrameRate = 10000;


    public static IReadOnlyList<(string Label, int Value)> FrameRateCaps { get; } = new[]
    {
        ("Uncapped", UncappedFrameRate),
        ("60", 60),
        ("120", 120),
        ("144", 144),
        ("240", 240),
    };

    public const string FrameRateGroup = "FRAME RATE";
    public const string GraphicsGroup = "GRAPHICS";
    public const string SkyGroup = "SKY & LIGHTING";
    public const string RenderGroup = "EFFECTS & RENDERER";
    public const string DetailGroup = "TEXTURES & DETAIL";
    public const string InterfaceGroup = "INTERFACE";
    public const string NetworkGroup = "NETWORK";




    public const string TextureToggleFlag = "DFFlagTextureQualityOverrideEnabled";

    public const string TextureQualityFlag = "DFIntTextureQualityOverride";


    public const string MsaaFlag = "FIntDebugForceMSAASamples";


    public static IReadOnlyList<(string Label, int Value)> TextureLevels { get; } = new[]
    {
        ("Fast", 0),
        ("Sharp", 3),
    };


    public static IReadOnlyList<(string Label, int Value)> MsaaLevels { get; } = new[]
    {
        ("Off", 1),
        ("2×", 2),
        ("4×", 4),
    };





    public static IReadOnlyList<FastFlagPreset> Presets { get; } = new List<FastFlagPreset>
    {
        new("Force the lowest graphics level", "Pins the graphics quality to level 1. The biggest change on weak hardware.", GraphicsGroup,
            QualityFlag, "1"),
        new("Graphics level 3", "Low quality with a little more scene detail than level 1.", GraphicsGroup,
            QualityFlag, "3"),
        new("Graphics level 5", "Low quality for older laptops, with water and distant scenery kept simple.", GraphicsGroup,
            QualityFlag, "5"),
        new("Graphics level 7", "A lower-middle setting for hardware that struggles at the default level.", GraphicsGroup,
            QualityFlag, "7"),
        new("Graphics level 10", "The balanced middle of Roblox's quality range.", GraphicsGroup,
            QualityFlag, "10"),
        new("Graphics level 13", "A middle-high setting for a card with some headroom.", GraphicsGroup,
            QualityFlag, "13"),
        new("Graphics level 16", "High quality without the most expensive last few steps.", GraphicsGroup,
            QualityFlag, "16"),
        new("Graphics level 18", "Very high quality for a strong GPU.", GraphicsGroup,
            QualityFlag, "18"),
        new("Force the highest graphics level", "Pins the graphics quality to level 21.", GraphicsGroup,
            QualityFlag, "21"),
        new("Low-end preset", "Lowest quality, coarse textures, no MSAA, no grass and the shortest CSG distance.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>(QualityFlag, "1"),
            new KeyValuePair<string, string>(TextureToggleFlag, "True"),
            new KeyValuePair<string, string>(TextureQualityFlag, "0"),
            new KeyValuePair<string, string>(MsaaFlag, "1"),
            new KeyValuePair<string, string>("FIntFRMMaxGrassDistance", "0"),
            new KeyValuePair<string, string>("FIntFRMMinGrassDistance", "0"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "0"),
        }),
        new("Balanced preset", "Quality 10, good textures, 2x MSAA, a short grass ring and moderate CSG detail.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>(QualityFlag, "10"),
            new KeyValuePair<string, string>(TextureToggleFlag, "True"),
            new KeyValuePair<string, string>(TextureQualityFlag, "2"),
            new KeyValuePair<string, string>(MsaaFlag, "2"),
            new KeyValuePair<string, string>("FIntFRMMaxGrassDistance", "300"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "1000"),
        }),
        new("Max quality preset", "Quality 21, sharp textures, 4x MSAA and detailed far meshes.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>(QualityFlag, "21"),
            new KeyValuePair<string, string>(TextureToggleFlag, "True"),
            new KeyValuePair<string, string>(TextureQualityFlag, "3"),
            new KeyValuePair<string, string>(MsaaFlag, "4"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "4000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "4000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "4000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "4000"),
        }),
        new("PvP visibility preset", "Low graphics, coarse textures, no MSAA and no grass. Less clutter in a fight.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>(QualityFlag, "5"),
            new KeyValuePair<string, string>(TextureToggleFlag, "True"),
            new KeyValuePair<string, string>(TextureQualityFlag, "0"),
            new KeyValuePair<string, string>(MsaaFlag, "1"),
            new KeyValuePair<string, string>("FIntFRMMaxGrassDistance", "0"),
            new KeyValuePair<string, string>("FIntFRMMinGrassDistance", "0"),
        }),
        new("Grass off", "Removes the grass distance range entirely.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>("FIntFRMMaxGrassDistance", "0"),
            new KeyValuePair<string, string>("FIntFRMMinGrassDistance", "0"),
        }),
        new("Grass draw distance 100", "Only grass right under the camera.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "100"),
        new("Grass draw distance 300", "A short grass ring around the character.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "300"),
        new("Grass draw distance 500", "Grass a little farther out than the short setting.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "500"),
        new("Grass draw distance 1000", "A useful ring of grass without filling the whole view.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "1000"),

        new("Coarse textures", "Forces the lowest texture quality.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>(TextureToggleFlag, "True"),
            new KeyValuePair<string, string>(TextureQualityFlag, "0"),
        }),
        new("Textures medium", "One step above the coarsest textures.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>(TextureToggleFlag, "True"),
            new KeyValuePair<string, string>(TextureQualityFlag, "1"),
        }),
        new("Textures good", "Two steps above the coarsest textures, one below the sharpest.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>(TextureToggleFlag, "True"),
            new KeyValuePair<string, string>(TextureQualityFlag, "2"),
        }),
        new("Sharp textures", "Forces the highest texture quality. It costs video memory.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>(TextureToggleFlag, "True"),
            new KeyValuePair<string, string>(TextureQualityFlag, "3"),
        }),
        new("Anti-aliasing off", "One MSAA sample. Rough edges, less GPU work.", DetailGroup,
            MsaaFlag, "1"),
        new("Anti-aliasing 2x", "Two MSAA samples.", DetailGroup,
            MsaaFlag, "2"),
        new("Anti-aliasing 4x", "Four MSAA samples. The smoothest edges and the most GPU work.", DetailGroup,
            MsaaFlag, "4"),
        new("Distant meshes stay detailed", "Raises all four CSG level-of-detail distances so far geometry keeps its shape.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "1000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "2000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "3000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "4000"),
        }),
        new("Distant meshes medium detail", "A middle CSG range for maps with far-away unions.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "500"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "1000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "1500"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "2000"),
        }),
        new("Distant meshes near detail", "A tighter CSG range for weaker GPUs.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "250"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "500"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "750"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "1000"),
        }),
        new("Distant meshes drop detail early", "Pulls the CSG distances in for the largest saving on weak GPUs.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "0"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "0"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "0"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "0"),
        }),

        new("Vulkan renderer", "Prefers Vulkan over Direct3D11. Some maps show visual glitches with it.", RenderGroup,
            "FFlagDebugGraphicsPreferVulkan", "True"),
        new("Direct3D 11 forced", "Pins the renderer to Direct3D11, the safest default.", RenderGroup,
            "FFlagDebugGraphicsPreferD3D11", "True"),
        new("OpenGL renderer", "Prefers OpenGL on Windows.", RenderGroup,
            "FFlagDebugGraphicsPreferOpenGL", "True"),
        new("DPI scaling off", "Switches DPI downscaling off. Sharper on scaled displays, but it can cost performance.", RenderGroup,
            "DFFlagDisableDPIScale", "True"),
        new("Alt+Enter without fullscreen", "Alt+Enter no longer toggles fullscreen.", RenderGroup,
            "FFlagHandleAltEnterFullscreenManually", "False"),

        new("Grey sky", "Replaces the default sky with flat grey.", SkyGroup,
            "FFlagDebugSkyGray", "True"),
        new("Voxel lighting off", "Pauses the voxelizer so old maps get flatter lighting.", SkyGroup,
            "DFFlagDebugPauseVoxelizer", "True"),

        new("Calm grass", "Damps the grass animation, mostly noticeable in windy maps.", InterfaceGroup,
            "FIntGrassMovementReducedMotionFactor", "0"),
    };

    public static string LoadJsonText()
    {
        try
        {
            if (File.Exists(AppPaths.FastFlagsFile)) return File.ReadAllText(AppPaths.FastFlagsFile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the FastFlags: {ex.Message}");
        }

        return "{\n}\n";
    }


    public static string? ParseJson(string json, out Dictionary<string, string> flags)
    {
        flags = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return "The JSON must be an object, e.g. { \"FFlagName\": \"True\" }.";

            foreach (var property in document.RootElement.EnumerateObject())
            {
                flags[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "True",
                    JsonValueKind.False => "False",
                    JsonValueKind.Null => "",
                    _ => property.Value.GetRawText(),
                };
            }

            return null;
        }
        catch (JsonException ex)
        {
            return $"Invalid JSON: {ex.Message}";
        }
    }


    public static string? TrySaveJson(string json, out Dictionary<string, string> flags)
    {
        var error = ParseJson(json, out flags);
        if (error is not null) return error;

        return JsonFile.Save(AppPaths.FastFlagsFile, flags)
            ? null
            : "FastFlags.json could not be written.";
    }





    public static IReadOnlyList<string> ValidatePresets()
    {
        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var preset in Presets)
        {
            if (!names.Add(preset.Name)) problems.Add($"Duplicate preset name: {preset.Name}");
            if (preset.Flags.Count == 0) problems.Add($"{preset.Name}: no flags");

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var flag in preset.Flags)
            {
                if (string.IsNullOrWhiteSpace(flag.Key) || !Regex.IsMatch(flag.Key, "^[A-Za-z][A-Za-z0-9_]*$"))
                    problems.Add($"{preset.Name}: invalid flag name '{flag.Key}'");
                else if (!keys.Add(flag.Key))
                    problems.Add($"{preset.Name}: flag '{flag.Key}' appears twice");
                else if (!FastFlagAllowlist.Contains(flag.Key))
                    problems.Add($"{preset.Name}: '{flag.Key}' is not on Roblox's current allowlist");
            }
        }

        return problems;
    }


    public static HashSet<string> LoadFavorites()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(AppPaths.FastFlagsFavoritesFile)) return names;
            foreach (var line in File.ReadAllLines(AppPaths.FastFlagsFavoritesFile))
            {
                var name = line.Trim();
                if (name.Length > 0) names.Add(name);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"The FastFlag favorites could not be read: {ex.Message}");
        }

        return names;
    }


    public static void SaveFavorites(IEnumerable<string> names)
    {
        try
        {
            File.WriteAllLines(AppPaths.FastFlagsFavoritesFile,
                names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).OrderBy(name => name));
        }
        catch (Exception ex)
        {
            Log.Warn($"The FastFlag favorites could not be saved: {ex.Message}");
        }
    }

    public static Dictionary<string, string> Load()
        => JsonFile.Load(AppPaths.FastFlagsFile, () => new Dictionary<string, string>());





    public static string? TrySet(string key, string value)
    {
        var flags = Load();
        flags[key] = value;
        return TrySaveJson(Serialize(flags), out _);
    }





    public static string? TrySetMany(IReadOnlyDictionary<string, string?> changes)
    {
        var flags = Load();

        foreach (var (key, value) in changes)
        {
            if (value is null) flags.Remove(key);
            else flags[key] = value;
        }

        return TrySaveJson(Serialize(flags), out _);
    }


    public static string? Read(string key)
    {
        var flags = Load();
        return flags.TryGetValue(key, out var value) ? value : null;
    }


    public static int? ReadFrameRate()
        => int.TryParse(Read(FrameRateFlag), out var value) ? value : null;


    public static int? ReadQualityLevel()
        => int.TryParse(Read(QualityFlag), out var value) ? value : null;


    public static int? ReadTextureQuality()
        => Read(TextureToggleFlag) is null ? null : int.TryParse(Read(TextureQualityFlag), out var value) ? value : null;


    public static int? ReadMsaa()
        => int.TryParse(Read(MsaaFlag), out var value) ? value : null;






    public static string ApplyToInstalledClient(LauncherSettings settings)
    {
        var version = RobloxInstall.Resolve(settings, out _);
        if (version is null) return "The flag is stored - no client folder was found to write it into.";

        Apply(version.Directory, out var message);
        return message;
    }


    public const string QualityFlag = "DFIntDebugFRMQualityLevelOverride";


    public static string Serialize(IReadOnlyDictionary<string, string> flags)
        => JsonFile.Serialize(flags);


    public static int MergePreset(IDictionary<string, string> flags, FastFlagPreset preset)
    {
        var written = 0;
        foreach (var flag in preset.Flags)
        {
            flags[flag.Key] = flag.Value;
            written++;
        }

        return written;
    }





    public static int RemovePreset(IDictionary<string, string> flags, FastFlagPreset preset)
    {
        var removed = 0;
        foreach (var flag in preset.Flags)
        {
            if (!flags.TryGetValue(flag.Key, out var current)) continue;
            if (!string.Equals(current, flag.Value, StringComparison.OrdinalIgnoreCase)) continue;

            flags.Remove(flag.Key);
            removed++;
        }

        return removed;
    }


    public static bool ContainsPreset(IReadOnlyDictionary<string, string> flags, FastFlagPreset preset)
        => preset.Flags.Count > 0 && preset.Flags.All(flag =>
               flags.TryGetValue(flag.Key, out var value) &&
               string.Equals(value, flag.Value, StringComparison.OrdinalIgnoreCase));


    public static bool Apply(string versionDirectory, out string message)
    {
        var flags = Load();
        if (flags.Count == 0)
        {
            message = "No FastFlags configured - nothing to apply.";
            return false;
        }

        return ApplyTo(versionDirectory, flags, out message);
    }


    public static bool ApplyTo(string versionDirectory, IReadOnlyDictionary<string, string> flags, out string message)
    {
        message = "";
        try
        {
            var settingsDir = Path.Combine(versionDirectory, "ClientSettings");
            Directory.CreateDirectory(settingsDir);

            var target = Path.Combine(settingsDir, FileName);
            var backup = target + BackupSuffix;
            if (File.Exists(target) && !File.Exists(backup))
            {
                File.Copy(target, backup);
                Log.Info($"Existing ClientAppSettings.json backed up as {Path.GetFileName(backup)}.");
            }

            File.WriteAllText(target, JsonSerializer.Serialize(flags, JsonFile.Options));



            var readBack = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(target));
            if (readBack is null)
            {
                message = "FastFlags were written but could not be read back.";
                return false;
            }

            var missing = flags
                .Where(pair => !readBack.TryGetValue(pair.Key, out var value) ||
                               !string.Equals(value, pair.Value, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToList();

            if (missing.Count > 0)
            {
                message = $"FastFlags were written, but {missing.Count} value(s) did not read back correctly.";
                return false;
            }

            message = $"{flags.Count} FastFlag(s) written and verified in the client.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"FastFlags could not be written: {ex.Message}";
            return false;
        }
    }
}
