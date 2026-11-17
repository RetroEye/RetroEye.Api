# RetroEye app API: contract 1.1

(1.1 adds to 1.0 only: the `restarting` event and capability `events.restarting`, theme.json's `restarting` member,
and in the library the reconnect events, `TryNotifyAsync` and waiting for the welcome. Every 1.0 client keeps working.)

Other programs can look like the Windows version RetroEye is showing, and talk to it, even with no window at all.
RetroEye publishes everything about its current look (version, look, colours, fonts, metrics, title bar, taskbar) and
pictures of its own title bars and buttons; it announces every change; and it shows other programs' alerts as the
shown version's own tray balloon.

Three ways in, from simplest to richest:

| Way | What you get | For |
| --- | --- | --- |
| **The file** `theme.json` | The whole state, rewritten atomically at every change | Anything that can read JSON |
| **The event** (a named Windows event) | Set for half a second at every change | Programs that can't keep a pipe open |
| **The pipe** (named pipe, JSON lines) | The state, pushed events, notifications and their clicks | Live clients, windowless alert senders |

Two libraries wrap it: **RetroEye.Api** (.NET Standard 2.0, no WPF, no packages) and **RetroEye.Theming.Wpf**
(.NET 6 and .NET 10 on Windows). Samples are in `Api\Samples\`.

The API is on by default; Properties > the era section > "Let other programs follow the era's look and show their
alerts here" turns it off (setting `AppApi`). A RetroEye built without the app API has none of it.

The libraries, their samples and tests, and this specification are open source (MIT) at
<https://github.com/RetroEye/RetroEye.Api>; RetroEye itself is a separate, closed program.

---

## 1. Where things are (contract major 1)

| Thing | Name |
| --- | --- |
| Folder | `%LOCALAPPDATA%\RetroEye\api\v1\` (a test copy of RetroEye with its own settings folder writes `<settings folder>\api\v1\`; clients can be pointed elsewhere with `RETROEYE_API_FOLDER`) |
| State | `api\v1\theme.json` |
| Pictures | `api\v1\parts\*.png` |
| Change event | `Local\RetroEye.Api.v1[.channel].Changed.<user SID>`, manual reset, set for 500 ms at each change |
| Pipe | `\\.\pipe\RetroEye.Api.v1[.channel].<session id>.<user SID>` |

`<user SID>` is the text SID (`S-1-5-21-...`) of the user RetroEye runs as; `<session id>` the Windows session.
`[.channel]` is empty for the real RetroEye. A channel (letters, digits, `-`, `_`, at most 32) keeps test copies apart:
`RETROEYE_API_CHANNEL`, and a RetroEye using a settings folder other than the real one picks `copy<hash>` by itself.

**Security.** The pipe's security names the current user alone (full control; no Everyone, no other user, no
inherited entries). The event lives in the session's `Local\` namespace. Nothing a client sends can run a command,
open a file or a URL, or reach outside the session: the only thing a client can make RetroEye do is show a plain-text
balloon.

---

## 2. theme.json

Written whole (temporary file, then `File.Replace`), so a reader never sees half a file. Read it with
`FileShare.ReadWrite | FileShare.Delete` and retry for a moment on a sharing error. Indented UTF-8. The schema is
`docs\api\theme.v1.schema.json`; the list of everything contract 1.0 promises is `docs\api\contract-1.0.golden.txt`
(frozen), and 1.1's is `docs\api\contract-1.1.golden.txt`.

Colours are always `"#RRGGBB"` (upper case). Sizes are in **the version's own pixels** (at scale 1); multiply by
`scale.factor` for screen pixels. Font sizes are in points.

```json
{
  "contract": { "major": 1, "minor": 1, "version": "1.1" },
  "capabilities": ["state", "state.file", "state.event", "subscribe", "events.themeChanged", "...", "parts.buttons", "glass"],
  "deprecated": [],
  "sequence": 12,
  "instance": "6c1f0e9a2b7d4c55a8e3f0d1b2c3a4e5",
  "running": true,
  "written": "2026-11-16T22:32:32.173Z",
  "retroEye": { "version": "1.0.0.8", "processId": 14820 },
  "windows": { "version": "98", "name": "Windows 98" },
  "look": { "id": "classic", "name": "Classic", "classic": true, "theme": "Windows 95-98", "visualStyle": "Classic", "scheme": "Windows Standard" },
  "colors": { "scrollBar": "#C0C0C0", "desktop": "#008080", "activeCaption": "#000080", "...": "31 slots, see below" },
  "fonts": {
    "caption": { "face": "MS Sans Serif", "drawnFace": "Microsoft Sans Serif", "sizePt": 8, "weight": 700, "italic": false, "bitmap": true },
    "smallCaption": {}, "menu": {}, "status": {}, "message": {}, "icon": {}
  },
  "metrics": { "captionHeight": 19, "captionButtonWidth": 18, "captionButtonHeight": 18, "smallCaptionHeight": 16,
    "borderWidth": 1, "sizingBorderWidth": 1, "paddedBorderWidth": 0, "frameWidth": 4, "fixedFrameWidth": 3, "edge": 2,
    "menuHeight": 19, "scrollBarWidth": 16, "scrollBarHeight": 16, "iconSize": 32, "iconSpacingX": 75, "iconSpacingY": 75,
    "captionTextInset": 2 },
  "scale": { "factor": 1.25, "dpi": 120, "windowsDpi": 120 },
  "chrome": {
    "frameKind": "raised3d",
    "corners": { "shape": "square", "radius": 0 },
    "caption": {
      "active":   { "fill": { "kind": "horizontal", "stops": [{ "offset": 0, "color": "#000080" }, { "offset": 1, "color": "#1084D0" }] },
                    "text": "#FFFFFF", "textShadow": null, "textGlow": false },
      "inactive": { "fill": { "kind": "horizontal", "stops": [{ "offset": 0, "color": "#808080" }, { "offset": 1, "color": "#C0C0C0" }] },
                    "text": "#C0C0C0", "textShadow": null, "textGlow": false },
      "textAlign": "left", "font": "caption", "iconShown": true, "iconSize": 16, "height": 18
    },
    "buttons": { "style": "classic3d", "order": ["minimize", "maximize", "close"], "dialogOrder": ["help", "close"],
                 "width": 16, "height": 14, "closeWidth": 16, "gap": 0, "gapBeforeClose": 2, "glyph": "#000000" },
    "frame": { "active": "#C0C0C0", "inactive": "#C0C0C0", "thickness": 4,
               "lines": { "active":   { "topLeft": ["#C0C0C0", "#FFFFFF"], "bottomRight": ["#000000", "#808080"] },
                          "inactive": { "topLeft": ["#C0C0C0", "#FFFFFF"], "bottomRight": ["#000000", "#808080"] } } },
    "glass": { "enabled": false, "color": null, "intensity": 0, "transparency": false, "opacity": 0 }
  },
  "taskbar": { "found": true, "edge": "bottom", "height": 28, "autoHide": false, "bounds": { "x": 0, "y": 2125, "width": 3840, "height": 35 } },
  "parts": { "folder": "parts", "scale": 1.25, "dpi": 120, "generation": 3, "items": {
    "frameActive": { "file": "frame-active.png", "width": 300, "height": 200,
                     "slice": { "left": 5, "top": 30, "right": 5, "bottom": 5 }, "content": { "left": 5, "top": 30, "right": 5, "bottom": 5 } },
    "button.close.pressed": { "file": "button-close-pressed.png", "width": 20, "height": 18, "slice": { "left": 0, "top": 0, "right": 0, "bottom": 0 } }
  } }
}
```

### Members

* **contract**: `major` (1 here; the folder and names carry it), `minor` (additions), `version` ("1.1").
* **capabilities**: the features this RetroEye has, by name. Check for a feature, not a version number.
* **deprecated**: `[{ "name", "since", "removedInMajor", "use" }]`; empty in 1.0 (see section 6).
* **sequence**: one more at each write since this RetroEye started. **instance**: new at each start (tells a restart
  from a change). **running**: false once RetroEye has closed or the API was turned off. **restarting** (1.1): true
  in the file RetroEye writes as it closes for its Restart, which starts a new copy at once: wait for the new
  `instance` rather than taking `running` false as gone (absent, or false, otherwise). **written**: UTC time.
* **retroEye**: `version`, `processId` (a client can check the process is still alive).
* **windows.version**: `"95"`, `"98"`, `"me"`, `"2000"`, `"xp"`, `"vista"`, `"7"`, or `"system"` (RetroEye's System
  themes, which follow the real Windows). Later minors may add values.
* **look.id**: `classic`, `lunaBlue`, `lunaOliveGreen`, `lunaSilver`, `royale`, `royaleNoir`, `zune`, `embedded`,
  `watercolor`, `aero`, `vistaBasic`, `system`; may grow. **look.visualStyle** is always one of WPF's own themes
  (`Classic`, `Luna.NormalColor`, `Luna.Homestead`, `Luna.Metallic`, `Royale.NormalColor`, `Aero.NormalColor`): fall
  back on it for an id you don't know. **look.scheme**: the Appearance scheme's name.
* **colors**: the 31 classic colour slots, named after `COLOR_*` in `GetSysColor` order (the index is the COLOR_
  number): `scrollBar` (0), `desktop`, `activeCaption`, `inactiveCaption`, `menu`, `window`, `windowFrame`, `menuText`,
  `windowText`, `captionText`, `activeBorder`, `inactiveBorder`, `appWorkspace`, `highlight`, `highlightText`,
  `buttonFace` (15), `buttonShadow`, `grayText`, `buttonText`, `inactiveCaptionText`, `buttonHighlight`,
  `buttonDarkShadow`, `buttonLight`, `infoText`, `infoBackground`, `buttonAlternateFace`, `hotLight`,
  `gradientActiveCaption`, `gradientInactiveCaption`, `menuHighlight`, `menuBar` (30). On 95 to 2000 they are the
  version's Appearance scheme **as the user changed it** in RetroEye's Display Properties.
* **fonts**: `caption`, `smallCaption`, `menu`, `status`, `message`, `icon`; `face` as the version named it,
  `drawnFace` to draw with today (Microsoft Sans Serif for the bitmap MS Sans Serif), `sizePt`, `weight` (400/700),
  `italic`, `bitmap` (draw it aliased, no smoothing, for the true look).
* **metrics** (version pixels): `captionHeight` (SM_CYCAPTION), `captionButtonWidth`/`Height` (SM_CXSIZE/SM_CYSIZE),
  `smallCaptionHeight`, `borderWidth`, `sizingBorderWidth` (the Appearance tab's border), `paddedBorderWidth`,
  `frameWidth` (a sizable frame's whole thickness), `fixedFrameWidth`, `edge` (3D edge), `menuHeight`,
  `scrollBarWidth`, `scrollBarHeight`, `iconSize`, `iconSpacingX/Y` (icon plus gap), `captionTextInset`.
* **scale**: `factor` = screen pixels per version pixel on the main screen as RetroEye draws it (its taskbar scale
  times Windows' scale), `dpi` = 96 × factor (the parts' DPI), `windowsDpi` = Windows' own DPI there.
* **chrome**: `frameKind` (`raised3d` 95-2000 and Classic, `luna` XP, `aero` Vista/7 glass, `basic` Vista Basic);
  `corners` (`square`/`rounded`, top radius); `caption.active/inactive` (`fill` kind `solid`/`horizontal`/`vertical`
  with stops; `text`; `textShadow` XP's one-pixel shadow or null; `textGlow` Vista/7's white glow), `textAlign`,
  `iconShown`, `iconSize`, `height` (the caption band as drawn); `buttons` (`style` `classic3d`/`luna`/`aero`, `order`
  of a sizable window, `dialogOrder`, sizes as drawn, `gap`, `gapBeforeClose`, glyph colour, and `maximizeWidth` on
  Aero); `frame` (band colours, thickness, `lines` outside-in per side); `glass` (Window Color: `enabled`, `color`,
  `intensity` 0-100, `transparency`, `opacity` 0-1).
* **taskbar**: main taskbar `edge`, `height` (its thickness, version pixels), `autoHide`, `bounds` (screen pixels;
  null when no taskbar was found).
* **parts**: see section 3.

Everything per version is computed by RetroEye's own code for that version.

---

## 3. Picture parts ("the actual header")

Drawn by RetroEye's **own** frame code (the same per-version frames its windows use; no Microsoft art),
off screen, at `parts.dpi`, redrawn whenever the look, scheme, glass or scale changes (`generation` goes up). Each
item: `file`, `width`, `height` (screen pixels), `slice` (9-slice insets: those pixels stay as they are, edges stretch
one way, the middle both ways) and, on the frame, `content` (where the client area starts). Pre-Vista looks at a
whole scale are drawn at 1× and blown up as sharp blocks, as RetroEye draws them on screen.

| Item | What |
| --- | --- |
| `frameActive`, `frameInactive` | A whole sizable window's frame and empty caption (no title, no buttons), 240×160 version pixels |
| `captionActive`, `captionInactive` | The caption band alone, stretch sideways |
| `button.<kind>.<state>` | kind `minimize`, `maximize`, `restore`, `close`, `help`; state `normal`, `hot`, `pressed`, `disabled`, `inactive` (help: no `disabled`) |
| `pushButton.<state>` | `normal`, `pressed`, `disabled`, `default`, 9-slice |
| `textBox.normal` | A text box's sunken edge, 9-slice |
| `scrollArrow.<dir>.<state>` | dir `up`, `down`, `left`, `right`; state `normal`, `pressed`, `disabled` |

Draw the title yourself over the caption (font `fonts.caption`, colours `chrome.caption.*.text`, start at
`metrics.captionTextInset` after the icon). Glass parts carry alpha.

---

## 4. The pipe protocol

UTF-8, one JSON object per line (`\n`). A client line may be at most **64 KiB** (longer: error `tooLarge` and the pipe
closes). At most **16** clients; **20** messages a second each, bursts of **40** (error `rateLimited`). Any request may
carry an `id` (number or string); its answer carries it back as `re`. Unknown request fields are ignored.

| Request | Answer |
| --- | --- |
| `{"type":"hello","client":"My App","contracts":[1],"minor":0,"id":1}` | `welcome` (`contract`, `capabilities`, `deprecated`, `retroEyeVersion`, `instance`, `folder`, `events`, `limits`), then `state` |
| `{"type":"get"}` | `{"type":"state","state":{...theme.json...}}` |
| `{"type":"subscribe","events":["versionChanged"],"knownInstance":"..."}` (events optional: all) | `subscribed`; `started` at once if `knownInstance` names an earlier RetroEye |
| `{"type":"unsubscribe"}` | `unsubscribed` |
| `{"type":"notify","title":"Backup done","text":"All files copied.","icon":"info","app":"Backup","timeoutMs":8000,"tag":"b1"}` | `{"type":"queued","notification":"n-17"}` |
| `{"type":"ping"}` | `pong` |
| `{"type":"bye"}` | `bye`, then the pipe closes |

`hello` must come first, and a client is connected only once the `welcome` has come: send nothing else (a `notify`,
say) before it. Errors: `{"type":"error","re":..,"code":..,"message":..}` with code `badJson`, `tooLarge`,
`unknownType`, `badArgument`, `rateLimited`, `queueFull`, `helloFirst`, `disabled`, `tooManyClients`,
`unsupportedContract` (with `"supported":[1]`).

**Events** (`{"type":"event","event":<name>,...}`): `themeChanged` (anything changed; always sent, after the more
specific ones), `versionChanged` (version or look), `dpiChanged`, `taskbarMoved` (these four carry `state`),
`started`, `shuttingDown`, `notificationClicked` and `notificationClosed` (`notification`, `tag`, and `reason`:
`timeout`, `dismissed`, `clicked`, `dropped`, `shuttingDown`), and (1.1) `restarting`, sent just before
`shuttingDown` when RetroEye closes for its Restart and a new copy (a new `instance`) is about to take over. Only the
client that sent a notification hears of it.

**Switching eras** (Properties' era or theme, 7 to 98 and so on) never restarts RetroEye or its pipe: the pipe stays
open and subscribed, and the switch arrives as `versionChanged` then `themeChanged`, with the new state. Only quitting
RetroEye sends `shuttingDown`; its Restart sends `restarting` first.

**Reconnecting.** A pipe can still drop (RetroEye restarting or crashing, the API turned off and on). A client should
then try again after a quarter of a second, backing off (doubling, up to a couple of seconds) while it can't get in,
say `hello` and `subscribe` again (with `knownInstance`, so a new copy says `started`), and compare the state that
comes with the `welcome` with the last one it saw, to catch a change it missed. Meanwhile the file and event still
work. RetroEye has stopped only if `theme.json` says `running` false without `restarting`, or its `processId` is gone;
a file that can't be read for a moment (it is being replaced) proves nothing.

### Notifications (for programs with no window, tray icon or menu)

Shown as the **shown version's own balloon** from RetroEye's notification area (pointing at RetroEye's own tray
icon), one at a time in order, never taking the focus. Plain text only: `title` at most 63 characters, `text` 255
(control characters removed, longer cut), `icon` `info`/`warning`/`error`/`none` (Windows' own icons, read at run time),
`timeoutMs` held to 2,000-30,000 (default 10,000). Up to 5 waiting per client, 32 in all (`queueFull`). Turned off in
Properties: `disabled`.

### Clients that can't keep a pipe open

Wait on the change event; when it's set, read `theme.json`, compare `sequence`/`instance`, then wait for the event to
fall (it is reset after 500 ms) before waiting again. If `running` is false, or `retroEye.processId` is gone,
RetroEye isn't running.

---

## 5. The libraries

### Install

Both libraries are MIT licensed (`Api\LICENSE`) NuGet packages from GoodEye Studios; RetroEye itself is not open
source and is never in a package.

```
dotnet add package RetroEye.Api            # any .NET program (.NET Standard 2.0)
dotnet add package RetroEye.Theming.Wpf    # WPF on .NET 6 or .NET 10; brings RetroEye.Api with it
```

**From a local build** (before a version is on nuget.org, or to try a change), use a local folder as the package
source. Pack them (`dotnet pack Api\RetroEye.Api\RetroEye.Api.csproj -c Release` and the same for
`Api\RetroEye.Theming.Wpf\`; the packages land in `Api\artifacts\`), then in the program that uses them:

```
dotnet nuget add source <your clone>\Api\artifacts --name retroeye-local
dotnet add package RetroEye.Api --version 1.1.0
```

or, for that program alone, a `nuget.config` beside it:

```xml
<configuration>
  <packageSources>
    <add key="retroeye-local" value="<your clone>\Api\artifacts" />
  </packageSources>
