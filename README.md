# Kenshi Modding Tool

A Windows tool for translating Kenshi `.mod` files, in the spirit of xTranslator / ESP-ESM Translator.

- **Mod translator**: edit texts side by side, import/export TSV, personal dictionary, official game dictionary, install as a separate "Name - FR" mod.
- **Workshop → mods**: copies Steam Workshop mods into `Kenshi\mods\<ModName>\`.
- **Modlist**: shows `Kenshi\data\mods.cfg` in load order with translation status.

## Download
- `KenshiModdingTool - ENG.exe`: English interface
- `KenshiModdingTool - FR.exe`: French interface
- Requires the [.NET 8 Desktop Runtime](https://aka.ms/dotnet/download).

## Build from source
Install the .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`), then run `build.bat`. The exe is created in `dist\`.
The source files in this repository are the English version. The French source is in `KenshiModdingTool - SOURCE - FR.zip`.

Not affiliated with Lo-Fi Games.
