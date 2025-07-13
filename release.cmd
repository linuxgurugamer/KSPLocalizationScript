echo on

cd ReleaseDir
mkdir KSPLocalizer
del KSPLocalizer\*.*
del KSPLocalizer\KSPLocalizer.zip
del KSPLocalizer.zip

copy * KSPLocalizer
"d:\Program Files\7-Zip"\7z.exe a KSPLocalizer.zip KSPLocalizer

pause