</configuration>
```

A locally packed 1.1.0 is cached in `%USERPROFILE%\.nuget\packages` under the same number the nuget.org one will
have; after testing a local pack, delete `%USERPROFILE%\.nuget\packages\retroeye.api` and `...\retroeye.theming.wpf`
(or `dotnet nuget locals global-packages --clear`) so the published one is fetched.

What a package holds: `lib\<target>\` with the DLL and its XML documentation, `README.md` (`Api\README.md`), the MIT
`LICENSE` and the GoodEye logo `icon.png`; symbols go in the `.snupkg` beside it. `RetroEye.Theming.Wpf` depends on
`RetroEye.Api` `[1.1.0, 2.0.0)`. `dotnet run Api\Tools\CheckPackages.cs -- Api\artifacts` checks the packages: it
fails if one holds RetroEye.exe or any other program or unexpected file, looks obfuscated or stripped, lacks its XML
documentation or symbols, or isn't MIT; `--words FILE` adds words no package may hold.

### Using them

**RetroEye.Api** (`Api\RetroEye.Api`, netstandard2.0, no dependencies):

```csharp
using var client = new RetroEyeClient("My App");          // finds the real RetroEye
client.StateChanged += (s, e) => Restyle(client.State);   // on your SynchronizationContext
client.NotificationClicked += (s, e) => Console.WriteLine($"{e.Id} clicked");
client.Start();
ThemeState state = client.State;                          // RetroEye's, or Windows 98's defaults if it isn't running
if (client.Supports("notify"))
    await client.NotifyAsync(new Notification { Title = "Backup done", Text = "All files copied." });
