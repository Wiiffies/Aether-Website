// Generated from Programm/src/AetherLauncher/Services/FastFlags.cs.
// Do not edit by hand; run tmp/export-flags after changing the launcher catalogue.
window.AETHER_FLAGS = {
  "generatedAt": "2026-11-04T22:40:45.1133062\u002B00:00",
  "totals": {
    "presets": 99,
    "flags": 153,
    "distinctFlags": 60,
    "allowlisted": 49,
    "legacy": 50
  },
  "categories": [
    {
      "id": "frame-rate",
      "label": "FPS",
      "group": "FRAME RATE",
      "order": 0,
      "count": 15,
      "allowlisted": 0
    },
    {
      "id": "graphics",
      "label": "Graphics",
      "group": "GRAPHICS",
      "order": 1,
      "count": 36,
      "allowlisted": 31
    },
    {
      "id": "lighting",
      "label": "Lighting",
      "group": "SKY \u0026 LIGHTING",
      "order": 2,
      "count": 7,
      "allowlisted": 2
    },
    {
      "id": "effects",
      "label": "Effects",
      "group": "EFFECTS \u0026 RENDERER",
      "order": 3,
      "count": 8,
      "allowlisted": 5
    },
    {
      "id": "textures",
      "label": "Textures",
      "group": "TEXTURES \u0026 DETAIL",
      "order": 4,
      "count": 16,
      "allowlisted": 11
    },
    {
      "id": "interface",
      "label": "Interface",
      "group": "INTERFACE",
      "order": 5,
      "count": 13,
      "allowlisted": 0
    },
    {
      "id": "network",
      "label": "Network",
      "group": "NETWORK",
      "order": 6,
      "count": 4,
      "allowlisted": 0
    }
  ],
  "presets": [
    {
      "name": "Cap the frame rate at 30",
      "description": "The gentlest setting - for battery life or very weak integrated graphics.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "30"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 60",
      "description": "Holds the frame rate at 60 FPS, the calmest stable target.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "60"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 144",
      "description": "Holds the frame rate at 144 FPS.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "144"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 240",
      "description": "For fast screens. Bloxstrap advises against going above 240 - rare crashes when joining or teleporting have been reported.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "240"
        }
      ]
    },
    {
      "name": "Lift the frame rate cap",
      "description": "Switches the built-in frame rate limit off (10000 FPS). Historical flags warn of rare network latency spikes; set it back to 0 if joining or teleporting misbehaves.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "10000"
        }
      ]
    },
    {
      "name": "Force the lowest graphics level",
      "description": "Pins the graphics quality to level 1 - the biggest win on old hardware.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "1"
        }
      ]
    },
    {
      "name": "Force the highest graphics level",
      "description": "Pins the graphics quality to level 21.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "21"
        }
      ]
    },
    {
      "name": "Post-processing off",
      "description": "Removes blur, bloom and similar post effects.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagDisablePostFx",
          "value": "True"
        }
      ]
    },
    {
      "name": "Shadows off",
      "description": "Sets the shadow intensity to zero.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": false,
      "flags": [
        {
          "key": "FIntRenderShadowIntensity",
          "value": "0"
        }
      ]
    },
    {
      "name": "Grass off",
      "description": "Removes grass and the grass draw distance.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "0"
        },
        {
          "key": "FIntFRMMinGrassDistance",
          "value": "0"
        },
        {
          "key": "FIntRenderGrassDetailStrands",
          "value": "0"
        }
      ]
    },
    {
      "name": "Calm grass",
      "description": "Damps the grass animation - noticed mostly in windy maps.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntGrassMovementReducedMotionFactor",
          "value": "0"
        }
      ]
    },
    {
      "name": "Coarse textures",
      "description": "Forces the lowest texture quality.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFFlagTextureQualityOverrideEnabled",
          "value": "True"
        },
        {
          "key": "DFIntTextureQualityOverride",
          "value": "0"
        }
      ]
    },
    {
      "name": "Sharp textures",
      "description": "Forces the highest texture quality - costs video memory.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFFlagTextureQualityOverrideEnabled",
          "value": "True"
        },
        {
          "key": "DFIntTextureQualityOverride",
          "value": "3"
        }
      ]
    },
    {
      "name": "Anti-aliasing off",
      "description": "One MSAA sample: the edges get rough, the frame rate gets a little breathing room.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntDebugForceMSAASamples",
          "value": "1"
        }
      ]
    },
    {
      "name": "Anti-aliasing 4\u00D7",
      "description": "Four MSAA samples - the smoothest edges, the most work for the GPU.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntDebugForceMSAASamples",
          "value": "4"
        }
      ]
    },
    {
      "name": "Distant meshes stay detailed",
      "description": "Raises the CSG level-of-detail distances so far geometry keeps its shape.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistance",
          "value": "1000"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL12",
          "value": "2000"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL23",
          "value": "3000"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL34",
          "value": "4000"
        }
      ]
    },
    {
      "name": "Distant meshes drop detail early",
      "description": "Pulls the CSG level-of-detail distances way in - helps on weak GPUs.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistance",
          "value": "0"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL12",
          "value": "0"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL23",
          "value": "0"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL34",
          "value": "0"
        }
      ]
    },
    {
      "name": "Grey sky",
      "description": "Replaces the default sky with flat grey - easier on the eyes and brighter maps.",
      "group": "SKY \u0026 LIGHTING",
      "category": "lighting",
      "categoryLabel": "Lighting",
      "categoryOrder": 2,
      "allowlisted": true,
      "flags": [
        {
          "key": "FFlagDebugSkyGray",
          "value": "True"
        }
      ]
    },
    {
      "name": "Voxel lighting off",
      "description": "Pauses the voxelizer, which bakes a good part of the lighting. Old maps get flatter.",
      "group": "SKY \u0026 LIGHTING",
      "category": "lighting",
      "categoryLabel": "Lighting",
      "categoryOrder": 2,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFFlagDebugPauseVoxelizer",
          "value": "True"
        }
      ]
    },
    {
      "name": "Force voxel lighting",
      "description": "Asks for the oldest lighting technology. Bloxstrap warns that forcing a technology the game did not choose can make lighting far too bright or too dark.",
      "group": "SKY \u0026 LIGHTING",
      "category": "lighting",
      "categoryLabel": "Lighting",
      "categoryOrder": 2,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFFlagDebugRenderForceTechnologyVoxel",
          "value": "True"
        }
      ]
    },
    {
      "name": "Force ShadowMap lighting",
      "description": "Asks for the second lighting generation - same warning: the map was not designed for it.",
      "group": "SKY \u0026 LIGHTING",
      "category": "lighting",
      "categoryLabel": "Lighting",
      "categoryOrder": 2,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagDebugForceFutureIsBrightPhase2",
          "value": "True"
        }
      ]
    },
    {
      "name": "Force Future lighting",
      "description": "Asks for the newest lighting generation - the prettiest and the most expensive, and not what every map expects.",
      "group": "SKY \u0026 LIGHTING",
      "category": "lighting",
      "categoryLabel": "Lighting",
      "categoryOrder": 2,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagDebugForceFutureIsBrightPhase3",
          "value": "True"
        }
      ]
    },
    {
      "name": "New light falloff",
      "description": "Uses the newer light attenuation curve.",
      "group": "SKY \u0026 LIGHTING",
      "category": "lighting",
      "categoryLabel": "Lighting",
      "categoryOrder": 2,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagNewLightAttenuation",
          "value": "True"
        }
      ]
    },
    {
      "name": "GPU light culling",
      "description": "Lets the GPU throw away lights that are not visible.",
      "group": "SKY \u0026 LIGHTING",
      "category": "lighting",
      "categoryLabel": "Lighting",
      "categoryOrder": 2,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagFastGPULightCulling3",
          "value": "True"
        }
      ]
    },
    {
      "name": "Vulkan renderer",
      "description": "Prefers Vulkan over Direct3D11. Reports of visual glitches and crashes - try it, but don\u0027t depend on it.",
      "group": "EFFECTS \u0026 RENDERER",
      "category": "effects",
      "categoryLabel": "Effects",
      "categoryOrder": 3,
      "allowlisted": true,
      "flags": [
        {
          "key": "FFlagDebugGraphicsPreferVulkan",
          "value": "True"
        }
      ]
    },
    {
      "name": "Direct3D 10 level",
      "description": "Direct3D11 with a Direct3D10 feature level - for very old GPUs. Not on the allowlist.",
      "group": "EFFECTS \u0026 RENDERER",
      "category": "effects",
      "categoryLabel": "Effects",
      "categoryOrder": 3,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagDebugGraphicsPreferD3D11FL10",
          "value": "True"
        }
      ]
    },
    {
      "name": "Direct3D 11 forced",
      "description": "Pins the renderer to Direct3D11, the safest default.",
      "group": "EFFECTS \u0026 RENDERER",
      "category": "effects",
      "categoryLabel": "Effects",
      "categoryOrder": 3,
      "allowlisted": true,
      "flags": [
        {
          "key": "FFlagDebugGraphicsPreferD3D11",
          "value": "True"
        }
      ]
    },
    {
      "name": "OpenGL renderer",
      "description": "Prefers OpenGL. On Windows this is the unusual one.",
      "group": "EFFECTS \u0026 RENDERER",
      "category": "effects",
      "categoryLabel": "Effects",
      "categoryOrder": 3,
      "allowlisted": true,
      "flags": [
        {
          "key": "FFlagDebugGraphicsPreferOpenGL",
          "value": "True"
        }
      ]
    },
    {
      "name": "DPI scaling off",
      "description": "Switches the DPI downscaling off - sharper on scaled displays. Bloxstrap warns this may cost performance and battery.",
      "group": "EFFECTS \u0026 RENDERER",
      "category": "effects",
      "categoryLabel": "Effects",
      "categoryOrder": 3,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFFlagDisableDPIScale",
          "value": "True"
        }
      ]
    },
    {
      "name": "Alt\u002BEnter without fullscreen",
      "description": "Alt\u002BEnter no longer toggles fullscreen.",
      "group": "EFFECTS \u0026 RENDERER",
      "category": "effects",
      "categoryLabel": "Effects",
      "categoryOrder": 3,
      "allowlisted": true,
      "flags": [
        {
          "key": "FFlagHandleAltEnterFullscreenManually",
          "value": "False"
        }
      ]
    },
    {
      "name": "FPS counter",
      "description": "Shows the frame rate in the corner of the game.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagDebugDisplayFPS",
          "value": "True"
        }
      ]
    },
    {
      "name": "Zoom far out",
      "description": "Raises the maximum camera zoom distance.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FIntCameraMaxZoomDistance",
          "value": "9999"
        }
      ]
    },
    {
      "name": "Allow first-person mouse lock",
      "description": "Enables the mouse-lock (shift lock) toggle.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagUserAllowMouseLock",
          "value": "True"
        }
      ]
    },
    {
      "name": "Smaller network packets",
      "description": "Lowers the MTU to 1400 - sometimes helps on shaky connections.",
      "group": "NETWORK",
      "category": "network",
      "categoryLabel": "Network",
      "categoryOrder": 6,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntConnectionMTUSize",
          "value": "1400"
        }
      ]
    },
    {
      "name": "Roblox telemetry off",
      "description": "Turns off the client\u0027s telemetry reports.",
      "group": "NETWORK",
      "category": "network",
      "categoryLabel": "Network",
      "categoryOrder": 6,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagDebugDisableTelemetryEphemeralCounter",
          "value": "True"
        },
        {
          "key": "FFlagDebugDisableTelemetryEphemeralStat",
          "value": "True"
        },
        {
          "key": "FFlagDebugDisableTelemetryEventIngest",
          "value": "True"
        },
        {
          "key": "FFlagDebugDisableTelemetryPoint",
          "value": "True"
        },
        {
          "key": "FFlagDebugDisableTelemetryV2Counter",
          "value": "True"
        },
        {
          "key": "FFlagDebugDisableTelemetryV2Event",
          "value": "True"
        },
        {
          "key": "FFlagDebugDisableTelemetryV2Stat",
          "value": "True"
        }
      ]
    },
    {
      "name": "No bloom or clouds",
      "description": "Community lists call this \u0027no bloom\u0027 - the meaning of the value is not documented anywhere solid.",
      "group": "EFFECTS \u0026 RENDERER",
      "category": "effects",
      "categoryLabel": "Effects",
      "categoryOrder": 3,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagRenderNoLowFrmBloom",
          "value": "False"
        }
      ]
    },
    {
      "name": "No loading blur",
      "description": "Removes the blur behind the loading and disconnect screens.",
      "group": "EFFECTS \u0026 RENDERER",
      "category": "effects",
      "categoryLabel": "Effects",
      "categoryOrder": 3,
      "allowlisted": false,
      "flags": [
        {
          "key": "FIntRobloxGuiBlurIntensity",
          "value": "0"
        }
      ]
    },
    {
      "name": "Material textures off",
      "description": "Empties all four material texture tables (parts and terrain, both years) - flat colours everywhere. Community lists warn this can break glass.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": false,
      "flags": [
        {
          "key": "FStringPartTexturePackTable2022",
          "value": ""
        },
        {
          "key": "FStringPartTexturePackTablePre2022",
          "value": ""
        },
        {
          "key": "FStringTerrainMaterialTable2022",
          "value": ""
        },
        {
          "key": "FStringTerrainMaterialTablePre2022",
          "value": ""
        }
      ]
    },
    {
      "name": "Old material textures (2022)",
      "description": "The historical way back to the pre-2022 material look: an empty part-texture table plus the workaround flag. Bloxstrap noted Roblox planned to drop this - it may do nothing now.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": false,
      "flags": [
        {
          "key": "FStringPartTexturePackTable2022",
          "value": ""
        },
        {
          "key": "FFlagMSRefactor4",
          "value": "False"
        }
      ]
    },
    {
      "name": "No player clothing textures",
      "description": "Skips compositing clothing textures onto avatars. Clothes turn into flat colours - a real difference on crowded servers.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTextureCompositorActiveJobs",
          "value": "0"
        }
      ]
    },
    {
      "name": "Skip mipmaps",
      "description": "Makes the texture manager skip the smaller texture levels. Sharper up close, more aliasing in the distance - and values below 0 remove textures altogether.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": false,
      "flags": [
        {
          "key": "FIntDebugTextureManagerSkipMips",
          "value": "0"
        }
      ]
    },
    {
      "name": "Legacy in-game menu",
      "description": "Brings back the older in-game menu. Bloxstrap warns it is no longer maintained, controls may misbehave, and the newer Chrome menu can override it anyway.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagDisableNewIGMinDUA",
          "value": "True"
        },
        {
          "key": "FFlagEnableInGameMenuControls",
          "value": "False"
        },
        {
          "key": "FFlagEnableInGameMenuModernization",
          "value": "False"
        }
      ]
    },
    {
      "name": "Chrome in-game menu",
      "description": "Forces the newer Chrome-style menu. A community list warns some games may fail to load with it.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagEnableInGameMenuChrome",
          "value": "True"
        }
      ]
    },
    {
      "name": "GUI hide buttons",
      "description": "Shows the toggles that hide the game\u0027s own interface and the core GUI. The group-id variant of this feature needs membership in a Roblox group.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "GuiHidingApiSupport2",
          "value": "True"
        },
        {
          "key": "FFlagUserShowGuiHideToggles",
          "value": "True"
        }
      ]
    },
    {
      "name": "New camera mode",
      "description": "Turns on the newer camera controls.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagNewCameraControls",
          "value": "True"
        }
      ]
    },
    {
      "name": "Fewer particles in first person",
      "description": "Hides avatar-item particles while you are in first person - less clutter on the screen.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagUserHideCharacterParticlesInFirstPerson",
          "value": "True"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 90",
      "description": "Sits between the usual 60 and 144 steps.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "90"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 165",
      "description": "For 165 Hz screens.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "165"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 360",
      "description": "Only worth it on a very fast screen - Bloxstrap advises against going far above 240.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "360"
        }
      ]
    },
    {
      "name": "Graphics level 5",
      "description": "A step above the lowest level: water and distance stay cheap, the view gets less bare.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "5"
        }
      ]
    },
    {
      "name": "Graphics level 10",
      "description": "The middle of the range - the usual balanced setting.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "10"
        }
      ]
    },
    {
      "name": "Graphics level 15",
      "description": "Nearly the top, without the most expensive extras.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "15"
        }
      ]
    },
    {
      "name": "Shadows at half strength",
      "description": "Keeps a little depth instead of removing the shadows completely.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": false,
      "flags": [
        {
          "key": "FIntRenderShadowIntensity",
          "value": "0.5"
        }
      ]
    },
    {
      "name": "Shadows very soft",
      "description": "A quarter of the usual shadow strength.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": false,
      "flags": [
        {
          "key": "FIntRenderShadowIntensity",
          "value": "0.25"
        }
      ]
    },
    {
      "name": "Short grass draw distance",
      "description": "Keeps grass only near the camera (300 studs) - the middle ground between full and none.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "300"
        }
      ]
    },
    {
      "name": "Textures: medium",
      "description": "One step above the coarsest textures.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFFlagTextureQualityOverrideEnabled",
          "value": "True"
        },
        {
          "key": "DFIntTextureQualityOverride",
          "value": "1"
        }
      ]
    },
    {
      "name": "Textures: good",
      "description": "Two steps above the coarsest textures, one below the sharpest.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFFlagTextureQualityOverrideEnabled",
          "value": "True"
        },
        {
          "key": "DFIntTextureQualityOverride",
          "value": "2"
        }
      ]
    },
    {
      "name": "Anti-aliasing 2\u00D7",
      "description": "Two MSAA samples - between rough edges and the smooth 4\u00D7 setting.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntDebugForceMSAASamples",
          "value": "2"
        }
      ]
    },
    {
      "name": "Old material textures - fallback",
      "description": "Bloxstrap\u0027s second step if the 2022 way back does not stick: switches the newer material refactor off as well.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagMSRefactor5",
          "value": "False"
        }
      ]
    },
    {
      "name": "No forced Chrome menu (AB test)",
      "description": "The other half of the menu workaround - also switches the newer menu test off.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagEnableInGameMenuChromeABTest2",
          "value": "False"
        }
      ]
    },
    {
      "name": "No forced report dialog (AB test)",
      "description": "Bloxstrap pairs this with the menu workaround: it also stops the newer report dialog from taking over.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FFlagEnableReportAbuseMenuRoactABTest2",
          "value": "False"
        }
      ]
    },
    {
      "name": "GUI hiding for your own group",
      "description": "The group variant of the GUI hiding toggles. Replace the 0 with a Roblox group id you are a member of - with 0 nothing is unlocked.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntCanHideGuiGroupId",
          "value": "0"
        }
      ]
    },
    {
      "name": "Zoom even farther out",
      "description": "Raises the camera zoom to 20000 studs. Very far views can look thin - it comes straight back out.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FIntCameraMaxZoomDistance",
          "value": "20000"
        }
      ]
    },
    {
      "name": "Zoom up close",
      "description": "Lowers the maximum zoom so the camera stays near the character.",
      "group": "INTERFACE",
      "category": "interface",
      "categoryLabel": "Interface",
      "categoryOrder": 5,
      "allowlisted": false,
      "flags": [
        {
          "key": "FIntCameraMaxZoomDistance",
          "value": "200"
        }
      ]
    },
    {
      "name": "Even smaller network packets",
      "description": "MTU 1200 - the more aggressive step for a shaky connection.",
      "group": "NETWORK",
      "category": "network",
      "categoryLabel": "Network",
      "categoryOrder": 6,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntConnectionMTUSize",
          "value": "1200"
        }
      ]
    },
    {
      "name": "Standard network packets",
      "description": "Back to MTU 1500, the Windows default.",
      "group": "NETWORK",
      "category": "network",
      "categoryLabel": "Network",
      "categoryOrder": 6,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntConnectionMTUSize",
          "value": "1500"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 75",
      "description": "A middle step for 75 Hz screens and older laptops.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "75"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 120",
      "description": "The usual choice for a 120 Hz screen.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "120"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 200",
      "description": "For 200 Hz screens - above the 240 warning line only when the GPU can keep up.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "200"
        }
      ]
    },
    {
      "name": "Graphics level 2",
      "description": "One step above the lowest quality - still very cheap.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "2"
        }
      ]
    },
    {
      "name": "Graphics level 3",
      "description": "A small step up from the lowest quality.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "3"
        }
      ]
    },
    {
      "name": "Graphics level 4",
      "description": "Low quality with a little more distance detail.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "4"
        }
      ]
    },
    {
      "name": "Graphics level 7",
      "description": "Lower-middle quality for older hardware.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "7"
        }
      ]
    },
    {
      "name": "Graphics level 8",
      "description": "A balanced low setting.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "8"
        }
      ]
    },
    {
      "name": "Graphics level 12",
      "description": "Slightly above the middle of the quality range.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "12"
        }
      ]
    },
    {
      "name": "Graphics level 16",
      "description": "High quality without the top few expensive extras.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "16"
        }
      ]
    },
    {
      "name": "Graphics level 18",
      "description": "Very high quality for a strong GPU.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "18"
        }
      ]
    },
    {
      "name": "Graphics level 20",
      "description": "One step below the maximum quality.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "20"
        }
      ]
    },
    {
      "name": "Grass draw distance 100",
      "description": "Only grass right under the camera - the most aggressive grass saving.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "100"
        }
      ]
    },
    {
      "name": "Grass draw distance 500",
      "description": "Grass a little farther out than the short setting.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "500"
        }
      ]
    },
    {
      "name": "Grass draw distance 1000",
      "description": "Keeps a useful ring of grass without filling the whole view.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "1000"
        }
      ]
    },
    {
      "name": "Distant meshes: medium detail",
      "description": "A middle level-of-detail range for maps with far-away unions.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistance",
          "value": "500"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL12",
          "value": "1000"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL23",
          "value": "1500"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL34",
          "value": "2000"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 48",
      "description": "For very weak hardware or a stable cinematic pace.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "48"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 72",
      "description": "A small step above 60 for 72 Hz panels.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "72"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 100",
      "description": "For 100 Hz screens and laptops that cannot hold 120.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "100"
        }
      ]
    },
    {
      "name": "Cap the frame rate at 160",
      "description": "For 160 Hz screens - below the 240 warning line.",
      "group": "FRAME RATE",
      "category": "frame-rate",
      "categoryLabel": "FPS",
      "categoryOrder": 0,
      "allowlisted": false,
      "flags": [
        {
          "key": "DFIntTaskSchedulerTargetFps",
          "value": "160"
        }
      ]
    },
    {
      "name": "Graphics level 6",
      "description": "Low quality with a little more headroom than level 5.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "6"
        }
      ]
    },
    {
      "name": "Graphics level 9",
      "description": "Just below the balanced level 10.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "9"
        }
      ]
    },
    {
      "name": "Graphics level 11",
      "description": "Just above the balanced level 10.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "11"
        }
      ]
    },
    {
      "name": "Graphics level 13",
      "description": "A comfortable middle-high setting for most GPUs.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "13"
        }
      ]
    },
    {
      "name": "Graphics level 17",
      "description": "High quality without the last four expensive steps.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "17"
        }
      ]
    },
    {
      "name": "Grass draw distance 200",
      "description": "A very short ring of grass - almost the grass-off saving.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "200"
        }
      ]
    },
    {
      "name": "Grass draw distance 750",
      "description": "Between the short 500 and the broad 1000 setting.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "750"
        }
      ]
    },
    {
      "name": "Shadows at quarter strength",
      "description": "Keeps a faint shadow instead of removing it completely.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": false,
      "flags": [
        {
          "key": "FIntRenderShadowIntensity",
          "value": "0.25"
        }
      ]
    },
    {
      "name": "Distant meshes: near detail",
      "description": "A tighter level-of-detail range than medium - still smoother than dropping detail immediately.",
      "group": "TEXTURES \u0026 DETAIL",
      "category": "textures",
      "categoryLabel": "Textures",
      "categoryOrder": 4,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistance",
          "value": "250"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL12",
          "value": "500"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL23",
          "value": "750"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL34",
          "value": "1000"
        }
      ]
    },
    {
      "name": "Low-end preset",
      "description": "The strongest safe combination: lowest graphics, coarse textures, no MSAA, no grass and early CSG detail.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "1"
        },
        {
          "key": "DFFlagTextureQualityOverrideEnabled",
          "value": "True"
        },
        {
          "key": "DFIntTextureQualityOverride",
          "value": "0"
        },
        {
          "key": "FIntDebugForceMSAASamples",
          "value": "1"
        },
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "0"
        },
        {
          "key": "FIntFRMMinGrassDistance",
          "value": "0"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistance",
          "value": "0"
        }
      ]
    },
    {
      "name": "Balanced preset",
      "description": "Quality 10, good textures, 2\u00D7 MSAA, a short grass ring and moderate CSG detail.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "10"
        },
        {
          "key": "DFFlagTextureQualityOverrideEnabled",
          "value": "True"
        },
        {
          "key": "DFIntTextureQualityOverride",
          "value": "2"
        },
        {
          "key": "FIntDebugForceMSAASamples",
          "value": "2"
        },
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "300"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistance",
          "value": "1000"
        }
      ]
    },
    {
      "name": "Max quality preset",
      "description": "Quality 21, sharp textures, 4\u00D7 MSAA and detailed far meshes - the heaviest safe combination here.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "21"
        },
        {
          "key": "DFFlagTextureQualityOverrideEnabled",
          "value": "True"
        },
        {
          "key": "DFIntTextureQualityOverride",
          "value": "3"
        },
        {
          "key": "FIntDebugForceMSAASamples",
          "value": "4"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistance",
          "value": "4000"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL12",
          "value": "4000"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL23",
          "value": "4000"
        },
        {
          "key": "DFIntCSGLevelOfDetailSwitchingDistanceL34",
          "value": "4000"
        }
      ]
    },
    {
      "name": "PvP visibility preset",
      "description": "Low-but-readable graphics with no grass, coarse textures and no MSAA - fewer distractions in a fight.",
      "group": "GRAPHICS",
      "category": "graphics",
      "categoryLabel": "Graphics",
      "categoryOrder": 1,
      "allowlisted": true,
      "flags": [
        {
          "key": "DFIntDebugFRMQualityLevelOverride",
          "value": "5"
        },
        {
          "key": "DFFlagTextureQualityOverrideEnabled",
          "value": "True"
        },
        {
          "key": "DFIntTextureQualityOverride",
          "value": "0"
        },
        {
          "key": "FIntDebugForceMSAASamples",
          "value": "1"
        },
        {
          "key": "FIntFRMMaxGrassDistance",
          "value": "0"
        },
        {
          "key": "FIntFRMMinGrassDistance",
          "value": "0"
        }
      ]
    }
  ],
  "flags": [
    {
      "key": "DFFlagDebugPauseVoxelizer",
      "values": [
        "True"
      ],
      "presets": [
        "Voxel lighting off"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "DFFlagDebugRenderForceTechnologyVoxel",
      "values": [
        "True"
      ],
      "presets": [
        "Force voxel lighting"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "DFFlagDisableDPIScale",
      "values": [
        "True"
      ],
      "presets": [
        "DPI scaling off"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "DFFlagTextureQualityOverrideEnabled",
      "values": [
        "True"
      ],
      "presets": [
        "Coarse textures",
        "Sharp textures",
        "Textures: medium",
        "Textures: good",
        "Low-end preset",
        "Balanced preset",
        "Max quality preset",
        "PvP visibility preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "DFIntCanHideGuiGroupId",
      "values": [
        "0"
      ],
      "presets": [
        "GUI hiding for your own group"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "DFIntConnectionMTUSize",
      "values": [
        "1400",
        "1200",
        "1500"
      ],
      "presets": [
        "Smaller network packets",
        "Even smaller network packets",
        "Standard network packets"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "DFIntCSGLevelOfDetailSwitchingDistance",
      "values": [
        "1000",
        "0",
        "500",
        "250",
        "4000"
      ],
      "presets": [
        "Distant meshes stay detailed",
        "Distant meshes drop detail early",
        "Distant meshes: medium detail",
        "Distant meshes: near detail",
        "Low-end preset",
        "Balanced preset",
        "Max quality preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "DFIntCSGLevelOfDetailSwitchingDistanceL12",
      "values": [
        "2000",
        "0",
        "1000",
        "500",
        "4000"
      ],
      "presets": [
        "Distant meshes stay detailed",
        "Distant meshes drop detail early",
        "Distant meshes: medium detail",
        "Distant meshes: near detail",
        "Max quality preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "DFIntCSGLevelOfDetailSwitchingDistanceL23",
      "values": [
        "3000",
        "0",
        "1500",
        "750",
        "4000"
      ],
      "presets": [
        "Distant meshes stay detailed",
        "Distant meshes drop detail early",
        "Distant meshes: medium detail",
        "Distant meshes: near detail",
        "Max quality preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "DFIntCSGLevelOfDetailSwitchingDistanceL34",
      "values": [
        "4000",
        "0",
        "2000",
        "1000"
      ],
      "presets": [
        "Distant meshes stay detailed",
        "Distant meshes drop detail early",
        "Distant meshes: medium detail",
        "Distant meshes: near detail",
        "Max quality preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "DFIntDebugFRMQualityLevelOverride",
      "values": [
        "1",
        "21",
        "5",
        "10",
        "15",
        "2",
        "3",
        "4",
        "7",
        "8",
        "12",
        "16",
        "18",
        "20",
        "6",
        "9",
        "11",
        "13",
        "17"
      ],
      "presets": [
        "Force the lowest graphics level",
        "Force the highest graphics level",
        "Graphics level 5",
        "Graphics level 10",
        "Graphics level 15",
        "Graphics level 2",
        "Graphics level 3",
        "Graphics level 4",
        "Graphics level 7",
        "Graphics level 8",
        "Graphics level 12",
        "Graphics level 16",
        "Graphics level 18",
        "Graphics level 20",
        "Graphics level 6",
        "Graphics level 9",
        "Graphics level 11",
        "Graphics level 13",
        "Graphics level 17",
        "Low-end preset",
        "Balanced preset",
        "Max quality preset",
        "PvP visibility preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "DFIntTaskSchedulerTargetFps",
      "values": [
        "30",
        "60",
        "144",
        "240",
        "10000",
        "90",
        "165",
        "360",
        "75",
        "120",
        "200",
        "48",
        "72",
        "100",
        "160"
      ],
      "presets": [
        "Cap the frame rate at 30",
        "Cap the frame rate at 60",
        "Cap the frame rate at 144",
        "Cap the frame rate at 240",
        "Lift the frame rate cap",
        "Cap the frame rate at 90",
        "Cap the frame rate at 165",
        "Cap the frame rate at 360",
        "Cap the frame rate at 75",
        "Cap the frame rate at 120",
        "Cap the frame rate at 200",
        "Cap the frame rate at 48",
        "Cap the frame rate at 72",
        "Cap the frame rate at 100",
        "Cap the frame rate at 160"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "DFIntTextureCompositorActiveJobs",
      "values": [
        "0"
      ],
      "presets": [
        "No player clothing textures"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "DFIntTextureQualityOverride",
      "values": [
        "0",
        "3",
        "1",
        "2"
      ],
      "presets": [
        "Coarse textures",
        "Sharp textures",
        "Textures: medium",
        "Textures: good",
        "Low-end preset",
        "Balanced preset",
        "Max quality preset",
        "PvP visibility preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FFlagDebugDisableTelemetryEphemeralCounter",
      "values": [
        "True"
      ],
      "presets": [
        "Roblox telemetry off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugDisableTelemetryEphemeralStat",
      "values": [
        "True"
      ],
      "presets": [
        "Roblox telemetry off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugDisableTelemetryEventIngest",
      "values": [
        "True"
      ],
      "presets": [
        "Roblox telemetry off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugDisableTelemetryPoint",
      "values": [
        "True"
      ],
      "presets": [
        "Roblox telemetry off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugDisableTelemetryV2Counter",
      "values": [
        "True"
      ],
      "presets": [
        "Roblox telemetry off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugDisableTelemetryV2Event",
      "values": [
        "True"
      ],
      "presets": [
        "Roblox telemetry off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugDisableTelemetryV2Stat",
      "values": [
        "True"
      ],
      "presets": [
        "Roblox telemetry off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugDisplayFPS",
      "values": [
        "True"
      ],
      "presets": [
        "FPS counter"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugForceFutureIsBrightPhase2",
      "values": [
        "True"
      ],
      "presets": [
        "Force ShadowMap lighting"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugForceFutureIsBrightPhase3",
      "values": [
        "True"
      ],
      "presets": [
        "Force Future lighting"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugGraphicsPreferD3D11",
      "values": [
        "True"
      ],
      "presets": [
        "Direct3D 11 forced"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FFlagDebugGraphicsPreferD3D11FL10",
      "values": [
        "True"
      ],
      "presets": [
        "Direct3D 10 level"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDebugGraphicsPreferOpenGL",
      "values": [
        "True"
      ],
      "presets": [
        "OpenGL renderer"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FFlagDebugGraphicsPreferVulkan",
      "values": [
        "True"
      ],
      "presets": [
        "Vulkan renderer"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FFlagDebugSkyGray",
      "values": [
        "True"
      ],
      "presets": [
        "Grey sky"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FFlagDisableNewIGMinDUA",
      "values": [
        "True"
      ],
      "presets": [
        "Legacy in-game menu"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagDisablePostFx",
      "values": [
        "True"
      ],
      "presets": [
        "Post-processing off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagEnableInGameMenuChrome",
      "values": [
        "True"
      ],
      "presets": [
        "Chrome in-game menu"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagEnableInGameMenuChromeABTest2",
      "values": [
        "False"
      ],
      "presets": [
        "No forced Chrome menu (AB test)"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagEnableInGameMenuControls",
      "values": [
        "False"
      ],
      "presets": [
        "Legacy in-game menu"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagEnableInGameMenuModernization",
      "values": [
        "False"
      ],
      "presets": [
        "Legacy in-game menu"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagEnableReportAbuseMenuRoactABTest2",
      "values": [
        "False"
      ],
      "presets": [
        "No forced report dialog (AB test)"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagFastGPULightCulling3",
      "values": [
        "True"
      ],
      "presets": [
        "GPU light culling"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagHandleAltEnterFullscreenManually",
      "values": [
        "False"
      ],
      "presets": [
        "Alt\u002BEnter without fullscreen"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FFlagMSRefactor4",
      "values": [
        "False"
      ],
      "presets": [
        "Old material textures (2022)"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagMSRefactor5",
      "values": [
        "False"
      ],
      "presets": [
        "Old material textures - fallback"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagNewCameraControls",
      "values": [
        "True"
      ],
      "presets": [
        "New camera mode"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagNewLightAttenuation",
      "values": [
        "True"
      ],
      "presets": [
        "New light falloff"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagRenderNoLowFrmBloom",
      "values": [
        "False"
      ],
      "presets": [
        "No bloom or clouds"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagUserAllowMouseLock",
      "values": [
        "True"
      ],
      "presets": [
        "Allow first-person mouse lock"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagUserHideCharacterParticlesInFirstPerson",
      "values": [
        "True"
      ],
      "presets": [
        "Fewer particles in first person"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FFlagUserShowGuiHideToggles",
      "values": [
        "True"
      ],
      "presets": [
        "GUI hide buttons"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FIntCameraMaxZoomDistance",
      "values": [
        "9999",
        "20000",
        "200"
      ],
      "presets": [
        "Zoom far out",
        "Zoom even farther out",
        "Zoom up close"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FIntDebugForceMSAASamples",
      "values": [
        "1",
        "4",
        "2"
      ],
      "presets": [
        "Anti-aliasing off",
        "Anti-aliasing 4\u00D7",
        "Anti-aliasing 2\u00D7",
        "Low-end preset",
        "Balanced preset",
        "Max quality preset",
        "PvP visibility preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FIntDebugTextureManagerSkipMips",
      "values": [
        "0"
      ],
      "presets": [
        "Skip mipmaps"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FIntFRMMaxGrassDistance",
      "values": [
        "0",
        "300",
        "100",
        "500",
        "1000",
        "200",
        "750"
      ],
      "presets": [
        "Grass off",
        "Short grass draw distance",
        "Grass draw distance 100",
        "Grass draw distance 500",
        "Grass draw distance 1000",
        "Grass draw distance 200",
        "Grass draw distance 750",
        "Low-end preset",
        "Balanced preset",
        "PvP visibility preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FIntFRMMinGrassDistance",
      "values": [
        "0"
      ],
      "presets": [
        "Grass off",
        "Low-end preset",
        "PvP visibility preset"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FIntGrassMovementReducedMotionFactor",
      "values": [
        "0"
      ],
      "presets": [
        "Calm grass"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FIntRenderGrassDetailStrands",
      "values": [
        "0"
      ],
      "presets": [
        "Grass off"
      ],
      "allowlisted": true,
      "legacy": false
    },
    {
      "key": "FIntRenderShadowIntensity",
      "values": [
        "0",
        "0.5",
        "0.25"
      ],
      "presets": [
        "Shadows off",
        "Shadows at half strength",
        "Shadows very soft",
        "Shadows at quarter strength"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FIntRobloxGuiBlurIntensity",
      "values": [
        "0"
      ],
      "presets": [
        "No loading blur"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FStringPartTexturePackTable2022",
      "values": [
        ""
      ],
      "presets": [
        "Material textures off",
        "Old material textures (2022)"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FStringPartTexturePackTablePre2022",
      "values": [
        ""
      ],
      "presets": [
        "Material textures off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FStringTerrainMaterialTable2022",
      "values": [
        ""
      ],
      "presets": [
        "Material textures off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "FStringTerrainMaterialTablePre2022",
      "values": [
        ""
      ],
      "presets": [
        "Material textures off"
      ],
      "allowlisted": false,
      "legacy": true
    },
    {
      "key": "GuiHidingApiSupport2",
      "values": [
        "True"
      ],
      "presets": [
        "GUI hide buttons"
      ],
      "allowlisted": false,
      "legacy": true
    }
  ]
};
