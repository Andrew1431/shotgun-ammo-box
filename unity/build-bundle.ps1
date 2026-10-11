# Builds the asset bundle headless with Unity 2022.3.9f1 (LC's editor line). Close the project in the editor first.
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.9f1\Editor\Unity.exe'
$project = Join-Path $PSScriptRoot 'ShotgunAmmoBoxAssets'
& $unity -batchmode -quit -projectPath $project -executeMethod BuildBox.Build -logFile (Join-Path $project 'build.log') | Out-Null
Select-String -Path (Join-Path $project 'build.log') -Pattern '\[BuildBox\]|error' | ForEach-Object { $_.Line }
exit $LASTEXITCODE
