Write-Output "Building with dotnet core . . ."
dotnet publish -c Release -r win-x64

Write-Output "Extracting installer files . . ."
Remove-Item "installer/inno" -Recurse -ErrorAction Ignore
Expand-Archive -Path "installer\innoSetupCLI.zip" -DestinationPath "installer/inno/"

Write-Output "Building installer . . ."
./installer/inno/ISCC.exe /f installer/installer.iss

Write-Output "Done!"