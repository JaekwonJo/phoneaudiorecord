$ErrorActionPreference = "Stop"

$appHome = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:PHONE_AUDIO_RECORDER_HOME = $appHome

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $appHome "PhoneAudioRecorder.dll")

[PhoneAudioRecorder.MainForm]::Main()
