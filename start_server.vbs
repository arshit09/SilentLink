Set FSO = CreateObject("Scripting.FileSystemObject")
ExePath = FSO.BuildPath(FSO.GetParentFolderName(WScript.ScriptFullName), "SilentLink.exe")

Set WshShell = CreateObject("WScript.Shell")
' Use %USERPROFILE% as CWD so the project folder is never locked by this process
WshShell.CurrentDirectory = WshShell.ExpandEnvironmentStrings("%USERPROFILE%")
WshShell.Run """" & ExePath & """", 0, False
Set WshShell = Nothing
Set FSO = Nothing
