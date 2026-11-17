# RetroEye.Api

The open source (MIT) libraries for talking to [RetroEye](https://github.com/RetroEye/RetroEye), the program that
turns Windows into an older Windows (95, 98, Me, 2000, XP, Vista or 7), and the specification of the app API they
speak. With them your program can look like the Windows version RetroEye is showing, switch live when it changes,
and show its alerts as that version's own tray balloons, even with no window at all.

| Package | For |
| --- | --- |
| [RetroEye.Api](https://www.nuget.org/packages/RetroEye.Api) | Any .NET program (.NET Standard 2.0, no dependencies) |
| [RetroEye.Theming.Wpf](https://www.nuget.org/packages/RetroEye.Theming.Wpf) | WPF on .NET 6 and .NET 10: the version's theme and `RetroWindow` |

```
dotnet add package RetroEye.Api
dotnet add package RetroEye.Theming.Wpf
```

How to use them: [Api/README.md](Api/README.md). The full specification (theme.json, the change event, the pipe
protocol, notifications, versioning): [docs/API.md](docs/API.md), with the schema and the golden lists of contract 1
in [docs/api](docs/api).

RetroEye itself is a separate program and is not in this repository; your users need it running for your program to
follow it. Without it the libraries give Windows 98's look, and a notification reports `NotRunning`.

## What's here

| Folder | What |
| --- | --- |
| `Api/RetroEye.Api` | The library for any .NET program |
| `Api/RetroEye.Theming.Wpf` | The WPF library |
| `Api/Samples` | `WpfSample` (a `RetroWindow` with every standard control) and `AlertConsole` (a program with no window) |
| `Api/Tests/RetroEye.Api.Tests` | The libraries' tests: JSON, the client on a real pipe against a stand-in RetroEye, the contract against the schema and golden lists, the public surface baselines, old and new versions together |
| `Api/Tools/CheckPackages.cs` | Checks built packages before they are published |
| `docs` | The specification |

## Building

Windows with the .NET 10 SDK (and the .NET 6 targeting pack, which the SDK fetches):

```
dotnet build Api/RetroEye.Theming.Wpf/RetroEye.Theming.Wpf.csproj -c Release
dotnet test Api/Tests/RetroEye.Api.Tests/RetroEye.Api.Tests.csproj
dotnet pack Api/RetroEye.Api/RetroEye.Api.csproj -c Release
dotnet pack Api/RetroEye.Theming.Wpf/RetroEye.Theming.Wpf.csproj -c Release
dotnet run Api/Tools/CheckPackages.cs -- Api/artifacts
```

The packages land in `Api/artifacts`. Releases are published to nuget.org by
[.github/workflows/api-nuget.yml](.github/workflows/api-nuget.yml) for a tag `api-v<version>`.

## Contributing

Issues are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

MIT, Copyright (c) 2026 GoodEye Studios. See [LICENSE](LICENSE).
