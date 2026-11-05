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

    
    
    
    
    
    
    public bool Allowlisted { get; init; }

    
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
        
        
        
        new("Cap the frame rate at 30", "The gentlest setting - for battery life or very weak integrated graphics.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "30"),
        new("Cap the frame rate at 60", "Holds the frame rate at 60 FPS, the calmest stable target.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "60"),
        new("Cap the frame rate at 144", "Holds the frame rate at 144 FPS.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "144"),
        new("Cap the frame rate at 240", "For fast screens. Bloxstrap advises against going above 240 - rare crashes when joining or teleporting have been reported.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "240"),
        new("Lift the frame rate cap", "Switches the built-in frame rate limit off (10000 FPS). Historical flags warn of rare network latency spikes; set it back to 0 if joining or teleporting misbehaves.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "10000"),

        
        new("Force the lowest graphics level", "Pins the graphics quality to level 1 - the biggest win on old hardware.", GraphicsGroup,
            "DFIntDebugFRMQualityLevelOverride", "1") { Allowlisted = true },
        new("Force the highest graphics level", "Pins the graphics quality to level 21.", GraphicsGroup,
            "DFIntDebugFRMQualityLevelOverride", "21") { Allowlisted = true },
        new("Post-processing off", "Removes blur, bloom and similar post effects.", GraphicsGroup,
            "FFlagDisablePostFx", "True"),
        new("Shadows off", "Sets the shadow intensity to zero.", GraphicsGroup,
            "FIntRenderShadowIntensity", "0"),
        new("Grass off", "Removes grass and the grass draw distance.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>("FIntFRMMaxGrassDistance", "0"),
            new KeyValuePair<string, string>("FIntFRMMinGrassDistance", "0"),
            new KeyValuePair<string, string>("FIntRenderGrassDetailStrands", "0"),
        }) { Allowlisted = true },
        new("Calm grass", "Damps the grass animation - noticed mostly in windy maps.", GraphicsGroup,
            "FIntGrassMovementReducedMotionFactor", "0") { Allowlisted = true },
        new("Coarse textures", "Forces the lowest texture quality.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFFlagTextureQualityOverrideEnabled", "True"),
            new KeyValuePair<string, string>("DFIntTextureQualityOverride", "0"),
        }) { Allowlisted = true },
        new("Sharp textures", "Forces the highest texture quality - costs video memory.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFFlagTextureQualityOverrideEnabled", "True"),
            new KeyValuePair<string, string>("DFIntTextureQualityOverride", "3"),
        }) { Allowlisted = true },
        new("Anti-aliasing off", "One MSAA sample: the edges get rough, the frame rate gets a little breathing room.", DetailGroup,
            MsaaFlag, "1") { Allowlisted = true },
        new("Anti-aliasing 4×", "Four MSAA samples - the smoothest edges, the most work for the GPU.", DetailGroup,
            MsaaFlag, "4") { Allowlisted = true },
        new("Distant meshes stay detailed", "Raises the CSG level-of-detail distances so far geometry keeps its shape.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "1000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "2000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "3000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "4000"),
        }) { Allowlisted = true },
        new("Distant meshes drop detail early", "Pulls the CSG level-of-detail distances way in - helps on weak GPUs.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "0"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "0"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "0"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "0"),
        }) { Allowlisted = true },

        
        new("Grey sky", "Replaces the default sky with flat grey - easier on the eyes and brighter maps.", SkyGroup,
            "FFlagDebugSkyGray", "True") { Allowlisted = true },
        new("Voxel lighting off", "Pauses the voxelizer, which bakes a good part of the lighting. Old maps get flatter.", SkyGroup,
            "DFFlagDebugPauseVoxelizer", "True") { Allowlisted = true },
        new("Force voxel lighting", "Asks for the oldest lighting technology. Bloxstrap warns that forcing a technology the game did not choose can make lighting far too bright or too dark.", SkyGroup,
            "DFFlagDebugRenderForceTechnologyVoxel", "True"),
        new("Force ShadowMap lighting", "Asks for the second lighting generation - same warning: the map was not designed for it.", SkyGroup,
            "FFlagDebugForceFutureIsBrightPhase2", "True"),
        new("Force Future lighting", "Asks for the newest lighting generation - the prettiest and the most expensive, and not what every map expects.", SkyGroup,
            "FFlagDebugForceFutureIsBrightPhase3", "True"),
        new("New light falloff", "Uses the newer light attenuation curve.", SkyGroup,
            "FFlagNewLightAttenuation", "True"),
        new("GPU light culling", "Lets the GPU throw away lights that are not visible.", SkyGroup,
            "FFlagFastGPULightCulling3", "True"),

        
        new("Vulkan renderer", "Prefers Vulkan over Direct3D11. Reports of visual glitches and crashes - try it, but don't depend on it.", RenderGroup,
            "FFlagDebugGraphicsPreferVulkan", "True") { Allowlisted = true },
        new("Direct3D 10 level", "Direct3D11 with a Direct3D10 feature level - for very old GPUs. Not on the allowlist.", RenderGroup,
            "FFlagDebugGraphicsPreferD3D11FL10", "True"),
        new("Direct3D 11 forced", "Pins the renderer to Direct3D11, the safest default.", RenderGroup,
            "FFlagDebugGraphicsPreferD3D11", "True") { Allowlisted = true },
        new("OpenGL renderer", "Prefers OpenGL. On Windows this is the unusual one.", RenderGroup,
            "FFlagDebugGraphicsPreferOpenGL", "True") { Allowlisted = true },
        new("DPI scaling off", "Switches the DPI downscaling off - sharper on scaled displays. Bloxstrap warns this may cost performance and battery.", RenderGroup,
            "DFFlagDisableDPIScale", "True") { Allowlisted = true },
        new("Alt+Enter without fullscreen", "Alt+Enter no longer toggles fullscreen.", RenderGroup,
            "FFlagHandleAltEnterFullscreenManually", "False") { Allowlisted = true },

        
        new("FPS counter", "Shows the frame rate in the corner of the game.", InterfaceGroup,
            "FFlagDebugDisplayFPS", "True"),
        new("Zoom far out", "Raises the maximum camera zoom distance.", InterfaceGroup,
            "FIntCameraMaxZoomDistance", "9999"),
        new("Allow first-person mouse lock", "Enables the mouse-lock (shift lock) toggle.", InterfaceGroup,
            "FFlagUserAllowMouseLock", "True"),

        
        new("Smaller network packets", "Lowers the MTU to 1400 - sometimes helps on shaky connections.", NetworkGroup,
            "DFIntConnectionMTUSize", "1400"),
        new("Roblox telemetry off", "Turns off the client's telemetry reports.", NetworkGroup, new[]
        {
            new KeyValuePair<string, string>("FFlagDebugDisableTelemetryEphemeralCounter", "True"),
            new KeyValuePair<string, string>("FFlagDebugDisableTelemetryEphemeralStat", "True"),
            new KeyValuePair<string, string>("FFlagDebugDisableTelemetryEventIngest", "True"),
            new KeyValuePair<string, string>("FFlagDebugDisableTelemetryPoint", "True"),
            new KeyValuePair<string, string>("FFlagDebugDisableTelemetryV2Counter", "True"),
            new KeyValuePair<string, string>("FFlagDebugDisableTelemetryV2Event", "True"),
            new KeyValuePair<string, string>("FFlagDebugDisableTelemetryV2Stat", "True"),
        }),

        
        new("No bloom or clouds", "Community lists call this 'no bloom' - the meaning of the value is not documented anywhere solid.", RenderGroup,
            "FFlagRenderNoLowFrmBloom", "False"),
        new("No loading blur", "Removes the blur behind the loading and disconnect screens.", RenderGroup,
            "FIntRobloxGuiBlurIntensity", "0"),
        new("Material textures off", "Empties all four material texture tables (parts and terrain, both years) - flat colours everywhere. Community lists warn this can break glass.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("FStringPartTexturePackTable2022", ""),
            new KeyValuePair<string, string>("FStringPartTexturePackTablePre2022", ""),
            new KeyValuePair<string, string>("FStringTerrainMaterialTable2022", ""),
            new KeyValuePair<string, string>("FStringTerrainMaterialTablePre2022", ""),
        }),
        new("Old material textures (2022)", "The historical way back to the pre-2022 material look: an empty part-texture table plus the workaround flag. Bloxstrap noted Roblox planned to drop this - it may do nothing now.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("FStringPartTexturePackTable2022", ""),
            new KeyValuePair<string, string>("FFlagMSRefactor4", "False"),
        }),
        new("No player clothing textures", "Skips compositing clothing textures onto avatars. Clothes turn into flat colours - a real difference on crowded servers.", DetailGroup,
            "DFIntTextureCompositorActiveJobs", "0"),
        new("Skip mipmaps", "Makes the texture manager skip the smaller texture levels. Sharper up close, more aliasing in the distance - and values below 0 remove textures altogether.", DetailGroup,
            "FIntDebugTextureManagerSkipMips", "0"),

        
        new("Legacy in-game menu", "Brings back the older in-game menu. Bloxstrap warns it is no longer maintained, controls may misbehave, and the newer Chrome menu can override it anyway.", InterfaceGroup, new[]
        {
            new KeyValuePair<string, string>("FFlagDisableNewIGMinDUA", "True"),
            new KeyValuePair<string, string>("FFlagEnableInGameMenuControls", "False"),
            new KeyValuePair<string, string>("FFlagEnableInGameMenuModernization", "False"),
        }),
        new("Chrome in-game menu", "Forces the newer Chrome-style menu. A community list warns some games may fail to load with it.", InterfaceGroup,
            "FFlagEnableInGameMenuChrome", "True"),
        new("GUI hide buttons", "Shows the toggles that hide the game's own interface and the core GUI. The group-id variant of this feature needs membership in a Roblox group.", InterfaceGroup, new[]
        {
            new KeyValuePair<string, string>("GuiHidingApiSupport2", "True"),
            new KeyValuePair<string, string>("FFlagUserShowGuiHideToggles", "True"),
        }),
        new("New camera mode", "Turns on the newer camera controls.", InterfaceGroup,
            "FFlagNewCameraControls", "True"),
        new("Fewer particles in first person", "Hides avatar-item particles while you are in first person - less clutter on the screen.", InterfaceGroup,
            "FFlagUserHideCharacterParticlesInFirstPerson", "True"),

        
        new("Cap the frame rate at 90", "Sits between the usual 60 and 144 steps.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "90"),
        new("Cap the frame rate at 165", "For 165 Hz screens.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "165"),
        new("Cap the frame rate at 360", "Only worth it on a very fast screen - Bloxstrap advises against going far above 240.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "360"),

        new("Graphics level 5", "A step above the lowest level: water and distance stay cheap, the view gets less bare.", GraphicsGroup,
            "DFIntDebugFRMQualityLevelOverride", "5") { Allowlisted = true },
        new("Graphics level 10", "The middle of the range - the usual balanced setting.", GraphicsGroup,
            "DFIntDebugFRMQualityLevelOverride", "10") { Allowlisted = true },
        new("Graphics level 15", "Nearly the top, without the most expensive extras.", GraphicsGroup,
            "DFIntDebugFRMQualityLevelOverride", "15") { Allowlisted = true },
        new("Shadows at half strength", "Keeps a little depth instead of removing the shadows completely.", GraphicsGroup,
            "FIntRenderShadowIntensity", "0.5"),
        new("Shadows very soft", "A quarter of the usual shadow strength.", GraphicsGroup,
            "FIntRenderShadowIntensity", "0.25"),
        new("Short grass draw distance", "Keeps grass only near the camera (300 studs) - the middle ground between full and none.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "300") { Allowlisted = true },

        new("Textures: medium", "One step above the coarsest textures.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFFlagTextureQualityOverrideEnabled", "True"),
            new KeyValuePair<string, string>("DFIntTextureQualityOverride", "1"),
        }) { Allowlisted = true },
        new("Textures: good", "Two steps above the coarsest textures, one below the sharpest.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFFlagTextureQualityOverrideEnabled", "True"),
            new KeyValuePair<string, string>("DFIntTextureQualityOverride", "2"),
        }) { Allowlisted = true },
        new("Anti-aliasing 2×", "Two MSAA samples - between rough edges and the smooth 4× setting.", DetailGroup,
            MsaaFlag, "2") { Allowlisted = true },
        new("Old material textures - fallback", "Bloxstrap's second step if the 2022 way back does not stick: switches the newer material refactor off as well.", DetailGroup,
            "FFlagMSRefactor5", "False"),

        new("No forced Chrome menu (AB test)", "The other half of the menu workaround - also switches the newer menu test off.", InterfaceGroup,
            "FFlagEnableInGameMenuChromeABTest2", "False"),
        new("No forced report dialog (AB test)", "Bloxstrap pairs this with the menu workaround: it also stops the newer report dialog from taking over.", InterfaceGroup,
            "FFlagEnableReportAbuseMenuRoactABTest2", "False"),
        new("GUI hiding for your own group", "The group variant of the GUI hiding toggles. Replace the 0 with a Roblox group id you are a member of - with 0 nothing is unlocked.", InterfaceGroup,
            "DFIntCanHideGuiGroupId", "0"),
        new("Zoom even farther out", "Raises the camera zoom to 20000 studs. Very far views can look thin - it comes straight back out.", InterfaceGroup,
            "FIntCameraMaxZoomDistance", "20000"),
        new("Zoom up close", "Lowers the maximum zoom so the camera stays near the character.", InterfaceGroup,
            "FIntCameraMaxZoomDistance", "200"),

        new("Even smaller network packets", "MTU 1200 - the more aggressive step for a shaky connection.", NetworkGroup,
            "DFIntConnectionMTUSize", "1200"),
        new("Standard network packets", "Back to MTU 1500, the Windows default.", NetworkGroup,
            "DFIntConnectionMTUSize", "1500"),

        
        new("Cap the frame rate at 75", "A middle step for 75 Hz screens and older laptops.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "75"),
        new("Cap the frame rate at 120", "The usual choice for a 120 Hz screen.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "120"),
        new("Cap the frame rate at 200", "For 200 Hz screens - above the 240 warning line only when the GPU can keep up.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "200"),
        new("Graphics level 2", "One step above the lowest quality - still very cheap.", GraphicsGroup,
            QualityFlag, "2") { Allowlisted = true },
        new("Graphics level 3", "A small step up from the lowest quality.", GraphicsGroup,
            QualityFlag, "3") { Allowlisted = true },
        new("Graphics level 4", "Low quality with a little more distance detail.", GraphicsGroup,
            QualityFlag, "4") { Allowlisted = true },
        new("Graphics level 7", "Lower-middle quality for older hardware.", GraphicsGroup,
            QualityFlag, "7") { Allowlisted = true },
        new("Graphics level 8", "A balanced low setting.", GraphicsGroup,
            QualityFlag, "8") { Allowlisted = true },
        new("Graphics level 12", "Slightly above the middle of the quality range.", GraphicsGroup,
            QualityFlag, "12") { Allowlisted = true },
        new("Graphics level 16", "High quality without the top few expensive extras.", GraphicsGroup,
            QualityFlag, "16") { Allowlisted = true },
        new("Graphics level 18", "Very high quality for a strong GPU.", GraphicsGroup,
            QualityFlag, "18") { Allowlisted = true },
        new("Graphics level 20", "One step below the maximum quality.", GraphicsGroup,
            QualityFlag, "20") { Allowlisted = true },
        new("Grass draw distance 100", "Only grass right under the camera - the most aggressive grass saving.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "100") { Allowlisted = true },
        new("Grass draw distance 500", "Grass a little farther out than the short setting.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "500") { Allowlisted = true },
        new("Grass draw distance 1000", "Keeps a useful ring of grass without filling the whole view.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "1000") { Allowlisted = true },

        
        new("Distant meshes: medium detail", "A middle level-of-detail range for maps with far-away unions.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "500"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "1000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "1500"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "2000"),
        }) { Allowlisted = true },

        
        new("Cap the frame rate at 48", "For very weak hardware or a stable cinematic pace.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "48"),
        new("Cap the frame rate at 72", "A small step above 60 for 72 Hz panels.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "72"),
        new("Cap the frame rate at 100", "For 100 Hz screens and laptops that cannot hold 120.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "100"),
        new("Cap the frame rate at 160", "For 160 Hz screens - below the 240 warning line.", FrameRateGroup,
            "DFIntTaskSchedulerTargetFps", "160"),
        new("Graphics level 6", "Low quality with a little more headroom than level 5.", GraphicsGroup,
            QualityFlag, "6") { Allowlisted = true },
        new("Graphics level 9", "Just below the balanced level 10.", GraphicsGroup,
            QualityFlag, "9") { Allowlisted = true },
        new("Graphics level 11", "Just above the balanced level 10.", GraphicsGroup,
            QualityFlag, "11") { Allowlisted = true },
        new("Graphics level 13", "A comfortable middle-high setting for most GPUs.", GraphicsGroup,
            QualityFlag, "13") { Allowlisted = true },
        new("Graphics level 17", "High quality without the last four expensive steps.", GraphicsGroup,
            QualityFlag, "17") { Allowlisted = true },
        new("Grass draw distance 200", "A very short ring of grass - almost the grass-off saving.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "200") { Allowlisted = true },
        new("Grass draw distance 750", "Between the short 500 and the broad 1000 setting.", GraphicsGroup,
            "FIntFRMMaxGrassDistance", "750") { Allowlisted = true },
        new("Shadows at quarter strength", "Keeps a faint shadow instead of removing it completely.", GraphicsGroup,
            "FIntRenderShadowIntensity", "0.25"),
        new("Distant meshes: near detail", "A tighter level-of-detail range than medium - still smoother than dropping detail immediately.", DetailGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "250"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "500"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "750"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "1000"),
        }) { Allowlisted = true },

        
        new("Low-end preset", "The strongest safe combination: lowest graphics, coarse textures, no MSAA, no grass and early CSG detail.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntDebugFRMQualityLevelOverride", "1"),
            new KeyValuePair<string, string>("DFFlagTextureQualityOverrideEnabled", "True"),
            new KeyValuePair<string, string>("DFIntTextureQualityOverride", "0"),
            new KeyValuePair<string, string>("FIntDebugForceMSAASamples", "1"),
            new KeyValuePair<string, string>("FIntFRMMaxGrassDistance", "0"),
            new KeyValuePair<string, string>("FIntFRMMinGrassDistance", "0"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "0"),
        }) { Allowlisted = true },
        new("Balanced preset", "Quality 10, good textures, 2× MSAA, a short grass ring and moderate CSG detail.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntDebugFRMQualityLevelOverride", "10"),
            new KeyValuePair<string, string>("DFFlagTextureQualityOverrideEnabled", "True"),
            new KeyValuePair<string, string>("DFIntTextureQualityOverride", "2"),
            new KeyValuePair<string, string>("FIntDebugForceMSAASamples", "2"),
            new KeyValuePair<string, string>("FIntFRMMaxGrassDistance", "300"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "1000"),
        }) { Allowlisted = true },
        new("Max quality preset", "Quality 21, sharp textures, 4× MSAA and detailed far meshes - the heaviest safe combination here.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntDebugFRMQualityLevelOverride", "21"),
            new KeyValuePair<string, string>("DFFlagTextureQualityOverrideEnabled", "True"),
            new KeyValuePair<string, string>("DFIntTextureQualityOverride", "3"),
            new KeyValuePair<string, string>("FIntDebugForceMSAASamples", "4"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistance", "4000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL12", "4000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL23", "4000"),
            new KeyValuePair<string, string>("DFIntCSGLevelOfDetailSwitchingDistanceL34", "4000"),
        }) { Allowlisted = true },
        new("PvP visibility preset", "Low-but-readable graphics with no grass, coarse textures and no MSAA - fewer distractions in a fight.", GraphicsGroup, new[]
        {
            new KeyValuePair<string, string>("DFIntDebugFRMQualityLevelOverride", "5"),
            new KeyValuePair<string, string>("DFFlagTextureQualityOverrideEnabled", "True"),
            new KeyValuePair<string, string>("DFIntTextureQualityOverride", "0"),
            new KeyValuePair<string, string>("FIntDebugForceMSAASamples", "1"),
            new KeyValuePair<string, string>("FIntFRMMaxGrassDistance", "0"),
            new KeyValuePair<string, string>("FIntFRMMinGrassDistance", "0"),
        }) { Allowlisted = true },
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