```

`IsRunning`, `IsConnected` (true only after RetroEye's welcome), `Started`/`Stopped`, `EventReceived` (every event by
name), `GetStateAsync`, `RetroEyeClient.ReadFile(folder)`. It reconnects by itself after any drop (a quarter of a
second, then backing off up to `RetryMs`), subscribes again, raises any change it missed (`versionChanged`...), then
`Reconnected` ("RetroEye is back"); `Disconnected` when a ready pipe drops while RetroEye still runs, `Restarting` (1.1)
for RetroEye's Restart, which is never reported as `Stopped` (`IsRestarting`; it waits up to `RestartWaitMs`, 30 s). It
falls back on the file and event when the pipe can't be opened. A request made while RetroEye runs but the pipe isn't
ready yet (just started, reconnecting) waits for it, up to `ConnectWaitMs` (5 s); `WaitForConnectionAsync(ms)` waits
on purpose. `TryNotifyAsync` says plainly what became of a notification (`NotifyResult`: `Queued` with its `Id`,
`NotRunning`, `NotConnected`, `Refused` with RetroEye's `Code`, `TimedOut`) instead of a null or an exception.
`ThemeState` types every member above and keeps the raw JSON (`Raw`, an `ApiJson`) for later additions.

**RetroEye.Theming.Wpf** (`Api\RetroEye.Theming.Wpf`, net6.0-windows and net10.0-windows):

* `RetroEyeTheme.Follow(Application.Current, client)` styles WPF's standard controls: the WPF theme matching the
  look (Classic, Luna in its colours, Royale, Aero, which ship with .NET) with Windows' `SystemColors` keys set to the
  version's 31 colours, plus `RetroEyeKeys` resources for the fonts, metrics and colours by contract name.
* `RetroWindow`: a `Window` framed with RetroEye's pictures (9-slice frame, title in the caption font and colours,
  caption buttons in every state), or drawn from the colours when there are none; WM_NCHITTEST gives Windows the
  caption, edges and corners, so moving, sizing, maximizing and snapping work; active and inactive as the version.
  `IsDialog` gives "?" and close; `RenderPreview(active)` draws it off screen.

Samples: `Api\Samples\WpfSample` (RetroWindow with every standard control; `--shot file.png` draws it off screen)
and `Api\Samples\AlertConsole` (a windowless app: see section 7).

**Shipping.** RetroEye.Api and RetroEye.Theming.Wpf are public surface: they ship **un-obfuscated** and readable,
while RetroEye.exe stays obfuscated. They are published as the NuGet packages above, on their own version line, by
the RetroEye.Api repository's `.github\workflows\api-nuget.yml` for a tag `api-v<version>`; RetroEye's own release
build doesn't touch them.

---

## 6. Versioning and compatibility

The promise: **a program built on an earlier contract or library keeps working with every later RetroEye.**

**Contract versions (`contract.major.minor`).**
* **Minor** (1.0, 1.1, ...): additions only: new members, events, messages, request fields, error codes, parts,
  capabilities, and new values of open lists (a new Windows version, look id, frame kind...).
* **Major** (1, 2, ...): anything that could break a client. The folder, event and pipe names carry the major alone
  (`api\v1\`, `RetroEye.Api.v1...`), so a new major is published **beside** the old one, never in its place.

**Rules within a major.** Never remove, rename or retype a member, event, message, request field, error code or part,
and never change its meaning or units. Clients must be tolerant readers: ignore members, events, enum values,
capabilities and parts they don't know (the libraries do: an unknown `windows.version` reads as
`RetroEyeWindowsVersion.Unknown`, an unknown `look.id` falls back on `visualStyle`'s family through `LookFamily`).
RetroEye ignores request fields it doesn't know.

**Negotiation.** `hello` carries `contracts` (the majors the client speaks) and `minor` (the minor it was built
against). RetroEye answers with the contract it speaks on that pipe and `capabilities`; a client checks for a
feature (`client.Supports("notify")`, `state.Has("parts.buttons")`) rather than a number. A major the pipe doesn't
speak gets `unsupportedContract` with the supported majors; the library then keeps to the file and event.

**A breaking change later.** It becomes major 2, published side by side: RetroEye keeps writing `api\v1\` and
serving the v1 pipe beside `api\v2\` and the v2 pipe for **at least 12 months and at least 4 RetroEye releases**
after the first release with v2, whichever is longer. Anything to be dropped is announced here and listed in
`deprecated` (`{ "name", "since", "removedInMajor", "use" }`) at least **one minor before** the new major appears,
and stays listed for as long as the old major is served.

**Libraries (semantic versioning).** RetroEye.Api and RetroEye.Theming.Wpf are versioned 1.x for contract major 1
(`Api\Directory.Build.props`): their public surface only grows within 1.x, and their assembly version stays 1.0.0.0
for all of 1.x so a program built on 1.0 loads any later 1.x. A 2.x library would speak contract 2 (and may speak 1).

**Locked by tests.** The libraries' own tests (`Api\Tests\RetroEye.Api.Tests`, `dotnet test`) need nothing but this
repository; RetroEye's own tests run its side of the contract against the same lists.
* `docs\api\contract-1.0.golden.txt`: every field path and type of every version's state, every event, request, reply,
  error code, colour slot, capability, part and name of contract 1.0. A test fails if any line is gone (renamed,
  removed or retyped); additions pass. It is frozen (never rewritten). `docs\api\contract-1.1.golden.txt` is the same
  for 1.1 (1.0 plus the `restarting` event and capability), and holds everything 1.0's does.
* `Api\Tests\RetroEye.Api.Tests\States\*.json`: theme.json as RetroEye writes it for each version (RetroEye's tests
  fail if they no longer are). Each matches `docs\api\theme.v1.schema.json`, reads as that version looked, and
  together they still have every field and part of the golden lists.
* `Api\Tests\RetroEye.Api.Tests\ApiBaseline\RetroEye.Api.txt` and `RetroEye.Theming.Wpf.txt`: the libraries' public
  surface; a test fails if anything in them is gone. After a deliberate *addition*, `RETROEYE_WRITE_API_BASELINE=1`
  writes them afresh.
* The client on a real pipe against a stand-in RetroEye: a notification sent the moment a client starts waits for the
  welcome; a dropped pipe comes back by itself with the change made meanwhile and isn't taken for a stop; a Restart
  is `restarting`, not a stop; a real shutdown still is; a 1.0 RetroEye still works; a pipe speaking another major
  (`unsupportedContract`) leaves the client on the file and event.
* A 1.0 client reads a newer 1.x file (unknown members, versions and looks); a newer client reads a bare 1.0 file
  (missing members fall back on defaults).

---

## 7. Windowless apps

A program with no window, tray icon or menu can still talk to the user, in the shown Windows version's own balloons.
`Api\Samples\AlertConsole` is one, start to finish.

* **Build it as a Windows app** (`<OutputType>WinExe</OutputType>`): starting it opens no console at all, from Explorer,
  a shortcut, Run or a command line. Keep a log in a file beside it for anything the user doesn't need to see.
* **Speak in balloons.** `client.Start()`, then `await client.TryNotifyAsync(...)`: it waits for RetroEye's welcome
  (no "not connected" race at start-up) and says what became of each notification. Balloons are plain text with no
  buttons; a click is the only answer. Give each a `tag` and act on `NotificationClicked`: the sample answers any click
  with a "Quit?" balloon, and quits when that one is clicked.
* **Follow RetroEye through anything.** Era switches arrive as `versionChanged` (the pipe stays up); a drop or a
  Restart is handled by the library (`Disconnected`, `Restarting`, then `Reconnected`), and only a real quit is
  `Stopped`. Keep running while RetroEye is away: the client finds it again when it comes back.
* **One copy.** Hold a named mutex (`Local\<your app>.<user SID>`); a second start sets a named event the first one
  waits on (the sample answers with an "already running" balloon) and exits. Offer `--quit` the same way.
* **Watching it: RetroEye's own Command Prompt.** A program that wants its lines seen asks RetroEye to show its console
  in RetroEye's era-styled Command Prompt instead of Windows' terminal:

  ```
  RetroEye.exe --command-prompt --run "<program> <arguments>" [--title "<window title>"] [--folder <start folder>]
  ```

  The window is the shown version's console (95/98/Me's MS-DOS Prompt with its toolbar; 2000's, XP's, Vista's and 7's
  Command Prompt; today's for the System themes), and it becomes the new version's when the era is switched, keeping
  what's on screen. Only that program runs in it (no `cmd.exe` round it); the window closes when the program exits,
  and closing the window ends the program, as a console's did. RetroEye runs it in a pseudoconsole of its own, so
  Windows' default terminal (Windows Terminal) is never involved or changed, and every other console program still
  opens where it always did. The program must be a console program to have a console: a Windows app runs its console
  copy through `dotnet.exe` (`"<dotnet.exe>" "<app>.dll" --console`), as the sample does. Find RetroEye.exe from
  `theme.json`'s `retroEye.processId` (that process's path), or `%LOCALAPPDATA%\Programs\RetroEye\RetroEye.exe`.
  (This is RetroEye's command line, beside the contract rather than part of it; builds without the `Terminal`
  feature don't have it.)

The sample's options: none (no window, balloons only, log `RetroEye.AlertConsole.log` beside it); `--console` (its
lines in RetroEye's Command Prompt, or in the console it was started from when it has one); `--quit` (ends a running
copy); `--seconds N`; `--log FILE`; `--folder`/`--channel` (a test copy of RetroEye); `--retroeye PATH` or
`RETROEYE_EXE` (whose Command Prompt to use).
