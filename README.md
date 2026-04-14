# Screen Spotlight

Draw spotlights, arrows, boxes, highlights, and numbered steps directly on your screen. Great for demos, trainings, presentations, and walkthroughs.

![Screen Spotlight toolbar](SpotlightOverlay/assets/background.png)

---

## Features

**Tools**
- **Spotlight**: Dims the screen and cuts out a bright rectangle to focus attention. Supports feathered edges, adjustable opacity, and multiple simultaneous spotlights.
- **Arrow**: Draw directional arrows with customizable arrowheads, line styles (solid/dashed/dotted), thickness, and color.
- **Box**: Draw a rectangle outline to frame an area.
- **Highlighter**: Semi-transparent color overlay for highlighting regions.
- **Steps**: Numbered teardrop or circle markers for annotating sequences.

**Toolbar**
- Flyout toolbar docks to any screen edge (left, right, or top)
- Draggable nub to reposition along the edge
- Toolbar hides automatically when a fullscreen app is in the foreground
- Fully customizable: reorder buttons, remove tools you don't use

**Settings**
- All hotkeys are rebindable
- Appearance of all tools is configurable
- Live or frozen background (freeze the screen at the moment you activate)
- Cumulative spotlights (multiple can coexist) vs. single-spotlight mode
- Undo last annotation with Esc vs. exit immediately
- Settings persist to `Settings.json` in the app directory

---

## Default hotkeys

| Action | Default |
|---|---|
| Activate overlay | `Ctrl + Shift + Click` (hold modifiers, then drag) |
| Cycle tool | `Ctrl + Shift + Space` |
| Pause / resume app | `Ctrl + Shift + Q` |
| Exit overlay | `Esc` |

All hotkeys are configurable in Settings > General.

---

## Requirements

- Windows 10 or 11 (x64)

(.NET 8 runtime is included in the self-contained build, no separate install needed.)

---

## Installation

Download the latest release from the [Releases](../../releases) page and run `SpotlightOverlay.exe`. No installer required. It's a single self-contained executable.

The app runs in the system tray. Left-click the tray icon to open Settings; right-click for the context menu.

---

## Building from source

```
git clone https://github.com/YOUR_USERNAME/screen-spotlight.git
cd screen-spotlight
dotnet build SpotlightOverlay
```

To publish a self-contained single-file release build:

```
dotnet publish SpotlightOverlay -c Release
```

Output lands in `SpotlightOverlay/bin/Release/net8.0-windows/win-x64/publish/`.

**Requirements:** .NET 8 SDK, Windows.

---

## Files written to disk

- `Settings.json`: user preferences, stored next to the executable

(`spotlight-debug.log` is also written, but only in dev DEBUG builds.)

---

## License

MIT
