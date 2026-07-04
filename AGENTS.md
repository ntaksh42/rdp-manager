# AGENTS.md

Guidance for AI coding agents working in this repository (read by Claude Code via CLAUDE.md import, and by other agents such as Codex / Copilot / Cursor directly).

## What this is

rdpmanager is a Windows WPF desktop app (.NET 10) that organizes RDP connections in a tree and displays each remote session **embedded as a tab inside the app window** (RDCMan-style). It also launches SSH/Telnet/VNC via external clients. Single developer project, distributed as an MSI via GitHub Releases.

UI text is **English**. Code comments are **Japanese** — keep both conventions when editing.

## Product priorities

Every change should be weighed against these three values (in this order):
1. **Performance** — fast startup, snappy session switching. Don't add background work or heavy dependencies.
2. **Simple UX** — the core loop is "manage many connections in a tree, switch between them in fullscreen". Reject feature creep that clutters the UI (e.g. the Quick Connect bar was removed for this reason); prefer removing chrome over adding it.
3. **Keyboard-complete operation** — every frequent action must be reachable without the mouse, including while a fullscreen RDP session has focus (use the `RegisterHotKey` layer for those). New features should ship with a shortcut, not just a menu item.

## Commands

```powershell
# Build (also the primary verification step — there are no automated tests)
dotnet build

# Run unit tests (pure-logic classes under tests/RdpManager.Tests)
dotnet test

# Run
dotnet run --project src/RdpManager

# Headless self-test of the RDP ActiveX embedding (no GUI interaction needed).
# Writes result to %TEMP%\rdpmanager_selftest.txt and exits.
src/RdpManager/bin/Debug/net10.0-windows/rdpmanager.exe --selftest

# Publish self-contained single-file exe (MSI payload)
dotnet publish src/RdpManager/RdpManager.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-sc

# Build the MSI (requires WiX 5 — NOT 7, which needs a paid OSMF EULA)
dotnet tool install --global wix --version 5.0.2   # one-time
wix build installer/RdpManager.wxs -bindpath publish-sc -o dist/rdpmanager-x.y.z.msi
```

- **Always kill any running `rdpmanager` process before rebuilding** — the running exe locks the output file and the build fails with MSB3026 (file copy errors), which look like build errors but are not.
- Pure-logic classes (`Common/`, `Services/`, `ViewModels/TreeNodeViewModel`) have xUnit tests under `tests/RdpManager.Tests`, run with `dotnet test`. UI/COM code (WPF, the RDP ActiveX embedding, Windows Credential Manager) has no test coverage — verify those by: build (0 warnings expected), launch + smoke test, and `--selftest` for the RDP control.
- Releasing a version means bumping `<Version>` in `RdpManager.csproj` AND `Version=` in `installer/RdpManager.wxs`, rebuilding the MSI, then `gh release create vX.Y.Z dist/...msi`. Commits use `Closes #N` to auto-close issues.

## Architecture — the non-obvious parts

