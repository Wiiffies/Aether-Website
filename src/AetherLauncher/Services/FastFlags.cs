using System.Text.Json;
using AetherLauncher.Infrastructure;
using AetherLauncher.Models;

namespace AetherLauncher.Services;

/// <summary>Ein Vorschlag im FastFlags-Editor. Ein Vorschlag kann mehrere Flags setzen.</summary>
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

    /// <summary>
    /// Steht das Flag auf Roblox' Allowlist? Seit September 2025 liest der Client aus
    /// ClientAppSettings.json nur noch Flags von dieser Liste - alles andere wird
    /// stillschweigend ignoriert. Die Liste steht im DevForum unter "Allowlist for local
    /// client configuration via FastFlags".
    /// </summary>
    public bool Allowlisted { get; init; }

    /// <summary>Die Flag-Namen, wie sie in der Liste unter dem Vorschlag stehen.</summary>
    public string Keys => string.Join(", ", Flags.Select(f => f.Key));
}

/// <summary>
/// Verwaltet die FastFlags des Clients: eigene Flags in %LOCALAPPDATA%\AetherLauncher\FastFlags.json,
/// die vor jedem Start in &lt;version&gt;\ClientSettings\ClientAppSettings.json geschrieben werden.
///
/// Hinweis: Roblox erkennt seit September 2025 nur noch Flags von einer Allowlist.
/// Nicht gelistete Flags werden vom Client ignoriert (aber nicht gefährlich).
/// </summary>
public static class FastFlags
{
    private const string FileName = "ClientAppSettings.json";
    private const string BackupSuffix = ".aetherbak";

    /// <summary>Das Flag, mit dem Roblox seine Bildrate begrenzt.</summary>
    public const string FrameRateFlag = "DFIntTaskSchedulerTargetFps";

    /// <summary>Der Wert, den die Community als "ohne Begrenzung" benutzt.</summary>
    public const int UncappedFrameRate = 10000;

    /// <summary>Feste Bildraten für die Chips im Ingame-Overlay.</summary>
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

    // ------------------------------------------------ Flags, die das Overlay schaltet

    /// <summary>Texturqualität: Schalter und Wert gehören zusammen (beide auf der Allowlist).</summary>
    public const string TextureToggleFlag = "DFFlagTextureQualityOverrideEnabled";

    public const string TextureQualityFlag = "DFIntTextureQualityOverride";

    /// <summary>Kantenglättung: 1, 2 oder 4 Abtastungen (auf der Allowlist).</summary>
    public const string MsaaFlag = "FIntDebugForceMSAASamples";

    /// <summary>Die Werte, die das Overlay für Texturqualität anbietet.</summary>
    public static IReadOnlyList<(string Label, int Value)> TextureLevels { get; } = new[]
    {
        ("Fast", 0),
        ("Sharp", 3),
    };

    /// <summary>Die Werte, die das Overlay für Kantenglättung anbietet.</summary>
    public static IReadOnlyList<(string Label, int Value)> MsaaLevels { get; } = new[]
    {
        ("Off", 1),
        ("2×", 2),
        ("4×", 4),
    };

    /// <summary>
    /// Bekannte, in der Community verbreitete Flags. Werte sind bewusst zurückhaltend gewählt:
    /// nichts, was den Client beschädigt, und nichts, was sich nicht wieder entfernen lässt.
    /// </summary>
    public static IReadOnlyList<FastFlagPreset> Presets { get; } = new List<FastFlagPreset>
    {
        // --- Bildrate ---
        // Die Bildrate ist der bekannteste Fall von "nicht mehr auf der Allowlist": Bloxstrap
        // rät zusätzlich zu Vorsicht - deshalb stehen die Warnungen hier ausgeschrieben.
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

        // --- Grafik ---
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

        // --- Himmel und Licht ---
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

        // --- Effekte und Renderer ---
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

        // --- Oberfläche ---
        new("FPS counter", "Shows the frame rate in the corner of the game.", InterfaceGroup,
            "FFlagDebugDisplayFPS", "True"),
        new("Zoom far out", "Raises the maximum camera zoom distance.", InterfaceGroup,
            "FIntCameraMaxZoomDistance", "9999"),
        new("Allow first-person mouse lock", "Enables the mouse-lock (shift lock) toggle.", InterfaceGroup,
            "FFlagUserAllowMouseLock", "True"),

        // --- Netzwerk ---
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

        // --- Weitere Effekte aus den Community-Listen (nicht auf der Allowlist) ---
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

        // --- Oberfläche ---
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

        // --- Weitere Stufen und Varianten derselben, verifizierten Flag-Namen ---
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
    };

    /// <summary>Inhalt der eigenen FastFlags-Datei als Text für den Editor.</summary>
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

