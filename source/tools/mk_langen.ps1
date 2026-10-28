# Generates source\DIP_GG_Fixes\LangEn.cs (the built-in English dispatches) from payload\Mods\IronAdmiralLang\en.txt.
param([string]$Root = (Split-Path (Split-Path $PSScriptRoot)))
$u = New-Object Text.UTF8Encoding $false
$t = [IO.File]::ReadAllText("$Root\payload\Mods\IronAdmiralLang\en.txt", [Text.Encoding]::UTF8) -replace "`r`n", "`n"
$q = $t.Replace('"', '""')
$cs = "// generated from payload\Mods\IronAdmiralLang\en.txt by pm\mk_langen.ps1 - edit the text file, not this one`nnamespace DipGgFixes`n{`n    internal static class LangEn`n    {`n        internal const string Text = @`"" + $q + "`";`n    }`n}`n"
[IO.File]::WriteAllText("$Root\source\DIP_GG_Fixes\LangEn.cs", $cs, $u)
"LangEn.cs " + $cs.Length + " chars"
