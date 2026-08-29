# DeskBox development workflow

## What this repo is

DeskBox is a free, open-source Windows desktop organizer with native-feeling WinUI 3 widgets (file organizer, todos, quick capture, search, weather, music). Stack: C# / WinUI 3, .NET 10 Native AOT, Windows App SDK 2.4, and a Rust native Shell layer; targets Windows 10 21H2+ on x64 and ARM64. Solo-maintained; external PRs are not accepted (see CONTRIBUTING.md).

- `src/DeskBox` — the WinUI 3 application: `Views/`, `ViewModels/`, `Controls/` (incl. `WidgetContents/`), `Services/`, `Models/`, `Contracts/`, `Helpers/`, `Strings/`, `Assets/`.
- `src/DeskBox.Updater` — direct-release updater helper.
- `native/` — Rust workspace: `deskbox-native` (production `deskbox_native.dll`), `deskbox-thumbnail-proxy`, and a test-only audio fixture.
- `tests/DeskBox.Tests` — service, policy, and AOT contract tests.
- `scripts/` — build, publish, audit, and memory-measurement PowerShell scripts; `installer/` — x64/ARM64 Inno Setup scripts.
- `docs/architecture/current_architecture.md` — the current-state architecture handoff; read before touching widget architecture.

## Build, test, and run

- After changing application code, first stop any running `DeskBox.exe` whose executable path is under this repository, then build the affected project, and start a fresh instance from the current Debug build unless the user explicitly asks not to restart it. Stopping before the build avoids locking the output executable.
- The canonical local development executable is `src/DeskBox/bin/Debug/net10.0-windows10.0.22621.0/DeskBox.exe`.
- Debug restore/build: `dotnet restore .\DeskBox.sln -p:Platform=x64` then `dotnet build .\src\DeskBox\DeskBox.csproj --configuration Debug --no-restore -p:Platform=x64`.
- After starting DeskBox, verify that exactly the intended repository build is running and report the executable path.
- Do not launch DeskBox from `Output`, `artifacts`, `.artifacts`, or `src/DeskBox/AppPackages` unless the user explicitly requests testing a packaged or published build.
- Preserve unrelated user changes and release artifacts. Ask before deleting material output directories or installer packages unless the user explicitly authorizes their removal.
- DeskBox is a packaged Windows application. Do not first run its tests with the default `AnyCPU` platform: MSIX packaging rejects a processor-neutral app-host executable. Run the test suite directly with `dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore --verbosity:minimal -p:Platform=x64` (add `-p:RuntimeIdentifier=win-x64` when using architecture-specific restored assets).
- For Release publishing, always specify a matching platform and runtime identifier from the start: `-p:Platform=x64 -p:RuntimeIdentifier=win-x64` for x64, or `-p:Platform=ARM64 -p:RuntimeIdentifier=win-arm64` for ARM64. Keep `SelfContained=false` and `WindowsAppSDKSelfContained=false` for the runtime-download installer workflow unless the user requests a self-contained build.
- The explicit architecture rules above apply to tests and Release publishing. Continue using the canonical non-platform Debug output for the normal local restart workflow.
- Retail packages must be produced by `scripts\publish-aot-retail.ps1 -Platform x64|ARM64`; never replace it with a bare `dotnet publish` — the installer requires the generated `DeskBox.InstallManifest.txt` to safely remove files from older payloads.

## Native AOT and Rust constraints

- The app project enforces `PublishAot=true`; AOT publishing additionally requires `-p:DeskBoxRustNative=true` (the build fails otherwise), because `deskbox_native.dll` owns the shortcut, Explorer-shell launch, Quick Access, music-volume, and exact Recycle Bin recovery paths.
- Ordinary JIT (non-AOT) Debug runs keep the established C# implementations as the default oracle; the Rust layer is exercised by AOT builds and the `scripts/run-aot-*.ps1` smoke scripts, with matching `App.Aot*.cs` smoke partials in the app project.
- The production Rust module is frozen at ABI 2, capability mask 511, ten exports; panic must never cross the C ABI. Contracts: `docs/architecture/*-native-abi-*.md` and `native/README.md`.

## Architecture boundaries

- New feature widgets must follow the shared content-window path: `WidgetKind -> WidgetRegistry -> WidgetContentDescriptor -> WidgetContentFactory / IWidgetContentProvider -> IWidgetContent -> ContentWidgetWindow -> WidgetManager`. Only QuickCapture still owns a dedicated host (`QuickCaptureWidgetWindow`); the legacy `WidgetWindow` host is removed.
- Widget kinds are `File`, `QuickCapture`, `Todo`, `Music`, `Weather` (plus planned `Tags`/`SystemMonitor` placeholders). Do not reintroduce the legacy `Productivity` kind into active creation paths. Reuse `WidgetShell` / `WidgetShellContentHost` for shared shell, menu, and lifecycle behavior.
- `docs/architecture/[重要勿删]widget_zorder_lifecycle.md` is explicitly marked do-not-delete; consult it before changing widget z-order or desktop-layer window lifecycle code.

## Conventions

- Localization lives in twelve JSON files under `src/DeskBox/Strings` (`en-US.json`, `zh-CN.json`, `zh-TW.json`, `ja-JP.json`, `de-DE.json`, `fr-FR.json`, `es-ES.json`, `pt-BR.json`, `ru-RU.json`, `ar-SA.json`, `hi-IN.json`, `bn-BD.json`). All twelve must keep identical resource-key and formatting-placeholder coverage — add every new string to all of them.
- `Nullable` is enabled; TFM is `net10.0-windows10.0.22621.0`; only x64/ARM64 platforms exist. The .NET SDK is pinned by `global.json` and the Rust toolchain by `rust-toolchain.toml`.
- NuGet lock files are enabled via `Directory.Build.props`; AOT builds use a separate `packages.aot.lock.json`.
- Windows 10 is the validated compatibility floor: unsupported materials, rounded corners, and some animations fall back there, so keep behavior working against that floor.

