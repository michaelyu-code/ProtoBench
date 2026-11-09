$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build src/NonStopToolkit.Desktop -p:Platform=x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $executable = Join-Path $PSScriptRoot 'src/NonStopToolkit.Desktop/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/ProtoBench.exe'
    Start-Process -FilePath $executable
}
finally { Pop-Location }
