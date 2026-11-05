<p align="center"><img src="docs/icon.png" width="120" alt="Omnipad icon"></p>

<h1 align="center">Omnipad</h1>
<p align="center">Use your Nintendo Switch Pro Controller and Joy-Cons as Xbox controllers on Windows - over USB or Bluetooth.<br>Made by <b>Omnific</b>, built on <a href="https://github.com/Davidobot/BetterJoy">BetterJoy</a>.</p>

<p align="center"><img src="docs/controllers.png" width="560" alt="Pro Controller, Joy-Con and Joy-Con pair"></p>

## Download

Grab **Omnipad-Setup.exe** from the [latest release](https://github.com/Ronan-creator/omnipad/releases/latest) and run it.
It installs Omnipad for your account and installs the ViGEmBus driver if you don't have it. On the last page, leave
"Start Omnipad now" ticked and allow the admin prompt once - that lets Omnipad start with Windows with full rights.

Prefer no installer? Download the `.zip` from the same release, unzip it anywhere and run `Omnipad.exe`
(you need [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) installed).

## What it does

- **Shows up as an Xbox controller**, so it works in Steam and practically every PC game. Deadzones, remapping and per-game layouts can be set in Steam.
- **One controller, one Xbox pad.** Plug the cable in while it's on Bluetooth (or unplug it) and Omnipad switches over without games seeing a new controller.
- **Wired works properly** - the USB start-up handshake is done right, so the Pro Controller doesn't die on the cable.
- **Runs in the tray** and starts when you sign in. Closing the window keeps it running.
- **Battery %** on each controller, in the tray, and a notification when it's low.
- **Auto-sleep** wireless controllers after a few idle minutes, or turn them off from the tray.
- **Home button glow** and **player lights** that match the Xbox controller number.
- **Gyro aiming** - turn the controller to move the mouse, while holding ZL, ZR, always, or toggled.
- **Capture button** takes a screenshot (Steam or Game Bar); **hold Home** for Steam Big Picture.
- **Input tester** with a stick-drift check, and a **USB device monitor** that shows what you plug in.
- Gets past **HidGuardian** (left behind by old BetterJoy installs) and can use **HidHide** to hide the real controller from Steam.
- Checks GitHub for **updates**.

## Steam

Omnipad presents an Xbox 360 controller. If Steam also lists the real Switch controller, either turn on
*Settings > Hide the real controller (HidHide)* in Omnipad (needs [HidHide](https://github.com/nefarius/HidHide/releases)),
or turn off Steam's own Switch controller support.

## Building

Visual Studio 2022 or the .NET SDK: `dotnet build Omnipad -c Release` (targets .NET Framework 4.8, x64).
The installer is built with NSIS: see `installer/omnipad.nsi`. Pushing a `v*` tag builds both and publishes a release
(`.github/workflows/release.yml`).

## Credits

- [BetterJoy](https://github.com/Davidobot/BetterJoy) by Davidobot - Omnipad started as a fork of BetterJoy v5 (MIT).
- [ViGEmBus](https://github.com/nefarius/ViGEmBus), [HidHide](https://github.com/nefarius/HidHide) and the ViGEm client by Nefarius.
- [hidapi](https://github.com/libusb/hidapi), and the Switch controller protocol notes by dekuNukem and the SDL project.

MIT License - see [LICENSE](LICENSE).
