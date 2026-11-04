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

/// <summary>
/// Selbsttest ohne Oberfläche: prüft die Kernlogik, die man nicht per Auge kontrollieren kann
/// (DPAPI, Speicher-Roundtrips, URI-Bau, FastFlags, Multi-Instanz-Objekte).
/// Aufruf: AetherLauncher.exe --selftest   (Rückgabecode 0 = alles in Ordnung)
/// </summary>
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
        report.AppendLine("Aether Launcher - Selbsttest");
        report.AppendLine($"Zeit: {DateTime.Now:dd.MM.yyyy HH:mm:ss}   Datenordner: {AppPaths.DataDir}   Version: {typeof(SelfTest).Assembly.GetName().Version}");
        report.AppendLine(new string('-', 78));

        try
        {
            Directory.CreateDirectory(tempRoot);

            Test("Schlüssel-Ablage (DPAPI + AES-256-GCM)", () =>
            {
                const string secret = "_|WARNING:-DO-NOT-SHARE|_testwert-123";
                var protectedValue = Dpapi.Protect(secret);
                var roundTrip = Dpapi.Unprotect(protectedValue);

                if (protectedValue.Contains(secret, StringComparison.Ordinal))
                    return ("FAIL", "Der Klartext steht noch im verschlüsselten Wert.");
                if (!protectedValue.StartsWith("aeg2:", StringComparison.Ordinal))
                    return ("FAIL", "Es wurde nicht das AES-GCM-Format geschrieben.");
                if (roundTrip != secret)
                    return ("FAIL", "Der entschlüsselte Wert weicht ab.");

                // Ein gekipptes Bit muss auffallen - GCM authentifiziert den Inhalt.
                var payload = Convert.FromBase64String(protectedValue[5..]);
                payload[^1] ^= 0x5A;
                var tampered = "aeg2:" + Convert.ToBase64String(payload);
                if (Dpapi.Unprotect(tampered).Length > 0)
                    return ("FAIL", "Ein veränderter Wert wurde trotzdem entschlüsselt.");

                // Alte, rein DPAPI-verschlüsselte Werte müssen weiter lesbar sein.
                var legacy = Dpapi.ProtectLegacy(secret);
                if (Dpapi.Unprotect(legacy) != secret)
                    return ("FAIL", "Ein alter DPAPI-Wert ließ sich nicht mehr lesen.");

                return ("OK", "AES-256-GCM mit DPAPI-geschütztem Schlüssel, Tamper-Schutz und alter DPAPI-Fallback.");
            }, results);

            Test("Kontospeicher (Roundtrip)", () =>
            {
                var store = new AccountStore(Path.Combine(tempRoot, "accounts.json"));
                store.Load();
                store.Upsert("cookie-testwert", new RobloxApi.AccountInfo(123456, "testuser", "Test User"));

                var reloaded = new AccountStore(Path.Combine(tempRoot, "accounts.json"));
                reloaded.Load();
                if (reloaded.Accounts.Count != 1) return ("FAIL", $"Erwartet 1 Konto, gefunden {reloaded.Accounts.Count}.");
                if (reloaded.Accounts[0].Username != "testuser") return ("FAIL", "Benutzername ging verloren.");
                if (!File.ReadAllText(Path.Combine(tempRoot, "accounts.json")).Contains("cookie-testwert"))
                    return ("OK", "Konto gespeichert und wiederhergestellt, Cookie nur verschlüsselt auf der Platte.");
                return ("FAIL", "Das Cookie steht im Klartext in der Datei!");
            }, results);

            Test("Alte Kontoschlüssel werden migriert", () =>
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

                if (store.Accounts.Count != 1) return ("FAIL", "Das alte Konto fehlt.");
                if (!File.ReadAllText(file).Contains("aeg2:", StringComparison.Ordinal))
                    return ("FAIL", "Der alte Schlüssel wurde nicht auf AES-GCM gehoben.");

                return store.GetCookie(store.Accounts[0]) == "legacy-cookie"
                    ? ("OK", "Alter DPAPI-Wert wurde beim Laden gelesen und neu verschlüsselt.")
                    : ("FAIL", "Der migrierte Schlüssel passt nicht mehr.");
            }, results);

            Test("Gespeicherte Konten entschlüsselbar", () =>
            {
                var store = new AccountStore();
                store.Load();
                if (store.Accounts.Count == 0) return ("OK", "Noch keine Konten gespeichert - nichts zu prüfen.");

                var broken = store.Accounts.Where(a => store.GetCookie(a) is null).Select(a => a.Username).ToList();
                return broken.Count == 0
                    ? ("OK", $"{store.Accounts.Count} Konto(en), jeder Schlüssel ließ sich mit dem Vault entschlüsseln.")
                    : ("FAIL", $"Nicht entschlüsselbar: {string.Join(", ", broken)}");
            }, results);

            Test("Spielspeicher (Roundtrip)", () =>
            {
                var store = new PlaceStore(Path.Combine(tempRoot, "places.json"));
                store.Load();
                store.Add(1818, "abc123", "Testspiel");

                var reloaded = new PlaceStore(Path.Combine(tempRoot, "places.json"));
                reloaded.Load();
                var place = reloaded.Find(1818);
                return place is not null && place.AccessCode == "abc123"
                    ? ("OK", "Place-ID und Zugangscode bleiben erhalten.")
                    : ("FAIL", "Der Platz wurde nicht korrekt gespeichert.");
            }, results);

            Test("Roblox-Anmeldung (WebView2)", () =>
            {
                try
                {
                    var runtime = CoreWebView2Environment.GetAvailableBrowserVersionString();
                    return string.IsNullOrWhiteSpace(runtime)
                        ? ("SKIP", "WebView2-Runtime fehlt - der Einfüge-Weg bleibt verfügbar.")
                        : ("OK", $"WebView2 {runtime} ist installiert; die Anmeldung läuft in Roblox' eigener Seite.");
                }
                catch (Exception ex)
                {
                    return ("SKIP", $"WebView2 nicht verfügbar: {ex.Message} - der Einfüge-Weg bleibt.");
                }
            }, results);

            Test("Anmeldefenster baut auf", () =>
            {
                // Ohne offenes Hauptfenster würde das Schließen dieses Fensters die
                // Anwendung beenden (Default: OnLastWindowClose) - die restlichen Tests
                // liefen dann gegen eine bereits heruntergefahrene App.
                var previous = Application.Current.ShutdownMode;
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var window = new RobloxLoginWindow();
                try
                {
                    return window.Cookie is null
                        ? ("OK", "Fenster, Theme und das eingebettete Browser-Steuerelement bauen auf.")
                        : ("FAIL", "Das Fenster liefert ohne Anmeldung schon ein Cookie.");
                }
                finally
                {
                    window.Close();
                    Application.Current.ShutdownMode = previous;
                }
            }, results);

            Test("Roblox-Client finden", () =>
            {
                var settings = new LauncherSettings();
                var version = RobloxInstall.Resolve(settings, out var roots);
                if (version is null) return ("FAIL", $"Kein Client gefunden. Gesucht in: {roots}");
                if (!File.Exists(version.PlayerExe)) return ("FAIL", "Gefundene Version enthält kein RobloxPlayerBeta.exe.");
                var count = RobloxInstall.Discover(Path.GetDirectoryName(Path.GetDirectoryName(version.Directory))).Count;
                return ("OK", $"{version.Guid} ({count} Version(en) installiert)");
            }, results);

            Test("Start-URI für ein Konto", () =>
            {
                var uri = RobloxUri.BuildPlayUri(1818, "accesscode-1", "TESTTICKET", "production", 4242);
                var checks = new Dictionary<string, bool>
                {
                    ["launchmode:play"] = uri.Contains("+launchmode:play", StringComparison.Ordinal),
                    ["Ticket in gameinfo"] = uri.Contains("+gameinfo:TESTTICKET", StringComparison.Ordinal),
                    ["browsertrackerid"] = uri.Contains("+browsertrackerid:4242", StringComparison.Ordinal),
                    ["placelauncherurl kodiert"] = uri.Contains("placelauncherurl:https%3A%2F%2Fassetgame.roblox.com", StringComparison.Ordinal),
                    ["Kanal leer bei production"] = uri.EndsWith("+channel:", StringComparison.Ordinal),
                };

                var broken = checks.Where(c => !c.Value).Select(c => c.Key).ToList();
                return broken.Count == 0
                    ? ("OK", "Aufbau, Kodierung und Kanal stimmen.")
                    : ("FAIL", $"Fehlerhaft: {string.Join(", ", broken)}");
            }, results);

            Test("Start-URI für einen bestimmten Server", () =>
            {
                var uri = RobloxUri.BuildPlayUri(1818, null, "TESTTICKET", "production", 4242, "JOB-123");
                var checks = new Dictionary<string, bool>
                {
                    ["RequestGameJob"] = uri.Contains("request%3DRequestGameJob", StringComparison.Ordinal),
                    ["gameId"] = uri.Contains("gameId%3DJOB-123", StringComparison.Ordinal),
                    ["kein accessCode"] = !uri.Contains("accessCode", StringComparison.Ordinal),
                };

                var broken = checks.Where(c => !c.Value).Select(c => c.Key).ToList();
                return broken.Count == 0
                    ? ("OK", "Die Job-ID landet als RequestGameJob in der Start-URI.")
                    : ("FAIL", $"Fehlerhaft: {string.Join(", ", broken)}");
            }, results);

            Test("Eingabe-Erkennung (ID/URL/Link)", () =>
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
                        failures.Add($"'{input}' nicht erkannt");
                    else if (code is not null && accessCode != code)
                        failures.Add($"Zugangscode fehlt bei '{input}'");
                }

                return failures.Count == 0
                    ? ("OK", $"{cases.Length} Eingabeformen korrekt interpretiert.")
                    : ("FAIL", string.Join("; ", failures));
            }, results);

            Test("FastFlags (JSON und Datei)", () =>
            {
                const string json = """
                    {
                      // Kommentare und Kommas am Ende sind erlaubt
                      "DFIntTaskSchedulerTargetFps": 144,
                      "FFlagDisablePostFx": true,
                    }
                    """;

                var error = FastFlags.ParseJson(json, out var flags);
                if (error is not null) return ("FAIL", error);
                if (flags.Count != 2) return ("FAIL", $"Erwartet 2 Flags, gelesen {flags.Count}.");
                if (flags["DFIntTaskSchedulerTargetFps"] != "144") return ("FAIL", "Zahl wurde nicht übernommen.");
                if (flags["FFlagDisablePostFx"] != "True") return ("FAIL", "Bool wurde nicht übernommen.");

                var versionDir = Path.Combine(tempRoot, "version-test");
                Directory.CreateDirectory(Path.Combine(versionDir, "ClientSettings"));
                var existing = Path.Combine(versionDir, "ClientSettings", "ClientAppSettings.json");
                File.WriteAllText(existing, "{ \"FremdesFlag\": \"True\" }");

                if (!FastFlags.ApplyTo(versionDir, flags, out var message)) return ("FAIL", message);

                var written = File.ReadAllText(existing);
                using var doc = System.Text.Json.JsonDocument.Parse(written);
                if (!doc.RootElement.TryGetProperty("FFlagDisablePostFx", out _)) return ("FAIL", "Die Datei enthält das Flag nicht.");
                if (!File.Exists(existing + ".aetherbak")) return ("FAIL", "Die vorhandene Datei wurde nicht gesichert.");

                return ("OK", $"JSON geprüft, {message}, Fremddatei gesichert.");
            }, results);

            Test("Multi-Instanz (Singleton halten)", () =>
            {
                if (MultiInstance.IsClientRunning())
                    return ("SKIP", "Roblox läuft gerade - Test würde einen laufenden Client stören.");

                // Ein anderer Launcher kann den Namen bereits als Event/Semaphore angelegt
                // haben. Windows erlaubt dann keinen Mutex mit demselben Namen - das ist kein
                // Fehler des Launchers, sondern nur ein belegter Singleton.
                Mutex? mutex;
                try
                {
                    mutex = new Mutex(false, "ROBLOX_singletonMutex");
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    return ("SKIP", "Der Singleton existiert bereits als anderer Kernel-Objekttyp (z. B. ein anderer Launcher).");
                }
                catch (UnauthorizedAccessException)
                {
                    return ("SKIP", "Der Singleton gehört einem anderen Prozess - der Test lässt ihn in Ruhe.");
                }
                catch (Exception ex)
                {
                    return ("SKIP", $"Der Singleton ließ sich hier nicht anlegen: {ex.Message}");
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
                        owned = true; // Vorbesitzer abgestürzt - der Mutex ist jetzt unser.
                    }

                    if (!owned)
                        return ("SKIP", "Der Singleton ist bereits belegt (Multi-Instanz-Wächter oder Client läuft).");

                    mutex.ReleaseMutex();
                }

                return ("OK", "Singleton-Mutex ist übernehmbar und wieder freigebbar.");
            }, results);

            Test("Geheimnisse im Log werden maskiert", () =>
            {
                var uri = RobloxUri.BuildPlayUri(1818, null, "GEHEIMES-TICKET", "production", 1);
                var redacted = Log.Redact($"Starte: {uri}");
                if (redacted.Contains("GEHEIMES-TICKET", StringComparison.Ordinal)) return ("FAIL", "Das Ticket steht im Log.");
                if (!redacted.Contains("gameinfo:<ticket>", StringComparison.Ordinal)) return ("FAIL", "Die Maskierung ist nicht sichtbar.");
                if (Log.Redact("_|WARNING:-DO-NOT-SHARE|_abcdef;").Contains("abcdef", StringComparison.Ordinal))
                    return ("FAIL", "Der Cookie steht im Log.");
                return ("OK", "Ticket und Cookie werden durch Platzhalter ersetzt.");
            }, results);

            Test("Protokollhandler (Lesezugriff)", () =>
            {
                var current = ProtocolHandler.ReadCurrentCommand();
                return current is null
                    ? ("SKIP", "HKCU\\Software\\Classes\\roblox-player ist nicht vorhanden.")
                    : ("OK", $"Aktueller Handler: {Log.Redact(current)}");
            }, results);

            Test("Zeigerpfeil der Anwendung (Assets/cursor.cur)", () =>
            {
                var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/cursor.cur"));
                if (resource is null) return ("FAIL", "Assets/cursor.cur fehlt im Programm.");

                using var stream = resource.Stream;
                var bytes = new byte[stream.Length];
                var read = stream.Read(bytes, 0, bytes.Length);

                if (read < 6 || bytes[0] != 0 || bytes[1] != 0 || bytes[2] != 2)
                    return ("FAIL", "Der Kopf ist kein Cursor-Container (ICONDIR).");

                var frames = bytes[4] | (bytes[5] << 8);
                if (frames < 1) return ("FAIL", "Der Container enthält keinen Frame.");

                // Erster Eintrag: Breite, Höhe, Klickpunkt, Länge, Versatz
                var size = bytes[6] == 0 ? 256 : bytes[6];
                var hotX = bytes[10] | (bytes[11] << 8);
                var hotY = bytes[12] | (bytes[13] << 8);
                var length = BitConverter.ToInt32(bytes, 14);
                var offset = BitConverter.ToInt32(bytes, 18);

                if (offset + length > bytes.Length) return ("FAIL", "Der Frame zeigt über die Datei hinaus.");
                if (hotX >= size || hotY >= size) return ("FAIL", $"Der Klickpunkt ({hotX},{hotY}) liegt außerhalb von {size}x{size}.");

                // Der Frame ist ein PNG (Vista-Container) - wenn das nicht stimmt, zeigt Windows nichts an.
                var isPng = bytes[offset] == 0x89 && bytes[offset + 1] == 0x50;
                if (!isPng) return ("FAIL", "Der Frame ist kein PNG.");

                using var frameStream = new MemoryStream(bytes, offset, length);
                var frame = BitmapFrame.Create(frameStream);
                if (frame.PixelWidth != size) return ("FAIL", $"Der Frame ist {frame.PixelWidth}px breit, im Kopf steht {size}.");

                // WPF muss den Container ebenfalls annehmen können.
                using var cursorStream = new MemoryStream(bytes);
                using var cursor = new Cursor(cursorStream);

                return ("OK", $"{frames} Frames, {size}x{size}, Klickpunkt {hotX},{hotY}");
            }, results);

            Test("Ingame-Zeiger (ersetzen und zurückholen)", () =>
            {
                var versionDir = Path.Combine(tempRoot, "version-cursor");
                var textures = Path.Combine(versionDir, @"content\textures");
                var keyboardMouse = Path.Combine(textures, @"Cursors\KeyboardMouse");
                Directory.CreateDirectory(keyboardMouse);

                // Zwei erfundene Zeiger-Dateien in den Größen des echten Clients - bewusst
                // einfarbig, damit sich "ersetzt" von "unverändert" sicher unterscheiden lässt.
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
                if (firstSize != (64, 64)) return ("FAIL", $"Die 64er-Textur ist jetzt {firstSize?.ToString() ?? "nicht lesbar"}.");
                if (secondSize != (32, 32)) return ("FAIL", $"Die 32er-Textur ist jetzt {secondSize}.");
                if (!File.Exists(first + ClientMods.BackupSuffix)) return ("FAIL", "Die Sicherung fehlt.");
                if (ClientMods.CountModified(versionDir) != 2) return ("FAIL", "Es werden nicht beide Texturen als ersetzt erkannt.");
                if (File.ReadAllBytes(first).SequenceEqual(originalFirst)) return ("FAIL", "Die Textur wurde nicht verändert.");

                if (!ClientMods.RestoreCursor(versionDir, out var restoreMessage)) return ("FAIL", restoreMessage);
                if (!File.ReadAllBytes(first).SequenceEqual(originalFirst)) return ("FAIL", "Die 64er-Textur ist nicht wieder im Originalzustand.");
                if (!File.ReadAllBytes(second).SequenceEqual(originalSecond)) return ("FAIL", "Die 32er-Textur ist nicht wieder im Originalzustand.");
                if (File.Exists(first + ClientMods.BackupSuffix)) return ("FAIL", "Die Sicherung wurde nicht aufgeräumt.");

                return ("OK", $"{applyMessage} Danach: {restoreMessage}");
            }, results);

            Test("Vanilla-Modus fasst den Client nicht an", () =>
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
                if (File.Exists(settingsFile)) return ("FAIL", "Im Vanilla-Modus wurde ClientAppSettings.json geschrieben.");

                settings.VanillaMode = false;
                FastFlags.ApplyTo(versionDir, new Dictionary<string, string> { ["FFlagTest"] = "True" }, out _);
                if (!File.Exists(settingsFile)) return ("FAIL", "Ohne Vanilla-Modus schreibt der Test nichts - dann prüft er nichts.");

                return ("OK", "Vanilla lässt den Client unberührt, normal schreibt FastFlags weiter.");
            }, results);

            Test("Versionsschalter (angeheftete Version)", () =>
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
                    return ("FAIL", "DiscoverAll findet die Testversionen nicht.");
                if (all[0].Guid != "version-bbbbbbbbbbbb")
                    return ("FAIL", $"Die neueste Version steht nicht oben, sondern {all[0].Guid}.");

                settings.PinnedVersionGuid = "version-aaaaaaaaaaaa";
                var pinned = RobloxInstall.Resolve(settings, out _);
                if (pinned?.Guid != "version-aaaaaaaaaaaa") return ("FAIL", $"Angeheftet kam {pinned?.Guid ?? "nichts"}.");

                // Eine angeheftete Version, die es nicht gibt, darf den Start nicht verhindern:
                // es gilt wieder die Auswahl, die auch ohne Anheftung gilt.
                settings.PinnedVersionGuid = "version-cccccccccccc";
                var fallback = RobloxInstall.Resolve(settings, out _);
                if (fallback?.Guid != all[0].Guid)
                    return ("FAIL", $"Eine fehlende angeheftete Version fällt auf {fallback?.Guid ?? "nichts"} statt auf {all[0].Guid} zurück.");

                return ("OK", $"{all.Count} Version(en); angeheftet {pinned.ShortGuid}, Unbekanntes fällt auf {fallback.ShortGuid} zurück.");
            }, results);

            Test("Eigene Schrift laden", () =>
            {
                var fontFile = Directory.EnumerateFiles(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\Fonts", "*.ttf")
                    .FirstOrDefault(f => Path.GetFileName(f).Equals("consola.ttf", StringComparison.OrdinalIgnoreCase))
                    ?? Directory.EnumerateFiles(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\Fonts", "*.ttf").FirstOrDefault();

                if (fontFile is null) return ("SKIP", "Auf diesem System liegt keine .ttf-Datei.");

                var target = Path.Combine(tempRoot, "fonts");
                var family = AppFonts.Import(fontFile, target);
                if (family is null) return ("FAIL", $"{Path.GetFileName(fontFile)} ließ sich nicht lesen.");
                if (!File.Exists(Path.Combine(target, Path.GetFileName(fontFile)))) return ("FAIL", "Die Schriftdatei wurde nicht kopiert.");

                var list = AppFonts.InstalledFamilies();
                if (list.Count < 10) return ("FAIL", $"Die Schriftliste ist verdächtig kurz ({list.Count}).");

                // Unbekannter Name darf nicht in einer Ersatzschrift mit falschem Namen enden.
                var settings = new LauncherSettings { UiFontFamily = "Aether Gibt Es Nicht" };
                if (AppFonts.Resolve(settings).Source != AppFonts.DefaultFamily) return ("FAIL", "Ein unbekannter Name fällt nicht auf Segoe UI zurück.");

                settings.UiFontFamily = family;
                if (!string.Equals(AppFonts.Resolve(settings).Source, family, StringComparison.OrdinalIgnoreCase))
                    return ("FAIL", $"Die geladene Schrift {family} wird nicht übernommen.");

                return ("OK", $"{family} geladen, {list.Count} Schriften sichtbar, Rückfall auf {AppFonts.DefaultFamily} geprüft.");
            }, results);

            Test("Vorschlag sofort ins JSON schreiben und zurücknehmen", () =>
            {
                var preset = FastFlags.Presets.First(p => p.Flags.Count > 1); // z. B. "Grass off"
                var flags = new Dictionary<string, string> { ["FFFlagBleibt"] = "True" };

                if (FastFlags.MergePreset(flags, preset) != preset.Flags.Count)
                    return ("FAIL", $"{preset.Name} setzt nicht alle seine Flags.");

                foreach (var flag in preset.Flags)
                {
                    if (!flags.TryGetValue(flag.Key, out var value) || value != flag.Value)
                        return ("FAIL", $"{flag.Key} steht nicht mit dem Wert des Vorschlags drin.");
                }

                if (!FastFlags.ContainsPreset(flags, preset)) return ("FAIL", "Der Vorschlag gilt nicht als aktiv.");

                // Ein von Hand geänderter Wert darf beim Abwählen nicht verschwinden.
                var edited = preset.Flags[0];
                flags[edited.Key] = "selbst-gesetzt";
                var removed = FastFlags.RemovePreset(flags, preset);
                if (removed != preset.Flags.Count - 1)
                    return ("FAIL", $"Erwartet {preset.Flags.Count - 1} entfernte Flags, entfernt wurden {removed}.");
                if (flags.GetValueOrDefault(edited.Key) != "selbst-gesetzt")
                    return ("FAIL", "Der selbst gesetzte Wert wurde gelöscht.");
                if (!flags.ContainsKey("FFFlagBleibt")) return ("FAIL", "Ein fremdes Flag wurde mitgelöscht.");
                if (FastFlags.ContainsPreset(flags, preset)) return ("FAIL", "Der Vorschlag gilt weiterhin als aktiv.");

                // Der geschriebene Text muss sich wieder einlesen lassen (so läuft der Editor).
                var json = FastFlags.Serialize(flags);
                if (FastFlags.ParseJson(json, out var again) is { } error) return ("FAIL", error);
                if (again.Count != flags.Count) return ("FAIL", "Der geschriebene Text hat eine andere Anzahl Flags.");

                return ("OK", $"{preset.Name}: {preset.Flags.Count} Flags gesetzt, eigene Werte bleiben erhalten.");
            }, results);

            Test("Start ohne Spiel (Roblox-App)", () =>
            {
                var uri = RobloxUri.BuildAppUri("TICKET", "production", 4242);
                var checks = new Dictionary<string, bool>
                {
                    ["launchmode:app"] = uri.Contains("+launchmode:app", StringComparison.Ordinal),
                    ["kein launchmode:play"] = !uri.Contains("launchmode:play", StringComparison.Ordinal),
                    ["Ticket in gameinfo"] = uri.Contains("+gameinfo:TICKET", StringComparison.Ordinal),
                    ["browsertrackerid"] = uri.Contains("+browsertrackerid:4242", StringComparison.Ordinal),
                    ["Kanal leer bei production"] = uri.EndsWith("+channel:", StringComparison.Ordinal),
                };

                var broken = checks.Where(c => !c.Value).Select(c => c.Key).ToList();
                if (broken.Count > 0) return ("FAIL", $"Fehlerhaft: {string.Join(", ", broken)}");

                var withoutTicket = RobloxUri.BuildAppUri(null, "zb", 1);
                if (!withoutTicket.Contains("+gameinfo:+", StringComparison.Ordinal))
                    return ("FAIL", "Ohne Konto fehlt das leere gameinfo-Feld.");
                if (!withoutTicket.EndsWith("+channel:zb", StringComparison.Ordinal))
                    return ("FAIL", "Ein anderer Kanal wird nicht übernommen.");

                return ("OK", "App-Start-URI trägt den Modus app, das Ticket und den Kanal.");
            }, results);

            Test("Installer-Adressen der Builds", () =>
            {
                var live = RobloxVersions.InstallerUrl("version-5a21b87bbc2641cf");
                if (live != "https://setup.rbxcdn.com/version-5a21b87bbc2641cf-RobloxPlayerInstaller.exe")
                    return ("FAIL", $"Falsche Adresse: {live}");

                var channel = RobloxVersions.InstallerUrl("version-5a21b87bbc2641cf", "zb");
                if (!channel.Contains("/channel/zb/", StringComparison.Ordinal))
                    return ("FAIL", $"Der Kanal fehlt in der Adresse: {channel}");

                if (RobloxVersions.InstallerUrl("version-5a21b87bbc2641cf", "production") != live)
                    return ("FAIL", "production hängt einen unnötigen Kanal an.");

                return ("OK", "Adressen für den LIVE-Zweig und für eigene Kanäle stimmen.");
            }, results);

            Test("Versionslisten (WEAO und DeployHistory, Netzwerk)", () =>
            {
                var weao = RobloxVersions.GetWeaoAsync().GetAwaiter().GetResult();
                var history = RobloxVersions.GetDeployHistoryAsync().GetAwaiter().GetResult();

                if (weao.Count == 0 && history.Count == 0)
                    return ("SKIP", "Keine Antwort - vermutlich offline.");

                if (weao.Count > 0 && weao.Any(b => !b.Guid.StartsWith("version-", StringComparison.Ordinal)))
                    return ("FAIL", "WEAO lieferte einen Eintrag ohne version-Präfix.");

                // Das Datum bestimmt die Reihenfolge in der Liste - ohne Datum rutschen die
                // von WEAO verfolgten Builds ans Ende und werden abgeschnitten.
                var undated = weao.Where(b => b.Date is null).Select(b => b.Guid).ToList();
                if (undated.Count > 0) return ("FAIL", $"Ohne Datum: {string.Join(", ", undated)}");

                if (RobloxVersions.ParseTimestamp("10/27/2026, 4:01:04 PM UTC") is null)
                    return ("FAIL", "Der WEAO-Zeitstempel wird nicht gelesen.");

                if (RobloxVersions.ParseTimestamp("3/11/2026 2:06:34 PM") is null)
                    return ("FAIL", "Der DeployHistory-Zeitstempel wird nicht gelesen.");

                if (history.Count > 0)
                {
                    var newest = history.OrderByDescending(b => b.Date ?? DateTimeOffset.MinValue).First();
                    if (newest.Guid.Length < 24) return ("FAIL", $"Verdächtiger Hash: {newest.Guid}");
                    if (string.IsNullOrWhiteSpace(newest.NumericVersion))
                        return ("FAIL", "Der DeployHistory-Eintrag hat keine Versionsnummer.");
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
                        return ("FAIL", $"Der neueste Eintrag ist {newest.Guid} vom {newest.Date:dd.MM.yyyy}.");
                }

                return ("OK", $"WEAO: {weao.Count} verfolgte Version(en){string.Join(", ", weao.Select(b => $" {b.ShortGuid} ({b.Date:dd.MM.yyyy})"))}; DeployHistory: {history.Count} Builds.");
            }, results);

            Test("FastFlags-Vorschläge", () =>
            {
                // Bekannte echte Flags ohne F/D-Vorsatz (Quelle: Bloxstraps FastFlag-Liste).
                var KeyWithoutPrefix = new HashSet<string>(StringComparer.Ordinal) { "GuiHidingApiSupport2" };

                var problems = new List<string>();
                var seen = new HashSet<string>();

                foreach (var preset in FastFlags.Presets)
                {
                    if (string.IsNullOrWhiteSpace(preset.Name) || string.IsNullOrWhiteSpace(preset.Group))
                        problems.Add($"'{preset.Name}' hat keine Gruppe.");
                    if (preset.Flags.Count == 0) problems.Add($"'{preset.Name}' setzt kein Flag.");

                    foreach (var flag in preset.Flags)
                    {
                        if (string.IsNullOrWhiteSpace(flag.Key))
                            problems.Add($"'{preset.Name}' hat einen leeren Eintrag.");

                        // Leere Werte sind nur bei den Texturtabellen erlaubt - dort bedeutet
                        // der leere Text genau das, was der Vorschlag verspricht.
                        if (flag.Value is null ||
                            (flag.Value.Length == 0 && !flag.Key.StartsWith("FString", StringComparison.Ordinal)))
                            problems.Add($"'{preset.Name}' hat einen leeren Wert an {flag.Key}.");

                        // Fast alle Flags beginnen mit F oder D (FFlag, DFFlag, FInt, ...). Ein
                        // paar alte Ausnahmen aus Bloxstraps Liste haben keinen Vorsatz - die
                        // stehen hier einzeln, damit ein Tippfehler trotzdem auffällt.
                        var prefixed = flag.Key.StartsWith("F", StringComparison.Ordinal) ||
                                       flag.Key.StartsWith("D", StringComparison.Ordinal);
                        if (!prefixed && !KeyWithoutPrefix.Contains(flag.Key))
                            problems.Add($"'{flag.Key}' sieht nicht nach einem FastFlag aus.");
                        seen.Add(flag.Key);
                    }
                }

                if (problems.Count > 0) return ("FAIL", string.Join("; ", problems.Take(3)));

                var groups = FastFlags.Presets.Select(p => p.Group).Distinct().Count();
                var flags = FastFlags.Presets.Sum(p => p.Flags.Count);
                return ("OK", $"{FastFlags.Presets.Count} Vorschläge in {groups} Gruppen, {flags} Flags, {seen.Count} verschiedene Namen.");
            }, results);

            Test("Allowlist-Kennzeichnung der Vorschläge", () =>
            {
                var active = FastFlags.Presets.Count(p => p.Allowlisted);
                if (active < 8)
                    return ("FAIL", $"Nur {active} Vorschläge sind als noch wirksam gekennzeichnet.");

                // Die Bildratenbegrenzung steht nicht auf der Allowlist - das muss auch so
                // gekennzeichnet sein, sonst verspricht der Launcher etwas, das nicht kommt.
                var caps = FastFlags.Presets.Where(p => p.Flags.Any(f => f.Key == FastFlags.FrameRateFlag)).ToList();
                if (caps.Count == 0) return ("FAIL", "Kein Bildraten-Vorschlag gefunden.");
                if (caps.Any(p => p.Allowlisted))
                    return ("FAIL", "Die Bildratenbegrenzung gilt fälschlich als allowlisted.");

                return ("OK", $"{active} von {FastFlags.Presets.Count} Vorschlägen laufen noch; die Bildratenbegrenzung ist richtig als ignoriert gekennzeichnet.");
            }, results);

            Test("Reihen im Overlay schalten (Texturen, Kantenglättung, Gras)", () =>
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
                    if (FastFlags.ReadTextureQuality() != 0) return ("FAIL", "Die Texturqualität kam nicht zurück.");
                    if (FastFlags.ReadMsaa() != 2) return ("FAIL", "Die Kantenglättung kam nicht zurück.");

                    // "Gras an" muss die Flags wieder herausnehmen, nicht auf einen anderen Wert setzen.
                    if (FastFlags.TrySetMany(new Dictionary<string, string?> { ["FIntFRMMaxGrassDistance"] = null }) is { } remove)
                        return ("FAIL", remove);

                    if (FastFlags.Read("FIntFRMMaxGrassDistance") is not null)
                        return ("FAIL", "Gras an nimmt das Flag nicht heraus.");

                    return ("OK", "Setzen, Lesen und Herausnehmen in einem Zug stimmen.");
                }
                finally
                {
                    if (before is not null) File.WriteAllText(file, before);
                    else if (File.Exists(file)) File.Delete(file);
                }
            }, results);

            Test("Zuletzt gespielt (recent.json)", () =>
            {
                var file = Path.Combine(tempRoot, "recent.json");
                var store = new RecentStore(file);
                store.Load();

                store.Record(1818, null, "Crossroads", 312, "tester");
                store.Record(1818, null, "Crossroads", 312, "tester");
                if (store.Entries.Count != 1) return ("FAIL", $"Derselbe Platz steht doppelt: {store.Entries.Count} Einträge.");
                if (store.Entries[0].Sessions != 2) return ("FAIL", $"Sitzungen: {store.Entries[0].Sessions}");
                if (store.Entries[0].Name != "Crossroads") return ("FAIL", "Der Name ging verloren.");

                for (var index = 1; index <= RecentStore.Capacity + 3; index++)
                    store.Record(2000 + index, null, $"Spiel {index}", null, null);

                if (store.Entries.Count != RecentStore.Capacity)
                    return ("FAIL", $"Die Liste wächst über {RecentStore.Capacity} hinaus: {store.Entries.Count}");

                var reread = new RecentStore(file);
                reread.Load();
                if (reread.Entries.Count != RecentStore.Capacity)
                    return ("FAIL", "Die Liste wurde nicht gespeichert.");

                // Ein Name, der erst nach dem Einlesen nachgetragen wird (Roblox-Abfrage läuft
                // im Hintergrund), muss in der Datei landen. Genau daran hing "Place 128…".
                var imported = reread.Entries[0].PlaceId;
                if (!reread.Update(imported, "Carry the Glass Together!", 10415694629))
                    return ("FAIL", "Ein nachgetragener Name wurde nicht übernommen.");

                var third = new RecentStore(file);
                third.Load();
                if (third.Find(imported)?.Name != "Carry the Glass Together!")
                    return ("FAIL", "Der nachgetragene Name wurde nicht gespeichert.");
                if (third.Find(imported)?.UniverseId != 10415694629)
                    return ("FAIL", "Die nachgetragene Universe-ID wurde nicht gespeichert.");

                var victim = reread.Entries[^1].PlaceId;
                if (!reread.Forget(victim) || reread.Entries.Count != RecentStore.Capacity - 1)
                    return ("FAIL", "Ein Eintrag lässt sich nicht vergessen.");

                if (!reread.Clear() || reread.Entries.Count != 0)
                    return ("FAIL", "Die Liste lässt sich nicht leeren.");

                return ("OK", $"Anlegen, Zusammenschieben, Deckel bei {RecentStore.Capacity}, Vergessen und Leeren.");
            }, results);

            Test("Roblox' eigene Sitzungen aus den Logs", () =>
            {
                // Genau die Zeilen, die der Client wirklich schreibt (aus einer echten Logdatei).
                const string text = """
                    [FLog::Output] Joining game 'b198fcc5-922a-47e6-8f26-26e30fe86ebf' place 123974602339071 at 10.20.3.14
                    universeid:9271746647
                    [FLog::Output] Joining game '8bb25232-d08a-4d65-a9d5-c4801818c716' place 128451689942376 at 10.20.6.19
                    universeid:6760085372
                    """;

                if (!RobloxLogImport.TryParseSession(text, out var placeId, out var universeId))
                    return ("FAIL", "Die Beitrittszeile wurde nicht erkannt.");

                // Die letzte Zeile zählt - das ist der Server, auf dem die Sitzung endete.
                if (placeId != 128451689942376) return ("FAIL", $"Platz-ID: {placeId}");
                if (universeId != 6760085372) return ("FAIL", $"Universe-ID: {universeId}");

                if (RobloxLogImport.TryParseSession("kein Beitritt", out _, out _))
                    return ("FAIL", "Text ohne Beitritt gilt als Sitzung.");

                if (!RobloxLogImport.TryParseStamp(
                        "0.744.0.7441080_20261027T210619Z_Player_5DB91_last.log", out var stamp))
                    return ("FAIL", "Der Zeitpunkt im Dateinamen wurde nicht gelesen.");

                if (stamp.Year != 2026 || stamp.Month != 10 || stamp.Day != 27)
                    return ("FAIL", $"Datum: {stamp:yyyy-MM-dd HH:mm}");

                if (RobloxLogImport.TryParseStamp("irgendwas.log", out _))
                    return ("FAIL", "Eine Datei ohne Zeitstempel liefert trotzdem einen.");

                return ("OK", $"Letzter Beitritt aus der Logzeile ({placeId}) samt Zeitstempel.");
            }, results);

            Test("Aktueller Platz und Server aus Roblox' Log", () =>
            {
                var join = RobloxLogImport.LastJoin();
                if (join is not RobloxJoin { PlaceId: > 0 } current)
                    return ("SKIP", "Keine Roblox-Sitzung im Log gefunden.");

                return string.IsNullOrWhiteSpace(current.JobId)
                    ? ("FAIL", $"Platz {current.PlaceId} erkannt, aber keine Job-ID.")
                    : ("OK", $"Platz {current.PlaceId} auf Server {current.JobId[..Math.Min(8, current.JobId.Length)]}…");
            }, results);

            Test("Spiele sortieren mit der Maus", () =>
            {
                var file = Path.Combine(tempRoot, "places-order.json");
                var store = new PlaceStore(file);
                store.Load();

                var first = store.Add(111, null, "Erster");
                var second = store.Add(222, null, "Zweiter");
                var third = store.Add(333, null, "Dritter");

                // Von Hand gezogen: der letzte soll nach oben.
                if (!store.SetOrder(new[] { third, first, second }))
                    return ("FAIL", "Die Reihenfolge ließ sich nicht schreiben.");

                if (third.SortIndex != 0 || first.SortIndex != 1 || second.SortIndex != 2)
                    return ("FAIL", $"Indizes: {third.SortIndex}/{first.SortIndex}/{second.SortIndex}");

                var reread = new PlaceStore(file);
                reread.Load();

                var order = reread.Places.OrderBy(place => place.SortIndex).Select(place => place.PlaceId).ToList();
                if (!order.SequenceEqual(new long[] { 333, 111, 222 }))
                    return ("FAIL", $"Gelesen: {string.Join(",", order)}");

                return ("OK", "Ziehen schreibt die Reihenfolge und sie bleibt in places.json stehen.");
            }, results);

            Test("Roblox-Sitzungen in die Zuletzt-Liste übernehmen", () =>
            {
                var file = Path.Combine(tempRoot, "recent-import.json");
                var store = new RecentStore(file);
                store.Load();

                store.Record(1818, null, "Crossroads", 312, "tester");

                var older = DateTimeOffset.Now.AddHours(-5);
                if (!store.Import(999, 4711, older))
                    return ("FAIL", "Eine fremde Sitzung ließ sich nicht übernehmen.");

                if (store.Import(999, 4711, older))
                    return ("FAIL", "Derselbe Platz wurde zweimal übernommen.");

                if (store.Import(1818, 312, DateTimeOffset.Now.AddHours(-1)))
                    return ("FAIL", "Ein eigenes Spiel wurde von der Logliste überschrieben.");

                var imported = store.Entries.FirstOrDefault(entry => entry.PlaceId == 999);
                if (imported is null || !imported.FromLog)
                    return ("FAIL", "Der Eintrag ist nicht als Roblox-Sitzung gekennzeichnet.");

                // Neueste zuerst - der eigene Start bleibt vorn.
                if (store.Entries[0].PlaceId != 1818)
                    return ("FAIL", $"Reihenfolge: {store.Entries[0].PlaceId}");

                var reread = new RecentStore(file);
                reread.Load();
                if (reread.Entries.Count != 2) return ("FAIL", "Die übernommene Sitzung wurde nicht gespeichert.");

                return ("OK", $"Fremde Sitzungen kommen dazu, eigene bleiben - {reread.Entries.Count} Einträge.");
            }, results);

            Test("Spiele anheften (places.json)", () =>
            {
                var file = Path.Combine(tempRoot, "places-pin.json");
                var store = new PlaceStore(file);
                store.Load();

                var record = store.Add(1818, null, "Crossroads");
                if (record.IsPinned) return ("FAIL", "Ein neues Spiel ist schon angeheftet.");

                store.SetPinned(record, true);

                var reread = new PlaceStore(file);
                reread.Load();
                var pinned = reread.Find(1818);
                if (pinned is null || !pinned.IsPinned || pinned.PinnedAt is null)
                    return ("FAIL", "Das Anheften wurde nicht gespeichert.");

                reread.SetPinned(pinned, false);
                var unpinned = new PlaceStore(file);
                unpinned.Load();
                if (unpinned.Find(1818) is not { IsPinned: false, PinnedAt: null })
                    return ("FAIL", "Das Abheften wurde nicht gespeichert.");

                return ("OK", "Anheften und Abheften bleiben in places.json stehen.");
            }, results);

            Test("Lage des Ingame-Overlays", () =>
            {
                // Die Lage liegt bewusst in einer eigenen Datei: Launcher und Overlay-Prozess
                // schreiben sonst beide settings.json und überschreiben sich gegenseitig.
                // Der Test benutzt eine eigene Datei: die echte Lage des Benutzers bleibt unberührt.
                var file = Path.Combine(tempRoot, "overlay.json");
                if (File.Exists(file)) File.Delete(file);

                if (OverlayPlacement.Load(file).Panels.Count != 0)
                    return ("FAIL", "Eine frische Datei enthält schon eine Lage.");

                var state = new OverlayState();
                state.Panels["menu"] = new OverlaySpot { Left = 812.5, Top = 96 };

                if (!OverlayPlacement.Save(state, file))
                    return ("FAIL", "Die Lage ließ sich nicht schreiben.");

                var back = OverlayPlacement.Load(file);
                if (!back.Panels.TryGetValue("menu", out var spot) || spot.Left != 812.5 || spot.Top != 96)
                    return ("FAIL", $"Zurückgelesen: {spot?.Left}/{spot?.Top}");

                if (!OverlayPlacement.Clear(file) || OverlayPlacement.Load(file).Panels.Count != 0)
                    return ("FAIL", "Die gemerkte Lage ließ sich nicht wieder entfernen.");

                // Unsinnige Werte dürfen eine Platte nicht unauffindbar machen.
                File.WriteAllText(file, "{ \"Panels\": { \"menu\": { \"Left\": 1e999, \"Top\": 0 } } }");
                if (OverlayPlacement.Load(file).Panels.Count != 0)
                    return ("FAIL", "Ein unbrauchbarer Wert wird übernommen.");

                return ("OK", "Merken, Zurücklesen, Zurücksetzen - und unbrauchbare Werte werden verworfen.");
            }, results);

            Test("Arbeitsfläche fürs Andocken", () =>
            {
                var area = GameOverlay.WorkAreaAt(40, 40);
                if (area.Right - area.Left < 320 || area.Bottom - area.Top < 240)
                    return ("FAIL", $"Unbrauchbare Arbeitsfläche: {area.Left},{area.Top} - {area.Right},{area.Bottom}");

                return ("OK", $"Bildschirm bis {area.Right}×{area.Bottom} - das Overlay bleibt darin greifbar.");
            }, results);

            Test("Tastenkürzel der Schnellansicht", () =>
            {
                if (Shortcuts.Format(ModifierKeys.Shift, Key.Tab) != "Shift+Tab")
                    return ("FAIL", "Shift+Tab wird nicht geschrieben.");

                if (!Shortcuts.TryParse("Shift+Tab", out var modifiers, out var key) ||
                    modifiers != ModifierKeys.Shift || key != Key.Tab)
                    return ("FAIL", "Shift+Tab wird nicht gelesen.");

                if (!Shortcuts.Matches("Shift+Tab", ModifierKeys.Shift, Key.Tab))
                    return ("FAIL", "Shift+Tab passt nicht auf den Tastendruck.");

                if (Shortcuts.Matches("Shift+Tab", ModifierKeys.Control | ModifierKeys.Shift, Key.Tab))
                    return ("FAIL", "Strg+Shift+Tab öffnet das Layout ungewollt.");

                if (Shortcuts.Matches("Shift+Tab", ModifierKeys.None, Key.Tab))
                    return ("FAIL", "Tab allein öffnet das Layout.");

                if (Shortcuts.Format(ModifierKeys.None, Key.L) is not null)
                    return ("FAIL", "Eine einzelne Buchstabentaste wäre als Kürzel erlaubt.");

                if (Shortcuts.Format(ModifierKeys.None, Key.F8) != "F8")
                    return ("FAIL", "Funktionstasten taugen nicht als Kürzel.");

                // Entf/Einfg allein: die klassischen Overlay-Tasten. Sie tippt man nie versehentlich.
                if (Shortcuts.Format(ModifierKeys.None, Key.Delete) != "Delete")
                    return ("FAIL", "Entf allein wäre kein Kürzel.");

                if (!Shortcuts.TryParse("Delete", out var delMods, out var delKey) ||
                    delMods != ModifierKeys.None || delKey != Key.Delete)
                    return ("FAIL", "Delete wird nicht gelesen.");

                if (!Shortcuts.TryParseKey("LeftShift", out var shiftKey) || shiftKey != Key.LeftShift)
                    return ("FAIL", "Eine einzelne Zusatztaste ist nicht lesbar (Anti-AFK).");

                if (!Shortcuts.CaptureCandidates(includeModifiers: true).Contains(Key.LeftShift))
                    return ("FAIL", "Beim Umbelegen fehlen die Zusatztasten.");

                if (Shortcuts.Format(ModifierKeys.Control | ModifierKeys.Alt, Key.D5) != "Ctrl+Alt+5")
                    return ("FAIL", $"Ziffern werden falsch benannt: {Shortcuts.Format(ModifierKeys.Control | ModifierKeys.Alt, Key.D5)}");

                if (Shortcuts.TryParse("Shift", out _, out _))
                    return ("FAIL", "Nur Zusatztasten gelten als Kürzel.");

                if (!Shortcuts.TryParse("F8", out var none, out var f8) || none != ModifierKeys.None || f8 != Key.F8)
                    return ("FAIL", "F8 wird nicht gelesen.");

                return ("OK", "Schreiben, Lesen und Vergleichen stimmen - auch die Absagen.");
            }, results);

            Test("Notizen der Schnellansicht", () =>
            {
                var file = Path.Combine(tempRoot, "notes.txt");
                if (!Notes.TrySave("erste Zeile\nzweite Zeile", out var error, file))
                    return ("FAIL", error ?? "Speichern fehlgeschlagen.");

                var read = Notes.Load(file);
                if (!read.Contains("zweite Zeile", StringComparison.Ordinal))
                    return ("FAIL", "Der Text kam nicht zurück.");

                if (!Notes.TrySave("", out _, file) || Notes.Load(file).Length != 0)
                    return ("FAIL", "Leere Notizen lassen sich nicht speichern.");

                return ("OK", "Schreiben und Lesen inklusive Leerfall.");
            }, results);

            Test("Schnellansicht ist ab Werk an", () =>
            {
                var settings = new LauncherSettings();
                if (!settings.OverlayEnabled) return ("FAIL", "OverlayEnabled ist ab Werk aus.");
                if (settings.OverlayShortcut != Shortcuts.Default)
                    return ("FAIL", $"Kürzel ab Werk: {settings.OverlayShortcut}");

                // Das Menü geht ab Werk mit einer einzelnen Taste auf (Entf): im Spiel hält man
                // nichts dabei, und in ein Eingabefeld tippt sie weiterhin normal hinein.
                if (!Shortcuts.TryParse(settings.OverlayShortcut, out var defaultModifiers, out var defaultKey) ||
                    defaultModifiers != ModifierKeys.None)
                    return ("FAIL", $"Kürzel ab Werk ({settings.OverlayShortcut}) braucht eine Zusatztaste.");

                return ("OK", $"OverlayEnabled = true, Kürzel {Shortcuts.Name(defaultKey)} allein - umbelegbar.");
            }, results);

            Test("Bildrate fürs Ingame-Overlay", () =>
            {
                var file = AppPaths.FastFlagsFile;
                var before = File.Exists(file) ? File.ReadAllText(file) : null;

                try
                {
                    if (FastFlags.TrySet(FastFlags.FrameRateFlag, "144") is { } error)
                        return ("FAIL", error);

                    if (FastFlags.ReadFrameRate() != 144)
                        return ("FAIL", $"Gelesen: {FastFlags.ReadFrameRate()}");

                    if (FastFlags.TrySet(FastFlags.FrameRateFlag, "10000") is { } uncapError)
                        return ("FAIL", uncapError);

                    if (FastFlags.ReadFrameRate() != FastFlags.UncappedFrameRate)
                        return ("FAIL", "Uncapped (10000) wurde nicht gespeichert.");

                    var values = FastFlags.FrameRateCaps.Select(c => c.Value).ToList();
                    if (!values.Contains(60) || !values.Contains(240) || !values.Contains(FastFlags.UncappedFrameRate))
                        return ("FAIL", $"Chips unvollständig: {string.Join(", ", values)}");

                    return ("OK", "Setzen, Lesen und die Chips (60/120/144/240/Uncapped) stimmen.");
                }
                finally
                {
                    // Der Test schreibt in die echte Datei - danach wieder auf den alten Stand.
                    if (before is not null) File.WriteAllText(file, before);
                    else if (File.Exists(file)) File.Delete(file);
                }
            }, results);

            Test("Ingame-Overlay ist ab Werk an", () =>
            {
                var settings = new LauncherSettings();
                if (!settings.GameOverlayEnabled) return ("FAIL", "GameOverlayEnabled ist ab Werk aus.");
                if (settings.GameOverlayAutoShow) return ("FAIL", "Das Menü würde sich ab Werk selbst öffnen.");
                if (settings.LastPlaceId is not null) return ("FAIL", "Es ist ab Werk ein Platz gemerkt.");

                return ("OK", "Overlay an, bleibt beim Start zu, ohne gemerktes Spiel.");
            }, results);

            Test("Ingame-Overlay (sechs Platten bauen auf)", () =>
            {
                // Baut die Platten nur auf, ohne sie zu zeigen: prüft XAML, Namen und Vorlagen -
                // ein fehlender StaticResource-Schlüssel fällt sonst erst beim Spiel auf.
                var previous = Application.Current.ShutdownMode;
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var host = new OverlayHost(preview: true, settings: new LauncherSettings { LastPlaceId = 1818 });
                try
                {
                    if (host.Windows.Count != 6)
                        return ("FAIL", $"Platten: {host.Windows.Count}");

                    var fps = host.WindowFor(PanelIds.Fps);
                    var graphics = host.WindowFor(PanelIds.Graphics);
                    var notes = host.WindowFor(PanelIds.Notes);
                    var server = host.WindowFor(PanelIds.Server);
                    var antiAfk = host.WindowFor(PanelIds.AntiAfk);
                    var menu = host.WindowFor(PanelIds.Menu);

                    if (fps is null || graphics is null || notes is null || server is null || antiAfk is null || menu is null)
                        return ("FAIL", "Mindestens eine Platte fehlt.");

                    var chips = All<RadioButton>(fps.Body).Count(chip => chip.GroupName == "Fps");
                    if (chips != 5) return ("FAIL", $"Bildraten-Chips: {chips}");

                    var graphicsRows = All<RadioButton>(graphics.Body).Count(chip => chip.GroupName is
                        "Quality" or "Texture" or "Msaa" or "Grass");
                    if (graphicsRows != 9) return ("FAIL", $"Grafik-Optionen: {graphicsRows}");

                    if (!All<TextBox>(notes.Body).Any())
                        return ("FAIL", "Das Notizfeld fehlt.");

                    if (!All<TextBlock>(server.Body).Any(text => text.Text == "Leave & rejoin"))
                        return ("FAIL", "Der Serverwechsel-Knopf fehlt.");

                    if (!All<CheckBox>(antiAfk.Body).Any(box => box.Name == "EnabledSwitch"))
                        return ("FAIL", "Der Anti-AFK-Schalter fehlt.");

                    if (!All<CheckBox>(menu.Body).Any(box => box.Name == "NoOverlapSwitch"))
                        return ("FAIL", "Der Schalter gegen Überlappen fehlt.");

                    // Jede Platte braucht ein Besitzerfenster, sonst steht sie in Alt+Tab.
                    var withoutOwner = host.Windows.Count(window => window.Owner is null);
                    if (withoutOwner > 0)
                        return ("FAIL", $"{withoutOwner} Platte(n) ohne Besitzerfenster.");

                    // Einmal außerhalb des Bildes zeigen und wirklich nachsehen, ob Windows
                    // die Platte noch im Fensterwechsler führt. Das Fenster ist topmost, liegt
                    // aber weit außerhalb des Virtuellen Bildschirms - niemand sieht es.
                    menu.Left = SystemParameters.VirtualScreenLeft - 2000;
                    menu.Top = SystemParameters.VirtualScreenTop - 2000;
                    menu.Show();
                    menu.UpdateLayout();
                    menu.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var hiddenFromSwitcher = menu.HiddenFromTaskSwitcher;
                    menu.Hide();

                    if (!hiddenFromSwitcher)
                        return ("FAIL", "Die Platte steht noch in Alt+Tab/Win+Tab.");

                    // Die Platten müssen nach dem Aufbauen eine brauchbare Größe haben - sonst
                    // landen sie beim Andocken halb außerhalb des Bildes.
                    fps.Body.Measure(new System.Windows.Size(324, double.PositiveInfinity));
                    var height = fps.Body.DesiredSize.Height;
                    if (height < 180) return ("FAIL", $"Die Bildraten-Platte bleibt zu klein: {height:0} px");

                    return ("OK", "6 Platten: Menü, Bildrate (5 Chips), Grafik (9 Optionen), Notizen, Server, Anti-AFK - und aus Alt+Tab heraus.");
                }
                finally
                {
                    host.Quit();
                    Application.Current.ShutdownMode = previous;
                }
            }, results);

            Test("Overlay merkt sich jede Platte einzeln", () =>
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

                    if (!OverlayPlacement.Save(state)) return ("FAIL", "Speichern ging nicht.");

                    var loaded = OverlayPlacement.Load();
                    if (loaded.Panels.Count != 2) return ("FAIL", $"Gelesen: {loaded.Panels.Count} Platten");
                    if (loaded.Panels["fps"].Left != 111 || loaded.Panels["notes"].Top != 444)
                        return ("FAIL", "Die Stellen stimmen nicht.");
                    if (loaded.Panels["notes"].Visible) return ("FAIL", "Die Sichtbarkeit ging verloren.");
                    if (!loaded.AntiAfkEnabled || loaded.AntiAfkIntervalSeconds != 300)
                        return ("FAIL", "Die Anti-AFK-Einstellungen fehlen.");
                    if (!loaded.HideOverOtherApps)
                        return ("FAIL", "Das Zurückweichen vor anderen Programmen ist nicht an.");

                    return ("OK", "Lage, Sichtbarkeit und Anti-AFK liegen getrennt in overlay.json.");
                }
                finally
                {
                    if (before is not null) File.WriteAllText(file, before);
                    else if (File.Exists(file)) File.Delete(file);
                }
            }, results);

            Test("Anti-AFK (Abstände, Pop-up, Taste)", () =>
            {
                if (!AntiAfk.ValidInterval(60) || !AntiAfk.ValidInterval(1800))
                    return ("FAIL", "Gültige Abstände werden abgelehnt.");

                if (AntiAfk.ValidInterval(5) || AntiAfk.ValidInterval(5000))
                    return ("FAIL", "Unsinnige Abstände sind erlaubt.");

                if (AntiAfk.DescribeInterval(120) != "2 min" || AntiAfk.DescribeInterval(90) != "1 min 30 s")
                    return ("FAIL", $"Abstände werden falsch benannt: {AntiAfk.DescribeInterval(120)} / {AntiAfk.DescribeInterval(90)}");

                if (!Shortcuts.TryParseKey(AntiAfk.DefaultKey, out var key) || key != Key.F8)
                    return ("FAIL", "Die voreingestellte Taste ist nicht lesbar.");

                if (AntiAfk.PopupTime < TimeSpan.FromMilliseconds(700) || AntiAfk.PopupTime > TimeSpan.FromSeconds(2))
                    return ("FAIL", $"Unsinnige Pop-up-Zeit: {AntiAfk.PopupTime}.");

                // Ohne Client darf nichts passieren - und es muss dabeistehen, warum.
                if (GameOverlay.FindWindow() is null)
                {
                    if (AntiAfk.Nudge(null, AntiAfk.DefaultKey, out var message) != AntiAfk.NudgeOutcome.NoWindow)
                        return ("FAIL", "Ohne Roblox wurde trotzdem ein Stoß gemeldet.");

                    if (string.IsNullOrWhiteSpace(message))
                        return ("FAIL", "Ohne Roblox fehlt die Erklärung.");
                }

                return ("OK", "30 s bis 30 min, einsekündiges Pop-up, F8 als sichere Vorgabe, klare Meldung ohne Client.");
            }, results);

            Test("Farbwelten (sofort umfärben und zurück)", () =>
            {
                if (ThemeManager.All.Count < 4)
                    return ("FAIL", $"Nur {ThemeManager.All.Count} Farbwelten vorhanden.");

                var before = ThemeManager.Current.Id;
                var accent = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;

                var chosen = ThemeManager.Apply("emerald");
                var changed = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;

                ThemeManager.Apply(before);
                var restored = ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color;

                if (chosen.Id != "emerald")
                    return ("FAIL", $"Emerald wurde nicht gewählt: {chosen.Id}");
                if (changed == accent)
                    return ("FAIL", "Der Akzentpinsel blieb gleich - er ist eingefroren.");
                if (restored != accent)
                    return ("FAIL", "Die alte Farbwelt kam nicht zurück.");
                if (ThemeManager.Find("gibt-es-nicht").Id != ThemeManager.DefaultId)
                    return ("FAIL", "Eine unbekannte Kennung fällt nicht auf den Standard zurück.");

                return ("OK", $"{ThemeManager.All.Count} Farbwelten, Akzent wechselt und kommt zurück.");
            }, results);

            Test("Anti-AFK (echte Eingabe, Zeiger kommt zurück)", () =>
            {
                if (AntiAfk.RetryDelay < TimeSpan.FromSeconds(5) || AntiAfk.RetryDelay > TimeSpan.FromSeconds(60))
                    return ("FAIL", $"Unsinnige Wartezeit nach einem Fehlversuch: {AntiAfk.RetryDelay}.");

                // Das ist der eigentliche Beweis: Windows muss die synthetische Bewegung annehmen.
                // Der Zeiger wird dabei um zwei Pixel bewegt und danach zurückgesetzt.
                if (!AntiAfk.TryTestInput(out var message))
                    return ("FAIL", message);

                return ("OK", message);
            }, results);

            Test("Serverwechsel: alter Client zuerst", () =>
            {
                var fresh = ServerHop.FreshIds(new[] { 10, 11 }, new[] { 10, 12 });
                if (fresh.Count != 1 || !fresh.Contains(12))
                    return ("FAIL", $"Neue Clients erkannt: {string.Join(",", fresh)}");

                if (ServerHop.FreshIds(new[] { 10, 11 }, new[] { 10, 11 }).Count != 0)
                    return ("FAIL", "Alte Clients gelten als neu.");

                return ("OK", "Aether schließt zuerst den alten Client und erkennt dann den neu gestarteten.");
            }, results);

            Test("Serverwechsel: freien Server wählen", () =>
            {
                var chosen = ServerHop.PickServer(new[]
                {
                    new RobloxApi.PublicServer("full", 10, 10),
                    new RobloxApi.PublicServer("free", 4, 10),
                });

                if (chosen?.JobId != "free")
                    return ("FAIL", $"Gewählt: {chosen?.JobId ?? "nichts"} - ein voller Server darf nicht gewinnen.");

                if (ServerHop.PickServer(new[] { new RobloxApi.PublicServer("full", 10, 10) }) is not null)
                    return ("FAIL", "Ein voller Server wurde trotzdem gewählt.");

                var other = ServerHop.PickServer(new[]
                {
                    new RobloxApi.PublicServer("current", 2, 10),
                    new RobloxApi.PublicServer("other", 2, 10),
                }, avoidJobId: "current");

                if (other?.JobId != "other")
                    return ("FAIL", $"Der aktuelle Server wurde erneut gewählt: {other?.JobId ?? "nichts"}.");

                return ("OK", "Volle und der aktuelle Server fallen raus, freie werden als Ziel genommen.");
            }, results);

            Test("Roblox-Deployment-Kanal (Netzwerk)", () =>
            {
                var deployed = RobloxInstall.GetDeployedVersionAsync("production").GetAwaiter().GetResult();
                return deployed is null
                    ? ("SKIP", "Keine Antwort vom Deployment-Dienst (offline?).")
                    : ("OK", $"Für Kanal production ist {deployed.Guid} ({deployed.NumericVersion}) ausgerollt.");
            }, results);
        }
        catch (Exception ex)
        {
            results.Add(new Result("Selbsttest-Ablauf", "FAIL", ex.Message));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Testdaten im Temp-Ordner sind nicht kritisch.
            }
        }

        report.AppendLine(new string('-', 78));
        foreach (var result in results)
            report.AppendLine($"  [{result.State,-4}] {result.Name,-38} {result.Detail}");

        var failed = results.Count(r => r.Failed);
        var skipped = results.Count(r => r.State == "SKIP");
        report.AppendLine(new string('-', 78));
        report.AppendLine($"  {results.Count - failed - skipped} bestanden, {failed} fehlgeschlagen, {skipped} übersprungen");
        report.AppendLine();

        var text = report.ToString();
        try
        {
            // Immer auch in eine Datei schreiben: bei einer Windows-GUI-Anwendung kann die
            // Konsolenausgabe verloren gehen, wenn der Aufrufer keine Konsole hat.
            File.WriteAllText(Path.Combine(AppPaths.DataDir, "selftest.log"), text);
        }
        catch
        {
            // Nicht kritisch.
        }

        try
        {
            Console.Write(text);
            Console.Out.Flush();
        }
        catch
        {
            // Ohne Ausgabekanal bleibt die Datei die Quelle der Wahrheit.
        }

        return failed == 0 ? 0 : 1;
    }

    /// <summary>Schreibt ein einfarbiges PNG - Baustein für die Testdaten.</summary>
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

    /// <summary>Das mitgelieferte Zeigerbild, aufgelöst neben der Anwendung.</summary>
    private static string BundledCursorImage()
    {
        // Genau der Pfad, den auch der Launcher benutzt - das Auspacken ist idempotent.
        var unpacked = ImageTool.EnsureBundledCursor(AppPaths.CursorImageFile);
        if (string.IsNullOrEmpty(unpacked)) throw new InvalidOperationException("Assets/cursor.png fehlt im Programm.");
        return unpacked;
    }

    /// <summary>Alle Elemente eines Baums - auch ohne gezeigtes Fenster (logischer Baum).</summary>
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
            results.Add(new Result(name, "FAIL", $"Ausnahme: {ex.Message}"));
        }
    }

    /// <summary>
    /// Sorgt dafür, dass die Ausgabe ankommt: Bei einer GUI-Anwendung hängt sich der Prozess
    /// an ein vorhandenes Terminal an, sonst ist die Ausgabe nur im Aufrufer-Pipe sichtbar.
    /// </summary>
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
            // Ohne Ausgabekanal läuft der Test trotzdem durch - die Datei selftest.log bleibt.
        }
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
