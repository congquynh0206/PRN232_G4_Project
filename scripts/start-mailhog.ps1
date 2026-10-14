[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runtimeDirectory = Join-Path $projectRoot 'artifacts\mailhog'
$executablePath = Join-Path $runtimeDirectory 'MailHog_windows_amd64.exe'
$mailHogUrl = 'http://127.0.0.1:8025'

# Reuse an existing MailHog, including the Docker service.
try {
    $existing = Invoke-RestMethod -Uri "$mailHogUrl/api/v2/messages?limit=1" -TimeoutSec 2
    if ($null -ne $existing.total -and $null -ne $existing.items) {
        Write-Host 'MailHog is already running: http://localhost:8025'
        return
    }
} catch {
    # No healthy MailHog yet; verify that its local ports are available.
}

foreach ($port in @(1025, 8025)) {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
    try {
        $listener.Start()
    } catch {
        throw "Port $port is in use. Stop the conflicting service before starting MailHog."
    } finally {
        $listener.Stop()
    }
}

if (-not (Test-Path -LiteralPath $executablePath)) {
    New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
    Write-Host 'Downloading MailHog v1.0.1 from the official GitHub release...'
    $downloadPath = "$executablePath.download"
    try {
        $releaseUrl = 'https://github.com/mailhog/MailHog/releases/download/v1.0.1/MailHog_windows_amd64.exe'
        if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
            & curl.exe --fail --location --silent --show-error --max-time 120 --output $downloadPath $releaseUrl
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not download the official MailHog release. Check your Internet connection.'
            }
        } else {
            Invoke-WebRequest -Uri $releaseUrl -OutFile $downloadPath -UseBasicParsing -TimeoutSec 120
        }
        Move-Item -LiteralPath $downloadPath -Destination $executablePath -Force
    } finally {
        if (Test-Path -LiteralPath $downloadPath) {
            Remove-Item -LiteralPath $downloadPath
        }
    }
}

$mailHogProcess = Start-Process `
    -FilePath $executablePath `
    -ArgumentList @(
        '-smtp-bind-addr', '127.0.0.1:1025',
        '-api-bind-addr', '127.0.0.1:8025',
        '-ui-bind-addr', '127.0.0.1:8025',
        '-storage', 'memory'
    ) `
    -WorkingDirectory $runtimeDirectory `
    -RedirectStandardOutput (Join-Path $runtimeDirectory 'stdout.log') `
    -RedirectStandardError (Join-Path $runtimeDirectory 'stderr.log') `
    -WindowStyle Hidden `
    -PassThru

for ($attempt = 0; $attempt -lt 20; $attempt++) {
    if ($mailHogProcess.HasExited) {
        throw "MailHog exited. Check logs in $runtimeDirectory."
    }
    try {
        $ready = Invoke-RestMethod -Uri "$mailHogUrl/api/v2/messages?limit=1" -TimeoutSec 1
        if ($null -ne $ready.total -and $null -ne $ready.items) {
            Write-Host "MailHog started (PID $($mailHogProcess.Id)): http://localhost:8025"
            Write-Host 'SMTP: 127.0.0.1:1025. Email is stored locally for testing.'
            return
        }
    } catch {
        Start-Sleep -Milliseconds 250
    }
}

throw "MailHog did not become ready. Check logs in $runtimeDirectory."
