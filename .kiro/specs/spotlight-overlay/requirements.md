# Requirements Document

## Introduction

The Spotlight Overlay application is a lightweight Windows desktop tool built with C# and WPF (.NET 8). It runs silently in the system tray and provides a live spotlight overlay during presentations. Users activate spotlight cutouts via a global Ctrl + Left-Click + Drag gesture, creating feathered rectangular highlights on a darkened screen overlay. Multiple cutouts can be created before dismissing the overlay with Escape. The application supports multi-monitor setups, configurable settings via JSON, and packages as a single standalone executable.

## Glossary

- **Overlay_Window**: A full-screen, borderless, transparent, topmost WPF window that displays the darkened layer and spotlight cutouts on a single monitor.
- **Spotlight_Cutout**: A rectangular region within the Overlay_Window where the dark layer is removed, revealing the underlying screen content with a soft feathered edge.
- **Feathered_Edge**: A gradient-based soft transition around the border of a Spotlight_Cutout, created using an OpacityMask with gradient brushes.
- **Opacity_Mask**: A combined DrawingGroup-based mask applied to the Overlay_Window that defines all visible Spotlight_Cutout regions simultaneously.
- **Global_Input_Hook**: A low-level Windows mouse and keyboard hook that captures input events system-wide, regardless of which application has focus.
- **Tray_Icon**: A Windows system tray notification icon that provides the primary user interface for the application when no overlay is active.
- **Settings_Service**: A service responsible for loading, saving, and exposing user-configurable settings from a JSON file.
- **Settings_Window**: A WPF dialog accessible from the Tray_Icon context menu for adjusting overlay opacity and feather radius.
- **Spotlight_Renderer**: A component that maintains the list of Spotlight_Cutout rectangles and generates the combined Opacity_Mask from gradient brushes.
- **Drag_Gesture**: The Ctrl + Left-Click + Drag input sequence used to define a new Spotlight_Cutout region.
- **Overlay_Opacity**: A configurable value between 0.0 and 1.0 that controls the darkness of the semi-transparent layer on the Overlay_Window.
- **Feather_Radius**: A configurable value in pixels that controls the width of the soft gradient transition around each Spotlight_Cutout.

## Requirements

### Requirement 1: System Tray Operation

**User Story:** As a presenter, I want the application to run in the system tray without a visible window, so that it stays out of the way until I need it.

#### Acceptance Criteria

1. WHEN the application starts, THE Tray_Icon SHALL appear in the Windows system tray with no visible application window.
2. WHEN the user right-clicks the Tray_Icon, THE Tray_Icon SHALL display a context menu with the options "Enable/Disable Spotlight", "Settings…", and "Exit".
3. WHEN the user selects "Exit" from the Tray_Icon context menu, THE application SHALL remove the Tray_Icon and terminate the process.
4. WHEN the user selects "Enable/Disable Spotlight" from the Tray_Icon context menu, THE application SHALL toggle the Global_Input_Hook between active and inactive states.
5. WHILE the Global_Input_Hook is inactive, THE application SHALL ignore all Drag_Gesture and Escape key inputs.

### Requirement 2: Global Input Detection

**User Story:** As a presenter, I want to use a global keyboard and mouse gesture to activate the spotlight, so that I can trigger it from any application.

#### Acceptance Criteria

1. THE Global_Input_Hook SHALL register low-level mouse and keyboard hooks using the Windows SetWindowsHookEx API.
2. WHEN the user holds Ctrl and presses the left mouse button, THE Global_Input_Hook SHALL begin tracking mouse movement to detect a Drag_Gesture.
3. WHEN the user releases the left mouse button after a Drag_Gesture, THE Global_Input_Hook SHALL emit the drag-start and drag-end screen coordinates as a rectangle to the Overlay_Window.
4. WHEN the user presses the Escape key while the Overlay_Window is visible, THE Global_Input_Hook SHALL emit a dismiss signal to the Overlay_Window.
5. IF the SetWindowsHookEx call fails, THEN THE Global_Input_Hook SHALL log the error and notify the user via a system tray balloon notification.

### Requirement 3: Overlay Window Creation

**User Story:** As a presenter, I want a darkened overlay to appear on my screen when I start a spotlight gesture, so that the audience focuses on the highlighted area.

#### Acceptance Criteria

1. WHEN the first Drag_Gesture is detected and no Overlay_Window is currently visible, THE application SHALL create a new Overlay_Window on the monitor where the drag started.
2. THE Overlay_Window SHALL use AllowsTransparency="True", WindowStyle="None", and Topmost="True" properties.
3. THE Overlay_Window SHALL cover the entire working area of the target monitor.
4. THE Overlay_Window SHALL display a semi-transparent dark layer with opacity equal to the configured Overlay_Opacity value.
5. WHILE no Drag_Gesture is in progress, THE Overlay_Window SHALL be click-through, allowing mouse events to pass to underlying windows.
6. WHILE a Drag_Gesture is in progress, THE Overlay_Window SHALL capture mouse events to track the drag selection rectangle.

### Requirement 4: Multiple Spotlight Cutouts

**User Story:** As a presenter, I want to create multiple spotlight regions before dismissing the overlay, so that I can highlight several areas simultaneously.

#### Acceptance Criteria

