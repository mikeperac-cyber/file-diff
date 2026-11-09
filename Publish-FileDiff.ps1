# Builds downloadable .exe outputs (fresh Light + Standalone + zips).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

dotnet publish "$root/src/FileDiff.App" -c Release -r win-x64 --self-contained false -o "$root/publish/FileDiff"
dotnet publish "$root/src/FileDiff.App" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$root/publish/FileDiff-standalone"

Compress-Archive -Path "$root/publish/FileDiff-standalone/FileDiff.App.exe" -DestinationPath "$root/publish/FileDiff-standalone.zip" -Force
Compress-Archive -Path "$root/publish/FileDiff/*" -DestinationPath "$root/publish/FileDiff-light.zip" -Force

Get-ChildItem "$root/publish/FileDiff/FileDiff.App.exe", "$root/publish/FileDiff-standalone/FileDiff.App.exe", "$root/publish/*.zip" | Select-Object FullName, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}, LastWriteTime