### RDP embedding (Controls/RdpClientHost.cs)
The remote desktop is the `mstscax.dll` ActiveX control hosted via a hand-rolled `AxHost` subclass and driven through **`dynamic` (IDispatch)** — there is intentionally **no generated AxInterop/AxMSTSCLib** (aximp isn't available; `<COMReference>` only yields the RCW, not the AxHost wrapper).
- **CLSID selection must probe by actually instantiating.** `Type.GetTypeFromCLSID` returns non-null even for unregistered/uninstantiable classes, so `ResolveClsid()` walks newest→oldest control versions and `Activator.CreateInstance`s each, picking the first that succeeds. On some machines v13 throws `CLASS_E_CLASSNOTAVAILABLE` and v12 is used. This was the root cause of an early "ClassFactory cannot supply requested class" bug.
- Most RDP properties can only be set **before `Connect()`**. `KeyboardHookMode` lives on **`SecuredSettings`** (NOT `AdvancedSettings`) and is disconnected-state-only; it's set to `1` so Alt+Tab/Win combos go to the remote.
- Window-size following uses `UpdateSessionDisplaySettings` (dynamic resolution, RDP 8.1+), debounced; falls back to `SmartSizing` on older servers.

### Sessions are managed in code-behind, not MVVM (Controls/SessionManager.cs)
The tree/data is MVVM (`MainViewModel`), but **live RDP sessions are deliberately NOT data-templated**. A data-templated `TabControl` destroys/recreates content on tab switch, which kills the ActiveX connection. Instead:
- `SessionManager` (plain class, constructed by `MainWindow` with the two `TabControl`s) owns the whole tab lifecycle: `OpenSession()` creates explicit `TabItem`s whose `Content` is an `RdpSessionControl`; close/cycle/jump/split logic lives there too.
- Each tab's `Tag` is a `SessionTag(NodeId, PostCommand, Info)` record — `TryActivateExisting(nodeId)` dedupes tabs per tree node (activates + auto-reconnects instead of opening a second tab), and `PostCommand` runs via `ExternalTools` on close.
- `RdpSessionControl` connects on **`Loaded`** (when actually in the visual tree), never on a one-shot timer, and **must NOT disconnect on `Unloaded`** (tab switch fires Unloaded — disconnecting there breaks multi-tab/split). Cleanup happens only on explicit close. `Reconnect()` may only be called in the Disconnected state — the ActiveX throws if connected (the tab context menu guards this).
- Two `TabControl`s (`SessionTabs`, `SessionTabsRight`) implement left/right split view; `_activePane` tracks which one hotkeys act on.

### Keyboard shortcuts come in two layers (MainWindow.xaml.cs)
- **Win32 `RegisterHotKey`** for keys that must work while a focused RDP session swallows normal keystrokes: F11 / Ctrl+Alt+Pause / Ctrl+Alt+Break (fullscreen toggle), Ctrl+Alt+PageDown/PageUp (cycle tabs). Avoid Ctrl+Alt+Arrow — it conflicts with Intel/AMD display-rotation hotkeys (caused intermittent failures).
- **`PreviewKeyDown` on the window** for app-side shortcuts that only need to work outside a session (Ctrl+N, Ctrl+Shift+N, Ctrl+F, Ctrl+D, Ctrl+W, Esc); tree-specific keys (Enter connect, F2 edit, Del delete) hang off the TreeView.

### Theming (Services/ThemeManager.cs + App.xaml)
Light/dark is implemented by swapping frozen brushes in `Application.Current.Resources` at runtime. **Never hardcode a color in XAML or code** — use `{DynamicResource X}` with the existing keys (`WindowBg`, `PanelBg`, `TextFg`, `SubtleFg`, `BarBg`, `SplitterBg`, `ControlBg`, `ControlBorder`, `TabBg`, `TabSelectedBg`, `HoverBg`, `PressedBg`); a `StaticResource` or literal color will not follow the theme toggle. App.xaml holds themed implicit styles for common controls and dialogs.

### Fullscreen is app-window, not control-fullscreen
`ToggleFullscreen()` hides menu/toolbar/statusbar/tree, sets the tree `ColumnDefinition.Width` **and `MinWidth`** to 0 (MinWidth=200 otherwise leaves a blank strip), and goes borderless-maximized (or spans the virtual screen if `FullscreenSpan`). It does NOT use the RDP control's own FullScreen property.

### Connect dispatch (MainViewModel.BuildLaunchInfo + MainWindow.ConnectEmbedded)
- Credentials resolve through a chain: `direct` / `profile` / `winCred` (reads Windows Credential Manager via `CredRead`) / `inheritFromParent` (walks up the tree).
- Display/RDP settings similarly inherit from ancestor folders when `InheritSettings` is set.
- `Protocol == "RDP"` → embedded tab. Otherwise → `ProtocolLauncher` (external SSH/Telnet/VNC). `RdpLauncher` is the external-mstsc fallback; it injects credentials via Win32 `CredWrite` (NOT cmdkey command line) so passwords never hit a command line.

### Persistence (Services/)
- `connections.json` (`%APPDATA%\rdpmanager`): tree + credential profiles. `MainViewModel` maps between the UI `TreeNodeViewModel` and the serializable `NodeDto`. On parse failure the file is backed up to `.bak` before reseeding (avoids silent data loss).
- `appsettings.json`: theme, restore-on-startup, recent IDs, fullscreen-span, last window placement (restored with an on-screen check for changed monitor setups).
- Passwords are **DPAPI-encrypted (CurrentUser)** in JSON via `CredentialProtector`; plaintext lives only in memory. DPAPI ciphertext is non-portable across user/machine by design.

### WinForms/WPF type ambiguity (important when editing any .cs)
`<UseWindowsForms>` + `<UseWPF>` both pull in conflicting types (`Application`, `Button`, `MessageBox`, `Brush`, `TabControl`, `TabItem`, `KeyEventArgs`, `MouseButtonEventArgs`, `Point`, `Orientation`, `DragEventArgs`, etc.). Files resolve these with **`using X = System.Windows....;` aliases at the top**. When you add code referencing an ambiguous type, add the alias or the build breaks with CS0104.

### Robustness conventions
- `App.OnUnhandled` swallows dispatcher exceptions (shown once) so an embedded-control glitch never crashes the whole app.
- Keep the build at **0 warnings**; `RdpClientHost` funnels dynamic access through `GetClient()` specifically to avoid nullable/dynamic warnings.
- **Do NOT enable `InvariantGlobalization`** — WPF resolves the "en-US" culture at startup and crashes with `InvalidOperationException: Cannot find non-neutral culture` (this shipped once as a perf tweak and broke launch). `PublishReadyToRun` and `TieredPGO` are fine.
