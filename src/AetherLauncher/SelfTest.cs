using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AetherLauncher.Infrastructure;
using Microsoft.Web.WebView2.Core;
using AetherLauncher.Models;
using AetherLauncher.Services;

namespace AetherLauncher;






public static class SelfTest
{
    private sealed record Result(string Name, string State, string Detail)
    {
        public bool Failed => State == "FAIL";
    }

    public static int Run()
    {
        PrepareOutput();

        var results = new List<Result>();
        var tempRoot = Path.Combine(Path.GetTempPath(), "aether-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        var report = new StringBuilder();

        report.AppendLine();
        report.AppendLine("Aether Launcher - self-test");
        report.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}   Data folder: {AppPaths.DataDir}   Version: {typeof(SelfTest).Assembly.GetName().Version}");
        report.AppendLine(new string('-', 78));

        try
        {
            Directory.CreateDirectory(tempRoot);

            Test("Key storage (DPAPI + AES-256-GCM)", () =>
            {
                const string secret = "_|WARNING:-DO-NOT-SHARE|_test-value-123";
                var protectedValue = Dpapi.Protect(secret);
                var roundTrip = Dpapi.Unprotect(protectedValue);

                if (protectedValue.Contains(secret, StringComparison.Ordinal))
                    return ("FAIL", "The plaintext is still in the encrypted value.");
                if (!protectedValue.StartsWith("aeg2:", StringComparison.Ordinal))
                    return ("FAIL", "The AES-GCM format was not written.");
                if (roundTrip != secret)
                    return ("FAIL", "The decrypted value differs.");

                
                var payload = Convert.FromBase64String(protectedValue[5..]);
                payload[^1] ^= 0x5A;
                var tampered = "aeg2:" + Convert.ToBase64String(payload);
                if (Dpapi.Unprotect(tampered).Length > 0)
                    return ("FAIL", "A changed value was still decrypted.");

                
                var legacy = Dpapi.ProtectLegacy(secret);
                if (Dpapi.Unprotect(legacy) != secret)
                    return ("FAIL", "An old DPAPI value could no longer be read.");

                return ("OK", "AES-256-GCM with DPAPI protected key, tamper protection and old DPAPI fallback.");
            }, results);

            Test("Account storage (round-trip)", () =>
            {
                var store = new AccountStore(Path.Combine(tempRoot, "accounts.json"));
                store.Load();
                store.Upsert("cookie-test-value", new RobloxApi.AccountInfo(123456, "testuser", "Test User"));

                var reloaded = new AccountStore(Path.Combine(tempRoot, "accounts.json"));
                reloaded.Load();
                if (reloaded.Accounts.Count != 1) return ("FAIL", $"Expected 1 account, found {reloaded.Accounts.Count}.");
                if (reloaded.Accounts[0].Username != "testuser") return ("FAIL", "Username was lost.");
                if (!File.ReadAllText(Path.Combine(tempRoot, "accounts.json")).Contains("cookie-test-value"))
                    return ("OK", "Account saved and restored, cookie only encrypted on disk.");
                return ("FAIL", "The cookie is in plain text in the file!");
            }, results);

            Test("Old account keys are migrated", () =>
            {
                var file = Path.Combine(tempRoot, "legacy-accounts.json");
                JsonFile.Save(file, new List<AccountRecord>
                {
                    new()
                    {
                        UserId = 4242,
                        Username = "legacy",
                        CookieProtected = Dpapi.ProtectLegacy("legacy-cookie"),
                    },
                });

                var store = new AccountStore(file);
                store.Load();

                if (store.Accounts.Count != 1) return ("FAIL", "The old account is missing.");
                if (!File.ReadAllText(file).Contains("aeg2:", StringComparison.Ordinal))
                    return ("FAIL", "The old key was not upgraded to AES-GCM.");

                return store.GetCookie(store.Accounts[0]) == "legacy-cookie"
                    ? ("OK", "Old DPAPI value was read and re-encrypted during load.")
                    : ("FAIL", "The migrated key no longer fits.");
            }, results);

            Test("Saved accounts decryptable", () =>
            {
                var store = new AccountStore();
                store.Load();
                if (store.Accounts.Count == 0) return ("OK", "No accounts saved yet - nothing to check.");

                var broken = store.Accounts.Where(a => store.GetCookie(a) is null).Select(a => a.Username).ToList();
                return broken.Count == 0
                    ? ("OK", $"{store.Accounts.Count} account(s), each key could be decrypted using the vault.")
                    : ("FAIL", $"Undecipherable: {string.Join(", ", broken)}");
            }, results);

            Test("Place storage (round-trip)", () =>
            {
                var store = new PlaceStore(Path.Combine(tempRoot, "places.json"));
                store.Load();
                store.Add(1818, "abc123", "Test place");

                var reloaded = new PlaceStore(Path.Combine(tempRoot, "places.json"));
                reloaded.Load();
                var place = reloaded.Find(1818);
                return place is not null && place.AccessCode == "abc123"
                    ? ("OK", "Place ID and access code are retained.")
                    : ("FAIL", "The place was not saved correctly.");
            }, results);

            Test("Roblox sign-in (WebView2)", () =>
            {
                try
                {
                    var runtime = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    return string.IsNullOrWhiteSpace(runtime)
                        ? ("SKIP", "WebView2 runtime is missing - the insert path remains available.")
                        : ("OK", $"WebView2 {runtime} is installed; sign-in happens on Roblox's own page.");
                }
                catch (Exception ex)
                {
                    return ("SKIP", $"WebView2 not available: {ex.Message} - the insertion path remains.");
                }
            }, results);

            Test("Login window opens", () =>
            {
                
                
                
                var previous = Application.Current.ShutdownMode;
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var window = new RobloxLoginWindow();
                try
                {
                    return window.Cookie is null
                        ? ("OK", "Window, theme and the embedded browser control build on.")
                        : ("FAIL", "The window already delivers a cookie without logging in.");
                }
                finally
                {
                    window.Close();
                    Application.Current.ShutdownMode = previous;
                }
            }, results);

            Test("Find the Roblox client", () =>
            {
                var settings = new LauncherSettings();
                var version = RobloxInstall.Resolve(settings, out var roots);
                if (version is null) return ("FAIL", $"No client found. Searched in: {roots}");
                if (!File.Exists(version.PlayerExe)) return ("FAIL", "Found version does not contain RobloxPlayerBeta.exe.");
                var count = RobloxInstall.Discover(Path.GetDirectoryName(Path.GetDirectoryName(version.Directory))).Count;
                return ("OK", $"{version.Guid} ({count} version(s) installed)");
            }, results);

            Test("Start URI for an account", () =>
            {
                var uri = RobloxUri.BuildPlayUri(1818, "accesscode-1", "TESTTICKET", "production", 4242);
                var checks = new Dictionary<string, bool>
                {
                    ["launchmode:play"] = uri.Contains("+launchmode:play", StringComparison.Ordinal),
                    ["Ticket in game info"] = uri.Contains("+gameinfo:TESTTICKET", StringComparison.Ordinal),
                    ["browsertrackerid"] = uri.Contains("+browsertrackerid:4242", StringComparison.Ordinal),
                    ["placelauncherurl encoded"] = uri.Contains("placelauncherurl:https%3A%2F%2Fassetgame.roblox.com", StringComparison.Ordinal),
                    ["Channel empty in production"] = uri.EndsWith("+channel:", StringComparison.Ordinal),
                };

                var broken = checks.Where(c => !c.Value).Select(c => c.Key).ToList();
                return broken.Count == 0
                    ? ("OK", "Structure, encoding, and channel are correct.")
                    : ("FAIL", $"Failed checks: {string.Join(", ", broken)}");
            }, results);

            Test("Start URI for a specific server", () =>
            {
                var uri = RobloxUri.BuildPlayUri(1818, null, "TESTTICKET", "production", 4242, "JOB-123");
                var checks = new Dictionary<string, bool>
                {
                    ["RequestGameJob"] = uri.Contains("request%3DRequestGameJob", StringComparison.Ordinal),
                    ["gameId"] = uri.Contains("gameId%3DJOB-123", StringComparison.Ordinal),
                    ["no access code"] = !uri.Contains("accessCode", StringComparison.Ordinal),
                };

                var broken = checks.Where(c => !c.Value).Select(c => c.Key).ToList();
                return broken.Count == 0
                    ? ("OK", "The job ID ends up in the start URI as RequestGameJob.")
                    : ("FAIL", $"Failed checks: {string.Join(", ", broken)}");
            }, results);

            Test("Input recognition (ID/URL/Link)", () =>
            {
                var cases = new (string Input, long Expected, string? Code)[]
                {
                    ("1818", 1818, null),
                    ("https://www.roblox.com/games/1818/Classic-Crossroads", 1818, null),
                    ("roblox://experiences/start?placeId=2753915549", 2753915549, null),
                    ("https://www.roblox.com/games/1818/Name?privateServerLinkCode=ABC123", 1818, "ABC123"),
                };

                var failures = new List<string>();
                foreach (var (input, expected, code) in cases)
                {
                    if (!RobloxUri.TryParsePlace(input, out var placeId, out var accessCode) || placeId != expected)
                        failures.Add($"'{input}' not recognized");
                    else if (code is not null && accessCode != code)
                        failures.Add($"Access code is missing for '{input}'");
                }

                return failures.Count == 0
                    ? ("OK", $"{cases.Length} input formats parsed correctly.")
                    : ("FAIL", string.Join("; ", failures));
            }, results);

            Test("FastFlags (JSON and file)", () =>
            {
                const string json = """
                    {
                      // Comments and trailing commas are allowed
                      "DFIntTaskSchedulerTargetFps": 144,
                      "FFlagDisablePostFx": true,
                    }
                    """;

                var error = FastFlags.ParseJson(json, out var flags);
                if (error is not null) return ("FAIL", error);
                if (flags.Count != 2) return ("FAIL", $"Expects 2 flags, read {flags.Count}.");
                if (flags["DFIntTaskSchedulerTargetFps"] != "144") return ("FAIL", "Number was not accepted.");
                if (flags["FFlagDisablePostFx"] != "True") return ("FAIL", "Bool was not adopted.");

                var versionDir = Path.Combine(tempRoot, "version-test");
                Directory.CreateDirectory(Path.Combine(versionDir, "ClientSettings"));
                var existing = Path.Combine(versionDir, "ClientSettings", "ClientAppSettings.json");
                File.WriteAllText(existing, "{ \"FremdesFlag\": \"True\" }");

                if (!FastFlags.ApplyTo(versionDir, flags, out var message)) return ("FAIL", message);

                var written = File.ReadAllText(existing);
                using var doc = System.Text.Json.JsonDocument.Parse(written);
                if (!doc.RootElement.TryGetProperty("FFlagDisablePostFx", out _)) return ("FAIL", "The file does not contain the flag.");
                if (!File.Exists(existing + ".aetherbak")) return ("FAIL", "The existing file was not backed up.");

                return ("OK", $"JSON checked, {message}, external file saved.");
            }, results);

            Test("FastFlags (all suggestions)", () =>
            {
                var problems = FastFlags.ValidatePresets();
                if (problems.Count > 0) return ("FAIL", string.Join("; ", problems));

                var versionDir = Path.Combine(tempRoot, "version-presets");
                var flags = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var preset in FastFlags.Presets) FastFlags.MergePreset(flags, preset);

                if (!FastFlags.ApplyTo(versionDir, flags, out var message)) return ("FAIL", message);

                var file = Path.Combine(versionDir, "ClientSettings", "ClientAppSettings.json");
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                var readBack = doc.RootElement.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => property.Value.GetString() ?? "",
                    StringComparer.Ordinal);

                var missing = flags.Where(pair => !readBack.TryGetValue(pair.Key, out var value) ||
                                                  !string.Equals(value, pair.Value, StringComparison.Ordinal))
                    .Select(pair => pair.Key)
                    .ToList();

                return missing.Count == 0
                    ? ("OK", $"{FastFlags.Presets.Count} suggestions, {flags.Count} flags written and read back completely.")
                    : ("FAIL", $"Not read back correctly: {string.Join(", ", missing)}");
            }, results);

