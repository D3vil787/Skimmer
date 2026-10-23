@echo off
rem Builds the portable single-file Skimmer.exe into .\out
dotnet publish Skimmer.csproj -c Release -p:PublishSingleFile=true -o out
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo.
echo Done: out\Skimmer.exe
