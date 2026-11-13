<#
.SYNOPSIS
    Verifies the SignalR/Redis path through Nginx in the Docker demo.

.DESCRIPTION
    This test is intentionally limited to the disposable G4_Demo database. It restores
    one buyer notification to unread, then performs the real "mark read" API action.
    Two buyer SignalR clients must receive notification-inbox; an unrelated seller
    must not. The script also proves the hub negotiation/WebSocket was proxied to
    api-b while the configured dispatcher remains api-a, which is the cross-instance
    Redis-backplane case.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Test-RealtimeCrossInstance.ps1
#>
param(
    [string]$BuyerEmail = 'buyer@demo.test',
    [string]$SellerEmail = 'seller.l1@demo.test',
    [string]$Password = 'G4@123456',
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $ProjectRoot
$ComposeFile = 'docker-compose.nginx.yml'
$compose = @('compose', '-f', $ComposeFile)
$sql = @(
    'exec', '-T', '-e', 'SQLCMDPASSWORD=G4@123456Strong!', 'sqlserver',
    '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-d', 'G4_Demo', '-C', '-I', '-b'
)
$baseUrl = 'http://127.0.0.1:8088'

function Invoke-Native([scriptblock]$Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $Command 2>&1 | ForEach-Object { "$_" })
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Get-SqlScalar([string]$Query) {
    $result = Invoke-Native { docker @compose @sql -h -1 -W -Q $Query }
    if ($result.ExitCode -ne 0) {
        throw "SQL command failed: $($result.Output -join [Environment]::NewLine)"
    }
    $line = @($result.Output | Where-Object { $_.Trim() -and $_.Trim() -notmatch '^[- ]+$' })[-1]
    if ($null -eq $line) { return '' }
    return $line.Trim()
}

function Invoke-Sql([string]$Query) {
    $result = Invoke-Native { docker @compose @sql -Q $Query }
    if ($result.ExitCode -ne 0) {
        throw "SQL command failed: $($result.Output -join [Environment]::NewLine)"
    }
}

if ((Invoke-Native { docker info }).ExitCode -ne 0) {
    throw 'Docker Desktop is not running. Start Docker Desktop in Linux-container mode first.'
}
$running = @(Invoke-Native { docker @compose ps --services --status running }).Output
$required = 'sqlserver', 'redis', 'api-a', 'api-b', 'nginx'
$missing = @($required | Where-Object { $_ -notin $running })
if ($missing) {
    throw "Docker stack is incomplete. Missing running services: $($missing -join ', '). Run scripts\Test-Infra.ps1 first."
}

$buyerId = Get-SqlScalar "SET NOCOUNT ON; SELECT id FROM dbo.[User] WHERE email = N'$BuyerEmail';"
$parsedBuyerId = 0
if (-not [int]::TryParse($buyerId, [ref]$parsedBuyerId)) {
    throw "Demo buyer $BuyerEmail was not found."
}
$notificationId = Get-SqlScalar "SET NOCOUNT ON; SELECT TOP (1) Id FROM dbo.UserNotification WHERE UserId = $buyerId ORDER BY Id DESC;"
$parsedNotificationId = [long]0
if (-not [long]::TryParse($notificationId, [ref]$parsedNotificationId)) {
    throw "No durable notification exists for buyer $BuyerEmail in G4_Demo. Re-import the demo database."
}

$apiBAddress = @(Invoke-Native { docker @compose exec -T api-b hostname -i }).Output |
    Select-Object -First 1
$apiBAddress = ($apiBAddress -split '\s+')[0].Trim()
if (-not $apiBAddress) {
    throw 'Could not resolve the api-b container IP address for the realtime probe.'
}

$nginxContainer = (@(Invoke-Native { docker @compose ps -q nginx }).Output |
    Select-Object -First 1).Trim()
if (-not $nginxContainer) {
    throw 'Could not resolve the running nginx container for the realtime probe.'
}

$smokeOutput = Join-Path $ProjectRoot 'tests/G4.RealtimeSmoke/bin/Release/net8.0'
$build = Invoke-Native {
    dotnet build tests/G4.RealtimeSmoke/G4.RealtimeSmoke.csproj --configuration Release
}
if ($build.ExitCode -ne 0) {
    throw "Could not build the realtime smoke client: $($build.Output -join [Environment]::NewLine)"
}
if (-not (Test-Path (Join-Path $smokeOutput 'G4.RealtimeSmoke.dll'))) {
    throw "Realtime smoke output is missing at $smokeOutput."
}


# Nginx ip_hash uses the first three IPv4 octets. Docker may assign api-a/api-b different
# addresses after a restart, so create short-lived client networks until one maps to api-b.
$probeNetwork = $null
$networkCreated = $false
$networkAttached = $false
try {
    foreach ($networkOctet in 22..33) {
        $candidateNetwork = "g4-realtime-probe-$PID-$networkOctet"
        $candidateSubnet = "172.$networkOctet.0.0/24"
        $created = Invoke-Native {
            docker network create --driver bridge --subnet $candidateSubnet $candidateNetwork
        }
        if ($created.ExitCode -ne 0) {
            Write-Warning "Could not reserve $candidateSubnet; trying the next probe subnet."
            continue
        }

        $candidateAttached = $false
        try {
            $attached = Invoke-Native {
                docker network connect --alias nginx $candidateNetwork $nginxContainer
            }
            if ($attached.ExitCode -ne 0) {
                throw "Could not attach nginx to realtime probe network: $($attached.Output -join [Environment]::NewLine)"
            }
            $candidateAttached = $true

            $probeStartedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            # Authentication is intentionally omitted: this request only selects an Nginx ip_hash route.
            $preflight = Invoke-Native {
                docker run --rm --network $candidateNetwork curlimages/curl:8.13.0 `
                    -sS -o /dev/null -w '%{http_code}' -X POST `
                    'http://nginx:8088/hubs/commerce/negotiate?negotiateVersion=1'
            }
            $preflightStatus = ($preflight.Output -join '').Trim()
            Start-Sleep -Milliseconds 250
            $probeLogs = (Invoke-Native {
                docker @compose logs --since $probeStartedAt nginx
            }).Output -join "`n"

            $probeHitsApiB = $probeLogs -match [regex]::Escape("upstream=${apiBAddress}:8080")
            $probeHubLogs = @($probeLogs -split "`r?`n" | Where-Object {
                $_ -match '/hubs/commerce'
            }) -join '; '
            Write-Verbose "Probe ${candidateSubnet}: HTTP $preflightStatus; api-b hit=$probeHitsApiB; $probeHubLogs"
            if ($preflight.ExitCode -eq 0 -and $probeHitsApiB) {
                $probeNetwork = $candidateNetwork
                $networkCreated = $true
                $networkAttached = $true
                Write-Host "Selected source network $probeNetwork; Nginx hashes it to api-b." -ForegroundColor Cyan
                break
            }
        } finally {
            if ($probeNetwork -ne $candidateNetwork) {
                if ($candidateAttached) {
                    Invoke-Native {
                        docker network disconnect -f $candidateNetwork $nginxContainer
                    } | Out-Null
                }
                Invoke-Native { docker network rm $candidateNetwork } | Out-Null
            }
        }
    }

    if (-not $probeNetwork) {
        throw 'Could not find a Docker client source network that Nginx hashes to api-b after checking 12 source subnets.'
    }

    # Preparation changes only disposable demo data; the following API mutation returns it to read state.
    Invoke-Sql @"
;WITH chosen AS (
    SELECT TOP (1) * FROM dbo.UserNotification WHERE Id = $notificationId ORDER BY Id DESC
)
UPDATE chosen SET ReadAt = NULL;
"@
    $minimumVersion = Get-SqlScalar 'SET NOCOUNT ON; SELECT ISNULL(MAX(Id), 0) FROM dbo.CommerceChange;'
    $parsedMinimumVersion = [long]0
    if (-not [long]::TryParse($minimumVersion, [ref]$parsedMinimumVersion)) {
        throw 'Could not read the current CommerceChange version.'
    }

    $startedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    Write-Host "Prepared buyer notification $notificationId. Waiting for CommerceChanged version > $minimumVersion..." -ForegroundColor Cyan

    $mount = "${smokeOutput}:/app:ro"
    $smoke = Invoke-Native {
        docker run --rm --network $probeNetwork -v $mount mcr.microsoft.com/dotnet/runtime:8.0 `
            dotnet /app/G4.RealtimeSmoke.dll --base-url http://nginx:8088 `
            --buyer-email $BuyerEmail --seller-email $SellerEmail --password $Password `
            --min-version $minimumVersion --timeout-seconds $TimeoutSeconds
    }
    $smoke.Output | ForEach-Object { Write-Host $_ -ForegroundColor DarkGray }
    if ($smoke.ExitCode -ne 0) {
        throw "SignalR client assertions failed (exit code $($smoke.ExitCode))."
    }
} finally {
    if ($networkAttached) {
        Invoke-Native { docker network disconnect -f $probeNetwork $nginxContainer } | Out-Null
    }
    if ($networkCreated) {
        Invoke-Native { docker network rm $probeNetwork } | Out-Null
    }
}

$dispatched = Get-SqlScalar "SET NOCOUNT ON; SELECT ISNULL(MAX(Id), 0) FROM dbo.CommerceChange WHERE Id > $minimumVersion AND DispatchedAt IS NOT NULL;"
$parsedDispatched = [long]0
if (-not [long]::TryParse($dispatched, [ref]$parsedDispatched) -or $parsedDispatched -le $parsedMinimumVersion) {
    throw 'The new CommerceChange was not marked DispatchedAt after the API mutation.'
}

$nginxLogs = (Invoke-Native { docker @compose logs --since $startedAt nginx }).Output -join "`n"
$hubLogs = @($nginxLogs -split "`r?`n" | Where-Object { $_ -match '/hubs/commerce' }) -join "`n"
if ($hubLogs -match [regex]::Escape("upstream=${apiBAddress}:8080")) {
    Write-Host "PASS: Nginx hub traffic reached api-b (${apiBAddress}:8080); api-a is the only configured dispatcher." -ForegroundColor Green
    Write-Host "PASS: CommerceChange $dispatched was dispatched and audience isolation passed." -ForegroundColor Green
    exit 0
}

Write-Host $hubLogs -ForegroundColor DarkGray
throw 'SignalR event and audience checks passed, but Nginx logs did not prove the hub was held by api-b.'