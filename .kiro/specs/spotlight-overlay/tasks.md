# Implementation Plan: Spotlight Overlay

## Overview

Build a Windows desktop application using C# and WPF (.NET 8) that runs in the system tray and provides a live spotlight overlay during presentations. Implementation proceeds bottom-up: project scaffolding → data models and settings → renderer → overlay window → global input hooks → tray icon → app wiring → packaging. Property-based tests use FsCheck.Xunit.

## Tasks

- [x] 1. Set up project structure and core data models
  - [x] 1.1 Create the solution and WPF application project
    - Create `SpotlightOverlay.sln` and `SpotlightOverlay.csproj` targeting `net8.0-windows` with `<UseWPF>true</UseWPF>` and `<UseWindowsForms>true</UseWindowsForms>`
    - Add NuGet references: `System.Text.Json` (built-in), `FsCheck.Xunit` (test project)
    - Create `SpotlightOverlay.Tests.csproj` as an xUnit test project referencing the main project and `FsCheck.Xunit`
    - Set `<PublishSingleFile>true</PublishSingleFile>`, `<SelfContained>true</SelfContained>`, `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` in the main csproj
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 1.2 Define the AppSettings record and DragRectEventArgs class
    - Create `Models/AppSettings.cs` with `public record AppSettings(double OverlayOpacity, int FeatherRadius)`
    - Create `Models/DragRectEventArgs.cs` with `Rect ScreenRect` and `Point DragStartPoint` properties
    - _Requirements: 2.3, 8.5_

- [x] 2. Implement SettingsService with validation and persistence
  - [x] 2.1 Implement SettingsService core logic
    - Create `Services/SettingsService.cs` with `Load()`, `Save()`, `Deserialize()`, `Serialize()`, and `Validate()` methods
    - Implement default values: OverlayOpacity=0.5, FeatherRadius=30
    - Implement clamping: OverlayOpacity to [0.0, 1.0], FeatherRadius to [0, int.MaxValue]
    - Handle missing file (create defaults), invalid JSON (log warning, use defaults)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6_

  - [x] 2.2 Write property test: Settings validation and clamping
    - **Property 10: Settings validation and clamping**
    - **Validates: Requirements 8.5, 8.6**

  - [x] 2.3 Write property test: Settings serialization round-trip
    - **Property 11: Settings serialization round-trip**
    - **Validates: Requirements 11.1, 11.2, 8.4**

  - [x] 2.4 Write unit tests for SettingsService
    - Test loading from existing file, creating defaults when missing, handling invalid JSON
    - _Requirements: 8.1, 8.2, 8.3_

- [x] 3. Checkpoint - Ensure settings tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement SpotlightRenderer
  - [x] 4.1 Implement SpotlightRenderer with DrawingGroup-based opacity mask
    - Create `Rendering/SpotlightRenderer.cs` with `AddCutout()`, `ClearCutouts()`, `BuildOpacityMask()`, `CutoutCount`, and `Cutouts` properties
    - Build a `DrawingGroup` containing a full-size black background drawing plus one gradient drawing per cutout
    - Use gradient brushes with configurable `FeatherRadius` for feathered edges on each cutout
    - Read `FeatherRadius` from `SettingsService`
    - _Requirements: 4.1, 4.2, 4.3, 5.1, 5.2, 5.3, 5.4_

  - [x] 4.2 Write property test: Cutout accumulation preserves all entries
    - **Property 5: Cutout accumulation preserves all entries**
    - **Validates: Requirements 4.1**

  - [x] 4.3 Write property test: Opacity mask reflects all active cutouts
    - **Property 6: Opacity mask reflects all active cutouts**
    - **Validates: Requirements 4.3, 5.3**

  - [x] 4.4 Write property test: Feather radius controls gradient extent
    - **Property 7: Feather radius controls gradient extent**
    - **Validates: Requirements 5.2**

  - [x] 4.5 Write unit tests for SpotlightRenderer
    - Test 10 simultaneous cutouts (Req 4.2), ClearCutouts resets state, gradient uses correct brush types (Req 5.1)
    - _Requirements: 4.2, 5.1, 5.4_

