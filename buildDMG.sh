rm -f macos-dmg/QuestPatcher.dmg # Remove old DMG

echo Building with .NET core . . .
dotnet publish -c Release -r osx-x64

echo Creating applications symlink . . .
ln -sfn "/Applications" "macos-dmg/dmg-root/Applications"

echo Copying built files to app directory . . .
mkdir -p macos-dmg/dmg-root/QuestPatcher.app/Contents/MacOS
cp -r QuestPatcher/bin/Release/net5.0/osx-x64/publish/* macos-dmg/dmg-root/QuestPatcher.app/Contents/MacOS/

echo Adding LICENSE . . .
cp LICENSE macos-dmg/dmg-root/LICENSE

echo Building DMG file . . .
hdiutil info | grep /dev/disk | grep partition | cut -f 1 | xargs hdiutil detach # Fixes attaching error
hdiutil create -srcfolder macos-dmg/dmg-root /tmp/QuestPatcher.dmg -volname QuestPatcher

echo Removing built files . . .
rm -r macos-dmg/dmg-root/QuestPatcher.app/Contents/MacOS/*

echo Compressing DMG file . . .
hdiutil convert /tmp/QuestPatcher.dmg -format UDZO -o macos-dmg/QuestPatcher.dmg
rm /tmp/QuestPatcher.dmg

echo Done!