            Test("Multi-instance (hold singleton)", () =>
            {
                if (MultiInstance.IsClientRunning())
                    return ("SKIP", "Roblox is currently running - test would disrupt a running client.");

                
                
                
                Mutex? mutex;
                try
                {
                    mutex = new Mutex(false, "ROBLOX_singletonMutex");
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    return ("SKIP", "The singleton already exists as another kernel object type (e.g. another launcher).");
                }
                catch (UnauthorizedAccessException)
                {
                    return ("SKIP", "The singleton belongs to another process - the test leaves it alone.");
                }
                catch (Exception ex)
                {
                    return ("SKIP", $"The singleton could not be created here: {ex.Message}");
                }

                using (mutex)
                {
                    bool owned;
                    try
                    {
                        owned = mutex.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        owned = true; 
                    }

                    if (!owned)
                        return ("SKIP", "The singleton is already occupied (multi-instance guard or client running).");

                    mutex.ReleaseMutex();
                }

                return ("OK", "Singleton mutex can be taken over and released again.");
            }, results);

            Test("Secrets in the log are masked", () =>
            {
                var uri = RobloxUri.BuildPlayUri(1818, null, "SECRET-TICKET", "production", 1);
                var redacted = Log.Redact($"Starting: {uri}");
                if (redacted.Contains("SECRET-TICKET", StringComparison.Ordinal)) return ("FAIL", "The ticket is in the log.");
                if (!redacted.Contains("gameinfo:<ticket>", StringComparison.Ordinal)) return ("FAIL", "The masking is not visible.");
                if (Log.Redact("_|WARNING:-DO-NOT-SHARE|_abcdef;").Contains("abcdef", StringComparison.Ordinal))
                    return ("FAIL", "The cookie is in the log.");
                return ("OK", "Ticket and cookie are replaced by placeholders.");
            }, results);

            Test("Protocol handler (read access)", () =>
            {
                var current = ProtocolHandler.ReadCurrentCommand();
                return current is null
                    ? ("SKIP", "HKCU\\\\Software\\\\Classes\\\\roblox-player does not exist.")
                    : ("OK", $"Current handler: {Log.Redact(current)}");
            }, results);

