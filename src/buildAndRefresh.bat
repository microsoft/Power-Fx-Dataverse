pwsh.exe -executionpolicy bypass -file %~dp0refreshLocalNugetCache.ps1

copy /y %~dp0PowerFx.Dataverse\bin\Debug\*.nupkg %~dp0outputpackages
copy /y %~dp0PowerFx.Dataverse.Eval\bin\Debug\*.nupkg  %~dp0outputpackages
copy /y %~dp0Microsoft.PowerFx.Dataverse.Sql\bin\Debug\*.nupkg  %~dp0outputpackages
copy /y %~dp0Microsoft.PowerFx.AzureStorage\bin\Debug\*.nupkg  %~dp0outputpackages

@dir %~dp0outputpackages

@echo To consume these nugets locally:
@echo 1) Add this to your nuget.config:
@echo  ^<add key="LocalFxDataverse" value="%~dp0outputpackages" /^>
@echo .
@echo 2) Set your current PowerFx version to
@echo  1.99.0-local