    /// <summary>Nimmt JSON mit Kommentaren und Kommas entgegen und macht daraus Flag-Name → Wert.</summary>
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
                return "Das JSON muss ein Objekt sein, z. B. { \"FFlagName\": \"True\" }.";

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

    /// <summary>Prüft und speichert eingegebenes JSON. Liefert eine Fehlermeldung oder null.</summary>
    public static string? TrySaveJson(string json, out Dictionary<string, string> flags)
    {
        var error = ParseJson(json, out flags);
        if (error is not null) return error;

        return JsonFile.Save(AppPaths.FastFlagsFile, flags)
            ? null
            : "FastFlags.json could not be written.";
    }

    public static Dictionary<string, string> Load()
        => JsonFile.Load(AppPaths.FastFlagsFile, () => new Dictionary<string, string>());

    /// <summary>
    /// Setzt genau ein Flag in der gespeicherten Datei - für die Bildraten-Chips im
    /// Ingame-Overlay. Liefert null bei Erfolg, sonst die Meldung.
    /// </summary>
    public static string? TrySet(string key, string value)
    {
        var flags = Load();
        flags[key] = value;
        return TrySaveJson(Serialize(flags), out _);
    }

    /// <summary>
    /// Setzt mehrere Flags auf einmal - und nimmt sie wieder heraus, wenn der Wert null ist.
    /// Das Overlay schaltet damit seine Reihen (Texturen, Gras) in einem Zug um.
    /// </summary>
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

    /// <summary>Ein gespeichertes Flag lesen - null, wenn es nicht gesetzt ist.</summary>
    public static string? Read(string key)
    {
        var flags = Load();
        return flags.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Die gespeicherte Bildratenbegrenzung oder null, wenn keine gesetzt ist.</summary>
    public static int? ReadFrameRate()
        => int.TryParse(Read(FrameRateFlag), out var value) ? value : null;

    /// <summary>Die gespeicherte Grafikstufe (1 = niedrigste, 21 = höchste) oder null.</summary>
    public static int? ReadQualityLevel()
        => int.TryParse(Read(QualityFlag), out var value) ? value : null;

    /// <summary>Die gespeicherte Texturqualität (0 = grob, 3 = scharf) oder null.</summary>
    public static int? ReadTextureQuality()
        => Read(TextureToggleFlag) is null ? null : int.TryParse(Read(TextureQualityFlag), out var value) ? value : null;

    /// <summary>Die gespeicherte Kantenglättung (1, 2 oder 4 Abtastungen) oder null.</summary>
    public static int? ReadMsaa()
        => int.TryParse(Read(MsaaFlag), out var value) ? value : null;

    /// <summary>
    /// Schreibt die gespeicherten Flags in die ClientSettings der installierten Version.
    /// Das Overlay benutzt das, damit eine Änderung schon beim nächsten Clientstart greift -
    /// auch wenn der Launcher gerade nicht gestartet hat.
    /// </summary>
    public static string ApplyToInstalledClient(LauncherSettings settings)
    {
        var version = RobloxInstall.Resolve(settings, out _);
        if (version is null) return "The flag is stored - no client folder was found to write it into.";

        Apply(version.Directory, out var message);
        return message;
    }

    /// <summary>Die Stufe, die der Client als Grafikqualität benutzt.</summary>
    public const string QualityFlag = "DFIntDebugFRMQualityLevelOverride";

    /// <summary>Schreibt die Flags als JSON-Text, so wie ihn der Editor zeigt.</summary>
    public static string Serialize(IReadOnlyDictionary<string, string> flags)
        => JsonFile.Serialize(flags);

    /// <summary>Übernimmt die Flags eines Vorschlags und liefert die Anzahl der gesetzten Einträge.</summary>
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

    /// <summary>
    /// Nimmt die Flags eines Vorschlags wieder heraus - aber nur dort, wo der Wert noch genau
    /// dem des Vorschlags entspricht. Wer einen Wert selbst angepasst hat, behält ihn.
    /// </summary>
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

    /// <summary>Steckt der Vorschlag vollständig in diesen Flags?</summary>
    public static bool ContainsPreset(IReadOnlyDictionary<string, string> flags, FastFlagPreset preset)
        => preset.Flags.Count > 0 && preset.Flags.All(flag =>
               flags.TryGetValue(flag.Key, out var value) &&
               string.Equals(value, flag.Value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Übernimmt die gespeicherten Flags in eine Client-Version.</summary>
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

    /// <summary>Schreibt die Flags in den ClientSettings-Ordner der Version und sichert Vorhandenes.</summary>
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
            message = $"{flags.Count} FastFlag(s) written to the client.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"FastFlags could not be written: {ex.Message}";
            return false;
        }
    }
}
