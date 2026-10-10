$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    dotnet restore .\WindowsSystemGuardian.sln
    dotnet build .\WindowsSystemGuardian.sln -c Release --no-restore
    dotnet test .\WindowsSystemGuardian.sln -c Release --no-build
    & .\tools\xaml-binding-audit.ps1
    & .\tools\static-safety-audit.ps1
    & .\tools\qa-static-audit.ps1
}
finally {
    Pop-Location
}
