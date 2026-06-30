Set shell = CreateObject("WScript.Shell")
appHome = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName)
cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File " & Chr(34) & appHome & "\Phone Audio Recorder.ps1" & Chr(34)
shell.Run cmd, 0, False
