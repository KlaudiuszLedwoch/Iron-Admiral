#!/bin/bash
# Build DIP_GG_Fixes.dll with csc against net6 reference set in /tmp/claude-0/il2 (game runtime) + MelonLoader/Il2Cpp assemblies.
set -e
cd /tmp/claude-0/fixmod
OUT=${1:-out11}; mkdir -p $OUT
# AssemblyInfo version follows MelonInfo in Mod.cs
VER=$(grep -oP 'MelonInfo\(typeof\(DipGgFixes.DipGgFixesMod\), "DIP GG Fixes", "\K[0-9.]+' Mod.cs)
cat > AssemblyInfo.cs <<EOA
[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v6.0", FrameworkDisplayName = ".NET 6.0")]
[assembly: System.Reflection.AssemblyVersion("$VER.0")]
[assembly: System.Reflection.AssemblyFileVersion("$VER.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$VER")]
[assembly: System.Reflection.AssemblyTitle("DIP GG Fixes")]
EOA
echo "version $VER"
R=/tmp/claude-0/il2
ML='/mnt/user-data/uploads/Ultimate Admiral Dreadnoughts/MelonLoader'
dotnet /usr/lib/dotnet/sdk/8.0.131/Roslyn/bincore/csc.dll -nologo -noconfig -nostdlib+ -target:library -optimize+ -deterministic -langversion:latest -nullable:disable \
  -out:$OUT/DIP_GG_Fixes.dll \
  -r:$R/System.Private.CoreLib.dll -r:$R/System.Runtime.dll -r:$R/System.Collections.dll -r:$R/System.Linq.dll -r:$R/System.Console.dll -r:$R/System.Runtime.InteropServices.dll -r:$R/System.Reflection.dll -r:$R/netstandard.dll -r:$R/System.Net.Http.dll -r:$R/System.Net.Primitives.dll -r:$R/System.Threading.dll -r:$R/System.Threading.Tasks.dll -r:$R/System.Threading.Thread.dll -r:$R/System.Private.Uri.dll -r:$R/System.IO.Compression.dll -r:$R/System.IO.Compression.ZipFile.dll -r:$R/System.Security.Cryptography.Algorithms.dll -r:$R/System.Security.Cryptography.Primitives.dll \
  -r:"$ML/net6/MelonLoader.dll" -r:"$ML/net6/0Harmony.dll" -r:"$ML/net6/Il2CppInterop.Runtime.dll" -r:"$ML/net6/Il2CppInterop.Common.dll" \
  -r:"$ML/Il2CppAssemblies/Assembly-CSharp.dll" -r:"$ML/Il2CppAssemblies/Il2Cppmscorlib.dll" -r:"$ML/Il2CppAssemblies/Il2CppSystem.dll" -r:"$ML/Il2CppAssemblies/Il2CppSystem.Core.dll" -r:"$ML/Il2CppAssemblies/UnityEngine.CoreModule.dll" -r:"$ML/Il2CppAssemblies/UnityEngine.IMGUIModule.dll" -r:"$ML/Il2CppAssemblies/UnityEngine.InputLegacyModule.dll" -r:"$ML/Il2CppAssemblies/UnityEngine.TextRenderingModule.dll" -r:"$ML/Il2CppAssemblies/Unity.TextMeshPro.dll" -r:"$ML/Il2CppAssemblies/UnityEngine.UI.dll" \
  Mod.cs Economy.cs EconomyUi.cs SettingsUi.cs AssemblyInfo.cs
ls -la $OUT/DIP_GG_Fixes.dll
