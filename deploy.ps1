<#
.SYNOPSIS
    Build the SMA app and put it on the connected device, verifying it actually landed.

.DESCRIPTION
    Builds a self-contained APK and installs it, then makes the device confirm what it has.

    The project sets EmbedAssembliesIntoApk so the APK really is the whole application. Without
    that, an Android Debug build uses "fast deployment": the assemblies live outside the APK and
    are pushed separately, which means the APK stops changing when only C# changes (so it looks
    stale when it is not), and an APK installed by hand dies on launch with "No assemblies found".
    That combination cost hours once; the property and this script exist so it cannot recur.

    What this adds over a plain build:

      1. Deletes the packaged APK first, so there is no ambiguity about what got installed.
      2. Refuses to install a package older than the build that just ran.
      3. Reads lastUpdateTime back off the device, so "installed" means the device agrees.
      4. Watches logcat after launch and reports a crash instead of leaving you at the home screen.

.EXAMPLE
    ./deploy.ps1
    ./deploy.ps1 -SkipLaunch
#>

[CmdletBinding()]
param(
    [switch]$SkipLaunch,
    [string]$Package = "com.sma.shopper",
    [string]$Jdk = "$env:USERPROFILE\.jdks\jdk-21.0.12+8"
)

$ErrorActionPreference = "Stop"

$root    = Join-Path $PSScriptRoot "Retail25.Shopper"
$project = Join-Path $root "Retail25.Shopper.csproj"
$outDir  = Join-Path $root "bin\Debug\net10.0-android"
$adb     = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"

if (Test-Path $Jdk) { $env:JAVA_HOME = $Jdk }
$env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA "Android\Sdk"

# --- 1. device present? ------------------------------------------------------------------------
$devices = & $adb devices | Select-String -Pattern "\sdevice$"
if (-not $devices) {
    Write-Host "No device attached." -ForegroundColor Red
    Write-Host "If the C72 is on wi-fi debugging, find it with:  adb mdns services" -ForegroundColor DarkGray
    exit 1
}
Write-Host "device : $(($devices[0] -split '\s+')[0])" -ForegroundColor DarkGray

# --- 2. force a genuinely fresh package --------------------------------------------------------
Get-ChildItem $outDir -Filter *.apk -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem (Join-Path $root "obj\Debug\net10.0-android\android\bin") -Filter *.apk -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "building..." -ForegroundColor DarkGray
$before = Get-Date
dotnet build $project -f net10.0-android -t:SignAndroidPackage -v minimal --nologo |
    Select-String -Pattern "error|warning CS|Build succeeded|Build FAILED"

$apk = Get-ChildItem $outDir -Filter "*-Signed.apk" -ErrorAction SilentlyContinue |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $apk) {
    Write-Host "No APK produced." -ForegroundColor Red
    exit 1
}

# The check the plain build does not do. A package older than the moment we started building is a
# package MSBuild declined to rebuild, and installing it would ship stale code.
if ($apk.LastWriteTime -lt $before) {
    Write-Host "APK is stale ($($apk.LastWriteTime.ToString('MM-dd HH:mm:ss'))) - packaging was skipped." -ForegroundColor Red
    Write-Host "Clear obj\Debug\net10.0-android and try again." -ForegroundColor DarkGray
    exit 1
}

Write-Host ("apk    : {0}  {1:N1} MB  {2}" -f $apk.Name, ($apk.Length / 1MB), $apk.LastWriteTime.ToString("HH:mm:ss")) -ForegroundColor DarkGray

# --- 3. install, and make the device confirm it ------------------------------------------------
$installed = & $adb install -r $apk.FullName 2>&1
if ($installed -notmatch "Success") {
    Write-Host ($installed -join "`n") -ForegroundColor Red
    exit 1
}

$stamp = (& $adb shell "dumpsys package $Package | grep -m1 lastUpdateTime") -replace '.*=', ''
Write-Host "device confirms lastUpdateTime = $($stamp.Trim())" -ForegroundColor Green

if ($SkipLaunch) { return }

& $adb logcat -c | Out-Null
& $adb shell am force-stop $Package | Out-Null

# Resolved rather than hardcoded: the launcher activity carries a generated CRC in its name, and
# that name changes when the build regenerates it.
$activity = (& $adb shell "cmd package resolve-activity --brief $Package" |
             Where-Object { $_ -match "^$([regex]::Escape($Package))/" } |
             Select-Object -First 1).Trim()

& $adb shell am start -n $activity | Out-Null
Start-Sleep -Seconds 8

$crash = & $adb logcat -d | Select-String -Pattern "UNHANDLED EXCEPTION|FATAL EXCEPTION|Abort message" |
         Select-Object -First 3

if ($crash) {
    Write-Host "app crashed on launch:" -ForegroundColor Red
    $crash | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor DarkGray }
    exit 1
}

$top = & $adb shell "dumpsys activity activities | grep -m1 mResumedActivity"
if ($top -match [regex]::Escape($Package)) {
    Write-Host "launched and running." -ForegroundColor Green
} else {
    Write-Host "started but is not in the foreground - check the device." -ForegroundColor Yellow
}
