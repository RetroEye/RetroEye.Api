# RetroEye app API libraries

[RetroEye](https://github.com/RetroEye/RetroEye) turns Windows into an older Windows: 95, 98, Me, 2000, XP, Vista
or 7, with that version's taskbar, Start menu, windows and sounds. These two libraries let **your** program follow it:
look like the version RetroEye is showing, switch live when it changes, and show your alerts as that version's own
tray balloons, even from a program with no window at all.

| Package | For | Targets |
| --- | --- | --- |
| **RetroEye.Api** | Any .NET program: read RetroEye's look (version, colours, fonts, metrics, title bars), follow its changes, send notifications | .NET Standard 2.0, no dependencies |
| **RetroEye.Theming.Wpf** | WPF programs: the standard controls in the version's theme and colours, and `RetroWindow`, a window framed with RetroEye's own title bars and buttons | .NET 6 and .NET 10 on Windows |

Both are free and open source under the MIT licence, from GoodEye Studios. (RetroEye itself is a separate program;
your users need it running for your program to follow it. Without it the libraries give Windows 98's look, and a
notification simply reports `NotRunning`.)

## Install

```
dotnet add package RetroEye.Api
dotnet add package RetroEye.Theming.Wpf
```

(`RetroEye.Theming.Wpf` brings `RetroEye.Api` with it.)

## RetroEye.Api: follow the look, send a balloon

```csharp
using RetroEye.Api;

using var client = new RetroEyeClient("My App");                 // finds the running RetroEye
client.StateChanged += (s, e) => Console.WriteLine($"Now {client.State.WindowsName}, {client.State.LookName}");
client.NotificationClicked += (s, e) => Console.WriteLine($"Balloon {e.Id} clicked");
client.Start();

NotifyResult result = await client.TryNotifyAsync(
    new Notification { Title = "Backup done", Text = "All files copied.", Icon = NotificationIcon.Info });
Console.WriteLine(result.Status);                                // Queued, NotRunning, NotConnected, Refused...
Console.ReadLine();
```

## RetroEye.Theming.Wpf: a window that looks the part

```csharp
using RetroEye.Api;
using RetroEye.Theming.Wpf;
using System.Windows;
using System.Windows.Controls;

var app = new Application();
var client = new RetroEyeClient("My WPF App");
RetroEyeTheme.Follow(app, client);       // standard controls in the version's theme, colours and fonts, live
client.Start();
var window = new RetroWindow { Title = "Hello", Width = 320, Height = 200, Content = new Button { Content = "OK" } };
app.Run(window);                         // framed with RetroEye's own title bar and caption buttons
```

`RetroWindow` needs an STA thread (`[STAThread]` on `Main`, as any WPF program).

## The contract

The libraries speak RetroEye's **app API contract 1** (version 1.1): a `theme.json` file, a named event and a named
pipe. The full specification (every member, event and message) is `docs/API.md` in the libraries' repository:
<https://github.com/RetroEye/RetroEye.Api/blob/main/docs/API.md>. Issues and questions:
<https://github.com/RetroEye/RetroEye.Api/issues>.

## Versioning promise

* **A program built on an earlier 1.x keeps working with every later RetroEye.** Within contract major 1, RetroEye
  only adds; a breaking change becomes contract 2, published *beside* contract 1 for at least 12 months and 4
  RetroEye releases.
* The packages follow semantic versioning: **1.x speaks contract 1**, and their public surface only grows within 1.x
  (checked by tests against a recorded baseline). The assembly version stays **1.0.0.0 for all of 1.x**, so a program
  built on 1.0 loads any later 1.x without a binding redirect.
* `RetroEye.Theming.Wpf` 1.x depends on `RetroEye.Api` `[1.1.0, 2.0.0)`.
* Be a tolerant reader: ignore members, events and values you don't know (the libraries do).

## Licence

MIT, Copyright (c) 2026 GoodEye Studios. See `LICENSE` in the package.
