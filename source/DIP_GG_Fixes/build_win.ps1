# Windows build of DIP_GG_Fixes.dll (same compiler call as buildkit/build.sh), uses the installed .NET SDK's Roslyn.
# Usage: build_win.ps1 -Work <folder with extracted buildkit> -Src <folder with *.cs> -Out <output folder>
param([string]$Work, [string]$Src, [string]$Out)
$ErrorActionPreference = 'Stop'
$K = Join-Path $Work 'buildkit'; $S = Join-Path $K 'src'
# remove stale sources of an earlier build (a file deleted from the source folder must not stay in the build)
Get-ChildItem $S -Filter '*.cs' -File -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item (Join-Path $Src '*.cs') $S -Force
$mod = Get-Content (Join-Path $S 'Mod.cs') -Raw
$ver = [regex]::Match($mod, 'MelonInfo\(typeof\(DipGgFixes.DipGgFixesMod\), "DIP GG Fixes", "([0-9.]+)"').Groups[1].Value
@"
[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v6.0", FrameworkDisplayName = ".NET 6.0")]
[assembly: System.Reflection.AssemblyVersion("$ver.0")]
[assembly: System.Reflection.AssemblyFileVersion("$ver.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$ver")]
[assembly: System.Reflection.AssemblyTitle("DIP GG Fixes")]
"@ | Set-Content (Join-Path $S 'AssemblyInfo.cs') -Encoding UTF8
"version $ver"
$csc = Get-ChildItem 'C:\Program Files\dotnet\sdk\*\Roslyn\bincore\csc.dll' | Sort-Object FullName -Descending | Select-Object -First 1
$R = Join-Path $K 'ref\net'; $ML = Join-Path $K 'ref\ml'
New-Item -ItemType Directory -Force $Out | Out-Null
$net = 'System.Private.CoreLib','System.Runtime','System.Collections','System.Linq','System.Console','System.Runtime.InteropServices','System.Reflection','netstandard','System.Net.Http','System.Net.Primitives','System.Threading','System.Threading.Tasks','System.Threading.Thread','System.Private.Uri','System.IO.Compression','System.IO.Compression.ZipFile','System.Security.Cryptography.Algorithms','System.Security.Cryptography.Primitives'
$mls = 'MelonLoader','0Harmony','Il2CppInterop.Runtime','Il2CppInterop.Common','Assembly-CSharp','Il2Cppmscorlib','Il2CppSystem','Il2CppSystem.Core','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.InputLegacyModule','UnityEngine.TextRenderingModule','Unity.TextMeshPro','UnityEngine.UI','UnityEngine.UIModule'
$files = @(Get-ChildItem (Join-Path $Src '*.cs') | ForEach-Object { $_.Name } | Where-Object { $_ -ne 'AssemblyInfo.cs' }) + 'AssemblyInfo.cs'  # every source file in -Src
$a = @('-nologo','-noconfig','-nostdlib+','-target:library','-optimize+','-deterministic','-langversion:latest','-nullable:disable',"-out:$(Join-Path $Out 'DIP_GG_Fixes.dll')")
$a += $net | ForEach-Object { "-r:$(Join-Path $R "$_.dll")" }
$a += $mls | ForEach-Object { "-r:$(Join-Path $ML "$_.dll")" }
$a += $files | ForEach-Object { Join-Path $S $_ }
& dotnet $csc.FullName @a
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
Get-Item (Join-Path $Out 'DIP_GG_Fixes.dll') | Select-Object FullName, Length, LastWriteTime
