<#
.SYNOPSIS
    Read-only, timestamped log of the AirPods' connection state as Windows sees it, with
    one-key markers for what you hear. For the Stage 1 trials.

.DESCRIPTION
    Every few seconds it records:
      - btLink        whether Windows holds a Bluetooth link: "yes" unless the documented
                      DN_DEVICE_DISCONNECTED bit is set in the device node's DevNodeStatus
      - btFlag15      an undocumented "connected" flag many scripts read ({83DA6326-...} 15),
                      logged raw (True/False) because sources disagree on its polarity
      - renderState   state of the AirPods playback endpoint (active / unplugged / ...)
      - captureState  state of the AirPods microphone endpoint
      - renderDefault whether the AirPods are Windows' default playback device
      - renderPeak    the level Windows is sending to them (0 = silence)
    and prints a line whenever one of those changes. Windows can report "connected" while
    the sound actually plays on the iPhone, so press a key to note what you hear:

      L  sound is on the laptop        P  sound is on the iPhone (phone)
      N  no sound anywhere             G  unwanted grab just happened
      S  start of a trial (asks for a label, for example "A S3 #2")
      T  type a free note              Q  quit

    It only reads. It sends nothing to the AirPods and changes no setting, driver or
    registry value. It needs DualConnect.exe (run Build.cmd once) for the audio endpoint
    readings.

    UNTESTED: written on 8 and 9 Oct 2026 without a Windows machine. Never run against real AirPods.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\AirPods-Log.ps1
