# Aether Launcher

Ein quelloffener Roblox-Launcher für Windows mit Kontomanager, Multi-Instanz-Start,
Versionsschalter, FastFlags-Editor, eigenen Schriften und eigenem Mauszeiger. Geschrieben in
C# / .NET 10 mit WPF, ohne Electron und ohne Installer. Die einzige NuGet-Abhängigkeit ist
Microsoft WebView2 für die eingebettete Roblox-Anmeldung.

Die Oberfläche folgt der Design-Sprache des Aether-Hub-Projekts: gleiche Palette, gleiche
Karten, gleiche Kopfleiste mit Reitern, gleiche Fußzeile - nur mit den Funktionen eines
Launchers und durchgehend weichen Übergängen.

![Plattform](https://img.shields.io/badge/Plattform-Windows%2010%20%7C%2011-0078D6)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Lizenz](https://img.shields.io/badge/Lizenz-MIT-green)
![Abhängigkeiten](https://img.shields.io/badge/NuGet--Abhängigkeiten-1-brightgreen)

---

## Funktionen

| Funktion | Beschreibung |
| --- | --- |
| **Kontomanager** | Beliebig viele Konten speichern, prüfen und starten. **Sign in with Roblox** öffnet Roblox' eigene Anmeldeseite in einem temporären Browser - das Passwort sieht Aether nie. Gespeichert wird nur das Cookie, mit AES-256-GCM verschlüsselt; der AES-Schlüssel ist per DPAPI an deinen Windows-Benutzer und eine app-eigene Entropie gebunden. |
| **Start als Konto** | Der Launcher holt ein offizielles Auth-Ticket (`auth.roblox.com`) für das gewählte Konto und startet den Client damit - kein Konto-Wechseln im Browser. |
| **Versionsschalter** | Jede installierte Client-Version einzeln anwählen oder auf **Automatic** (neueste) lassen. Damit startet genau die Fassung, die ein anderes Programm erwartet. |
| **Vanilla-Modus** | Der Client bleibt, wie Roblox ihn installiert hat: keine Flags, keine Texturen, kein Zusatzprozess. Programme wie Velocity, Madium oder Volt finden den normalen Zweig. |
| **Multi-Instanz** | Mehrere Roblox-Clients gleichzeitig. Ein Wächterprozess hält den Roblox-Singleton, solange Clients laufen - auch wenn der Launcher geschlossen wird. |
| **Multi-Start** | Denselben Platz nacheinander mit allen eingeschalteten Konten starten, mit einstellbarer Pause zwischen den Starts. |
| **Spieleliste** | Plätze als Place-ID, `roblox.com/games`-Link oder privater Serverlink speichern - mit Name und Icon aus der Roblox-API. |
| **Favoriten (anheften)** | Der große Stern neben einer Zeile heftet ein Spiel oben an: Angeheftete stehen vor allem anderen, egal wie alt der letzte Start ist. Bleibt in `places.json` stehen. |
| **Spiele sortieren** | Eine Zeile mit der Maus packen und nach oben oder unten ziehen - die Reihenfolge wird in `places.json` festgehalten. Angeheftete Spiele wandern beim Anheften nach oben, bleiben aber sonst genau da, wo man sie hinzieht. |
| **Zuletzt gespielt** | Die Liste zeigt die Spiele selbst, in derselben Zeilengröße wie **Your games**: jeder Start über Aether (`recent.json`) **und** die Sitzungen, die Roblox selbst gestartet hat - die liest Aether beim Start aus den Logdateien des Clients (`place`-ID und Zeitstempel, sonst nichts). Den echten Spielnamen und das Bild holt Aether für solche Sitzungen von der Roblox-API nach und schreibt beides in `recent.json` - die Zeile heißt also nicht dauerhaft "Place 128…". **Rescan** auf der Kontenseite liest sie sofort noch einmal ein - auch Spiele, die erst während einer laufenden Aether-Sitzung ohne Aether gestartet wurden, stehen dann in der Liste. Mit `+` wandert ein Eintrag in die eigene Sammlung, mit `✕` heraus. |
| **FastFlags** | **Kartenansicht mit 99 Vorschlägen in sieben Gruppen** (Bildrate, Grafik, Himmel & Licht, Effekte & Renderer, Texturen & Detail, Oberfläche, Netzwerk) und **153 einzelnen Flags**; die Flags landen vor jedem Start in den `ClientSettings` des Clients. Suche, Filter-Chips und Favoriten halten die Liste kurz; ein Klick auf eine Karte setzt ihre Flags, ein zweiter nimmt sie wieder heraus. Über **Visual / JSON** wechselt dieselbe Seite in den JSON-Editor. Vorhandene Dateien werden gesichert; beim Schreiben liest Aether die Datei zurück und prüft jeden Wert. |
| **Allowlist-Kennzeichnung** | Jede Karte trägt eine Markierung: **ALLOWLISTED**, wenn das Flag auf Roblox' Allowlist steht, sonst **LEGACY**. 49 der 99 Karten stehen derzeit auf der Liste. Seit September 2025 liest der Client nur noch allowlistete Flags - so sieht man vorher, was wirken kann statt sich zu wundern. |
| **Start ohne Spiel** | Kein Spiel gewählt? Dann öffnet **Play** die Roblox-App selbst (Startseite) - wahlweise mit dem gewählten Konto. Für den direkten Weg dorthin gibt es den Knopf **Roblox app**. |
| **Versionen herunterladen** | Jede verfolgte oder historische Roblox-Version von WEAO bzw. der offiziellen DeployHistory auswählen und den original Installer vom Roblox-CDN laden, mit Fortschritt und Installieren-Knopf. |
| **FastFlags sofort** | Eine angeklickte Karte schreibt sich unmittelbar in den JSON-Editor (und nimmt sich beim Abwählen wieder heraus). Die Kartenansicht ist der Start; der **Visual / JSON**-Umschalter öffnet den Editor mit Formatieren, Kopieren, Import/Export und Leeren. Suche und Kategorie-Chips filtern beide Ansichten, Favoriten bleiben in `fastflags-favorites.txt`. |
| **Ingame-Zeiger** | Der Mauszeiger im Spiel lässt sich durch ein eigenes PNG ersetzen - auf Wunsch mit dem mitgelieferten Aether-Pfeil. Originale bleiben als `.aetherbak`. |
| **Eigene Schriften** | Jede installierte Schrift oder eine eigene `.ttf`/`.otf` für die Oberfläche. Die Datei wird nur für diesen Prozess angemeldet. |
| **Aether-Zeiger** | Der Pfeil aus `tools/make-cursor.ps1` ersetzt den Windows-Zeiger im Launcher. |
| **Farbwelten** | Zwölf Paletten - **Midnight**, **Amethyst**, **Emerald**, **Ember**, **Rose**, **Glacier**, **Sandstone**, **Onyx**, **Aurora**, **Crimson**, **Moss** und **Dusk** - schalten die ganze Oberfläche live um, ohne Neustart. Der Wechsel blendet Flächen, Linien, Akzente und Verläufe über 420 ms weich hinüber; die Auswahl steht unter Einstellungen → Theme und bleibt gespeichert. Die Ingame-Platten folgen mit. |
| **Schnellansicht** | **Entf** legt eine Karte über die laufende Seite: starten, Vanilla-Modus, Flags, Ingame-Zeiger, Start als jedes gespeicherte Konto - und Notizen, die von selbst gespeichert werden. Ab Werk an; das Kürzel lässt sich in den Einstellungen **und** im Menü des Overlays ändern und dort auch zurücksetzen. In Eingabefeldern bleibt eine blanke Taste dem Text - dort reißt Entf keine Karte auf. |
| **Ingame-Overlay** | Dieselbe Taste über dem **Roblox-Fenster**: sechs einzelne Platten - Menü (Schalter je Platte, Menütaste, Deckkraft, Zurückweichen vor anderen Programmen, **Reset layout**), **Frame rate**, **Graphics**, **Notes**, **Server** (Leave & rejoin, Neustart, Spielseite, Link kopieren) und **Anti-AFK** (Intervall, Taste, Countdown, Selbsttest, kurzes Pop-up zum Tippen). Jede Platte wird einzeln gezogen: Sie folgt dem Zeiger gedämpft, hebt sich leicht an, rastet an Bildschirmkanten ein und merkt sich ihre Lage. Die Startaufteilung ist dreispaltig und überlappungsfrei. Alle Platten sind Toolwindows - sie tauchen nicht in Alt+Tab, Win+Tab oder der Taskleiste auf. Ein eigener, kleiner Prozess; in den Client wird nichts eingeschleust. **Fällt das Roblox-Fenster weg, nimmt der Host die Platten sofort weg. Ohne Roblox-Prozess beendet er sich nach drei Sekunden, mit laufendem Prozess ohne Fenster nach zwanzig; während eines Serverwechsels wartet er länger.** So bleibt kein eingefrorenes Overlay ohne Spiel stehen. |
| **Animationen** | Reiterstrich, Karten, Zeilen, Schalter und Knöpfe bewegen sich weich; Knöpfe antworten mit einer Aufhellung und einem Druck-Ring statt zu springen, Seiten gleiten herein, neue Zeilen wachsen auf, ein neu gewähltes Spiel blendet in der Hauptkarte hinüber, die Schnellansicht federt auf, das Overlay öffnet in Stufen und die Platten gleiten beim Ziehen mit einem kurzen Nachlauf in ihre Endlage. **Playful mode** legt federnde Einblendungen und einen Glanz über die Hauptkarte darüber. |
| **Protokollhandler (optional)** | Aether kann den `roblox-player`-Handler übernehmen. Dann laufen auch "Play"-Klicks der Website durch den Launcher. Der vorherige Handler wird gesichert. |
| **Client-Pflege** | Installierte Version, ausgerollte Version im Kanal und Update-Knopf direkt im Kontobereich. |
| **Tastenkürzel** | `Strg`+`1`…`5` Seiten, `Strg`+`Enter` starten, `Entf` Menü/Schnellansicht (umbelegbar), `F5` neu einlesen. |

## Website

Der Download-Auftritt liegt neben dem Launcher in [`../Website`](../Website): eine statische
Seite mit Feature-Übersicht, Theme-Vorschau, GitHub-Release-Download und einem
Feedback-Formular. Sie läuft ohne Build-Schritt auf **GitHub Pages** oder **Cloudflare Pages**;
**Supabase** speichert auf Wunsch Download-Klicks und Feedback (nur Insert, per RLS).
Einrichtung und Deployment stehen in [Website/README.md](../Website/README.md).

## Screenshots

| Home | Accounts |
| --- | --- |
| ![Home](screenshots/aether-home.png) | ![Accounts](screenshots/aether-accounts.png) |

| Engine (FastFlags) | Settings | Log |
| --- | --- | --- |
| ![Engine](screenshots/aether-engine.png) | ![Settings](screenshots/aether-settings.png) | ![Log](screenshots/aether-log.png) |

| Schnellansicht (`Entf`) | Ingame-Overlay (über Roblox) |
| --- | --- |
| ![Quick layout](screenshots/aether-layout.png) | ![Ingame overlay](screenshots/aether-ingame-overlay.png) |

![Settings, unterer Teil](screenshots/aether-settings-lower.png)

Die Bilder zeigen Beispieldaten (das offizielle Roblox-Konto und den Platz 1818) und werden mit
`--screenshot` erzeugt, siehe [Werkzeuge](#werkzeuge).

## Voraussetzungen

- Windows 10 oder 11 (x64)
- [.NET SDK 10](https://dotnet.microsoft.com/download) zum Bauen
- Eine installierte Roblox-Client-Version unter `%LOCALAPPDATA%\Roblox`

## Schnellstart

```bash
git clone <dein-fork> aether-launcher
cd aether-launcher
dotnet build src/AetherLauncher/AetherLauncher.csproj -c Release
./src/AetherLauncher/bin/Release/net10.0-windows/AetherLauncher.exe
```

Optional eine einzelne, in sich geschlossene EXE erzeugen:

```bash
dotnet publish src/AetherLauncher/AetherLauncher.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## Benutzung

### Konto hinzufügen

Der einfachste Weg: **Accounts → Add key → Sign in with Roblox** öffnet Roblox' eigene
Anmeldeseite in einem eigenen Fenster. Dort mit Namen und Passwort (oder Passkey) anmelden;
Aether liest nach der Anmeldung nur das `.ROBLOSECURITY`-Cookie aus dem temporären
Browserprofil. Das Passwort wird nirgends von Aether gesehen oder gespeichert, und das
Browserprofil wird beim Schließen gelöscht.

Der zweite Weg ohne das Fenster:

1. Im Browser bei [roblox.com](https://www.roblox.com) anmelden.
2. `F12` → **Application** → **Cookies** → `https://www.roblox.com` → Wert von `.ROBLOSECURITY` kopieren.
3. Im Launcher **Accounts → Add key**, Cookie einfügen, **Add account**.

In beiden Fällen prüft der Launcher den Schlüssel gegen die Roblox-API und zeigt Benutzername,
Anzeigename und Profilbild. Ein bereits gespeichertes Konto wird aktualisiert, nicht doppelt
angelegt. **Log in** neben einem Konto öffnet dieselbe Anmeldung noch einmal und ersetzt den
alten Schlüssel - praktisch, wenn eine Sitzung abgelaufen ist.

> [!WARNING]
> Das `.ROBLOSECURITY`-Cookie ist ein vollwertiger Zugangsschlüssel zu deinem Konto - wer es
> hat, ist dein Konto. Gib es nie weiter und poste es nicht in Screenshots oder Issues.
> Aether speichert es ausschließlich verschlüsselt: AES-256-GCM, der Schlüssel selbst per
> Windows-DPAPI an deinen Benutzer gebunden (lesbar nur für dich auf diesem Rechner). Das
> Passwort deines Roblox-Kontos wird nie gespeichert. Gesendet wird das Cookie nur an
> `roblox.com`; das Log maskiert Cookies und Auth-Tickets automatisch.

### Spiel starten

Auf **Home** oben eine Place-ID, eine `roblox.com/games/<id>`-URL oder einen privaten
Serverlink eintragen (**Add game**). Das Spiel erscheint in der Liste und lässt sich über die
große Karte oder über **Launch** neben einem Konto starten. Ohne Kontoauswahl startet der
Client mit der aktuell in Roblox angemeldeten Sitzung (**Settings → Start with the current
Roblox login**).

### Ohne Spiel starten (Roblox-App)

Ein Spiel muss nicht gewählt sein: **Play** heißt dann **Open Roblox** und öffnet die
Roblox-App (Startseite), mit dem gewählten Konto oder mit der vorhandenen Anmeldung. Der
Knopf **Roblox app** in der Hauptkarte macht das jederzeit - auch wenn ein Spiel gewählt ist.
Auch die Zeilen **Launch** in der Kontenliste öffnen ohne gewähltes Spiel die App statt ein
Spiel.

Technisch sind das zwei Wege: mit Konto baut der Launcher eine `roblox-player:…`-URI mit
`launchmode:app` und frischem Auth-Ticket, ohne Konto startet `RobloxPlayerBeta.exe` ganz ohne
Argumente - genau so öffnet Windows die Roblox-App.

### Welche Version startet

Unter **Settings → Roblox client → Starting version** stehen alle gefundenen
`version-<hash>`-Ordner. Die Zeile **Automatic** nimmt die neueste installierte Version - das
ist das, was Roblox selbst aktuell hält und in der Spalte "served by the channel" auftaucht.
Eine bestimmte Zeile anzuklicken heftet diese Version an; sie wird ab dann gestartet, auch
wenn inzwischen eine neuere im Kanal liegt.

Das ist die Einstellung für alles, was eine bestimmte Client-Fassung erwartet:
**Version anhängen + Vanilla-Modus an** - der Launcher startet dann genau die offizielle
Version aus dem normalen Zweig, ohne etwas an ihr zu verändern.

> [!NOTE]
> Angeheftet wird nur, was schon installiert ist. Aether lädt keine älteren Versionen nach;
> die Spalte "installed" zeigt, wie alt der jeweilige Ordner ist.

### Jede Roblox-Version herunterladen

**Settings → Download a version**. **Load list** holt die aktuelle Liste:

- **[WEAO](https://docs.weao.xyz)** (`weao.xyz`) - der Dienst, der für Windows die aktuelle,
die kommende und die vorherige Version verfolgt. Genau die Angaben, die man zum Zurücksetzen
auf die Version braucht, die ein bestimmtes Programm unterstützt. Die API verlangt den
User-Agent `WEAO-3PService`, den der Launcher selbstverständlich setzt.
- **Roblox DeployHistory** (`setup.rbxcdn.com/DeployHistory.txt`) - die offizielle Liste aller
ausgelieferten Builds. Daraus kommen rund 394 Windows-Builds mit Hash, Versionsnummer und Datum.

In der Liste filtern (Version, Nummer oder Quelle), Zeile auswählen und **Download installer**
drücken: der Launcher lädt den **offiziellen Installer von `setup.rbxcdn.com`** in den
Datenordner (mit Fortschritt in der Fußzeile), nichts von Drittanbietern. **Install now** ruft
ihn dann auf; anschließend **Rescan client** drücken und die Version oben anheften.

> [!NOTE]
> Der Download ist für jede Version derselbe offizielle Installer. Ob Roblox für einen sehr
alten Hash noch Dateien vorhält, entscheidet der CDN - antwortet er nicht, meldet der Launcher
es im Log, statt etwas anderes zu nehmen.

### Vanilla-Modus (für andere Tools)

**Settings → Aether → Vanilla mode** schaltet alles ab, was den Client anfasst:

| | Normal | Vanilla |
| --- | --- | --- |
| FastFlags schreiben | ja | nein |
| Ingame-Zeiger ersetzen | wenn eingeschaltet | nein |
| Multi-Instanz-Wächterprozess | ja | nein |
| Client-Dateien | nur die erlaubten | unberührt |

Der Client startet dadurch so, wie Roblox ihn installiert hat. Programme, die an den normalen
Client andocken (Velocity, Madium, Volt und ähnliche), finden die Version, die sie
unterstützen. Der Preis: nur ein Client gleichzeitig, und **Launch all** meldet, dass es im
Vanilla-Modus nicht geht.

### Ingame-Zeiger ändern

**Settings → In-game cursor**. Der Client zeichnet seinen Pfeil aus PNG-Dateien unter
`content\textures`; Aether ersetzt genau diese Zeiger-Texturen:

```
content\textures\ArrowCursor.png                     (Hauptzeiger)
content\textures\ArrowFarCursor.png                  (Zeiger in der Ferne)
content\textures\advCursor-default.png               (Zeiger auf Klickflächen)
content\textures\advCursor-white.png                 (helle Variante)
content\textures\MouseLockedCursor.png               (Punkt bei Maus-Sperre)
content\textures\Cursors\KeyboardMouse\ArrowCursor.png    (neue Ablage)
content\textures\Cursors\KeyboardMouse\ArrowFarCursor.png
content\textures\Cursors\CrossMouseIcon.png          (Fadenkreuz)
```

- **Replace the pointer in the game** einschalten, dann beim nächsten Start automatisch.
- **Choose PNG…** nimmt ein eigenes Bild; **Use bundled** nimmt den mitgelieferten Aether-Pfeil.
- **Apply now** schreibt sofort in die installierte Version, **Restore original** holt die
  gesicherten Dateien zurück und löscht die Sicherungen.

Jede Datei behält ihre ursprüngliche Pixelgröße (der Client rechnet damit), und jede wird
einmalig als `.aetherbak` gesichert. Nicht angefasst werden Programme, Skripte oder Speicher
des Clients - nur diese acht Bilddateien.

### Schrift der Oberfläche

**Settings → UI font** listet alle installierten Schriften; jeder Eintrag ist in seiner eigenen
Schrift gezeigt. **Load font file…** lädt eine eigene `.ttf`/`.otf`: die Datei landet in
`%LOCALAPPDATA%\AetherLauncher\fonts` und wird nur für diesen Prozess angemeldet
(`AddFontResourceEx` mit `FR_PRIVATE`), die Systemliste bleibt also unverändert. Der Wechsel
greift sofort, ohne Neustart.

### Mehrere Clients gleichzeitig

**Settings → Multiple Roblox instances** aktiv lassen. Im Bereich **Multi-Start** die gewünschten
Konten einschalten, die Pause wählen (4s / 8s / 15s) und **Launch all** drücken. Zwischen zwei
Starts wartet der Launcher, damit sich die Clients nicht überholen.

### FastFlags

Auf **Engine** stehen **99 Karten in sieben Gruppen** bereit (FPS, Graphics, Lighting,
Effects, Textures, Interface, Network). Unter jeder Karte stehen die Flag-Namen, die sie setzt -
einige setzen mehrere, etwa "Low-end preset" oder "Roblox telemetry off". Die Ansicht startet
als Kartenraster wie eine Script-Bibliothek: Suche, Kategorie-Chips und **★ Favorites** filtern
die Karten; der Stern oben rechts auf jeder Karte heftet sie an. **Visual / JSON** schaltet
dieselbe Seite zwischen Karten und JSON-Editor um.

**Ein Klick genügt:** Sobald eine Karte aktiviert wird, stehen ihre Flags sofort im JSON und
sind gespeichert; ein zweiter Klick nimmt sie wieder heraus. Wer einen Wert von Hand ändert,
behält ihn - beim Abwählen verschwindet nur, was noch genau dem Vorschlag entspricht. Eine
Karte, deren Flags schon vollständig im JSON stehen, ist beim Öffnen aktiv.

Rund um den Editor (JSON-Ansicht):

| Knopf | Wirkung |
| --- | --- |
| **Format** | liest das JSON und schreibt es sauber eingerückt zurück (Kommentare und Kommas am Ende sind erlaubt) |
| **Copy** | legt den gesamten Text in die Zwischenablage |
| **Import…** | lädt eine vorhandene `ClientAppSettings.json` in den Editor |
| **Export…** | schreibt den Editorinhalt in eine Datei |
| **Clear** | leert den Editor |
| Suchfeld | filtert Karten und Editor nach Name, Beschreibung oder Flag-Namen |
| **Visual / JSON** | wechselt zwischen Kartenraster und JSON-Editor; die Karten schreiben in dasselbe JSON |
| Kategorie-Chips | **All**, **★ Favorites** und die sieben Gruppen - auch mit Suche kombinierbar |

Unter dem Editor steht laufend, wie viele Flags drinstehen und wie viele Vorschläge aktiv
sind - oder warum das JSON gerade nicht gelesen werden kann (dann bleibt kein Vorschlag hängen).

Beim nächsten Start schreibt der Launcher die Flags nach
`…\Versions\version-<hash>\ClientSettings\ClientAppSettings.json`. **Write to the client now**
liest die Datei anschließend zurück und meldet, wenn ein Wert nicht angekommen ist; der
Selbsttest prüft alle 99 Vorschläge und 60 verschiedene Flag-Namen auf diesem Weg.

> [!NOTE]
> Roblox beachtet seit September 2025 nur noch Flags auf einer Allowlist. Andere Flags werden
> stillschweigend ignoriert - sie schaden nichts, wirken aber auch nicht. Eine vorhandene
> `ClientAppSettings.json` wird einmalig als `.aetherbak` gesichert, und im Vanilla-Modus wird
> gar nichts geschrieben.

### Protokollhandler (optional)

Unter **Settings → Protocol handler** kann Aether den `roblox-player`-Handler übernehmen.
Danach landen "Play"-Klicks der Roblox-Website im Launcher, und FastFlags sowie Multi-Instanz
greifen auch dort. **Restore previous** setzt den vorherigen Befehl zurück.

### Schnellansicht (`Entf`)

Die Schnellansicht ist eine Karte über der Seite, auf der man gerade steht. Sie ist ab Werk
an und lässt sich auf zwei Wegen öffnen: mit dem Kürzel oder über **Layout** in der Fußzeile.
Darin liegen die Dinge, für die man sonst die Seite wechselt:

- **Play** bzw. **Roblox app** - genau wie auf der Startseite,
- **Vanilla mode**, **Apply FastFlags on launch** und der **Ingame-Zeiger** als Schalter,
- **Launch as** - die Roblox-App als jedes gespeicherte Konto starten,
- **Notes** - ein Notizfeld; der Text wird kurz nach dem Tippen in `notes.txt` gespeichert
  (und beim Schließen sofort).

`Esc` schließt sie, ein Klick daneben ebenfalls. Dieselbe Taste öffnet auch die Platten des Ingame-Overlays
über Roblox - siehe unten.

**Ein- und ausschalten:** auf der Engine-Seite unten rechts in der Vorschlags-Karte. Dort
sitzt auch **Open**, um sie ohne Kürzel zu zeigen.

**Kürzel umbelegen:** in den Einstellungen bei **Quick layout shortcut** auf **Change** und
dann die neue Kombination drücken (eine Buchstaben-, Tab- oder F-Taste zusammen mit `Strg`,
`Alt` oder `Shift`; `Esc` bricht ab). **Reset** stellt `Entf` wieder her. Die
Einstellung steht in `settings.json` und gilt ab sofort - ohne Neustart.

### Ingame-Overlay (über Roblox)

Startet Aether einen Client (oder öffnet die Roblox-App), läuft daneben ein kleiner
Overlay-Prozess. Die Platten bleiben beim Start **zu** und warten auf `Entf` (umbelegbar,
siehe unten); wer sie beim Start von Roblox von selbst sehen will, schaltet das in den
Einstellungen ein. `Esc` oder das ✕ einer Platte
schließt sie, ein Klick auf ihren Kopf zieht sie woandershin. Die Platte folgt dem Zeiger
leicht gedämpft, hebt sich beim Ziehen an, rastet in der Nähe einer Bildschirmkante ein und
gleitet weich in ihre Endlage - **jede Platte merkt sich ihre eigene Lage** und steht beim
nächsten Mal wieder dort. Keine lässt sich aus dem Bild ziehen. Alle Platten sind
Toolwindows mit einem unsichtbaren Besitzerfenster: Sie erscheinen nicht in Alt+Tab,
Win+Tab oder der Taskleiste. Ist ein
**anderes Programm vorne**, treten die Platten zurück und sind wieder da, sobald man im Spiel ist -
damit liegen sie nie über Browser, Launcher oder Discord (abschaltbar im Menü).

Was drin ist:

| Platte | Wirkung |
| --- | --- |
| **Menü** | Schalter für jede Platte, die **Menütaste** (umschalten oder eine einzelne Taste wie `Delete`/`Entf`), das Zurückweichen vor anderen Programmen, **Deckkraft 100/90/80/70 %**, **Open Aether**, **Hide all** und **Reset layout**. Das Menü selbst bleibt immer erreichbar. |
| **Frame rate** | `Uncapped` (10000), 60, 120, 144, 240 oder ein eigener Wert von 10 bis 10000. Schreibt `DFIntTaskSchedulerTargetFps` in `ClientSettings`. **Achtung:** genau dieses Flag steht *nicht* auf Roblox' Allowlist - der Client kann es ignorieren (siehe unten). |
| **Graphics** | Grafikstufe `Lowest` (1) / `Highest` (21), Texturen `Fast` (0) / `Sharp` (3), Kantenglättung `Off` / `2×` / `4×`, Gras `On` / `Off` - alle auf der Allowlist, also wirklich wirksam. `Off` bei Gras nimmt die Flags wieder heraus, statt sie auf 0 zu setzen. |
| **Notes** | Dasselbe `notes.txt` wie die Schnellansicht im Launcher: tippen, speichert von selbst, mit Zeitstempel darunter. Als eigene Platte lässt sie sich dorthin schieben, wo sie nicht stört. |
| **Server** | **Leave & rejoin**: verlässt den laufenden Server sofort und startet direkt danach einen neuen Beitritt auf einem anderen Server. **Restart client** ist der getrennte harte Weg für neue Flags, **Game page** öffnet den Platz im Browser, **Copy link** legt den Spiellink in die Zwischenablage. |
| **Anti-AFK** | Holt das Spielfenster im gewählten Abstand (1/2/5/10 min) für etwa eine Sekunde nach vorn, tippt eine echte Taste und minimiert es danach wieder; war ein anderes Fenster vorn, bekommt es den Fokus zurück. Ist Roblox schon vorn, wird nur getippt und nichts minimiert. Vor dem Tippen prüft Aether, dass wirklich der Client im Vordergrund steht. F8 ist die Vorgabe, weil es die Spielfigur nicht bewegt; die Taste lässt sich ändern. Fehlversuche werden nach 20 s wiederholt, der Countdown steht im Panel, und jeder Stoß landet im Protokoll. **Nudge now** testet den Stoß, **Self-check input** prüft die Eingabekette ohne Roblox. |
| **⌖** | Setzt genau diese Platte wieder über das Spiel. |

**Apply & rejoin** auf **Frame rate** und **Graphics** schreibt die Werte, schließt den laufenden
Client und startet direkt danach den neuen Beitritt. Der Desktop ist während des Clientstarts
kurz sichtbar - schneller geht es mit Roblox nicht.

### Was ein Serverwechsel kann - und was nicht

Roblox hat **keine Schnittstelle, mit der ein laufender Client auf einen anderen Server umziehen
kann**. Jeder Wechsel ist technisch ein neuer Beitritt. Den Platz nimmt Aether aus Roblox'
eigenem Log - also auch ein Spiel, das über die Startseite geöffnet wurde. Dann fragt es die
öffentlichen Server ab, überspringt den, auf dem man gerade sitzt, und bittet Roblox um einen
anderen. Der laufende Client wird **zuerst geschlossen**, direkt danach startet der neue Join;
der neue Client übernimmt Fenstergröße und Lage des alten. Das ist ein Makro: während Roblox
startet, ist kurz der Desktop zu sehen. **Restart client** bleibt der getrennte harte Neustart
für neue Flags. Klappt der Start nicht, sagt die Statuszeile es - der alte Client ist dann
bereits geschlossen.

> [!IMPORTANT]
> **Warum die Bildrate sich manchmal nicht ändert:** Roblox liest seit September 2025 nur noch
> Flags von seiner Allowlist. `DFIntTaskSchedulerTargetFps` gehört nicht dazu - der Client darf
> ihn komplett verwerfen. Aether schreibt ihn trotzdem (mit einer festgenagelten älteren Version
> greift er noch), sagt es im Overlay aber ehrlich. Die Schalter unter **Graphics** stehen alle
> auf der Allowlist und wirken.

**Was das Overlay bewusst nicht macht:** Es liest keinen Spielspeicher und hängt sich nicht in den
Client ein. Es zeichnet eigene Fenster über das Spielfenster und arbeitet nur mit Dingen, die
ohnehin erlaubt sind: `ClientSettings`, ein normaler Clientstart und der Launcher. **Eine Ausnahme
ist Anti-AFK** und sie ist ausdrücklich ab Werk aus: schaltet man es ein, holt Aether das
Spielfenster für etwa eine Sekunde nach vorn, tippt eine Taste und minimiert es wieder. Das ist
ein sichtbarer Fokuswechsel - nur so kommt die Eingabe zuverlässig im Spiel an. War ein anderes
Fenster vorn, bekommt es den Fokus zurück.

**Ein- und ausschalten:** Settings → **In-game overlay (over Roblox)**; **Start now** startet den
Host ohne Spiel. Darunter steht, ob das Overlay beim Start von Roblox von selbst aufgehen soll.
Der Host beendet sich selbst, wenn eine Weile kein Client mehr läuft.

**Im Vanilla-Modus** schreibt Aether nichts in den Client - dann sind die Schalter gesperrt und
das Overlay sagt, warum, statt still ins Leere zu schreiben.

> [!NOTE]
> Die Platten liegen als normale, oberste Fenster über dem Spiel. In einem echten
> Exklusiv-Vollbild sieht man sie nicht - Roblox läuft aber ohnehin im randlosen Fenstermodus.
> **Flags greifen nur beim Start:** Roblox liest `ClientSettings`, wenn der Client startet.
> Einem *laufenden* Client lässt sich die Bildrate oder die Grafikstufe von außen nicht umstellen -
> ohne Eingriff in den Prozess geht das nicht, und genau das macht Aether nicht. Deshalb steht
> neben den Schaltern **Apply & rejoin** (verlässt und startet sofort neu; der Clientstart ist kurz sichtbar) und **Restart client** (immer).

### Tastenkürzel

| Kürzel | Wirkung |
| --- | --- |
| `Entf` (Delete) | Schnellansicht im Launcher - und Ingame-Overlay über Roblox. Ab Werk eine einzelne Taste, damit im Spiel nichts dazwischenfunkt; im Menü der Platten oder in den Einstellungen auf eine andere Taste, `Insert`, `F8` oder eine Kombination wie `Strg`+`L` umbelegbar und dort auch wieder zurücksetzbar |
| `Strg` + `1` … `5` | Home, Accounts, Engine, Settings, Log |
| `Strg` + `Enter` | das gewählte Spiel starten - oder die Roblox-App öffnen (wie **Play**) |
| `Esc` | Schnellansicht schließen bzw. das Umbelegen abbrechen |
| `F5` | Client, Kanal und Spiele neu einlesen |

## Aussehen

Die Oberfläche ist eine WPF-Umsetzung der Aether-Hub-Sprache, nicht eine eigene Erfindung:

- **Palette** - die Farbwerte stammen eins zu eins aus `AetherLoader/UiKit.cs` (`Pal`) des
  Hub-Projekts: Hintergrund `#0A0C11`, Karten `#10131A`, Rahmen `#212632`, Text `#EAEDF4`,
  Akzent `#2672FA`, Hero-Verlauf `#143474 → #080D1A` und so weiter. In
  [Theme.xaml](src/AetherLauncher/Theme.xaml) stehen sie als benannte Brushes.
- **Rahmenloses Fenster** - eigene Kopfleiste mit Logo, Wortmarke, Version, Reiter-Pillen und
  gezeichneten Fensterknöpfen (Minimieren/Maximieren/Schließen). Auf Windows 11 werden die
  Ecken über `DwmSetWindowAttribute` abgerundet, und ein `WM_GETMINMAXINFO`-Hook verhindert,
  dass das maximierte Fenster die Taskleiste überdeckt.
- **Karten und Steuerelemente** - Karten mit 10 px Radius, Schalter mit Kapsel-Spur und
  Knopf, Segmentsteuerungen, "Plain"-Knöpfe in Kartenköpfen, Zeilen mit Akzentbalken links.
- **Fußzeile** - seitenabhängiger Hinweis links, Status, Version und **Restart Aether** rechts.

### Wie die Übergänge funktionieren

Bewegung entsteht an drei Stellen, damit sie überall gleich aussieht:

- **Vorlagen in [Theme.xaml](src/AetherLauncher/Theme.xaml)** decken die Zustände ab
  (überfahren, gedrückt, ausgewählt). Statt Farben hart umzuschalten, liegt eine zweite Fläche
  darüber, deren **Deckkraft** animiert wird. Grund: die Pinsel des Themes sind geteilt - eine
  geteilte Farbe lässt sich nicht animieren, eine Deckkraft schon.
- **[Controls/RippleButton.cs](src/AetherLauncher/Controls/RippleButton.cs)** und
  **[Controls/CardPanel.cs](src/AetherLauncher/Controls/CardPanel.cs)** sind die beiden Bauteile
  dahinter. Ein Knopf drückt sich beim Klick leicht zusammen und schickt einen Ring von der
  Klickstelle aus; eine Karte bekommt beim Überfahren eine weiche Aufhellung.
- **[Controls/Anim.cs](src/AetherLauncher/Controls/Anim.cs)** steuert, was vom Code aus läuft:
  das kurze Auffahren des Fensters beim Start, der gleitende Reiterstrich, das seitliche
  Einfliegen der Seite samt gestaffelter Karten, das Aufblenden und Hereingleiten neuer
  Statusmeldungen, das Aufpoppen der Schnellansicht und der Glanz über der Hauptkarte.
- **[Controls/RippleButton.cs](src/AetherLauncher/Controls/RippleButton.cs)** hebt einen Knopf
  beim Überfahren um 1,5 px an und lässt ihn beim Verlassen zurückfedern.
- **[Controls/SmoothScroll.cs](src/AetherLauncher/Controls/SmoothScroll.cs)** ersetzt das
  stufenweise Mausrad-Scrollen durch eine 240-ms-Animation zur Zielposition (Einstellungen und
  Schnellansicht). Angehängt wird es in XAML mit `c:SmoothScroll.Enabled="True"`.

**Playful mode** (Standard: an) macht daraus federnde Varianten mit leichtem Überschwinger;
ausgeschaltet sind alle Bewegungen kürzer und geradlinig.

> [!NOTE]
> Objekte, die in einer geteilten Vorlage stehen, friert WPF ein - eingefrorene Objekte lassen
> sich nicht animieren. Deshalb bringt die Knopfvorlage keine Skalierung mit (der Code legt sie
> je Instanz an), und der Schalterknopf bewegt sich über seinen Rand statt über eine
> Transformation. Der Oberflächen-Selbsttest fährt mit dem echten Zeiger über die Bauteile und
> **klickt** sie auch, siehe [Werkzeuge](#werkzeuge).
>
> Außerdem braucht jede Animation einen echten Ausgangswert: `Canvas.Left` ist an einem neuen
> Element `NaN`, und eine Animation von `NaN` aus wirft erst im Animationstakt - der Prozess
> stirbt dann ohne Meldung. Genau das war der Absturz beim Klick auf **Play** oder **Save**;
> der Klick-Test im Selbsttest prüft ihn jetzt.

Die **Sprache der Oberfläche ist Englisch**, weil sie direkt neben dem Aether-Hub steht; diese
README und die Entwicklerwerkzeuge bleiben deutsch. Icons und Logo stammen aus dem Hub-Projekt,
siehe [Credits](#credits).

## Wie es funktioniert

```
 Browser-Klick auf "Play"  ─┐
 Launcher (Home/Accounts)  ─┤
                            ▼
              Launcher bereitet lokal vor:
              · gewählte Client-Version (angeheftet oder automatisch)
              · im Vanilla-Modus: nichts davon, der Client bleibt unberührt
              · FastFlags nach  ...\Versions\version-<hash>\ClientSettings\  schreiben
              · Zeiger-Texturen ersetzen, wenn eingeschaltet
              · Multi-Instanz-Wächter starten (hält ROBLOX_singletonMutex)
                            │
                            ▼
        Konto gewählt?  ── ja ──▶ POST auth.roblox.com/v1/authentication-ticket/
                            │         (403 → x-csrf-token → Wiederholung → Ticket)
                            │         Start-URI mit +gameinfo:<ticket>
                            └─ nein ─▶ roblox://experiences/start?placeId=<id>
                            ▼
              RobloxPlayerBeta.exe "<Start-URI>"   (als einziges Argument)
```

Details, die beim Lesen des Quellcodes wichtig sind:

- **Start-URI.** Der Client versteht die alte, mit `+` getrennte `roblox-player:`-URI. Das
  Ticket steht in `gameinfo`, die Zieladresse URL-kodiert in `placelauncherurl`. Ohne
  Kontoauswahl wird der dokumentierte Deep-Link benutzt, damit die vorhandene Roblox-Sitzung
  greift. Siehe [RobloxUri.cs](src/AetherLauncher/Services/RobloxUri.cs).
- **Auth-Ticket.** Roblox verlangt einen CSRF-Austausch: Der erste `POST` liefert `403` mit
  `x-csrf-token`, der zweite mit Token liefert das Ticket im Antwort-Header
  `rbx-authentication-ticket`. Siehe [RobloxApi.cs](src/AetherLauncher/Services/RobloxApi.cs).
- **Versionswahl.** `RobloxInstall.Resolve` sucht über alle bekannten Installationsordner
  (`%LOCALAPPDATA%\Roblox`, Bloxstrap, Fishstrap, eigener Pfad). Reihenfolge: angeheftete
  Version → Hinweis aus der Registry (die zuletzt installierte) → neueste gefundene. Eine
  angeheftete Version, die fehlt, fällt mit einer Log-Warnung auf die nächste Stufe zurück.
- **Multi-Instanz.** Der Client prüft ein benanntes Kernel-Objekt (`ROBLOX_singletonMutex`,
  je nach Version auch `ROBLOX_singletonEvent`). Der Wächterprozess erzeugt diese Objekte und
  hält sie, solange Roblox läuft. Siehe [MultiInstance.cs](src/AetherLauncher/Services/MultiInstance.cs).
- **Nur ein Fenster.** Ein zweiter Start (etwa über den Protokollhandler) übergibt die URI an
  das laufende Fenster und beendet sich. **Restart Aether** nutzt dasselbe Prinzip, nur
  umgekehrt: der neue Prozess wartet mit `--restart-wait` auf das Ende des alten. Siehe
  [SingleInstance.cs](src/AetherLauncher/Services/SingleInstance.cs).
- **Kein Patchen, kein Injizieren.** Es wird kein Prozessspeicher angefasst, nichts in den
  Client geladen und keine Programmdatei ersetzt. Es gibt genau zwei Eingriffe, beide optional
  und beide mit Sicherung: die `ClientSettings` (FastFlags) und die acht Zeiger-PNGs
  ([ClientMods.cs](src/AetherLauncher/Services/ClientMods.cs)). Im Vanilla-Modus passiert
  keiner von beiden.

## Projektstruktur

```
Programm/
├── screenshots/                 Mit --screenshot erzeugte Bilder
├── tools/
│   ├── make-cursor.ps1          Erzeugt cursor.cur und cursor.png aus einem Bild
│   └── build-release.ps1        Single-File-Release ohne Symbole, SHA-256, optional signiert
├── README.md
├── LICENSE
└── src/AetherLauncher/
    ├── Assets/                  Icons, Logo, Zeigerpfeil (cursor.cur/cursor.png) + Icons-Lizenz
    ├── App.xaml(.cs)            Startlogik, Schriften anmelden, Hilfsmodi (--selftest, --selftest-ui, --screenshot, --multi-watcher)
    ├── MainWindow.xaml(.cs)     Rahmenloses Fenster: Kopfleiste mit gleitendem Reiterstrich, fünf Seiten, Fußzeile, Fenster-Hooks
    ├── RobloxLoginWindow.xaml(.cs)  Roblox' eigene Anmeldung im temporären WebView2-Profil
    ├── OverlayHost.cs           stellt die Platten auf, blendet sie ein/aus, hält Fenster und Zustand
    ├── OverlayMode.cs           --overlay: eigener Hostprozess für das Overlay
    ├── Overlay/
    │   ├── PanelWindow.xaml(.cs)  eine ziehbare Platte (Kopf, Inhalt, Andocken)
    │   ├── HubPanel.xaml(.cs)     Menü: Schalter, Menütaste, Rückzug vor anderen Programmen
    │   ├── FpsPanel.xaml(.cs)     Bildrate, Apply & rejoin
    │   ├── GraphicsPanel.xaml(.cs) Grafikstufe, Texturen, Kantenglättung, Gras
    │   ├── NotesPanel.xaml(.cs)   Notizen als eigene Platte
    │   ├── ServerPanel.xaml(.cs)  Leave & rejoin, Restart client
    │   └── AntiAfkPanel.xaml(.cs) Anti-AFK, Intervall, Stoß jetzt
    ├── Theme.xaml               Palette, Vorlagen und Stile (Karten, Knöpfe, Reiter, Zeilen, Schalter, Segmente)
    ├── ScreenshotMode.cs        Rendert jede Seite als PNG
    ├── DownloadMode.cs          --download: Installer einer Version laden
    ├── SelfTest.cs              Selbsttest der Kernlogik
    ├── UiSelfTest.cs            Selbsttest der Oberfläche (Bindings, Übergänge, Vorschläge, Maximieren)
    ├── Controls/
    │   ├── Anim.cs              Bewegungshelfer (Einblenden, Gleiten, Drücken, Aufpoppen, Seitenwechsel)
    │   ├── SmoothScroll.cs      weiches Mausrad-Scrollen als angehängte Eigenschaft
    │   ├── CardPanel.cs         Karte mit weicher Aufhellung
    │   ├── RippleButton.cs      Schaltfläche mit Ring, Anheben und Druck-Feedback
    │   └── IconImage.cs         PNG-Icon, das in die jeweilige Textfarbe eingefärbt wird
    ├── Infrastructure/          INotifyPropertyChanged, Commands, JSON-Helfer
    ├── Models/                  AccountRecord, PlaceRecord, LauncherSettings
    ├── Services/
    │   ├── Stores.cs            accounts.json, places.json, settings.json
    │   ├── Dpapi.cs             Verschlüsselung der Schlüssel
    │   ├── RobloxApi.cs         Schlüssel prüfen, Auth-Ticket, Avatare, Spieldaten
    │   ├── RobloxInstall.cs     Versionen finden, anheften, Version im Kanal
    │   ├── RobloxVersions.cs    Versionslisten (WEAO, DeployHistory) und Installer-Download
    │   ├── GameOverlay.cs       Roblox-Fenster finden, Hotkey-Tasten, Eingabe, Client beenden
    │   ├── OverlayPlacement.cs  Lage der Platten und Bildschirmgrenzen
    │   ├── ServerHop.cs         Leave & rejoin: alter Client zuerst, neuer Join direkt danach
    │   ├── AntiAfk.cs           Pop-up, Tastendruck, Minimieren im gewählten Abstand
    │   ├── RobloxLogImport.cs   "Zuletzt gespielt" aus den Client-Logs
    │   ├── RobloxUri.cs         Start-URIs bauen und lesen
    │   ├── MultiInstance.cs     Singleton-Wächter
    │   ├── FastFlags.cs         Flags und Vorschläge verwalten und übernehmen
    │   ├── ClientMods.cs        Zeiger-Texturen ersetzen und zurückholen
    │   ├── ImageTool.cs         Bilder lesen, skalieren, mitgelieferten Zeiger auspacken
    │   ├── AppFonts.cs          Schriften auflisten, laden, anmelden
    │   ├── LaunchService.cs     kompletter Startablauf
    │   ├── ProtocolHandler.cs   roblox-player übernehmen/zurückgeben
    │   ├── SingleInstance.cs    ein Fenster, Übergabe von Startanfragen
    │   ├── ImageCache.cs        Avatare und Spiel-Icons
    │   ├── Shortcuts.cs         Tastenkürzel schreiben, lesen, vergleichen
    │   ├── Notes.cs             Notizen der Schnellansicht (notes.txt)
    │   └── Time.cs              "2 d ago" für Zeilen
    └── ViewModels/              MainViewModel und die Zeilen-Wrapper
```

## Speicherorte

| Was | Pfad |
| --- | --- |
| Konten (Schlüssel AES-256-GCM-verschlüsselt) | `%LOCALAPPDATA%\AetherLauncher\accounts.json` |
| AES-Schlüssel (per DPAPI + App-Entropie gebunden) | `%LOCALAPPDATA%\AetherLauncher\vault.key` |
| Spieleliste | `%LOCALAPPDATA%\AetherLauncher\places.json` |
| Einstellungen | `%LOCALAPPDATA%\AetherLauncher\settings.json` |
| FastFlags | `%LOCALAPPDATA%\AetherLauncher\FastFlags.json` |
| Angeheftete FastFlag-Karten | `%LOCALAPPDATA%\AetherLauncher\fastflags-favorites.txt` |
| Eigene Schriften | `%LOCALAPPDATA%\AetherLauncher\fonts` |
| Zeigerbild (mitgeliefert) | `%LOCALAPPDATA%\AetherLauncher\cursor.png` |
| Log | `%LOCALAPPDATA%\AetherLauncher\launcher.log` |
| Bilder/Logos | `%LOCALAPPDATA%\AetherLauncher\avatars` bzw. `icons` |
| Flags im Client | `%LOCALAPPDATA%\Roblox\Versions\version-<hash>\ClientSettings\ClientAppSettings.json` |
| Ersetzte Zeiger-Texturen | `…\Versions\version-<hash>\content\textures\…` (Originale daneben als `.aetherbak`) |
| Heruntergeladene Installer | `%LOCALAPPDATA%\AetherLauncher\downloads` |
| Zuletzt gestartetes Spiel und Menütaste | `settings.json` (`LastPlaceId`, `LastPlaceAccessCode`, `LastAccountId`, `OverlayShortcut`) |
| Lage, Sichtbarkeit und Anti-AFK der sechs Platten | `%LOCALAPPDATA%\AetherLauncher\overlay.json` |
| Notizen der Schnellansicht | `%LOCALAPPDATA%\AetherLauncher\notes.txt` |
| Absturzbericht (falls je einer auftritt) | `%LOCALAPPDATA%\AetherLauncher\crash.log` |

## Werkzeuge

```bash
EXE=./src/AetherLauncher/bin/Release/net10.0-windows/AetherLauncher.exe

# Kernlogik: DPAPI, Speicher-Roundtrips, Versionswahl, URI-Bau, FastFlags, Zeiger, Schriften
$EXE --selftest

# Oberfläche: öffnet das Fenster, schaltet alle Seiten durch, prüft Bindings und Maximieren
$EXE --selftest-ui

# zusätzlich mit dem echten Zeiger über Karten, Knöpfe und Zeilen fahren: führt die Übergänge aus
$EXE --selftest-ui --hover

# Bilder aller Seiten und des Ingame-Overlays als PNG (Beispieldaten) - siehe screenshots/
$EXE --screenshot ./screenshots

# Installer einer Version laden: current | next | past | version-<hash>
$EXE --download past

# Download-Ausgabe: Single-File, ohne Symbole, mit SHA-256 (optional signiert)
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-release.ps1 -Version 1.2.0
```

Der Oberflächen-Test klickt auch wirklich: sein Mauszeiger fährt auf **Format** und **Save** und
löst dort einen echten Klick aus - der Weg, auf dem die App früher beim Klick-Ring abstürzte. Dazu
prüft ein echter Tastendruck das eingestellte Menükürzel (ab Werk `Entf`) und `Esc`. Liegt der Knopf
nicht unter dem Zeiger - weil ein anderes Fenster darüber liegt oder das Fenster auf einem
Bildschirm steht, den der Test nicht erreicht -, meldet er dafür `SKIP` samt Grund, statt einen
Fehler zu behaupten, den es nicht gibt. Der Logik-Test überspringt den Multi-Instanz-Teil, solange
ein Client läuft, damit kein laufendes Spiel gestört wird.

Alle Prüfläufe geben `0` zurück, wenn alles in Ordnung ist, und schreiben zusätzlich
`selftest.log` bzw. `selftest-ui.log` in den Datenordner.

Der Zeigerpfeil entsteht aus einem Bild mit einfarbigem Hintergrund - das Skript stellt frei,
rechnet den Saum an den Kanten heraus und schreibt einen `.cur`-Container mit 32- und 48-px-
Frame samt Klickpunkt:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-cursor.ps1 -Source mein-pfeil.png
```

## Sicherheit und Schutz

### Konten und Passwörter

- **Kein Passwort im Launcher.** Die Anmeldung läuft auf `roblox.com` in einem temporären
  WebView2-Profil. Aether liest nur das `.ROBLOSECURITY`-Cookie - kein Passwortfeld, kein
  Speichern, kein Mitschnitt. DevTools, Kontextmenüs und Passwort-Autofill sind in diesem
  Fenster abgeschaltet. Das Profil liegt unter `%TEMP%\aether-roblox-login-*` und wird beim
  Schließen gelöscht; Reste eines Absturzes räumt der nächste Start weg.
- **Zwei Lagen Verschlüsselung.** `accounts.json` enthält jedes Cookie nur als
  `aeg2:`-Blob: **AES-256-GCM** (Vertraulichkeit und Integrität). Der 32-Byte-AES-Schlüssel
  liegt in `vault.key`, verschlüsselt mit **Windows DPAPI** *und* einer app-eigenen Entropie -
  ohne dein Windows-Konto und ohne diesen Build ist die Datei wertlos. Ein verändertes Byte
  lässt GCM scheitern statt still einen falschen Wert zu liefern.
- **Keine Klartext-Reste.** Zwischenpuffer werden nach dem Ver- und Entschlüsseln genullt;
  alte reine DPAPI-Werte hebt der Launcher beim Laden automatisch auf AES-GCM. Der neue
  Vault wird vor dem Ersetzen zurückgeprüft, damit ein halb geschriebener Schlüssel keine
  Konten sperrt.
- **Logs.** Cookies und Auth-Tickets werden nicht geloggt; Fehler nennen nur Statuscodes
  und Hosts.

### Release bauen und prüfen

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-release.ps1 -Version 1.2.0
# optional mit eigenem Code-Signing-Zertifikat:
# ... -CertificatePath .\aether.pfx
```

Der Build legt `dist/AetherLauncher-<Version>-win-x64.zip` plus `.sha256` an: Single-File,
ReadyToRun, **ohne PDB und ohne XML-Dokumentation** der Bibliotheken. Mit `-CertificatePath`
signiert das Skript die EXE per `signtool` (SHA-256 + Zeitstempel) und prüft die Signatur
anschließend. Auf einem anderen Rechner lässt sich der Download prüfen:

```powershell
certutil -hashfile AetherLauncher-1.0.0-win-x64.zip SHA256
Get-AuthenticodeSignature .\AetherLauncher.exe   # "Valid", wenn signiert
```

### Was das kann - und was nicht

Keine lokale .NET/WPF-Anwendung lässt sich "unmöglich" reverse engineeren: Der IL-Code ist
im Download, und wer den Prozess unter **deinem** angemeldeten Windows-Benutzer debuggt,
kann auch entschlüsselte Werte sehen. Entscheidend ist deshalb, dass es nichts Geheimes zu
finden gibt:

- Im Programm stehen **kein Roblox-Passwort, kein API-Geheimnis und kein Server-Token**.
- Die Kontoschlüssel sind an dein Windows-Konto gebunden, nicht nur an den Rechner; eine
  kopierte `accounts.json` samt `vault.key` ist auf einem anderen Konto unbrauchbar.
- Der Release verzichtet auf Symbole und Doku, signiert auf Wunsch und veröffentlicht eine
  SHA-256 - ein veränderter Download fällt damit auf.
- Bewusst **keine** Obfuskatoren mit Anti-Debug-Tricks: Sie brechen WPF-Bindings, lösen
  Virenwarnungen aus und halten niemanden auf, der es ernst meint. Der ehrliche Schutz ist
  Verschlüsselung der Daten plus Signierung der Ausgabe.

> [!IMPORTANT]
> Wer Zugriff auf dein angemeldetes Windows-Konto hat, kann Aether auch bedienen. Schutz vor
> einem bereits kompromittierten Benutzerprofil kann eine normale Desktop-Anwendung nicht
> liefern.

## Grenzen und bekannte Punkte

- **Ende-zu-Ende-Start.** Der Startablauf ist bis zum Prozessaufruf automatisch geprüft; ein
  echter Spielbeitritt braucht ein gültiges Konto und wird deshalb nicht automatisch getestet.
- **Versionsschalter und Download.** Angeheftet werden kann nur, was installiert ist; die
  Download-Karte holt den passenden Installer dazu. Ob Roblox für einen sehr alten Hash noch
  Dateien bereithält, entscheidet der CDN.
- **App-Start mit Konto.** Der Weg über die vorhandene Anmeldung ist live geprüft (der Client
  öffnet seine Startseite). Dass der Client den `launchmode:app`-Weg mit Auth-Ticket genauso
  annimmt, ist bis zur URI automatisch geprüft - ein Test würde ein echtes Konto anmelden.
- **Multi-Instanz.** Wirkt über den Singleton-Objektnamen des Clients. Ändert Roblox diesen
  Mechanismus, hat die Funktion keine Wirkung - sie schadet aber auch nicht. Im Vanilla-Modus
  ist sie aus.
- **FastFlags.** Roblox beachtet nur Flags auf seiner Allowlist. Aether reicht sie durch und
  kennzeichnet jeden Vorschlag, ob er noch dazugehört - erzwingen kann es nichts. Die
  Bildratenbegrenzung ist der bekannteste Fall: sie steht nicht mehr auf der Liste.
- **Anmeldung statt Passwortspeicher.** **Sign in with Roblox** erledigt die Anmeldung auf
  Roblox' Seite inklusive Captcha; Aether bekommt nur das Cookie. Es gibt bewusst keinen
  eigenen Passwort-Dialog, weil Aether das Passwort weder sehen noch speichern soll.
- **Zeiger-Texturen ändern sich mit dem Client.** Findet ein Update die Dateien an anderer
  Stelle oder unter anderem Namen, ersetzt Aether nur, was es kennt - die Liste steht in
  [ClientMods.cs](src/AetherLauncher/Services/ClientMods.cs).
- **Ingame-Overlay.** Es zeichnet Fenster über das Spiel und schreibt Flags - mehr nicht.
  Ein FPS-Zähler im Spiel wäre nur mit Eingriffen in den Client möglich und ist deshalb nicht
  eingebaut. `Server hop` liest die öffentlichen Server über die offizielle API, überspringt den
  aktuellen und bittet Roblox um einen anderen; der laufende Client wird zuerst geschlossen,
  danach startet der neue Join. Ob der Client den Wunschserver annimmt, entscheidet Roblox.
- **Bildrate im laufenden Spiel.** Roblox liest `ClientSettings` beim Start. Einem *laufenden*
  Client lässt sich die Bildrate nicht umstellen, ohne in den Prozess einzugreifen - und das tut
  Aether nicht. Deshalb steht neben den Reglern **Apply & rejoin**: schreiben, den Client schließen und direkt
  neu beitreten. Der Desktop ist während des Clientstarts kurz sichtbar. Es ist kein Absturz und
  kein Neustart des Rechners.
- **Anti-AFK.** Es holt das Spielfenster für etwa eine Sekunde nach vorn, tippt die gewählte
  Taste und minimiert es wieder. War ein anderes Fenster vorn, bekommt es den Fokus zurück. Ist
  Roblox schon vorn, wird nur getippt. Vor dem Tippen wird geprüft, dass wirklich der Client im
  Vordergrund steht; sonst ginge die Taste an ein fremdes Programm. F8 ist die Vorgabe, weil es
  die Spielfigur nicht bewegt. Bleibt ein Versuch erfolglos, wiederholt Aether ihn nach
  20 Sekunden. **Self-check input** prüft ohne Spiel, dass Windows die synthetische Eingabe
  annimmt.
- **Nur Windows.** WPF und DPAPI gibt es dort; das ist bewusst so gewählt.

## Credits

- **[Aether Hub](https://github.com/)** - Design-Sprache, Palette, Logo und Icon-Satz stammen
  aus diesem Projekt (`AetherLoader/UiKit.cs`, `icons/`). Die Palette ist dort mit ihren
  Hub-eigenen Themen (2007/2013/2018) dokumentiert; dieser Launcher nutzt die Standardwerte.
- **Icons** - [Lucide](https://lucide.dev), bezogen über die Iconify-API, ISC/MIT-lizenziert.
  Der Lizenztext liegt als [Assets/ICONS-LICENSE.txt](src/AetherLauncher/Assets/ICONS-LICENSE.txt) bei.
- **Verfahren** - inspiriert von [Bloxstrap](https://github.com/bloxstraplabs/bloxstrap)
  (Multi-Instanz-Wächter, FastFlags, Protokollhandler, Zeiger-Mods) und
  [Roblox Account Manager](https://github.com/ic3w0lf22/Roblox-Account-Manager)
  (Kontowechsel per Auth-Ticket). Es wurde kein Code aus diesen Projekten übernommen; umgesetzt
  sind öffentlich dokumentierte Schnittstellen des Clients.

Aether Launcher steht unter der MIT-Lizenz (siehe [LICENSE](LICENSE)). Roblox ist eine Marke von
Roblox Corporation. Dieses Projekt ist unabhängig und wird von Roblox nicht unterstützt.
