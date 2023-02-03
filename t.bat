copy /y D:\dev\temp\Power-Fx-Dataverse\src\PowerFx.Dataverse\bin\Debug\*.nupkg D:\dev\temp\Power-Fx-Dataverse\feed
copy /y D:\dev\temp\Power-Fx-Dataverse\src\PowerFx.Dataverse.Eval\bin\Debug\*.nupkg D:\dev\temp\Power-Fx-Dataverse\feed

copy /y D:\dev\temp\Power-Fx-Dataverse\src\PowerFx.Dataverse.Eval\bin\Debug\netstandard2.0\*.pdb D:\dev\temp\Power-Fx-Dataverse\feed

rmdir /S /Q C:\Users\jmstall\.nuget\packages\microsoft.powerfx.dataverse.eval\1.0.0
rmdir /S /Q C:\Users\jmstall\.nuget\packages\microsoft.powerfx.dataverse\1.0.0