            Test("Application pointer arrow (Assets/cursor.cur)", () =>
            {
                var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/cursor.cur"));
                if (resource is null) return ("FAIL", "Assets/cursor.cur is missing from the program.");

                using var stream = resource.Stream;
                var bytes = new byte[stream.Length];
                var read = stream.Read(bytes, 0, bytes.Length);

                if (read < 6 || bytes[0] != 0 || bytes[1] != 0 || bytes[2] != 2)
                    return ("FAIL", "The header is not a cursor container (ICONDIR).");

                var frames = bytes[4] | (bytes[5] << 8);
                if (frames < 1) return ("FAIL", "The container does not contain a frame.");

                
                var size = bytes[6] == 0 ? 256 : bytes[6];
                var hotX = bytes[10] | (bytes[11] << 8);
                var hotY = bytes[12] | (bytes[13] << 8);
                var length = BitConverter.ToInt32(bytes, 14);
                var offset = BitConverter.ToInt32(bytes, 18);

                if (offset + length > bytes.Length) return ("FAIL", "The frame points beyond the file.");
                if (hotX >= size || hotY >= size) return ("FAIL", $"The click point ({hotX},{hotY}) is outside {size}x{size}.");

                
                var isPng = bytes[offset] == 0x89 && bytes[offset + 1] == 0x50;
                if (!isPng) return ("FAIL", "The frame is not a PNG.");

                using var frameStream = new MemoryStream(bytes, offset, length);
                var frame = BitmapFrame.Create(frameStream);
                if (frame.PixelWidth != size) return ("FAIL", $"The frame is {frame.PixelWidth}px wide, in the head it says {size}.");

                
                using var cursorStream = new MemoryStream(bytes);
                using var cursor = new Cursor(cursorStream);

                return ("OK", $"{frames} Frames, {size}x{size}, hotspot {hotX},{hotY}");
            }, results);

            Test("In-game pointers (replace and get back)", () =>
            {
                var versionDir = Path.Combine(tempRoot, "version-cursor");
                var textures = Path.Combine(versionDir, @"content\textures");
                var keyboardMouse = Path.Combine(textures, @"Cursors\KeyboardMouse");
                Directory.CreateDirectory(keyboardMouse);

                
                
                var first = Path.Combine(textures, "ArrowCursor.png");
                var second = Path.Combine(keyboardMouse, "ArrowCursor.png");
                WriteSolidPng(first, 64, 0x20, 0x60, 0xC0);
                WriteSolidPng(second, 32, 0xC0, 0x60, 0x20);

                var originalFirst = File.ReadAllBytes(first);
                var originalSecond = File.ReadAllBytes(second);

                var source = BundledCursorImage();
                if (!ClientMods.ApplyCursor(versionDir, source, out var applyMessage)) return ("FAIL", applyMessage);

                var firstSize = ImageTool.SizeOf(first);
                var secondSize = ImageTool.SizeOf(second);
                if (firstSize != (64, 64)) return ("FAIL", $"The 64px texture is now {firstSize?.ToString() ?? "unreadable"}.");
                if (secondSize != (32, 32)) return ("FAIL", $"The 32px texture is now {secondSize}.");
                if (!File.Exists(first + ClientMods.BackupSuffix)) return ("FAIL", "The backup is missing.");
                if (ClientMods.CountModified(versionDir) != 2) return ("FAIL", "Not both textures were recognized as replaced.");
                if (File.ReadAllBytes(first).SequenceEqual(originalFirst)) return ("FAIL", "The texture has not been changed.");

                if (!ClientMods.RestoreCursor(versionDir, out var restoreMessage)) return ("FAIL", restoreMessage);
                if (!File.ReadAllBytes(first).SequenceEqual(originalFirst)) return ("FAIL", "The 64px texture is not back to its original state.");
                if (!File.ReadAllBytes(second).SequenceEqual(originalSecond)) return ("FAIL", "The 32px texture is not back to its original state.");
                if (File.Exists(first + ClientMods.BackupSuffix)) return ("FAIL", "The backup was not cleaned up.");

                return ("OK", $"{applyMessage} Afterwards: {restoreMessage}");
            }, results);

            Test("Vanilla mode does not touch the client", () =>
            {
                var versionDir = Path.Combine(tempRoot, "version-vanilla");
                Directory.CreateDirectory(versionDir);
                var exe = Path.Combine(versionDir, "RobloxPlayerBeta.exe");
                File.WriteAllText(exe, "");
                var version = new RobloxVersion("version-vanilla", versionDir, exe, DateTimeOffset.Now);

                var settings = new LauncherSettings
                {
                    RobloxRoot = tempRoot,
                    ApplyFastFlags = true,
                    InGameCursorEnabled = true,
                    MultiInstance = false,
                    VanillaMode = true,
                };

                var store = new AccountStore(Path.Combine(tempRoot, "accounts-vanilla.json"));
                store.Load();
                var launcher = new LaunchService(settings, store);
                launcher.Prepare(version);

                var settingsFile = Path.Combine(versionDir, "ClientSettings", "ClientAppSettings.json");
                if (File.Exists(settingsFile)) return ("FAIL", "In vanilla mode, ClientAppSettings.json was written.");

                settings.VanillaMode = false;
                FastFlags.ApplyTo(versionDir, new Dictionary<string, string> { ["FFlagTest"] = "True" }, out _);
                if (!File.Exists(settingsFile)) return ("FAIL", "Without vanilla mode, the test writes nothing - then it doesn't check anything.");

                return ("OK", "Vanilla leaves the client untouched, FastFlags continues to write normally.");
            }, results);

