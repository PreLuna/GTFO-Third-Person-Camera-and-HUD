param([Parameter(Mandatory=$true)][string]$RuntimeRoot,[string]$BuildToolsRoot=(Join-Path (Split-Path $PSScriptRoot -Parent) 'GTFO-Mod-Build-and-Verification-Tools'))
$ErrorActionPreference='Stop'
& (Join-Path $BuildToolsRoot 'tools/Test-Recipes.ps1') -RepositoryRoot $PSScriptRoot -RuntimeRoot $RuntimeRoot

# This regression exercises the installed, managed BepInEx ConfigFile; no plugin Load or game API.
$manifestPath=Join-Path $PSScriptRoot 'out/build-manifest.json'
if(Test-Path -LiteralPath $manifestPath){
 $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
 $core=Join-Path $manifest.GameRoot 'BepInEx/core/BepInEx.Core.dll'
 $bin=Join-Path $PSScriptRoot 'out/tests'
 $sources=@('ThirdPerson/src/Control/ThirdPersonControlModels.cs','ThirdPerson/src/Control/ThirdPersonControlFileStore.cs','ThirdPerson/src/Control/ThirdPersonControlState.cs','ThirdPerson/src/Control/ThirdPersonControlApi.cs','IntegrationTests/Keybinds/KeybindAutomaticSaveTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
 $refs=@(Get-ChildItem -LiteralPath $RuntimeRoot -Filter '*.dll' | Where-Object {$_.Name -match '^(System\.|mscorlib\.|netstandard\.)' -and $_.Name -notmatch '\.Native\.'} | ForEach-Object FullName)
 $target=Join-Path $bin 'KeybindAutomaticSaveTests.dll'
 & (Join-Path $BuildToolsRoot 'tools/Compile.ps1') -Sources $sources -References ($refs+@($core)) -Output $target -Executable
 Copy-Item -LiteralPath $core -Destination $bin -Force
 Copy-Item -LiteralPath (Join-Path $manifest.GameRoot 'BepInEx/core/SemanticVersioning.dll') -Destination $bin -Force
 @{runtimeOptions=@{tfm='net6.0';framework=@{name='Microsoft.NETCore.App';version=(Split-Path $RuntimeRoot -Leaf)}}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $bin 'KeybindAutomaticSaveTests.runtimeconfig.json') -Encoding utf8
 & dotnet $target (Join-Path $bin 'KeybindAutomaticSaveTests.json') (Join-Path $bin 'keybind-auto-save-fixtures') | Tee-Object -FilePath (Join-Path $bin 'KeybindAutomaticSaveTests.log')
 if($LASTEXITCODE -ne 0){throw 'KeybindAutomaticSaveTests failed.'}
}else{
 Write-Output 'SKIP KeybindAutomaticSaveTests: build manifest required to locate installed BepInEx.Core.'
}
