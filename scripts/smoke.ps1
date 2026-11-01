. (Join-Path $PSScriptRoot 'env.ps1')
$Exe=Join-Path $ProjectRoot '.runtime/publish/ResearchNotebook.Cli.exe'
$Out=Join-Path $ProjectRoot ('.runtime/demo-'+[Guid]::NewGuid().ToString('N'))
& $Exe demo (Join-Path $ProjectRoot '.runtime/publish') $Out
if($LASTEXITCODE){throw 'Native demo failed'}
foreach($Factor in @(2,3)) {
    & $Exe verify (Join-Path $Out ('plan-'+$Factor+'.json')) (Join-Path $Out ('receipt-'+$Factor+'.json'))
    if($LASTEXITCODE){throw 'Native evidence validation failed'}
}
$Shot=Join-Path $ProjectRoot 'docs/screenshots/native-before-after.png'
$Ui=Start-Process -FilePath (Join-Path $ProjectRoot '.runtime/publish/ResearchNotebook.exe') -ArgumentList @('--ui-self-test',('"'+$Shot+'"')) -WorkingDirectory (Join-Path $ProjectRoot '.runtime/publish') -WindowStyle Hidden -Wait -PassThru
if($Ui.ExitCode){throw 'Real native UI self-test failed'}
