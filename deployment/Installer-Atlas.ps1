param([switch]$Elevated)

$ErrorActionPreference = 'Stop'
$installRoot = 'C:\Ideo_Atlas'
$applicationRoot = Join-Path $installRoot 'Application'
$payloadRoot = Join-Path $PSScriptRoot 'Application'
$programDataRoot = Join-Path $env:ProgramData 'Ideo Solutions\Atlas'

if (-not $Elevated) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Elevated')
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path (Join-Path $payloadRoot 'Atlas.exe'))) {
    throw 'Le paquet d’installation Atlas est incomplet.'
}

New-Item -ItemType Directory -Force -Path $applicationRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $installRoot 'Configuration') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $installRoot 'Cache') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $installRoot 'Logs') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $installRoot 'MisesAJour') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $installRoot 'Donnees') | Out-Null
New-Item -ItemType Directory -Force -Path $programDataRoot | Out-Null

Copy-Item (Join-Path $payloadRoot '*') $applicationRoot -Recurse -Force

foreach ($writable in @('Configuration', 'Cache', 'Logs', 'MisesAJour', 'Donnees')) {
    & icacls (Join-Path $installRoot $writable) /grant '*S-1-5-32-545:(OI)(CI)M' /T /C | Out-Null
}
& icacls $programDataRoot /grant '*S-1-5-32-545:(OI)(CI)M' /T /C | Out-Null

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Biblidéo Atlas.lnk'))
$shortcut.TargetPath = Join-Path $applicationRoot 'Atlas.exe'
$shortcut.WorkingDirectory = $applicationRoot
$shortcut.IconLocation = (Join-Path $applicationRoot 'Atlas.exe') + ',0'
$shortcut.Save()

Start-Process (Join-Path $applicationRoot 'Atlas.exe')