#>
param(
    [string]$Name = 'AirPods',
    [ValidateRange(0.5, 60)][double]$IntervalSeconds = 2,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $here 'DualConnect.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "DualConnect.exe is missing. Run Build.cmd in this folder first." -ForegroundColor Yellow
    exit 1
}
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') {
    Write-Host ("PowerShell is in {0} mode on this PC (a security policy), so this logger cannot load DualConnect.exe. Leave the policy as it is and tell Claude in the project thread." -f $ExecutionContext.SessionState.LanguageMode) -ForegroundColor Yellow
    exit 1
}
try { [void][Console]::KeyAvailable } catch {
    Write-Host "Run this from a PowerShell or Windows Terminal window with the command in the README, not from PowerShell ISE." -ForegroundColor Yellow
    exit 1
}
# Add-Type -Path accepts only .dll and source files. Loading the bytes also leaves the exe
# unlocked, so Build.cmd can still rebuild it while the logger runs.
try {
    [void][System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exe))
} catch {
    Write-Host ("Could not load DualConnect.exe: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
    exit 1
}

if (-not $OutFile) {
    $OutFile = Join-Path $here ('airpods-log-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.csv')
}

# Documented: DEVPKEY_Device_DevNodeStatus holds the DN_* flags from cfg.h, and
# DN_DEVICE_DISCONNECTED (0x02000000) means the driver reports the device as not connected.
$statusKey = 'DEVPKEY_Device_DevNodeStatus'
$DN_DEVICE_DISCONNECTED = 0x02000000
# Undocumented: a key many scripts read as "is connected". Logged raw for comparison.
$flag15Key = '{83DA6326-97A6-4088-9453-A1923F573B29} 15'

# The AirPods' classic Bluetooth device node: BTHENUM\DEV_<address>\...
$nodes = @(Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue |
    Where-Object { $_.FriendlyName -like "*$Name*" -and $_.InstanceId -like 'BTHENUM\DEV_*' })
if ($nodes.Count -eq 0) {
    Write-Host "No paired Bluetooth device named like '$Name' was found. btLink and btFlag15 will be blank." -ForegroundColor Yellow
} else {
    foreach ($n in $nodes) { Write-Host ("Watching device node: {0}  [{1}]" -f $n.FriendlyName, $n.InstanceId) }
}

function Get-BtState {
    if ($nodes.Count -eq 0) { return @('', '') }
    $link = @(); $flag = @()
    foreach ($n in $nodes) {
        try {
            $st = Get-PnpDeviceProperty -InstanceId $n.InstanceId -KeyName $statusKey -ErrorAction Stop
            if ($null -eq $st.Data) { $link += 'unknown' }
            elseif (([uint32]$st.Data -band $DN_DEVICE_DISCONNECTED) -ne 0) { $link += 'no' }
            else { $link += 'yes' }
        } catch { $link += 'unknown' }
        try {
            $f = Get-PnpDeviceProperty -InstanceId $n.InstanceId -KeyName $flag15Key -ErrorAction Stop
            if ($null -eq $f.Data) { $flag += 'none' } else { $flag += [string]$f.Data }
        } catch { $flag += 'none' }
    }
    return @(($link -join '/'), ($flag -join '/'))
}

function Get-Sample {
    # Observe matches by name only and skips the device-topology walk, the most passive read.
    # It sums up all of the AirPods' playback endpoints (stereo and hands-free): the state is
    # the most connected one, the level the highest one.
    $note = ''
    try {
        $o = [DualConnect.Core]::Observe($Name)
        $render = $o.RenderState; $capture = $o.CaptureState; $default = $o.RenderDefault; $peak = $o.RenderPeak
        if ($o.Devices -gt 1) { $note = "$($o.Devices) devices match '$Name'" }
    } catch {
        $render = 'error'; $capture = 'error'; $default = ''; $peak = ''
        $note = 'audio read failed: ' + $_.Exception.Message
    }
    $bt = Get-BtState
    [pscustomobject]@{
        time          = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss.fffzzz', [Globalization.CultureInfo]::InvariantCulture)
        btLink        = $bt[0]
        btFlag15      = $bt[1]
        renderState   = $render
        captureState  = $capture
        renderDefault = $default
        renderPeak    = $peak
        marker        = ''
        note          = $note
    }
}

# Rows that could not be written yet, for example while the CSV is open in Excel (which locks it).
$pending = New-Object System.Collections.ArrayList
$warnedLocked = $false

function Write-Row($row) {
    $line = $row | ConvertTo-Csv -NoTypeInformation | Select-Object -Skip 1
    [void]$pending.Add($line)
    $null = Save-Pending
}

function Save-Pending {
    if ($pending.Count -eq 0) { return $true }
    try {
        [System.IO.File]::AppendAllText($OutFile, (($pending -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
        $pending.Clear()
        $script:warnedLocked = $false
        return $true
    } catch {
        if (-not $script:warnedLocked) {
            Write-Host "The log file is open in another program. Rows are kept and written once it is closed." -ForegroundColor Yellow
            $script:warnedLocked = $true
        }
        return $false
    }
}

function Get-Summary($s) {
    $sound = if ($s.renderPeak -ne '' -and [double]::Parse($s.renderPeak, [Globalization.CultureInfo]::InvariantCulture) -gt 0.01) { 'sending sound' } else { 'silent' }
    "link={0} flag15={1} playback={2} mic={3} default={4} windows {5}" -f $s.btLink, $s.btFlag15, $s.renderState, $s.captureState, $s.renderDefault, $sound
}

$header = "time,btLink,btFlag15,renderState,captureState,renderDefault,renderPeak,marker,note`r`n"
[System.IO.File]::WriteAllText($OutFile, $header, (New-Object System.Text.UTF8Encoding($true)))   # with a BOM, so Excel reads typed notes correctly
Write-Host "Logging to $OutFile"
Write-Host "Keys: L laptop  P iPhone  N no sound  G unwanted grab  S start trial  T note  Q quit" -ForegroundColor Cyan
Write-Host "To focus this window, click its title bar, not inside it. If the title starts with 'Select', press Esc." -ForegroundColor Cyan
Write-Host "Quit with Q (Ctrl+C works too). Closing the window instead can lose rows while the CSV is open in Excel." -ForegroundColor Cyan

$markers = @{ 'L' = 'heard-laptop'; 'P' = 'heard-iphone'; 'N' = 'heard-nothing'; 'G' = 'unwanted-grab' }
$lastSummary = ''
$running = $true
try {
    while ($running) {
        $s = Get-Sample
        Write-Row $s
        $summary = Get-Summary $s
        if ($summary -ne $lastSummary) {
            Write-Host ("{0}  {1}" -f $s.time.Substring(11, 12), $summary)
            $lastSummary = $summary
        }

        $until = (Get-Date).AddSeconds($IntervalSeconds)
        while ((Get-Date) -lt $until -and $running) {
            if ([Console]::KeyAvailable) {
                $key = [Console]::ReadKey($true).Key.ToString().ToUpperInvariant()
                $m = Get-Sample
                if ($markers.ContainsKey($key)) {
                    $m.marker = $markers[$key]
                } elseif ($key -eq 'S') {
                    $m.marker = 'trial-start'
                    $m.note = Read-Host 'Trial label (for example A S3 #2)'
                } elseif ($key -eq 'T') {
                    $m.marker = 'note'
                    $m.note = Read-Host 'Note'
                } elseif ($key -eq 'Q') {
                    $m.marker = 'quit'
                    $running = $false
                } else {
                    continue
                }
                Write-Row $m
                Write-Host ("{0}  [{1}] {2}  ({3})" -f $m.time.Substring(11, 12), $m.marker, $m.note, (Get-Summary $m)) -ForegroundColor Green
            }
            Start-Sleep -Milliseconds 100
        }
    }
} finally {
    # Rows still held because the CSV was locked: write them now, or to a separate file with its own
    # header. In finally, so Ctrl+C saves them too.
    if (Save-Pending) {
        Write-Host "Saved $OutFile"
    } else {
        $rescue = [System.IO.Path]::ChangeExtension($OutFile, '.unsaved.csv')
        [System.IO.File]::WriteAllText($rescue, $header + (($pending -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($true)))
        Write-Host "$OutFile was still open in another program. The last $($pending.Count) rows are in $rescue." -ForegroundColor Yellow
    }
}
