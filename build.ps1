$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project src/tests/Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Protocol tests failed.' }
    dotnet publish src/FriendsChat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Get-FileHash artifacts/release/FriendsChat.exe -Algorithm SHA256 | ForEach-Object { $_.Hash + '  FriendsChat.exe' } | Set-Content artifacts/release/SHA256.txt
    Copy-Item docs/Testing-guide.md artifacts/release/Testing-guide.md
    Compress-Archive -Path artifacts/release/FriendsChat.exe,artifacts/release/SHA256.txt,artifacts/release/Testing-guide.md -DestinationPath artifacts/FriendsChat-Test-Download-Windows-x64.zip -Force
    Write-Host 'Ready: artifacts/FriendsChat-Test-Download-Windows-x64.zip'
} finally { Pop-Location }

