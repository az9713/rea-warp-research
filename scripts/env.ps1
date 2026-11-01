$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_MULTILEVEL_LOOKUP='0'
$env:DOTNET_NOLOGO='true'
$env:PYTHONDONTWRITEBYTECODE='1'
$env:PYTHONUTF8='1'
$env:PYTHONIOENCODING='utf-8'
foreach ($Pair in @(@('DOTNET_CLI_HOME','dotnet-home'),@('NUGET_PACKAGES','nuget'),@('NUGET_HTTP_CACHE_PATH','nuget-http'),@('NUGET_PLUGINS_CACHE_PATH','nuget-plugins'),@('NUGET_SCRATCH','nuget-scratch'),@('TEMP','tmp'),@('TMP','tmp'),@('TMPDIR','tmp'),@('PYTHONPYCACHEPREFIX','pycache'))) {
    $Value=Join-Path $ProjectRoot ('.runtime/'+$Pair[1])
    [Environment]::SetEnvironmentVariable($Pair[0],$Value,'Process')
    New-Item -ItemType Directory -Force -Path $Value | Out-Null
}
$SiblingSdk=Join-Path (Split-Path -Parent $ProjectRoot) 'sharex-receipts/.tools/dotnet/dotnet.exe'
$Dotnet=if($env:DOTNET_EXE){$env:DOTNET_EXE}elseif(Test-Path -LiteralPath $SiblingSdk){$SiblingSdk}else{(Get-Command dotnet -ErrorAction Stop).Source}
$env:DOTNET_ROOT=Split-Path -Parent $Dotnet
Set-Location -LiteralPath $ProjectRoot
