param(
    [string]$Game = "C:\Program Files (x86)\Steam\steamapps\common\Cuphead"
)

$managed = "$Game\Cuphead_Data\Managed"
$core = "$Game\BepInEx\core"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = "$PSScriptRoot\bin\NoFlashing.dll"

New-Item -ItemType Directory -Force "$PSScriptRoot\bin" | Out-Null

$refs = @(Get-ChildItem "$managed\*.dll" | ForEach-Object { "/r:$($_.FullName)" })
$refs += "/r:$core\BepInEx.dll"
$refs += "/r:$core\0Harmony.dll"

& $csc /nologo /noconfig /nostdlib+ /target:library /optimize+ /out:$out @refs "$PSScriptRoot\NoFlashing.cs"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item $out "$Game\BepInEx\plugins\NoFlashing.dll" -Force
