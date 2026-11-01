. (Join-Path $PSScriptRoot 'env.ps1')
foreach($Command in @(
 @('restore','tests/Fixture.csproj','--runtime','win-x64','--configfile','NuGet.Config'),
 @('publish','tests/Fixture.csproj','--no-restore','--runtime','win-x64','--self-contained','true','-c','Release','-o','.runtime/fixture'),
 @('restore','tests/Notebook.Tests.csproj','--configfile','NuGet.Config'),
 @('run','--project','tests/Notebook.Tests.csproj','--no-restore','--','.runtime/fixture','artifacts/core-tests.json')
)) {
    New-Item -ItemType Directory -Force -Path artifacts | Out-Null
    & $Dotnet @Command
    if($LASTEXITCODE){throw ('Failed: dotnet '+($Command -join ' '))}
}