- [x] 5. Checkpoint - Ensure renderer tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Implement OverlayWindow with fade-out animation
  - [x] 6.1 Create OverlayWindow XAML and code-behind
    - Create `Windows/OverlayWindow.xaml` with `AllowsTransparency="True"`, `WindowStyle="None"`, `Topmost="True"`, semi-transparent black background
    - Create `Windows/OverlayWindow.xaml.cs` with constructor accepting `Rect monitorBounds` and `double overlayOpacity`
    - Position and size the window to cover the full monitor bounds
    - Implement `ApplyOpacityMask(DrawingGroup mask)` to set the overlay's OpacityMask
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 7.2_

  - [x] 6.2 Implement click-through toggle via P/Invoke
    - Add P/Invoke declarations for `GetWindowLohe system tray and provides a live spotlight overlay during presentations. Implementation proceeds bottom-up: project scaffolding → data models and settings → renderer → overlay window → global input hooks → tray icon → app wiring → packaging. Property-based tests use FsCheck.Xunit.

## Tasks

- [x] 1. Set up project structure and core data models
  - [x] 1.1 Create the solution and WPF application project
    - Create `SpotlightOverlay.sln` and `SpotlightOverlay.csproj` targeting `net8.0-windows` with `<UseWPF>true</UseWPF>` and `<UseWindowsForms>true</UseWindowsForms>`
    - Add NuGet references: `System.Text.ng`, `SetWindowLong` with `GWL_EXSTYLE`, `WS_EX_TRANSPARENT`, `WS_EX_LAYERED`
    - Implement `SetClickThrough(bool enabled)` to toggle WS_EX_TRANSPARENT on the overlay window handle
    - _Requirements: 3.5, 3.6_

  - [x] 6.3 Implement 300ms fade-out animation
    - Create a `Storyboard` with `DoubleAnimation` targeting `Opacity` from current value to 0.0 over 300ms
    - Implement `BeginFadeOut(Action onComplete)` that runs the storyboard and invokes the callback on completion
    - On completion: close window, clear cutouts via callback
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

  - [x] 6.4 Write property test: Overlay matches monitor bounds and opacity
    - **Property 4: Overlay matches monitor bounds and opacity**
    - **Validates: Requirements 3.3, 3.4, 7.2**

- [x] 7. Implement GlobalInputHook with P/Invoke
  - [x] 7.1 Implement low-level mouse and keyboard hooks
    - Create `Input/GlobalInputHook.cs` with P/Invoke for `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx`, `GetModuleHandle`, `GetKeyState`
    - Register `WH_MOUSE_LL` and `WH_KEYBOARD_LL` hooks in `Install()`, unregister in `Uninstall()`
    - Implement `IsEnabled` property to gate event processing
    - _Requirements: 2.1, 1.4, 1.5_

  - [x] 7.2 Implement Ctrl+Click+Drag gesture detection
    - In the mouse hook callback, check Ctrl state via `GetKeyState(VK_CONTROL)`
    - Track drag start on `WM_LBUTTONDOWN` when Ctrl is held, track movement, emit `DragCompleted` with `DragRectEventArgs` on `WM_LBUTTONUP`
    - Compute rectangle as `X=min(start.X, end.X)`, `Y=min(start.Y, end.Y)`, `Width=|end.X-start.X|`, `Height=|end.Y-start.Y|`
    - _Requirements: 2.2, 2.3_

  - [x] 7.3 Implement Escape key dismiss and error handling
    - In the keyboard hook callback, detect Escape key press and emit `DismissRequested` event
    - Handle `SetWindowsHookEx` failure: log error, show balloon notification via `TrayIconService`
    - _Requirements: 2.4, 2.5_

  - [x] 7.4 Write property test: Toggle hook state is consistent
    - **Property 1: Toggle hook state is consistent**
    - **Validates: Requirements 1.4**

  - [x] 7.5 Write property test: Disabled hook ignores all inputs
    - **Property 2: Disabled hook ignores all inputs**
    - **Validates: Requirements 1.5**

  - [x] 7.6 Write property test: Drag points produce correct rectangle
    - **Property 3: Drag points produce correct rectangle**
    - **Validates: Requirements 2.3**

- [x] 8. Checkpoint - Ensure input hook tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Implement multi-monitor support helpers
  - [x] 9.1 Implement monitor identification and coordinate translation
    - Create `Helpers/MonitorHelper.cs` with a method to find the monitor containing a given screen point using `System.Windows.Forms.Screen.AllScreens`
    - Implement screen-to-window coordinate translation (subtract monitor top-left offset)
    - Fall back to primary monitor if no monitor contains the point
    - _Requirements: 7.1, 7.2, 7.3_

  - [x] 9.2 Write property test: Monitor identification from point
    - **Property 8: Monitor identification from point**
    - **Validates: Requirements 7.1**

  - [x] 9.3 Write property test: Screen-to-window coordinate translation
    - **Property 9: Screen-to-window coordinate translation**
    - **Validates: Requirements 7.3**

- [x] 10. Implement TrayIconService and SettingsWindow
  - [x] 10.1 Implement TrayIconService
    - Create `Services/TrayIconService.cs` using `System.Windows.Forms.NotifyIcon`
    - Build context menu with "Enable/Disable Spotlight", "Settings…", "Exit" items
    - Expose events: `ToggleSpotlightRequested`, `SettingsRequested`, `ExitRequested`
    - Implement `SetEnabled(bool)` to update menu text, `ShowBalloon()` for notifications, `Dispose()` for cleanup
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 10.2 Implement SettingsWindow
    - Create `Windows/SettingsWindow.xaml` with Slider for OverlayOpacity (0.0–1.0) and Slider for FeatherRadius (0–100+)
    - Create `Windows/SettingsWindow.xaml.cs` binding to `SettingsService`, triggering `Save()` on value change
    - Implement singleton pattern: bring existing window to foreground if already open
    - _Requirements: 9.1, 9.2, 9.3, 9.4_

- [x] 11. Wire everything together in App.xaml.cs
  - [x] 11.1 Implement App.xaml.cs entry point and service wiring
    - Set `ShutdownMode = ShutdownMode.OnExplicitShutdown` in App.xaml
    - In `OnStartup`: instantiate `SettingsService`, `SpotlightRenderer`, `GlobalInputHook`, `TrayIconService`
    - Wire `TrayIconService.ToggleSpotlightRequested` → toggle `GlobalInputHook.IsEnabled` and call `TrayIconService.SetEnabled()`
    - Wire `TrayIconService.SettingsRequested` → open/focus `SettingsWindow`
    - Wire `TrayIconService.ExitRequested` → cleanup hooks, dispose tray icon, call `Shutdown()`
    - Wire `GlobalInputHook.DragCompleted` → identify monitor via `MonitorHelper`, create/reuse `OverlayWindow`, translate coordinates, call `SpotlightRenderer.AddCutout()`, apply mask
    - Wire `GlobalInputHook.DismissRequested` → call `OverlayWindow.BeginFadeOut()`, clear cutouts on completion
    - Set overlay click-through on/off based on drag state
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 2.3, 3.1, 3.5, 3.6, 6.1, 6.3, 6.4, 6.5, 7.1_

  - [x] 11.2 Add application icon resource
    - Add an embedded icon resource for the tray icon and application
    - Pass the icon to `TrayIconService` constructor
    - _Requirements: 1.1_

- [x] 12. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties using FsCheck.Xunit
- Unit tests validate specific examples and edge cases
- The overlay window and global input hook tasks involve P/Invoke and must run on Windows
