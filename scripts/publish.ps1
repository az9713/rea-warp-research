. (Join-Path $PSScriptRoot 'env.ps1')
foreach($Project in @('src/Desktop/Notebook.Desktop.csproj','src/Cli/Notebook.Cli.csproj')) {
    & $Dotnet restore $Project --runtime win-x64 --configfile NuGet.Config
    if($LASTEXITCODE){throw 'Restore failed'}
    $Destination=if($Project -like '*Desktop*'){'.runtime/publish'}else{'.runtime/publish-cli'}
    & $Dotnet publish $Project --no-restore --runtime win-x64 --self-contained true -c Release -o $Destination
    if($LASTEXITCODE){throw 'Publish failed'}
}
foreach($Pair in @(@('publish-cli','ResearchNotebook.Cli'),@('fixture','NotebookFixture'))) {
    foreach($Suffix in @('.exe','.dll','.deps.json','.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $ProjectRoot ('.runtime/'+$Pair[0]+'/'+$Pair[1]+$Suffix)) -Destination (Join-Path $ProjectRoot ('.runtime/publish/'+$Pair[1]+$Suffix))
    }
}
python scripts/record_publish.py
if($LASTEXITCODE){throw 'Runtime allowlist recording failed'}
