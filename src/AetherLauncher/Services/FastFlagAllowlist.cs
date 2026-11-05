namespace AetherLauncher.Services;

/// <summary>
/// The keys Roblox currently recognises from a local ClientAppSettings.json.
/// Keep this list exact and date it when Roblox changes the published policy.
/// </summary>
public static class FastFlagAllowlist
{
    public const string SourceUrl = "https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569";

    public static IReadOnlySet<string> Keys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "DFIntCSGLevelOfDetailSwitchingDistance",
        "DFIntCSGLevelOfDetailSwitchingDistanceL12",
        "DFIntCSGLevelOfDetailSwitchingDistanceL23",
        "DFIntCSGLevelOfDetailSwitchingDistanceL34",
        "FFlagHandleAltEnterFullscreenManually",
        "DFFlagTextureQualityOverrideEnabled",
        "DFIntTextureQualityOverride",
        "FIntDebugForceMSAASamples",
        "DFFlagDisableDPIScale",
        "FFlagDebugGraphicsPreferD3D11",
        "FFlagDebugSkyGray",
        "DFFlagDebugPauseVoxelizer",
        "DFIntDebugFRMQualityLevelOverride",
        "FIntFRMMaxGrassDistance",
        "FIntFRMMinGrassDistance",
        "FFlagDebugGraphicsPreferVulkan",
        "FFlagDebugGraphicsPreferOpenGL",
        "FIntGrassMovementReducedMotionFactor",
    };

    public static bool Contains(string key) => Keys.Contains(key);
}
