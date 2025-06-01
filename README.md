# KSP Localization Script

This tool scans `.cs` and `.cfg` files for localizable strings and generates:

- `Localization/en-us.cfg` – for use in KSP
- `Localization/en-us.csv` – for easy editing in spreadsheet tools

It updates the source files with the localization tag.
Original files are backed up to <file>.bak file for easy reverts

It was created with the assistance of ChatGPT 

---

## Usage

KSPLocalizationScript.exe  <PROJECTDIR> --prefix="<PREFIX>" --outdir="<OUTPUT_DIRECTORY>"

The <PROJECTDIR> is the directory of the project being localized.

The <OUTPUT_DIRECTORY> should be formatted using forward slashes for safety.  If on Linux or MacOS, it is required to use forward slashes.
Surround the <OUTPUT_DIRECTORY> with double quotes if there are any spaces in the path

To ignore a section of CSharp code, surround the section to be ignored with a region as follows:

#region NO_LOCALIZATION
.....
#endregion


### Options

| Option                      | Description                                                                  |
|-----------------------------|------------------------------------------------------------------------------|
| --outdir=<path>             | Output path for `en-us.cfg` and en-us.csv files                              |
| --prefix=<string>           | Localization key prefix (default: `MyMod_`)                                  |
| --maxkeylength=<number>     | Maximum length for localization keys (default: 25)                           |
| --numerictags               | Use a sequential number for the tags                                         |
| --separatePartsCfg          | Create a file for the part tags and one file for the code tags               |
| --inifile=<file>            | specify a ini file which contains include and exclude strings (example below)|
| --csonly                    | Only process .cs files                                                       |
| --cfgonly                   | Only process .cfg files                                                      |
| --revert                    | Restores the original files from the .bak files                              |
| --cleanbak                  | Deletes all the .bak files                                                   |
| --help                      | Display help                                                                 |


## INI file

An ini file is included which contains a few common strings to be ignored. It can also exclude and/or 
include specific files.  The strings can be either simple strings or regular expressions.  Both strings 
and files can be excluded, only files can be included.  Full documentation for the file is at the head
of the file.  The ini file included can be replaced by specifying the --initfile=<file> to use your
own ini file


## --revert and --cleanbak

These two options override all others.  In other words, if either of them is on the command line in 
addition to any other options, the revert/cleanbak functions will be run instead


## Example

Using the L-Tech mod as an example, the following will replace all strings with appropriate localization 
tags and put the resulting files into the GameData/LTech/Localization directory

	KSPLocalizationScript.exe   L-Tech --prefix=LTech_LOC --outdir=L-Tech/GameData/LTech/Localization --numerictags 


This will:
- Scan files
- Generate localization entries
- Write modified copies of `.cs` and `.cfg` files
- Output `Localization/en-us.cfg` and `en-us.csv`


## Note

While pre-existing localized tags are not treated as strings, there is no mechanism to check 
for duplicates with any pre-existing tags.  If the mod does have some pre-existing tags, the
safest thing to do is just use a different prefix.

Pre-existing localization files may be overwritten if the names are the same

Another quirk is the way KSP deals with Event strings.  While normally code needs to call Localizer.Format
to get the localized string, there is no need to do this for events since the event code itself will be 
calling the localizer.  It's almost impossible to detect this, so this edge case is not dealt with.  
Other than a tiny bit of performance loss due to the double call of Localizer.Format, there should be no 
change in functionality.
This is something the user will need to look at during the conversion process

When running this on a C-Sharp code-based mod, there is no way to determine if a string is meant for 
internal use or not.  This is where the --revert option comes in handy.  Simply add the --revert to the 
existing line and run to undo all the changes.  There are two tools available to fix this:

	1.  Use the #region NO_LOCALIZATION  / #endregion to mark sections of code which should not be localized
	2.  Use the --ignorelistfile=ignorelist.txt, and add the strings which should be ignored to the file
