cd ReleaseDir
mkdir KSPLocalizer
del KSPLocalizer\*.*
del KSPLocalizer\KSPLocalizer.zip
del KSPLocalizer.zip

copy * KSPLocalizer

zip -9r KSPLocalizer.zip KSPLocalizer

pause
