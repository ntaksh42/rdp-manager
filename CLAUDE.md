# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

RdpManager is a Windows WPF desktop app (.NET 10) that organizes RDP connections in a tree and displays each remote session **embedded as a tab inside the app window** (RDCMan-style). It also launches SSH/Telnet/VNC via external clients. Single developer project, distributed as an MSI via GitHub Releases.

UI text is **English**. Code comments are **Japanese** — keep both conventions when editing.

## Commands

```powershell
# Build (also the primary verification step — there are no automated tests)
dotnet build

# Run
dotnet run --project src/RdpManager

# Headless self-test of the RDP ActiveX embedding (no GUI interaction needed).
# Writes result to %TEMP%\rdpmanager_selftest.txt and exits.
src/RdpManager/bin/Debug/net10.0-windows/RdpManager.exe --selftest

# Publish self-contained single-file exe (MSI payload)
dotnet publish src/RdpManager/RdpManager.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-sc

# Build the MSI (requires WiX 5 — NOT 7, which needs a paid OSMF EULA)
dotnet tool install --global wix --version 5.0.2   # one-time
wix build installer/RdpManager.wxs -bindpath publish-sc -o dist/RdpManager-x.y.z.msi
```

- **Always kill any running `RdpManager` process before rebuilding** — the running exe locks the output file and the build fails with MSB3026 (file copy errors), which look like build errors but are not.
- There is no test framework. Verify changes by: build (0 warnings expected), launch + smoke test, and `--selftest` for the RDP control.
- Releasing a version means bumping `<Version>` in `RdpManager.csproj` AND `Version=` in `installer/RdpManager.wxs`, rebuilding the MSI, then `gh release create vX.Y.Z dist/...msi`. Commits use `Closes #N` to auto-close issues.

## Architecture — the non-obvious parts

### RDP embedding (Controls/RdpClientHost.cs)
The remote desktop is the `mstscax.dll` ActiveX control hosted via a hand-rolled `AxHost` subclass and driven through **`dynamic` (IDispatch)** — there is intentionally **no generated AxInterop/AxMSTSCLib** (aximp isn't available; `<COMReference>` only yields the RCW, not the AxHost wrapper).
- **CLSID selection must probe by actually instantiating.** `Type.GetTypeFromCLSID` returns non-null even for unregistered/uninstantiable classes, so `ResolveClsid()` walks newest→oldest control versions and `Activator.CreateInstance`s each, picking the first that succeeds. On some machines v13 throws `CLASS_E_CLASSNOTAVAILABLE` and v12 is used. This was the root cause of an early "ClassFactory cannot supply requested class" bug.
- Most RDP properties can only be set **before `Connect()`**. `KeyboardHookMode` lives on **`SecuredSettings`** (NOT `AdvancedSettings`) and is disconnected-state-only; it's set to `1` so Alt+Tab/Win combos go to the remote.
- Window-size following uses `UpdateSessionDisplaySettings` (dynamic resolution, RDP 8.1+), debounced; falls back to `SmartSizing` on older servers.

### Sessions are managed in code-behind, not MVVM (MainWindow.xaml.cs)
The tree/data is MVVM (`MainViewModel`), but **live RDP sessions are deliberately NOT data-templated**. A data-templated `TabControl` destroys/recreates content on tab switch, which kills the ActiveX connection. Instead:
- `OpenSession()` creates explicit `TabItem`s whose `Content` is an `RdpSessionControl`.
- `RdpSessionControl` connects on **`Loaded`** (when actually in the visual tree), never on a one-shot timer, and **must NOT disconnect on `Unloaded`** (tab switch fires Unloaded — disconnecting there breaks multi-tab/split). Cleanup happens only on explicit close.
- Two `TabControl`s (`SessionTabs`, `SessionTabsRight`) implement left/right split view; `_activePane` tracks which one hotkeys act on.

### Global hotkeys (MainWindow.xaml.cs, RegisterHotKey)
Because a focused RDP session swallows normal keystrokes, all app shortcuts use Win32 `RegisterHotKey`: F11 / Ctrl+Alt+Pause / Ctrl+Alt+Break (fullscreen toggle), Ctrl+Alt+PageDown/PageUp (cycle tabs). Avoid Ctrl+Alt+Arrow — it conflicts with Intel/AMD display-rotation hotkeys (caused intermittent failures).

### Fullscreen is app-window, not control-fullscreen
`ToggleFullscreen()` hides menu/toolbar/statusbar/tree, sets the tree `ColumnDefinition.Width` **and `MinWidth`** to 0 (MinWidth=200 otherwise leaves a blank strip), and goes borderless-maximized (or spans the virtual screen if `FullscreenSpan`). It does NOT use the RDP control's own FullScreen property.

### Connect dispatch (MainViewModel.BuildLaunchInfo + MainWindow.ConnectEmbedded)
- Credentials resolve through a chain: `direct` / `profile` / `winCred` (reads Windows Credential Manager via `CredRead`) / `inheritFromParent` (walks up the tree).
- Display/RDP settings similarly inherit from ancestor folders when `InheritSettings` is set.
- `Protocol == "RDP"` → embedded tab. Otherwise → `ProtocolLauncher` (external SSH/Telnet/VNC). `RdpLauncher` is the external-mstsc fallback; it injects credentials via Win32 `CredWrite` (NOT cmdkey command line) so passwords never hit a command line.

### Persistence (Services/)
- `connections.json` (`%APPDATA%\RdpManager`): tree + credential profiles. `MainViewModel` maps between the UI `TreeNodeViewModel` and the serializable `NodeDto`. On parse failure the file is backed up to `.bak` before reseeding (avoids silent data loss).
- `appsettings.json`: theme, restore-on-startup, recent IDs, fullscreen-span.
- Passwords are **DPAPI-encrypted (CurrentUser)** in JSON via `CredentialProtector`; plaintext lives only in memory. DPAPI ciphertext is non-portable across user/machine by design.

### WinForms/WPF type ambiguity (important when editing any .cs)
`<UseWindowsForms>` + `<UseWPF>` both pull in conflicting types (`Application`, `Button`, `MessageBox`, `Brush`, `TabControl`, `TabItem`, `KeyEventArgs`, `MouseButtonEventArgs`, `Point`, `Orientation`, `DragEventArgs`, etc.). Files resolve these with **`using X = System.Windows....;` aliases at the top**. When you add code referencing an ambiguous type, add the alias or the build breaks with CS0104.

### Robustness conventions
- `App.OnUnhandled` swallows dispatcher exceptions (shown once) so an embedded-control glitch never crashes the whole app.
- Keep the build at **0 warnings**; `RdpClientHost` funnels dynamic access through `GetClient()` specifically to avoid nullable/dynamic warnings.
