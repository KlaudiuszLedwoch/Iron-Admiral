param([string]$Path)
# Release check: every 'param' cell in parts.csv must have balanced, non-nested brackets.
# Returns the number of bad cells (0 = OK) and prints them. Same rule as check_parts_param.py.
$rows = Import-Csv -LiteralPath $Path -Encoding UTF8
$bad = 0; $line = 1
foreach ($r in $rows) {
    $line++
    $n = $r.'@name'; $p = $r.param
    if (-not $n -or $n.StartsWith('#') -or -not $p) { continue }
    $d = 0; $ok = $true
    foreach ($ch in $p.ToCharArray()) {
        if ($ch -eq '(') { $d++ } elseif ($ch -eq ')') { $d-- }
        if ($d -lt 0 -or $d -gt 1) { $ok = $false }
    }
    if ($d -ne 0) { $ok = $false }
    if (-not $ok) { $bad++; Write-Host "parts.csv row $line ${n}: $p" }
}
Write-Host "$bad param cell(s) with unbalanced or nested brackets"
return $bad
