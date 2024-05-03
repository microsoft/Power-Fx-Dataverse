$localCache = $env:NUGET_PACKAGES
if (! $localCache) {
 $localCache = $Env:USERPROFILE + "\.nuget\packages"
}

Write-Host "Deleting Microsoft.PowerFx.Dataverse*\1.0.0 from nuget cache. This will cause the newly built packages to be downloaded."

Get-ChildItem $localCache Microsoft.PowerFx.Dataverse.* | ForEach-Object {
    $path = ($_.FullName + "\1.0.0")

    if (Test-Path $path) {
      Remove-Item $path -Recurse
    }
}