            Test("Version switch (pinned version)", () =>
            {
                var root = Path.Combine(tempRoot, "RobloxRoot");
                var oldDir = Path.Combine(root, "Versions", "version-aaaaaaaaaaaa");
                var newDir = Path.Combine(root, "Versions", "version-bbbbbbbbbbbb");
                foreach (var dir in new[] { oldDir, newDir })
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "RobloxPlayerBeta.exe"), "");
                }

                File.SetLastWriteTimeUtc(Path.Combine(oldDir, "RobloxPlayerBeta.exe"), DateTime.UtcNow.AddDays(-30));
                File.SetLastWriteTimeUtc(Path.Combine(newDir, "RobloxPlayerBeta.exe"), DateTime.UtcNow);

                var settings = new LauncherSettings { RobloxRoot = root };
                var all = RobloxInstall.DiscoverAll(settings);
                if (!all.Any(v => v.Guid == "version-aaaaaaaaaaaa") || !all.Any(v => v.Guid == "version-bbbbbbbbbbbb"))
                    return ("FAIL", "DiscoverAll cannot find the trial versions.");
                if (all[0].Guid != "version-bbbbbbbbbbbb")
                    return ("FAIL", $"The latest version is not at the top, but {all[0].Guid}.");

                settings.PinnedVersionGuid = "version-aaaaaaaaaaaa";
                var pinned = RobloxInstall.Resolve(settings, out _);
                if (pinned?.Guid != "version-aaaaaaaaaaaa") return ("FAIL", $"Pinned came {pinned?.Guid ?? "nothing"}.");

                
                
                settings.PinnedVersionGuid = "version-cccccccccccc";
                var fallback = RobloxInstall.Resolve(settings, out _);
                if (fallback?.Guid != all[0].Guid)
                    return ("FAIL", $"A missing pinned version falls on {fallback?.Guid ?? "nothing"} instead of {all[0].Guid}.");

                return ("OK", $"{all.Count} version(s); pinned {pinned.ShortGuid}, unknown falls back to {fallback.ShortGuid}.");
            }, results);

            Test("Load your own font", () =>
            {
                var fontFile = Directory.EnumerateFiles(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\Fonts", "*.ttf")
                    .FirstOrDefault(f => Path.GetFileName(f).Equals("consola.ttf", StringComparison.OrdinalIgnoreCase))
                    ?? Directory.EnumerateFiles(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\Fonts", "*.ttf").FirstOrDefault();

                if (fontFile is null) return ("SKIP", "There is no .ttf file on this system.");

                var target = Path.Combine(tempRoot, "fonts");
                var family = AppFonts.Import(fontFile, target);
                if (family is null) return ("FAIL", $"{Path.GetFileName(fontFile)} could not be read.");
                if (!File.Exists(Path.Combine(target, Path.GetFileName(fontFile)))) return ("FAIL", "The font file was not copied.");

                var list = AppFonts.InstalledFamilies();
                if (list.Count < 10) return ("FAIL", $"The font list is suspiciously short ({list.Count}).");

                
                var settings = new LauncherSettings { UiFontFamily = "Aether Doesn't Exist" };
                if (AppFonts.Resolve(settings).Source != AppFonts.DefaultFamily) return ("FAIL", "An unknown name does not fall back on Segoe UI.");

                settings.UiFontFamily = family;
                if (!string.Equals(AppFonts.Resolve(settings).Source, family, StringComparison.OrdinalIgnoreCase))
                    return ("FAIL", $"The loaded font {family} is not adopted.");

                return ("OK", $"{family} loaded, {list.Count} fonts visible, fallback to {AppFonts.DefaultFamily} checked.");
            }, results);

            Test("Write the suggestion immediately into JSON and take it back", () =>
            {
                var preset = FastFlags.Presets.First(p => p.Flags.Count > 1); 
                var flags = new Dictionary<string, string> { ["FFFlagBleibt"] = "True" };

                if (FastFlags.MergePreset(flags, preset) != preset.Flags.Count)
                    return ("FAIL", $"{preset.Name} does not set all of its flags.");

                foreach (var flag in preset.Flags)
                {
                    if (!flags.TryGetValue(flag.Key, out var value) || value != flag.Value)
                        return ("FAIL", $"{flag.Key} does not contain the value of the suggestion.");
                }

                if (!FastFlags.ContainsPreset(flags, preset)) return ("FAIL", "The proposal is not considered active.");

                
                var edited = preset.Flags[0];
                flags[edited.Key] = "self-set";
                var removed = FastFlags.RemovePreset(flags, preset);
                if (removed != preset.Flags.Count - 1)
                    return ("FAIL", $"Expects {preset.Flags.Count - 1} removed flags, removed {removed}.");
                if (flags.GetValueOrDefault(edited.Key) != "self-set")
                    return ("FAIL", "The self-set value was deleted.");
                if (!flags.ContainsKey("FFFlagBleibt")) return ("FAIL", "A foreign flag was also deleted.");
                if (FastFlags.ContainsPreset(flags, preset)) return ("FAIL", "The proposal is still considered active.");

                
                var json = FastFlags.Serialize(flags);
                if (FastFlags.ParseJson(json, out var again) is { } error) return ("FAIL", error);
                if (again.Count != flags.Count) return ("FAIL", "The written text has a different number of flags.");

                return ("OK", $"{preset.Name}: {preset.Flags.Count} Flags set, own values are retained.");
            }, results);

            Test("Start without game (Roblox app)", () =>
            {
                var uri = RobloxUri.BuildAppUri("TICKET", "production", 4242);
                var checks = new Dictionary<string, bool>
                {
                    ["launchmode:app"] = uri.Contains("+launchmode:app", StringComparison.Ordinal),
                    ["no launchmode:play"] = !uri.Contains("launchmode:play", StringComparison.Ordinal),
                    ["Ticket in game info"] = uri.Contains("+gameinfo:TICKET", StringComparison.Ordinal),
                    ["browsertrackerid"] = uri.Contains("+browsertrackerid:4242", StringComparison.Ordinal),
                    ["Channel empty in production"] = uri.EndsWith("+channel:", StringComparison.Ordinal),
                };

                var broken = checks.Where(c => !c.Value).Select(c => c.Key).ToList();
                if (broken.Count > 0) return ("FAIL", $"Failed checks: {string.Join(", ", broken)}");

                var withoutTicket = RobloxUri.BuildAppUri(null, "zb", 1);
                if (!withoutTicket.Contains("+gameinfo:+", StringComparison.Ordinal))
                    return ("FAIL", "Without an account, the empty game info field is missing.");
                if (!withoutTicket.EndsWith("+channel:zb", StringComparison.Ordinal))
                    return ("FAIL", "No other channel is taken over.");

                return ("OK", "App start URI carries the mode app, ticket and channel.");
            }, results);

            Test("Installer addresses of the builds", () =>
            {
                var live = RobloxVersions.InstallerUrl("version-5a21b87bbc2641cf");
                if (live != "https://setup.rbxcdn.com/version-5a21b87bbc2641cf-RobloxPlayerInstaller.exe")
                    return ("FAIL", $"Wrong address: {live}");

                var channel = RobloxVersions.InstallerUrl("version-5a21b87bbc2641cf", "zb");
                if (!channel.Contains("/channel/zb/", StringComparison.Ordinal))
                    return ("FAIL", $"The channel is missing in the address: {channel}");

                if (RobloxVersions.InstallerUrl("version-5a21b87bbc2641cf", "production") != live)
                    return ("FAIL", "production appends an unnecessary channel.");

                return ("OK", "Addresses for the LIVE branch and for your own channels are correct.");
            }, results);

            Test("Version lists (WEAO and DeployHistory, network)", () =>
            {
                var weao = RobloxVersions.GetWeaoAsync().GetAwaiter().GetResult();
                var history = RobloxVersions.GetDeployHistoryAsync().GetAwaiter().GetResult();

                if (weao.Count == 0 && history.Count == 0)
                    return ("SKIP", "No answer - probably offline.");

                if (weao.Count > 0 && weao.Any(b => !b.Guid.StartsWith("version-", StringComparison.Ordinal)))
                    return ("FAIL", "WEAO returned an entry without a version prefix.");

                
                
                var undated = weao.Where(b => b.Date is null).Select(b => b.Guid).ToList();
                if (undated.Count > 0) return ("FAIL", $"Without date: {string.Join(", ", undated)}");

                if (RobloxVersions.ParseTimestamp("10/27/2026, 4:01:04 PM UTC") is null)
                    return ("FAIL", "The WEAO timestamp is not read.");

                if (RobloxVersions.ParseTimestamp("3/11/2026 2:06:34 PM") is null)
                    return ("FAIL", "The DeployHistory timestamp is not read.");

                if (history.Count > 0)
                {
                    var newest = history.OrderByDescending(b => b.Date ?? DateTimeOffset.MinValue).First();
                    if (newest.Guid.Length < 24) return ("FAIL", $"Suspicious hash: {newest.Guid}");
                    if (string.IsNullOrWhiteSpace(newest.NumericVersion))
                        return ("FAIL", "The DeployHistory entry does not have a version number.");
                }

                if (history.Count > 0)
                {
                    var combined = new List<RobloxBuild>(weao.Concat(history))
                        .GroupBy(b => b.Guid, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderByDescending(b => b.Date ?? DateTimeOffset.MinValue)
                        .ToList();

                    var newest = combined[0];
                    if (newest.Date is null || newest.Date < DateTimeOffset.UtcNow.AddDays(-30))
                        return ("FAIL", $"The latest entry is {newest.Guid} from {newest.Date:dd.MM.yyyy}.");
                }

                return ("OK", $"WEAO: {weao.Count} tracked version(s){string.Join(", ", weao.Select(b => $" {b.ShortGuid} ({b.Date:yyyy-MM-dd})"))}; DeployHistory: {history.Count} Builds.");
            }, results);

            Test("FastFlags suggestions", () =>
            {
                
                var KeyWithoutPrefix = new HashSet<string>(StringComparer.Ordinal) { "GuiHidingApiSupport2" };

                var problems = new List<string>();
                var seen = new HashSet<string>();

                foreach (var preset in FastFlags.Presets)
                {
                    if (string.IsNullOrWhiteSpace(preset.Name) || string.IsNullOrWhiteSpace(preset.Group))
                        problems.Add($"'{preset.Name}' has no group.");
                    if (preset.Flags.Count == 0) problems.Add($"'{preset.Name}' does not set a flag.");

                    foreach (var flag in preset.Flags)
                    {
                        if (string.IsNullOrWhiteSpace(flag.Key))
                            problems.Add($"'{preset.Name}' has an empty entry.");

                        
                        
                        if (flag.Value is null ||
                            (flag.Value.Length == 0 && !flag.Key.StartsWith("FString", StringComparison.Ordinal)))
                            problems.Add($"'{preset.Name}' has an empty value at {flag.Key}.");

                        
                        
                        
                        var prefixed = flag.Key.StartsWith("F", StringComparison.Ordinal) ||
                                       flag.Key.StartsWith("D", StringComparison.Ordinal);
                        if (!prefixed && !KeyWithoutPrefix.Contains(flag.Key))
                            problems.Add($"'{flag.Key}' doesn't look like a FastFlag.");
                        seen.Add(flag.Key);
                    }
                }

                if (problems.Count > 0) return ("FAIL", string.Join("; ", problems.Take(3)));

                var groups = FastFlags.Presets.Select(p => p.Group).Distinct().Count();
                var flags = FastFlags.Presets.Sum(p => p.Flags.Count);
                return ("OK", $"{FastFlags.Presets.Count} suggestions in {groups} groups, {flags} flags, {seen.Count} various names.");
            }, results);

            Test("Allowlist marking of suggestions", () =>
            {
                var active = FastFlags.Presets.Count(p => p.Allowlisted);
                if (active < 8)
                    return ("FAIL", $"Only {active} suggestions are marked as still effective.");

                
                
                var caps = FastFlags.Presets.Where(p => p.Flags.Any(f => f.Key == FastFlags.FrameRateFlag)).ToList();
                if (caps.Count == 0) return ("FAIL", "No frame rate suggestion found.");
                if (caps.Any(p => p.Allowlisted))
                    return ("FAIL", "The frame rate limit is incorrectly considered allowlisted.");

                return ("OK", $"{active} of {FastFlags.Presets.Count} suggestions are still running; the frame rate limit is correctly marked as ignored.");
            }, results);

            Test("Switch rows in the overlay (textures, anti-aliasing, grass)", () =>
            {
                var file = AppPaths.FastFlagsFile;
                var before = File.Exists(file) ? File.ReadAllText(file) : null;

                try
                {
                    var set = FastFlags.TrySetMany(new Dictionary<string, string?>
                    {
                        [FastFlags.TextureToggleFlag] = "True",
                        [FastFlags.TextureQualityFlag] = "0",
                        [FastFlags.MsaaFlag] = "2",
                        ["FIntFRMMaxGrassDistance"] = "0",
                        ["FIntFRMMinGrassDistance"] = "0",
                    });

                    if (set is { } error) return ("FAIL", error);
                    if (FastFlags.ReadTextureQuality() != 0) return ("FAIL", "The texture quality didn't come back.");
                    if (FastFlags.ReadMsaa() != 2) return ("FAIL", "The anti-aliasing didn't come back.");

                    
                    if (FastFlags.TrySetMany(new Dictionary<string, string?> { ["FIntFRMMaxGrassDistance"] = null }) is { } remove)
                        return ("FAIL", remove);

                    if (FastFlags.Read("FIntFRMMaxGrassDistance") is not null)
                        return ("FAIL", "Grass on doesn't take out the flag.");

                    return ("OK", "Putting, reading and taking out all in one go.");
                }
                finally
                {
                    if (before is not null) File.WriteAllText(file, before);
                    else if (File.Exists(file)) File.Delete(file);
                }
            }, results);

            Test("Recently played (recent.json)", () =>
            {
                var file = Path.Combine(tempRoot, "recent.json");
                var store = new RecentStore(file);
                store.Load();

                store.Record(1818, null, "Crossroads", 312, "tester");
                store.Record(1818, null, "Crossroads", 312, "tester");
                if (store.Entries.Count != 1) return ("FAIL", $"The same place is duplicated: {store.Entries.Count} entries.");
                if (store.Entries[0].Sessions != 2) return ("FAIL", $"Sessions: {store.Entries[0].Sessions}");
                if (store.Entries[0].Name != "Crossroads") return ("FAIL", "The name was lost.");

                for (var index = 1; index <= RecentStore.Capacity + 3; index++)
                    store.Record(2000 + index, null, $"Game {index}", null, null);

                if (store.Entries.Count != RecentStore.Capacity)
                    return ("FAIL", $"The list grows beyond {RecentStore.Capacity}: {store.Entries.Count}");

                var reread = new RecentStore(file);
                reread.Load();
                if (reread.Entries.Count != RecentStore.Capacity)
                    return ("FAIL", "The list was not saved.");

                
                
                var imported = reread.Entries[0].PlaceId;
                if (!reread.Update(imported, "Carry the Glass Together!", 10415694629))
                    return ("FAIL", "A name added later was not adopted.");

                var third = new RecentStore(file);
                third.Load();
                if (third.Find(imported)?.Name != "Carry the Glass Together!")
                    return ("FAIL", "The added name was not saved.");
                if (third.Find(imported)?.UniverseId != 10415694629)
                    return ("FAIL", "The universe ID that was added was not saved.");

                var victim = reread.Entries[^1].PlaceId;
                if (!reread.Forget(victim) || reread.Entries.Count != RecentStore.Capacity - 1)
                    return ("FAIL", "An entry cannot be forgotten.");

                if (!reread.Clear() || reread.Entries.Count != 0)
                    return ("FAIL", "The list cannot be cleared.");

                return ("OK", $"Create, merge, cap at {RecentStore.Capacity}, forget, and clear.");
            }, results);

            Test("Roblox's own sessions from the logs", () =>
            {
                
                const string text = """
                    [FLog::Output] Joining game 'b198fcc5-922a-47e6-8f26-26e30fe86ebf' place 123974602339071 at 10.20.3.14
                    universeid:9271746647
                    [FLog::Output] Joining game '8bb25232-d08a-4d65-a9d5-c4801818c716' place 128451689942376 at 10.20.6.19
                    universeid:6760085372
                    """;

                if (!RobloxLogImport.TryParseSession(text, out var placeId, out var universeId))
                    return ("FAIL", "The join line was not recognized.");

                
                if (placeId != 128451689942376) return ("FAIL", $"Place ID: {placeId}");
                if (universeId != 6760085372) return ("FAIL", $"Universe ID: {universeId}");

                if (RobloxLogImport.TryParseSession("no join", out _, out _))
                    return ("FAIL", "Text without a join is treated as a session.");

                if (!RobloxLogImport.TryParseStamp(
                        "0.744.0.7441080_20261027T210619Z_Player_5DB91_last.log", out var stamp))
                    return ("FAIL", "The time in the file name was not read.");

                if (stamp.Year != 2026 || stamp.Month != 10 || stamp.Day != 27)
                    return ("FAIL", $"Date: {stamp:yyyy-MM-dd HH:mm}");

                if (RobloxLogImport.TryParseStamp("anything.log", out _))
                    return ("FAIL", "A file without a timestamp still provides one.");

                return ("OK", $"Last join from the log line ({placeId}) including timestamp.");
            }, results);

            Test("Current location and server from Roblox's log", () =>
            {
                var join = RobloxLogImport.LastJoin();
                if (join is not RobloxJoin { PlaceId: > 0 } current)
                    return ("SKIP", "No Roblox session found in log.");

                return string.IsNullOrWhiteSpace(current.JobId)
                    ? ("FAIL", $"Place {current.PlaceId} detected but no job ID.")
                    : ("OK", $"Place {current.PlaceId} on server {current.JobId[..Math.Min(8, current.JobId.Length)]}…");
            }, results);

            Test("Sort games with the mouse", () =>
            {
                var file = Path.Combine(tempRoot, "places-order.json");
                var store = new PlaceStore(file);
                store.Load();

                var first = store.Add(111, null, "First");
                var second = store.Add(222, null, "Second");
                var third = store.Add(333, null, "Third");

                
                if (!store.SetOrder(new[] { third, first, second }))
                    return ("FAIL", "The order could not be written.");

                if (third.SortIndex != 0 || first.SortIndex != 1 || second.SortIndex != 2)
                    return ("FAIL", $"Indices: {third.SortIndex}/{first.SortIndex}/{second.SortIndex}");

                var reread = new PlaceStore(file);
                reread.Load();

                var order = reread.Places.OrderBy(place => place.SortIndex).Select(place => place.PlaceId).ToList();
                if (!order.SequenceEqual(new long[] { 333, 111, 222 }))
                    return ("FAIL", $"Read: {string.Join(",", order)}");

                return ("OK", "Dragging writes the order and it remains in places.json.");
            }, results);

            Test("Add Roblox sessions to your recent list", () =>
            {
                var file = Path.Combine(tempRoot, "recent-import.json");
                var store = new RecentStore(file);
                store.Load();

                store.Record(1818, null, "Crossroads", 312, "tester");

                var older = DateTimeOffset.Now.AddHours(-5);
                if (!store.Import(999, 4711, older))
                    return ("FAIL", "It was not possible to take over someone else's meeting.");

                if (store.Import(999, 4711, older))
                    return ("FAIL", "The same place was taken over twice.");

                if (store.Import(1818, 312, DateTimeOffset.Now.AddHours(-1)))
                    return ("FAIL", "A custom game was overwritten by the log list.");

                var imported = store.Entries.FirstOrDefault(entry => entry.PlaceId == 999);
                if (imported is null || !imported.FromLog)
                    return ("FAIL", "The entry is not marked as a Roblox session.");

                
                if (store.Entries[0].PlaceId != 1818)
                    return ("FAIL", $"Order: {store.Entries[0].PlaceId}");

                var reread = new RecentStore(file);
                reread.Load();
                if (reread.Entries.Count != 2) return ("FAIL", "The imported session was not saved.");

                return ("OK", $"Imported sessions are added; saved games stay - {reread.Entries.Count} entries.");
            }, results);

            Test("Pin games (places.json)", () =>
            {
                var file = Path.Combine(tempRoot, "places-pin.json");
                var store = new PlaceStore(file);
                store.Load();

                var record = store.Add(1818, null, "Crossroads");
                if (record.IsPinned) return ("FAIL", "A new game is already pinned.");

                store.SetPinned(record, true);

                var reread = new PlaceStore(file);
                reread.Load();
                var pinned = reread.Find(1818);
                if (pinned is null || !pinned.IsPinned || pinned.PinnedAt is null)
                    return ("FAIL", "The pin was not saved.");

                reread.SetPinned(pinned, false);
                var unpinned = new PlaceStore(file);
                unpinned.Load();
                if (unpinned.Find(1818) is not { IsPinned: false, PinnedAt: null })
                    return ("FAIL", "Unpinning was not saved.");

                return ("OK", "Pinning and unpinning remain in places.json.");
            }, results);

            Test("Location of the in-game overlay", () =>
            {
                
                
                
                var file = Path.Combine(tempRoot, "overlay.json");
                if (File.Exists(file)) File.Delete(file);

                if (OverlayPlacement.Load(file).Panels.Count != 0)
                    return ("FAIL", "A fresh file already contains a layout.");

                var state = new OverlayState();
                state.Panels["menu"] = new OverlaySpot { Left = 812.5, Top = 96 };

                if (!OverlayPlacement.Save(state, file))
                    return ("FAIL", "The layout could not be written.");

                var back = OverlayPlacement.Load(file);
                if (!back.Panels.TryGetValue("menu", out var spot) || spot.Left != 812.5 || spot.Top != 96)
                    return ("FAIL", $"Read back: {spot?.Left}/{spot?.Top}");

                if (!OverlayPlacement.Clear(file) || OverlayPlacement.Load(file).Panels.Count != 0)
                    return ("FAIL", "The saved layout could not be removed.");

                
                File.WriteAllText(file, "{ \"Panels\": { \"menu\": { \"Left\": 1e999, \"Top\": 0 } } }");
                if (OverlayPlacement.Load(file).Panels.Count != 0)
                    return ("FAIL", "An unusable value was accepted.");

                return ("OK", "Save, read back, reset - and unusable values are discarded.");
            }, results);

            Test("Work area for docking", () =>
            {
                var area = GameOverlay.WorkAreaAt(40, 40);
                if (area.Right - area.Left < 320 || area.Bottom - area.Top < 240)
                    return ("FAIL", $"Unusable work area: {area.Left},{area.Top} - {area.Right},{area.Bottom}");

                return ("OK", $"Screen up to {area.Right}×{area.Bottom} - the overlay stays reachable.");
            }, results);

            Test("Quick View keyboard shortcut", () =>
            {
                if (Shortcuts.Format(ModifierKeys.Shift, Key.Tab) != "Shift+Tab")
                    return ("FAIL", "Shift+Tab is not written.");

                if (!Shortcuts.TryParse("Shift+Tab", out var modifiers, out var key) ||
                    modifiers != ModifierKeys.Shift || key != Key.Tab)
                    return ("FAIL", "Shift+Tab is not read.");

                if (!Shortcuts.Matches("Shift+Tab", ModifierKeys.Shift, Key.Tab))
                    return ("FAIL", "Shift+Tab doesn't fit the keystroke.");

                if (Shortcuts.Matches("Shift+Tab", ModifierKeys.Control | ModifierKeys.Shift, Key.Tab))
                    return ("FAIL", "Ctrl+Shift+Tab opens the layout unintentionally.");

                if (Shortcuts.Matches("Shift+Tab", ModifierKeys.None, Key.Tab))
                    return ("FAIL", "Tab alone opens the layout.");

                if (Shortcuts.Format(ModifierKeys.None, Key.L) is not null)
                    return ("FAIL", "A single letter key would be permitted as a shortcut.");

                if (Shortcuts.Format(ModifierKeys.None, Key.F8) != "F8")
                    return ("FAIL", "Function keys are not suitable as shortcuts.");

                
                if (Shortcuts.Format(ModifierKeys.None, Key.Delete) != "Delete")
                    return ("FAIL", "Del alone would not be a shortcut.");

                if (!Shortcuts.TryParse("Delete", out var delMods, out var delKey) ||
                    delMods != ModifierKeys.None || delKey != Key.Delete)
                    return ("FAIL", "Delete is not read.");

                if (!Shortcuts.TryParseKey("LeftShift", out var shiftKey) || shiftKey != Key.LeftShift)
                    return ("FAIL", "A single additional button is not readable (anti-AFK).");

                if (!Shortcuts.CaptureCandidates(includeModifiers: true).Contains(Key.LeftShift))
                    return ("FAIL", "The additional keys are missing when reassigning.");

                if (Shortcuts.Format(ModifierKeys.Control | ModifierKeys.Alt, Key.D5) != "Ctrl+Alt+5")
                    return ("FAIL", $"Digits are named incorrectly: {Shortcuts.Format(ModifierKeys.Control | ModifierKeys.Alt, Key.D5)}");

                if (Shortcuts.TryParse("Shift", out _, out _))
                    return ("FAIL", "Only additional keys are considered shortcuts.");

                if (!Shortcuts.TryParse("F8", out var none, out var f8) || none != ModifierKeys.None || f8 != Key.F8)
                    return ("FAIL", "F8 is not read.");

                return ("OK", "Writing, reading and comparing are correct - even the rejections.");
            }, results);

            Test("Quick view notes", () =>
            {
                var file = Path.Combine(tempRoot, "notes.txt");
                if (!Notes.TrySave("first line\nsecond line", out var error, file))
                    return ("FAIL", error ?? "Save failed.");

                var read = Notes.Load(file);
                if (!read.Contains("second line", StringComparison.Ordinal))
                    return ("FAIL", "The text didn't come back.");

                if (!Notes.TrySave("", out _, file) || Notes.Load(file).Length != 0)
                    return ("FAIL", "Empty notes cannot be saved.");

                return ("OK", "Writing and reading including blank space.");
            }, results);

            Test("Quick view is on by default", () =>
            {
                var settings = new LauncherSettings();
                if (!settings.OverlayEnabled) return ("FAIL", "OverlayEnabled is off by default.");
                if (settings.OverlayShortcut != Shortcuts.Default)
                    return ("FAIL", $"Default shortcut: {settings.OverlayShortcut}");

                
                
                if (!Shortcuts.TryParse(settings.OverlayShortcut, out var defaultModifiers, out var defaultKey) ||
                    defaultModifiers != ModifierKeys.None)
                    return ("FAIL", $"The default shortcut ({settings.OverlayShortcut}) requires an additional key.");

                return ("OK", $"OverlayEnabled = true, shortcut {Shortcuts.Name(defaultKey)} alone - reassignable.");
            }, results);

            Test("Frame rate for in-game overlay", () =>
            {
                var file = AppPaths.FastFlagsFile;
                var before = File.Exists(file) ? File.ReadAllText(file) : null;

                try
                {
                    if (FastFlags.TrySet(FastFlags.FrameRateFlag, "144") is { } error)
                        return ("FAIL", error);

                    if (FastFlags.ReadFrameRate() != 144)
                        return ("FAIL", $"Read: {FastFlags.ReadFrameRate()}");

                    if (FastFlags.TrySet(FastFlags.FrameRateFlag, "10000") is { } uncapError)
                        return ("FAIL", uncapError);

                    if (FastFlags.ReadFrameRate() != FastFlags.UncappedFrameRate)
                        return ("FAIL", "Uncapped (10000) was not saved.");

                    var values = FastFlags.FrameRateCaps.Select(c => c.Value).ToList();
                    if (!values.Contains(60) || !values.Contains(240) || !values.Contains(FastFlags.UncappedFrameRate))
                        return ("FAIL", $"Chips incomplete: {string.Join(", ", values)}");

                    return ("OK", "Set, read and the chips (60/120/144/240/Uncapped) are correct.");
                }
                finally
                {
                    
                    if (before is not null) File.WriteAllText(file, before);
                    else if (File.Exists(file)) File.Delete(file);
                }
            }, results);

            Test("In-game overlay is on by default", () =>
            {
                var settings = new LauncherSettings();
                if (!settings.GameOverlayEnabled) return ("FAIL", "GameOverlayEnabled is off by default.");
                if (settings.GameOverlayAutoShow) return ("FAIL", "The menu would open itself by default.");
                if (settings.LastPlaceId is not null) return ("FAIL", "A place is set by default.");

                return ("OK", "Overlay on, stays closed at start, no saved place.");
            }, results);

            Test("In-game overlay (six panels build up)", () =>
            {
                
                
                var previous = Application.Current.ShutdownMode;
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var host = new OverlayHost(preview: true, settings: new LauncherSettings { LastPlaceId = 1818 });
                try
                {
                    if (host.Windows.Count != 6)
                        return ("FAIL", $"Panels: {host.Windows.Count}");

                    var fps = host.WindowFor(PanelIds.Fps);
                    var graphics = host.WindowFor(PanelIds.Graphics);
                    var notes = host.WindowFor(PanelIds.Notes);
                    var server = host.WindowFor(PanelIds.Server);
                    var antiAfk = host.WindowFor(PanelIds.AntiAfk);
                    var menu = host.WindowFor(PanelIds.Menu);

                    if (fps is null || graphics is null || notes is null || server is null || antiAfk is null || menu is null)
                        return ("FAIL", "At least one panel is missing.");

                    var chips = All<RadioButton>(fps.Body).Count(chip => chip.GroupName == "Fps");
                    if (chips != 5) return ("FAIL", $"Frame-rate chips: {chips}");

                    var graphicsRows = All<RadioButton>(graphics.Body).Count(chip => chip.GroupName is
                        "Quality" or "Texture" or "Msaa" or "Grass");
                    if (graphicsRows != 9) return ("FAIL", $"Graphics options: {graphicsRows}");

                    if (!All<TextBox>(notes.Body).Any())
                        return ("FAIL", "The note field is missing.");

                    if (!All<TextBlock>(server.Body).Any(text => text.Text == "Leave & rejoin"))
                        return ("FAIL", "The server change button is missing.");

                    if (!All<CheckBox>(antiAfk.Body).Any(box => box.Name == "EnabledSwitch"))
                        return ("FAIL", "The anti-AFK switch is missing.");

                    if (!All<CheckBox>(menu.Body).Any(box => box.Name == "NoOverlapSwitch"))
                        return ("FAIL", "The no-overlap switch is missing.");

                    
                    var withoutOwner = host.Windows.Count(window => window.Owner is null);
                    if (withoutOwner > 0)
                        return ("FAIL", $"{withoutOwner} panel(s) without an owner window.");

                    
                    
                    
                    menu.Left = SystemParameters.VirtualScreenLeft - 2000;
                    menu.Top = SystemParameters.VirtualScreenTop - 2000;
                    menu.Show();
                    menu.UpdateLayout();
                    menu.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var hiddenFromSwitcher = menu.HiddenFromTaskSwitcher;
                    menu.Hide();

                    if (!hiddenFromSwitcher)
                        return ("FAIL", "The panel is still in Alt+Tab/Win+Tab.");

                    
                    
                    fps.Body.Measure(new System.Windows.Size(324, double.PositiveInfinity));
                    var height = fps.Body.DesiredSize.Height;
                    if (height < 180) return ("FAIL", $"The frame-rate panel is too small: {height:0} px");

                    return ("OK", "6 panels: menu, frame rate (5 chips), graphics (9 options), notes, server, anti-AFK - and out of Alt+Tab.");
                }
                finally
                {
                    host.Quit();
                    Application.Current.ShutdownMode = previous;
                }
            }, results);

            Test("Overlay remembers each panel individually", () =>
            {
                var file = AppPaths.OverlayPlacementFile;
                var before = File.Exists(file) ? File.ReadAllText(file) : null;

                try
                {
                    var state = new OverlayState();
                    state.Panels["fps"] = new OverlaySpot { Left = 111, Top = 222, Visible = true };
                    state.Panels["notes"] = new OverlaySpot { Left = 333, Top = 444, Visible = false };
                    state.AntiAfkEnabled = true;
                    state.AntiAfkIntervalSeconds = 300;

                    if (!OverlayPlacement.Save(state)) return ("FAIL", "Saving didn't work.");

                    var loaded = OverlayPlacement.Load();
                    if (loaded.Panels.Count != 2) return ("FAIL", $"Read: {loaded.Panels.Count} Panels");
                    if (loaded.Panels["fps"].Left != 111 || loaded.Panels["notes"].Top != 444)
                        return ("FAIL", "The positions are not correct.");
                    if (loaded.Panels["notes"].Visible) return ("FAIL", "Visibility was lost.");
                    if (!loaded.AntiAfkEnabled || loaded.AntiAfkIntervalSeconds != 300)
                        return ("FAIL", "The anti-AFK settings are missing.");
                    if (!loaded.HideOverOtherApps)
                        return ("FAIL", "Backing away from other programs is not on.");

                    return ("OK", "Location, visibility and anti-AFK are separated in overlay.json.");
                }
                finally
                {
                    if (before is not null) File.WriteAllText(file, before);
                    else if (File.Exists(file)) File.Delete(file);
                }
            }, results);

            Test("Anti-AFK (distances, pop-up, button)", () =>
            {
                if (!AntiAfk.ValidInterval(60) || !AntiAfk.ValidInterval(1800))
                    return ("FAIL", "Valid distances are rejected.");

                if (AntiAfk.ValidInterval(5) || AntiAfk.ValidInterval(5000))
                    return ("FAIL", "Nonsensical distances are allowed.");

                if (AntiAfk.DescribeInterval(120) != "2 min" || AntiAfk.DescribeInterval(90) != "1 min 30 s")
                    return ("FAIL", $"Intervals are named incorrectly: {AntiAfk.DescribeInterval(120)} / {AntiAfk.DescribeInterval(90)}");

                if (!Shortcuts.TryParseKey(AntiAfk.DefaultKey, out var key) || key != Key.F8)
                    return ("FAIL", "The preset button is not readable.");

                if (AntiAfk.PopupTime < TimeSpan.FromMilliseconds(700) || AntiAfk.PopupTime > TimeSpan.FromSeconds(2))
                    return ("FAIL", $"Invalid popup time: {AntiAfk.PopupTime}.");

                
                if (GameOverlay.FindWindow() is null)
                {
                    if (AntiAfk.Nudge(null, AntiAfk.DefaultKey, out var message) != AntiAfk.NudgeOutcome.NoWindow)
                        return ("FAIL", "Without Roblox, a shock was still reported.");

                    if (string.IsNullOrWhiteSpace(message))
                        return ("FAIL", "Without Roblox the explanation is missing.");
                }

                return ("OK", "30 s to 30 min, one-second pop-up, F8 as safe default, clear message without client.");
            }, results);

            Test("Color worlds (immediately recolor and back)", () =>
            {
                if (ThemeManager.All.Count < 4)
                    return ("FAIL", $"Only {ThemeManager.All.Count} color worlds available.");

                var before = ThemeManager.Current.Id;
                var accent = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;

                var chosen = ThemeManager.Apply("emerald");
                var changed = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;

                ThemeManager.Apply(before);
                var restored = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;

                if (chosen.Id != "emerald")
                    return ("FAIL", $"Emerald was not chosen: {chosen.Id}");
                if (changed == accent)
                    return ("FAIL", "The accent brush stayed the same - it froze.");
                if (restored != accent)
                    return ("FAIL", "The old color world did not come back.");
                if (ThemeManager.Find("doesn't exist").Id != ThemeManager.DefaultId)
                    return ("FAIL", "An unknown identifier does not fall back to the standard.");

                return ("OK", $"{ThemeManager.All.Count} Color worlds, accent changes and comes back.");
            }, results);

            Test("Anti-AFK (real input, pointer comes back)", () =>
            {
                if (AntiAfk.RetryDelay < TimeSpan.FromSeconds(5) || AntiAfk.RetryDelay > TimeSpan.FromSeconds(60))
                    return ("FAIL", $"Nonsensical waiting time after a failed attempt: {AntiAfk.RetryDelay}.");

                
                
                if (!AntiAfk.TryTestInput(out var message))
                    return ("FAIL", message);

                return ("OK", message);
            }, results);

            Test("Server hop: close old client first", () =>
            {
                var fresh = ServerHop.FreshIds(new[] { 10, 11 }, new[] { 10, 12 });
                if (fresh.Count != 1 || !fresh.Contains(12))
                    return ("FAIL", $"New clients detected: {string.Join(",", fresh)}");

                if (ServerHop.FreshIds(new[] { 10, 11 }, new[] { 10, 11 }).Count != 0)
                    return ("FAIL", "Old clients are considered new.");

                return ("OK", "Aether first closes the old client and then detects the newly started one.");
            }, results);

            Test("Server change: choose a free server", () =>
            {
                var chosen = ServerHop.PickServer(new[]
                {
                    new RobloxApi.PublicServer("full", 10, 10),
                    new RobloxApi.PublicServer("free", 4, 10),
                });

                if (chosen?.JobId != "free")
                    return ("FAIL", $"Chosen: {chosen?.JobId ?? "nothing"} - a full server is not allowed to win.");

                if (ServerHop.PickServer(new[] { new RobloxApi.PublicServer("full", 10, 10) }) is not null)
                    return ("FAIL", "A full server was chosen anyway.");

                var other = ServerHop.PickServer(new[]
                {
                    new RobloxApi.PublicServer("current", 2, 10),
                    new RobloxApi.PublicServer("other", 2, 10),
                }, avoidJobId: "current");

                if (other?.JobId != "other")
                    return ("FAIL", $"The current server was chosen again: {other?.JobId ?? "nothing"}.");

                return ("OK", "Full servers and the current server are removed, free ones are targeted.");
            }, results);

            Test("Roblox Deployment Channel (Network)", () =>
            {
                var deployed = RobloxInstall.GetDeployedVersionAsync("production").GetAwaiter().GetResult();
                return deployed is null
                    ? ("SKIP", "No response from deployment service (offline?).")
                    : ("OK", $"{deployed.Guid} ({deployed.NumericVersion}) is rolled out for channel production.");
            }, results);
        }
        catch (Exception ex)
        {
            results.Add(new Result("Self-test procedure", "FAIL", ex.Message));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                
            }
        }

        report.AppendLine(new string('-', 78));
        foreach (var result in results)
            report.AppendLine($"  [{result.State,-4}] {result.Name,-38} {result.Detail}");

        var failed = results.Count(r => r.Failed);
        var skipped = results.Count(r => r.State == "SKIP");
        report.AppendLine(new string('-', 78));
        report.AppendLine($"{results.Count - failed - skipped} passed, {failed} failed, {skipped} skipped");
        report.AppendLine();

        var text = report.ToString();
        try
        {
            
            
            File.WriteAllText(Path.Combine(AppPaths.DataDir, "selftest.log"), text);
        }
        catch
        {
            
        }

        try
        {
            Console.Write(text);
            Console.Out.Flush();
        }
        catch
        {
            
        }

        return failed == 0 ? 0 : 1;
    }

    
    private static void WriteSolidPng(string path, int size, byte red, byte green, byte blue)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 0xFF;
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    
    private static string BundledCursorImage()
    {
        
        var unpacked = ImageTool.EnsureBundledCursor(AppPaths.CursorImageFile);
        if (string.IsNullOrEmpty(unpacked)) throw new InvalidOperationException("Assets/cursor.png is missing from the program.");
        return unpacked;
    }

    
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T match) yield return match;
            if (child is DependencyObject node)
                foreach (var nested in All<T>(node)) yield return nested;
        }
    }

    private static void Test(string name, Func<(string State, string Detail)> body, List<Result> results)
    {
        try
        {
            var (state, detail) = body();
            results.Add(new Result(name, state, detail));
        }
        catch (Exception ex)
        {
            results.Add(new Result(name, "FAIL", $"Exception: {ex.Message}"));
        }
    }

    
    
    
    
    private static void PrepareOutput()
    {
        try
        {
            if (!Console.IsOutputRedirected) AttachConsole(AttachParentProcess);

            var stdout = Console.OpenStandardOutput();
            Console.SetOut(new StreamWriter(stdout, Encoding.UTF8) { AutoFlush = true });
        }
        catch
        {
            
        }
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
