# Pen Scroll for macOS

An [OpenTabletDriver](https://opentabletdriver.net/) plugin for macOS. Hold a pen button and
move the pen to scroll, like two fingers on a trackpad. The tip never clicks while the
button is held, so you can rest the pen on the tablet and drag, and the cursor keeps
following the pen the whole time.

Two modes:

- **Drag** (default): the content follows the pen one-to-one, like scrolling on a touch screen.
- **Joystick**: the further you hold the pen from where you pressed, the faster it scrolls, and
  holding still keeps scrolling.

This is a macOS port of [otd-pen-scroll](https://github.com/MatsudaTetsuya/otd-pen-scroll)
by Matsuda Tetsuya, which is Linux-only. The scrolling is posted through CoreGraphics instead
of a Linux virtual wheel device, the cursor follows the pen instead of being pinned, the tip
is muted while scrolling, and Drag mode is new.

## Requirements

- macOS with OpenTabletDriver **0.6.7** (the same driver version the plugin is built against).
- Output mode set to **Absolute Mode**.
- OpenTabletDriver already has the Accessibility permission it needs to move the cursor;
  scrolling uses the same permission.

## Installing

1. Download `PenScroll.zip` from the [latest release](https://github.com/natesute/otd-pen-scroll-macos/releases/latest).
2. Unzip it into its own folder under the plugin directory:

   ```
   ~/Library/Application Support/OpenTabletDriver/Plugins/PenScroll/PenScroll.dll
   ```

3. Quit and relaunch OpenTabletDriver.
4. In the **Filters** tab, select **Pen Scroll** and tick **Enable Pen Scroll**.
5. In the **Pen Settings** tab, clear the binding of the pen button you chose as the modifier
   (Pen Binding 2 for the default button below), otherwise its click fires alongside the scrolling.
6. **Apply**, then **Save**.

## Settings

| Setting | Default | What it does |
| --- | --- | --- |
| Modifier Button | `1` | Which pen button starts scrolling, counting from 1. On Wacom pens button 2 is the upper side button, which is normally middle click and the least missed. |
| Mode | `Drag` | `Drag` follows the pen one-to-one. `Joystick` scrolls faster the further the pen is held from where you pressed. |
| Drag Sensitivity | `1` | Drag mode: pixels scrolled per pixel of pen movement. |
| Dead Zone | `12` px | Joystick mode: how far the pen must be held from the press point before scrolling starts. |
| Speed | `800` px/s | Joystick mode: scroll speed when the pen is held 100 px past the dead zone. Twice the distance scrolls twice as fast. |
| Invert Direction | off | Reverse the scroll direction. |
| Horizontal Scrolling | off | Also scroll sideways from horizontal pen movement. |
| Pin Cursor | off | Keep the cursor where you pressed while scrolling instead of following the pen. |

## How it works

It is a filter rather than a binding, because a binding only sees press and release, never
the movement in between. The filter runs after the driver has mapped the pen to screen
coordinates and before the driver's binding handler. While the modifier button is held it
turns pen movement into pixel-unit scroll-wheel events at the cursor's position, and zeroes
the tip pressure in the report so the binding handler never sees a click. The tip stays
muted after the button is released until the pen lifts, so letting go of the button before
lifting does not click whatever just scrolled under the pen.

## Building

Requires the .NET 8 SDK.

```console
$ dotnet publish src/PenScroll/PenScroll.csproj -c Release -o publish
```

`publish/PenScroll.dll` is the plugin. It compiles against the `OpenTabletDriver.Plugin`
package from NuGet, so no local driver install is needed to build.

## Licence

GPL-3.0-only, the same as the original. See [LICENSE](LICENSE).
