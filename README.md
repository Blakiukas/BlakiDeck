# BlakiDeck

BlakiDeck brings everyday PC controls into a customizable Windows desktop dashboard. Designed for touchscreen monitors and also usable with a mouse, it offers one to eleven tabs with individual layouts and skins.

## Features

- Arrange Windows and web shortcuts, configurable macros and clocks.
- Build a three-section dashboard, combine modules across sections, or use a wide music player.
- Monitor available CPU, GPU, RAM, storage and network readings with configurable cells, graphs, adaptive colors and saved layouts. View and copy PC specifications.
- Play local music or control supported music applications, including Spotify through Windows media sessions. Streaming artwork comes from the selected player.
- Use themed music players, stereo VU meters, spectrum displays and the BlakiHUD music visualizer.
- Open web shortcuts in the integrated browser, with full-dashboard, compact-bar and hidden-bar views.
- Control OBS Studio and individual Philips Hue lights, view Airthings readings and check the weather.
- Personalize fonts, colors, icons, skins and module layouts.
- Game mode automatically reduces visual effects and background activity while gaming, keeps music controls available and tracks time spent in the game.

## Installation

1. Download the latest BlakiDeck installer from Releases. SHA256SUMS.txt is provided for file verification.
2. Close BlakiDeck and run the installer. Review the license, choose the installation folder and components, and select shortcut/start-with-Windows options as desired.
3. Approve installation of any selected missing component when Windows asks. The included prerequisites do not require a separate download.
4. Launch BlakiDeck. Configure only the optional integrations you want to use.
5. To remove it, use Uninstall in the application or Windows Installed apps. The uninstaller also offers an optional checkbox to delete this installation's user data.

## Requirements and optional integrations

- Windows x64. Touch input is optional; mouse control is supported.
- The offline installer includes the .NET/WPF runtime and Microsoft Edge WebView2 Runtime installer. No separate .NET installation is needed.
- Optional PawnIO hardware-sensor support is included. Installing its driver requires administrator approval; available readings depend on the hardware and drivers. Other dashboard functions can be used without this sensor support.
- Local playback depends on supported Windows media components and codecs. Windows N may require Media Feature Pack.
- OBS integration requires OBS Studio with its WebSocket server enabled. Philips Hue requires a Hue Bridge on the same local network.
- Airthings monitoring requires compatible Airthings hardware, an account, API credentials and internet access.
- Streaming controls require a supported music application and any account or subscription it needs. Weather and other online features require internet access. External services have their own terms.

## License

BlakiDeck is free and open source under the MIT License. You may use, modify and redistribute it, including commercially, while retaining the copyright and license notice. All features are free; donations are optional. Third-party components retain their own licenses. Source code: https://github.com/Blakiukas/BlakiDeck. See LICENSE.txt and THIRD-PARTY-NOTICES.txt.

## Privacy

No BlakiDeck account is required. Settings are stored locally. BlakiDeck does not send usage analytics or automatically upload settings and diagnostic logs to the author. Optional integrations and visited websites communicate with their respective providers. See PRIVACY.txt for details.

## Support and another application

Questions or bug reports: blaki.support@gmail.com. Include the version and steps to reproduce the issue; do not send credentials.

[BlakiBridge](https://blakiukas.itch.io/blakibridge) is another application by the same author, designed to make mouse-pointer transitions between differently sized or arranged monitors correct.

The main interface is predominantly English. About, Help and legal information are available in English and Lithuanian.

## Screenshots

<img width="2560" height="747" alt="01-wide-music-player" src="https://github.com/user-attachments/assets/6fa0cc5b-ac1e-491c-9dd2-916a1826bdd3" />

<img width="2560" height="747" alt="02-airthings-macros-philips-hue" src="https://github.com/user-attachments/assets/85fed0fd-8e5d-4957-874f-6b1a5c616137" />

<img width="2560" height="747" alt="03-macros-obs-weather" src="https://github.com/user-attachments/assets/234cce70-835c-4747-b710-e2118d94caae" />

<img width="2560" height="747" alt="04-system-monitor" src="https://github.com/user-attachments/assets/25bbbf85-9b61-4bb7-bb9c-a9bbe087c939" />

<img width="1590" height="1307" alt="05-modules-layout" src="https://github.com/user-attachments/assets/3533b22c-e27d-4974-ba9e-bcd3a50f838a" />

<img width="2551" height="1598" alt="06-web-with-dashboard" src="https://github.com/user-attachments/assets/f3dea5dd-325f-4d05-9656-8d0380e98e18" />

<img width="2560" height="1600" alt="07-web-with-compact-bar" src="https://github.com/user-attachments/assets/96e72b90-f369-4668-a91a-91a8ba0983b8" />

<img width="2560" height="1600" alt="08-web-hidden-bars" src="https://github.com/user-attachments/assets/878fd425-8cf8-4aa0-8477-8dab2ce7c5b8" />

<img width="2560" height="747" alt="11-wide-shortcuts" src="https://github.com/user-attachments/assets/3bd7461b-c153-493d-b1bd-aae6c990322a" />

<img width="2560" height="747" alt="12-web-player-visualizer" src="https://github.com/user-attachments/assets/32e9f0b3-0d3f-4811-8eaf-c5f464b7c4df" />

<img width="2560" height="596" alt="13-wide-music-player-military" src="https://github.com/user-attachments/assets/b6e9ec31-f456-4f92-b7db-4d899e733d14" />

## Build from source

Requires Windows x64 and the .NET 10 SDK. From the repository root:

```powershell
dotnet restore Ekrano_valdymas/Ekrano_valdymas.csproj
dotnet build Ekrano_valdymas/Ekrano_valdymas.csproj -c Release
```

Run `Ekrano_valdymas/bin/Release/net10.0-windows10.0.19041.0/BlakiDeck.exe`.
The source distribution contains production C#/XAML sources and assets required to build them. Local accounts, tokens, browser profiles, user settings, diagnostics and development history are excluded. Configure optional integrations with your own credentials. Prototype and diagnostic-only code is not included in this production source distribution.

Application source is MIT licensed; third-party components retain their own licenses. See THIRD-PARTY-NOTICES.txt.

## Recent changes

- Assign global trigger keys to any macro, including microphone controls.
- Web shortcut context menus offer Edit, Move and Remove again.
- Web shortcut icons default to original colors independently of the shared icon effect; individual choices are preserved.
- Weather settings can show one decimal place for current, feels-like and forecast temperatures.