1. WHEN a Drag_Gesture completes, THE Spotlight_Renderer SHALL add the resulting rectangle to the list of active Spotlight_Cutout regions.
2. THE Spotlight_Renderer SHALL support a minimum of 10 simultaneous Spotlight_Cutout regions.
3. WHEN a new Spotlight_Cutout is added, THE Spotlight_Renderer SHALL rebuild the Opacity_Mask to include all active Spotlight_Cutout regions.
4. THE Overlay_Window SHALL display all active Spotlight_Cutout regions simultaneously until the user presses Escape.

### Requirement 5: Feathered Spotlight Rendering

**User Story:** As a presenter, I want the spotlight edges to have a soft gradient transition, so that the visual effect looks polished and professional.

#### Acceptance Criteria

1. THE Spotlight_Renderer SHALL generate a gradient-based Feathered_Edge for each Spotlight_Cutout using DrawingBrush or GradientBrush elements.
2. THE Spotlight_Renderer SHALL apply the configured Feather_Radius value to control the width of the gradient transition around each Spotlight_Cutout.
3. THE Spotlight_Renderer SHALL combine all Spotlight_Cutout gradients into a single DrawingGroup used as the Opacity_Mask on the Overlay_Window.
4. WHEN the Feather_Radius setting changes, THE Spotlight_Renderer SHALL apply the updated radius to subsequent Spotlight_Cutout regions.

### Requirement 6: Overlay Dismissal and Fade Animation

**User Story:** As a presenter, I want the overlay to fade out smoothly when I press Escape, so that the transition back to the normal screen is not jarring.

#### Acceptance Criteria

1. WHEN the user presses Escape while the Overlay_Window is visible, THE Overlay_Window SHALL begin a smooth fade-out animation reducing opacity from the current value to 0.0.
2. THE fade-out animation SHALL complete within 300 milliseconds.
3. WHEN the fade-out animation completes, THE Overlay_Window SHALL close and remove itself from the screen.
4. WHEN the fade-out animation completes, THE Spotlight_Renderer SHALL clear all active Spotlight_Cutout regions.
5. WHEN the Overlay_Window is dismissed, THE application SHALL return to idle state and accept new Drag_Gesture inputs.

### Requirement 7: Multi-Monitor Support

**User Story:** As a presenter, I want the overlay to appear on the correct monitor, so that the spotlight works properly in multi-monitor setups.

#### Acceptance Criteria

1. WHEN a Drag_Gesture begins, THE application SHALL determine which monitor contains the drag start point using the Windows screen geometry API.
2. THE Overlay_Window SHALL be positioned and sized to cover the full bounds of the identified monitor.
3. THE Spotlight_Cutout coordinates SHALL be relative to the Overlay_Window position on the target monitor.

### Requirement 8: Settings Persistence

**User Story:** As a user, I want my overlay settings to be saved and loaded automatically, so that I do not have to reconfigure the application each time.

#### Acceptance Criteria

1. WHEN the application starts, THE Settings_Service SHALL load settings from a Settings.json file located in the application directory.
2. IF the Settings.json file does not exist, THEN THE Settings_Service SHALL create a default Settings.json file with Overlay_Opacity set to 0.5 and Feather_Radius set to 30 pixels.
3. IF the Settings.json file contains invalid JSON, THEN THE Settings_Service SHALL log a warning and use default values for Overlay_Opacity (0.5) and Feather_Radius (30 pixels).
4. WHEN settings are modified through the Settings_Window, THE Settings_Service SHALL save the updated values to Settings.json immediately.
5. THE Settings_Service SHALL validate that Overlay_Opacity is between 0.0 and 1.0 inclusive, and that Feather_Radius is a non-negative integer.
6. IF a setting value is outside the valid range, THEN THE Settings_Service SHALL clamp the value to the nearest valid boundary.

### Requirement 9: Settings Window

**User Story:** As a user, I want a simple settings dialog to adjust overlay appearance, so that I can customize the spotlight to my preference.

#### Acceptance Criteria

1. WHEN the user selects "Settings…" from the Tray_Icon context menu, THE application SHALL open the Settings_Window.
2. THE Settings_Window SHALL display controls for adjusting Overlay_Opacity and Feather_Radius.
3. WHEN the user modifies a setting in the Settings_Window, THE Settings_Window SHALL pass the updated value to the Settings_Service for persistence.
4. IF the Settings_Window is already open when the user selects "Settings…" again, THEN THE application SHALL bring the existing Settings_Window to the foreground instead of opening a duplicate.

### Requirement 10: Packaging and Deployment

**User Story:** As a user, I want the application to be a single executable with no external dependencies, so that I can easily distribute and run it.

#### Acceptance Criteria

1. THE application SHALL target .NET 8 and produce a single self-contained executable for Windows.
2. THE application SHALL include all required runtime dependencies within the single executable.
3. THE application SHALL run without requiring a separate .NET runtime installation on the target machine.

### Requirement 11: Settings Serialization Round-Trip

**User Story:** As a developer, I want settings serialization to be lossless, so that saved settings are always loaded back identically.

#### Acceptance Criteria

1. FOR ALL valid settings objects, THE Settings_Service SHALL produce an identical settings object when saving to JSON and then loading from the same JSON file (round-trip property).
2. THE Settings_Service SHALL preserve the exact numeric precision of Overlay_Opacity and Feather_Radius values through the save and load cycle.
