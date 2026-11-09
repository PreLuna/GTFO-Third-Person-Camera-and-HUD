param(
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [Parameter(Mandatory=$true)][string]$HudAssembly,
 [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Join-Path $GameRoot 'BepInEx/interop'))
$resolver.AddSearchDirectory((Join-Path $GameRoot 'BepInEx/core'))
$resolver.AddSearchDirectory((Split-Path ([IO.Path]::GetFullPath($HudAssembly)) -Parent))
$parameters=[Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver=$resolver
$nativePath=Join-Path $GameRoot 'BepInEx/interop/UnityEngine.TextRenderingModule.dll'
$native=[Mono.Cecil.ModuleDefinition]::ReadModule($nativePath,$parameters)
$hud=[Mono.Cecil.ModuleDefinition]::ReadModule([IO.Path]::GetFullPath($HudAssembly),$parameters)
try {
 $font=$native.Types | Where-Object FullName -eq 'UnityEngine.Font'
 $single=$font.Methods | Where-Object {$_.Name -eq 'CreateDynamicFontFromOSFont' -and $_.Parameters[0].ParameterType.FullName -eq 'System.String'}
 $array=$font.Methods | Where-Object {$_.Name -eq 'CreateDynamicFontFromOSFont' -and $_.Parameters[0].ParameterType.Name -eq 'Il2CppStringArray'}
 $badArray=@($single.Body.Instructions | Where-Object {"$($_.Operand)" -match 'Il2CppReferenceArray.*System.String'}).Count -gt 0
 $missing=@($array.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'newobj'} | Where-Object {$reference=$_.Operand;!@($font.Methods | Where-Object {$_.FullName -ceq $reference.FullName}).Count}).Count -gt 0
 $prepare=($hud.Types | Where-Object FullName -eq 'PreLuna.ThirdPersonHUD.Runtime.HolographicHud').Methods | Where-Object Name -eq 'PrepareFont'
 $calls=@($prepare.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
 $usesFactory=@($calls | Where-Object {$_.DeclaringType.FullName -eq 'UnityEngine.Font' -and $_.Name -eq 'CreateDynamicFontFromOSFont'}).Count -gt 0
 $usesNative=@($calls | Where-Object {$_.DeclaringType.FullName -eq 'UnityEngine.Font' -and $_.Name -eq 'Internal_CreateDynamicFont'}).Count -eq 1
 $usesPointer=@($calls | Where-Object {$_.DeclaringType.FullName -eq 'UnityEngine.Font' -and $_.Name -eq '.ctor' -and $_.Parameters[0].ParameterType.FullName -eq 'System.IntPtr'}).Count -eq 1
 $resolved=@(foreach($call in ($calls | Where-Object {$_.DeclaringType.FullName -eq 'UnityEngine.Font'})){$definition=$call.Resolve();if(!$definition){throw ('Unresolved font call: '+$call.FullName)};$definition.FullName})
 $icall=($font.Methods | Where-Object Name -eq 'Internal_CreateDynamicFont')
 if(!$icall.IsPublic -or @($icall.Body.Instructions | Where-Object {"$($_.Operand)" -match 'Font::.ctor|CreateDynamicFontFromOSFont|Il2CppReferenceArray'}).Count){throw 'Native font wrapper has unsafe transitive calls'}
 $result=[ordered]@{Passed=$badArray -and $missing -and !$usesFactory -and $usesNative -and $usesPointer;ActualSingleFactoryInvalidArrayConfirmed=$badArray;ActualArrayFactoryMissingConstructorConfirmed=$missing;BuiltHudAvoidsBothFactories=!$usesFactory;BuiltHudUsesNativeDynamicCreation=$usesNative;BuiltHudUsesPointerWrapper=$usesPointer;ResolvedFontCalls=$resolved;NativeFontCreationIL=@($icall.Body.Instructions | ForEach-Object ToString);PrepareFontIL=@($prepare.Body.Instructions | ForEach-Object ToString);InteropSHA256=(Get-FileHash -LiteralPath $nativePath).Hash;HudSHA256=(Get-FileHash -LiteralPath $HudAssembly).Hash;GameFilesWritten=$false;GameplayTested=$false}
 $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
 if(!$result.Passed){throw 'Actual font interop verification failed'}
 'PASS: both actual factory defects detected; all new font calls resolve without the missing constructor.'
} finally {$hud.Dispose();$native.Dispose();$resolver.Dispose()}
