# DIP_GG_Fixes — build instructions

DIP_GG_Fixes.dll is a MelonLoader 0.6.6 mod for Ultimate Admiral: Dreadnoughts
(IL2CPP, .NET 6). It is two source files: `Mod.cs` and `AssemblyInfo.cs`.
No NuGet packages, no obfuscation, no network code.

## References

All referenced assemblies come from an installed game with MelonLoader 0.6.6
already run once (MelonLoader generates the Il2Cpp interop assemblies on first
launch):

- `<game>\MelonLoader\net6\MelonLoader.dll`
- `<game>\MelonLoader\net6\0Harmony.dll`
- `<game>\MelonLoader\net6\Il2CppInterop.Runtime.dll`
- `<game>\MelonLoader\net6\Il2CppInterop.Common.dll`
- `<game>\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll`
- `<game>\MelonLoader\Il2CppAssemblies\Il2Cppmscorlib.dll`
- `<game>\MelonLoader\Il2CppAssemblies\Il2CppSystem.dll`
- `<game>\MelonLoader\Il2CppAssemblies\Il2CppSystem.Core.dll`
- `<game>\MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll`
- .NET 6 runtime assemblies (e.g. `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.x\`):
  `System.Private.CoreLib.dll`, `System.Runtime.dll`, `System.Collections.dll`,
  `System.Linq.dll`, `System.Console.dll`, `System.Reflection.dll`,
  `System.Runtime.InteropServices.dll`, `System.Drawing.Primitives.dll`,
  `mscorlib.dll`, `netstandard.dll`

## Build with the Roslyn compiler shipped in any .NET SDK (6, 7 or 8)

PowerShell, from this folder:

```powershell
$game = "C:\Program Files (x86)\Steam\steamapps\common\Ultimate Admiral Dreadnoughts"
$net6 = (Get-ChildItem "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.*" | Select-Object -Last 1).FullName
$csc  = (Get-ChildItem "C:\Program Files\dotnet\sdk\*\Roslyn\bincore\csc.dll" | Select-Object -Last 1).FullName

$refs = @(
  "System.Private.CoreLib","System.Runtime","System.Collections","System.Linq","System.Console",
  "System.Reflection","System.Runtime.InteropServices","System.Drawing.Primitives","mscorlib","netstandard"
) | ForEach-Object { "-r:`"$net6\$_.dll`"" }
$refs += Get-ChildItem "$game\MelonLoader\net6\*.dll","$game\MelonLoader\Il2CppAssemblies\*.dll" | ForEach-Object { "-r:`"$($_.FullName)`"" }

dotnet $csc -nostdlib -noconfig -target:library -optimize+ -nullable:disable -langversion:latest `
  -out:DIP_GG_Fixes.dll @refs Mod.cs AssemblyInfo.cs
```

The output `DIP_GG_Fixes.dll` is byte-for-byte reproducible for the same compiler
version apart from the PE timestamp/MVID. Copy it to `<game>\Mods\`.

## Alternative: `DipGgFixes.csproj`

A minimal SDK-style project is included. Adjust the HintPath entries to your game
folder and run `dotnet build -c Release`. The project has no package references.

## What the code does (for reviewers)

Harmony patches on game methods, all guarded by try/catch and MelonPreferences
switches: turn-error guards (CheckMinorNationThreat, TryGenerateSmallerNationsBattle),
shared-design export (writes design files to the game's own shared designs folder via
the game's Storage API), AI build ratios, minor port capacity, balance-of-power cash
grants, diplomacy multipliers, stalled-construction watch, battle AI (torpedo charge,
fight when unable to escape, detach slow ships, focus fire, national doctrines read
from `Mods\battleDoctrines.csv`). File access: reads `Mods\battleDoctrines.csv` and `Mods\techBonuses.csv`,
writes nothing outside the game's own save/design folders. Network: one HTTPS GET of the repository's VERSION file at start for the update check (CheckForUpdates, can be switched off), and - only after the player clicks Download in the update message - one GET of the Mods_only.zip release asset, verified by SHA-256 before anything is written (UpdateDownloadButton). Nothing is sent.

License: Apache 2.0.
