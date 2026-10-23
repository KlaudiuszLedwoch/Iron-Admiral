# Iron Admiral - naval film look for ReShade (optional)

A ReShade preset in the spirit of the classic naval war films: warm light, a cooler sea, film contrast, a touch of Technicolor, a vignette and fine film grain, with AMD CAS sharpening.

## In game (beta line, DIP_GG_Fixes 1.11.155 and later)
F10 > Other > **ReShadeNavalLook** (off by default). Switched on, the mod downloads the package from this repository's release [reshade-naval-1](https://github.com/KlaudiuszLedwoch/Iron-Admiral/releases/tag/reshade-naval-1), checks its SHA-256 and unpacks it into the game folder. Switched off, the files go to Mods_backup_IronAdmiral - nothing is deleted. Both take effect at the next game start.

- Home opens the ReShade menu, Scroll Lock switches the effects on and off.
- A ReShade you installed yourself (a dxgi.dll without the Iron Admiral marker) is never touched; you can load IronAdmiral_Naval.ini in it instead.

## By hand
1. Install ReShade from https://reshade.me for Ultimate Admiral Dreadnoughts.exe, DirectX 10/11/12.
2. Choose the standard effects (SweetFX and AMD CAS are enough).
3. Copy IronAdmiral_Naval.ini into the game folder and pick it in the ReShade menu (Home).

## Licences
ReShade - BSD 3-clause (Patrick Mours); SweetFX shaders - MIT (CeeJay.dk); AMD FidelityFX CAS - MIT (Advanced Micro Devices). The preset is part of Iron Admiral.
