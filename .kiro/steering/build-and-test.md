---
inclusion: always
---

# Build and Test Rules for SpotlightOverlay

## Build Rules
- NEVER use `dotnet build --no-incremental`. It corrupts the WPF build output (BAML, resources) and causes 0xc0000142 crashes.
- If the build enters a bad state (MainResourcesGeneration errors), fix it by deleting both `obj/` and `bin/` folders, then rebuild: `Remove-Item -Recurse -Force SpotlightOverlay\obj, SpotlightOverlay\bin; dotnet build SpotlightOverlay`
- Always do a clean rebuild (`Remove-Item obj+bin`) after any XAML file changes to avoid stale BAML cache issues.
- After editing code, always build and verify before telling the user to run.
- ALWAYS kill SpotlightOverlay.exe before building: `taskkill /IM SpotlightOverlay.exe /F` — the exe locks DLLs and causes build failures if still running.
- Use `run.ps1` to build and run the app — it kills any existing instance, does a clean rebuild, and launches.

## Test Script
- Use `test-spotlight.ps1` to do automated end-to-end testing (simulates Ctrl+Drag via SendInput, checks debug log).
- The test script builds, launches the app, simulates input, reads the log, and kills the process.
- Debug logs write to `SpotlightOverlay\bin\Debug\net8.0-windows\win-x64\spotlight-debug.log`.
- LIMITATION: WPF Window.Show() throws "Not enough memory resources" (0xc0000142) when launched from Start-Process in PowerShell scripts. The automated test verifies hook/input pipeline but cannot test window rendering. Window rendering must be tested manually via `dotnet run --project SpotlightOverlay`.

## WPF + DPI
- Low-level hooks report coordinates in physical screen pixels.
- WPF windows use device-independent pixels (DIPs).
- Always convert hook coordinates to DIPs using the ratio of monitor physical size to window ActualWidth/ActualHeight.
- The overlay window's ActualWidth/ActualHeight may differ from the monitor resolution due to DPI scaling.

## WPF + Hooks Threading
- Low-level hook callbacks run on the thread that installed them (the UI thread in this app).
- Use `Dispatcher.BeginInvoke` (not `Dispatcher.Invoke`) from hook callbacks to avoid blocking the Windows message pump.
- `Dispatcher.Invoke` causes the hook thread to block, preventing subsequent hook callbacks from firing.

## Key State Detection
- Use `GetAsyncKeyState` (not `GetKeyState`) in low-level hooks to check modifier key state.
- `GetKeyState` queries the thread's message queue state which is unreliable in low-level hooks.
