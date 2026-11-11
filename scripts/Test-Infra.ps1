<#
.SYNOPSIS
    Chay mot lan de demo/kiem tra: Nginx chia tai + failover, rate limiting,
    JMeter (do tai + 429) va CI GitHub Actions. Khong gom K8s va Jira.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Test-Infra.ps1
    powershell -ExecutionPolicy Bypass -File scripts\Test-Infra.ps1 -SkipJMeter -LocalCi
#>
param(
    [string]$BuyerEmail = 'buyer@demo.test',
    [string]$BuyerPassword = 'G4@123456',
    [string]$JMeterPath,
    [int]$QuotaWaitSeconds = 65,
    [switch]$SkipBuild,
    [switch]$SkipJMeter,
    [switch]$SkipCi,
    [switch]$LocalCi,
    [switch]$Down
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $ProjectRoot
if (-not (Test-Path 'G4_Project.sln')) {
    throw 'Khong tim thay G4_Project.sln. Script phai nam trong thu muc scripts cua repo.'
}

$ComposeFile = 'docker-compose.nginx.yml'
$compose = @('compose', '-f', $ComposeFile)
$BaseUrl = 'http://127.0.0.1:8088'
$CatalogPath = '/api/catalog/random?count=3'
$RunDir = Join-Path $ProjectRoot "artifacts/jmeter/infra-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
if (-not $JMeterPath) {
    $JMeterPath = Join-Path $ProjectRoot 'artifacts/jmeter-tools/apache-jmeter-5.6.3/bin/jmeter.bat'
}
$sql = @(
    '-T', '-e', 'SQLCMDPASSWORD=G4@123456Strong!', 'sqlserver',
    '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-I', '-b'
)

$results = New-Object System.Collections.Generic.List[object]
$apiBStopped = $false

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "=== $Text ===" -ForegroundColor Cyan
}

function Add-Result([string]$Name, [string]$Result, [string]$Detail) {
    $color = switch ($Result) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Yellow' } }
    Write-Host "[$Result] $Name - $Detail" -ForegroundColor $color
    $results.Add([pscustomobject]@{ Test = $Name; Result = $Result; Detail = $Detail })
}

# Docker/JMeter ghi tien trinh ra stderr; khong de no thanh loi PowerShell.
function Invoke-Native([scriptblock]$Command, [switch]$Quiet) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $Command 2>&1 | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
        if (-not $Quiet) {
            $output | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        }
        [pscustomobject]@{ ExitCode = $code; Output = $output }
    } finally {
        $ErrorActionPreference = $previous
    }
}

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(10)

# Goi HTTP khong nem loi khi gap 4xx/5xx, tra ve status + header can xem.
function Send-Http([string]$Method, [string]$Path, $Body, [string]$Token) {
    $request = New-Object System.Net.Http.HttpRequestMessage(
        (New-Object System.Net.Http.HttpMethod($Method)),
        "$BaseUrl$Path"
    )
    if ($Token) {
        $request.Headers.Authorization =
            New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)
    }
    if ($null -ne $Body) {
        $request.Content = New-Object System.Net.Http.StringContent(
            ($Body | ConvertTo-Json -Compress),
            [Text.Encoding]::UTF8,
            'application/json'
        )
    }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
    } catch {
        return [pscustomobject]@{ Status = 0; Upstream = ''; RetryAfter = 0; Body = "$_" }
    }
    $upstream = ''
    $values = $null
    if ($response.Headers.TryGetValues('X-G4-Upstream', [ref]$values)) {
        # Nginx thu lai upstream khac khi loi: dia chi cuoi cung la ban tra loi.
        $upstream = (($values -join ',') -split ',')[-1].Trim()
    }
    $retryAfter = 0
    if ($response.Headers.RetryAfter -and $response.Headers.RetryAfter.Delta) {
        # PowerShell tu bo lop Nullable, Delta da la TimeSpan.
        $retryAfter = [int]$response.Headers.RetryAfter.Delta.TotalSeconds
    }
    [pscustomobject]@{
        Status = [int]$response.StatusCode
        Upstream = $upstream
        RetryAfter = $retryAfter
        Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    }
}

# Doi den khi Nginx thay du so upstream dang tra loi (401 khi chua co token la binh thuong).
function Wait-Upstreams([int]$Expected, [int]$TimeoutSeconds = 120) {
    $seen = @{}
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $r = Send-Http 'GET' $CatalogPath $null $null
        if (($r.Status -in @(200, 401, 403)) -and $r.Upstream) {
            $seen[$r.Upstream] = $true
            if ($seen.Count -ge $Expected) { return $true }
        }
        Start-Sleep -Milliseconds 700
    }
    Write-Host "    Upstream da thay: $($seen.Keys -join ', ')" -ForegroundColor DarkGray
    return $false
}

function Get-BuyerToken {
    $body = @{ email = $BuyerEmail; password = $BuyerPassword }
    $r = Send-Http 'POST' '/api/auth/login' $body $null
    if ($r.Status -eq 429) {
        Write-Host "    Login dang bi rate limit, doi $($r.RetryAfter + 1) giay..."
        Start-Sleep -Seconds ($r.RetryAfter + 1)
        $r = Send-Http 'POST' '/api/auth/login' $body $null
    }
    if ($r.Status -ne 200) { return $null }
    $json = $r.Body | ConvertFrom-Json
    if ($json.role -ne 'buyer' -or -not $json.token) { return $null }
    return $json.token
}

function Read-Jtl([string]$Path) {
    if (-not (Test-Path $Path)) { return @() }
    return @(Import-Csv $Path)
}

try {
    # ------------------------------------------------------------------ 0. Stack
    Write-Step '0. Khoi dong stack Nginx (sqlserver, api-a, api-b, nginx)'
    if ((Invoke-Native { docker info } -Quiet).ExitCode -ne 0) {
        throw 'Docker chua chay. Hay bat Docker Desktop (Linux containers) roi chay lai.'
    }
    Invoke-Native { docker @compose up -d sqlserver } | Out-Null

    $ready = $false
    for ($i = 1; $i -le 24; $i++) {
        if ((Invoke-Native { docker @compose exec @sql -Q 'SELECT 1' } -Quiet).ExitCode -eq 0) {
            $ready = $true
            break
        }
        Write-Host "    SQL Server chua san sang, doi 5 giay... ($i/24)"
        Start-Sleep -Seconds 5
    }
    if (-not $ready) { throw 'SQL Server khong khoi dong duoc.' }

    $check = "SET NOCOUNT ON; SELECT CASE WHEN DB_ID('G4_Demo') IS NULL THEN 0 ELSE 1 END"
    $exists = Invoke-Native { docker @compose exec @sql -h -1 -Q $check } -Quiet
    if (($exists.Output | Where-Object { $_.Trim() -eq '1' }).Count -eq 0) {
        Write-Host '    Chua co G4_Demo, dang import (mat vai phut)...'
        Invoke-Native {
            docker @compose cp database/demo/G4_Demo.sql sqlserver:/tmp/G4_Demo.sql
        } | Out-Null
        $import = Invoke-Native { docker @compose exec @sql -f 65001 -i /tmp/G4_Demo.sql } -Quiet
        if ($import.ExitCode -ne 0) {
            $import.Output | Select-Object -Last 20 | ForEach-Object { Write-Host "    $_" }
            throw 'Import G4_Demo that bai.'
        }
    } else {
        Write-Host '    G4_Demo da co, bo qua import.'
    }

    if (-not $SkipBuild) {
        Write-Host '    Build image API (lan dau mat vai phut)...'
        $build = Invoke-Native { docker @compose build api-a } -Quiet
        if ($build.ExitCode -ne 0) {
            $build.Output | Select-Object -Last 30 | ForEach-Object { Write-Host "    $_" }
            throw 'Build image API that bai.'
        }
    }
    if ((Invoke-Native { docker @compose up -d } -Quiet).ExitCode -ne 0) {
        throw 'docker compose up that bai.'
    }
    $nginxTest = Invoke-Native { docker @compose exec -T nginx nginx -t } -Quiet
    if ($nginxTest.ExitCode -ne 0) { throw 'Cau hinh Nginx loi (nginx -t).' }

    Write-Host '    Doi ca hai API tra loi qua Nginx...'
    if (-not (Wait-Upstreams -Expected 2)) {
        throw (
            'Khong thay du 2 API qua Nginx. Xem: ' +
            "docker compose -f $ComposeFile logs api-a"
        )
    }
    Add-Result 'Stack' 'PASS' 'sqlserver, api-a, api-b, nginx deu chay; nginx -t ok'

    # ------------------------------------------------------------------ 1. Login
    Write-Step '1. Dang nhap buyer qua Nginx'
    $token = Get-BuyerToken
    if (-not $token) { throw "Khong dang nhap duoc $BuyerEmail qua $BaseUrl." }
    $env:G4_JMETER_BUYER_TOKEN = $token
    Add-Result 'Login' 'PASS' "$BuyerEmail nhan JWT role buyer"

    # ------------------------------------------------------------------ 2. Load balance
    Write-Step '2. Nginx chia tai (6 request catalog)'
    $calls = 1..6 | ForEach-Object {
        $r = Send-Http 'GET' $CatalogPath $null $token
        Write-Host "    #$_ status=$($r.Status) upstream=$($r.Upstream)"
        $r
    }
    $upstreams = @($calls | Select-Object -ExpandProperty Upstream -Unique)
    $allOk = @($calls | Where-Object { $_.Status -ne 200 }).Count -eq 0
    if ($allOk -and $upstreams.Count -ge 2) {
        Add-Result 'Load balance' 'PASS' "6/6 HTTP 200, chia cho: $($upstreams -join ' | ')"
    } else {
        Add-Result 'Load balance' 'FAIL' "upstream: $($upstreams -join ' | '); tat ca 200: $allOk"
    }

    # ------------------------------------------------------------------ 3. Failover
    Write-Step '3. Failover: tat api-b, request van thanh cong'
    Invoke-Native { docker @compose stop api-b } -Quiet | Out-Null
    $apiBStopped = $true
    Start-Sleep -Seconds 6
    $calls = 1..4 | ForEach-Object {
        $r = Send-Http 'GET' $CatalogPath $null $token
        Write-Host "    #$_ status=$($r.Status) upstream=$($r.Upstream)"
        $r
    }
    $failed = @($calls | Where-Object { $_.Status -ne 200 }).Count
    if ($failed -eq 0) {
        Add-Result 'Failover' 'PASS' '4/4 HTTP 200 khi api-b da tat'
    } else {
        Add-Result 'Failover' 'FAIL' "$failed/4 request loi khi api-b tat"
    }
    Invoke-Native { docker @compose start api-b } -Quiet | Out-Null
    $apiBStopped = $false
    if (-not (Wait-Upstreams -Expected 2)) {
        Write-Host '    Canh bao: api-b chua tra loi lai sau khi bat.' -ForegroundColor Yellow
    }

    # ------------------------------------------------------------------ 4. JMeter
    if ($SkipJMeter) {
        Add-Result 'JMeter do tai' 'SKIP' 'bo qua theo -SkipJMeter'
        Add-Result 'JMeter rate limit' 'SKIP' 'bo qua theo -SkipJMeter'
    } elseif (-not (Test-Path $JMeterPath)) {
        Add-Result 'JMeter do tai' 'SKIP' "khong thay $JMeterPath (dung -JMeterPath)"
        Add-Result 'JMeter rate limit' 'SKIP' 'khong co JMeter'
    } else {
        $target = @('-Jprotocol=http', '-Jhost=127.0.0.1', '-Jport=8088')
        New-Item -ItemType Directory -Force -Path $RunDir | Out-Null

        Write-Step '4a. JMeter do tai catalog qua Nginx (4 luong, 30 giay)'
        $loadDir = Join-Path $RunDir 'load'
        Invoke-Native {
            & $JMeterPath -n -t tests/jmeter/catalog-load.jmx @target `
                '-Jthreads=4' '-Jramp_seconds=3' '-Jduration_seconds=30' `
                '-Jrequests_per_minute=60' `
                -l "$loadDir.jtl" -j "$loadDir.log" -e -o $loadDir
        } | Out-Null
        $rows = Read-Jtl "$loadDir.jtl"
        $bad = @($rows | Where-Object { $_.success -ne 'true' }).Count
        if ($rows.Count -gt 0 -and $bad -eq 0) {
            $avg = [math]::Round(($rows | Measure-Object elapsed -Average).Average)
            Add-Result 'JMeter do tai' 'PASS' "$($rows.Count) mau, 0 loi, trung binh $avg ms"
        } else {
            Add-Result 'JMeter do tai' 'FAIL' "$($rows.Count) mau, $bad loi (xem $loadDir.jtl)"
        }

        Write-Step '4b. JMeter 150 request de thay 429 (chi api-a, quota dem rieng moi API)'
        Write-Host "    Doi $QuotaWaitSeconds giay cho quota read hoi lai..."
        Start-Sleep -Seconds $QuotaWaitSeconds
        Invoke-Native { docker @compose stop api-b } -Quiet | Out-Null
        $apiBStopped = $true
        Start-Sleep -Seconds 6
        $rateDir = Join-Path $RunDir 'rate-limit'
        Invoke-Native {
            & $JMeterPath -n -t tests/jmeter/catalog-rate-limit.jmx @target '-Jrequests=150' `
                -l "$rateDir.jtl" -j "$rateDir.log" -e -o $rateDir
        } | Out-Null
        Invoke-Native { docker @compose start api-b } -Quiet | Out-Null
        $apiBStopped = $false
        $rows = Read-Jtl "$rateDir.jtl"
        $ok200 = @($rows | Where-Object { $_.responseCode -eq '200' -and $_.success -eq 'true' })
        $ok429 = @($rows | Where-Object { $_.responseCode -eq '429' -and $_.success -eq 'true' })
        $bad = @($rows | Where-Object { $_.success -ne 'true' }).Count
        if ($ok200.Count -gt 0 -and $ok429.Count -gt 0 -and $bad -eq 0) {
            Add-Result 'JMeter rate limit' 'PASS' (
                "$($rows.Count) mau: $($ok200.Count) x 200, $($ok429.Count) x 429, 0 loi"
            )
        } else {
            Add-Result 'JMeter rate limit' 'FAIL' (
                "200=$($ok200.Count), 429=$($ok429.Count), loi=$bad (xem $rateDir.jtl)"
            )
        }
        Wait-Upstreams -Expected 2 | Out-Null
    }

    # ------------------------------------------------------------------ 5. Login rate limit
    Write-Step '5. Rate limiting login: 12 lan sai lien tiep (gioi han 10 lan/60 giay theo IP)'
    $bad = @{ email = 'khongtontai@demo.test'; password = 'sai-mat-khau' }
    $attempts = 1..12 | ForEach-Object {
        $r = Send-Http 'POST' '/api/auth/login' $bad $null
        Write-Host "    #$_ status=$($r.Status) retry-after=$($r.RetryAfter)"
        $r
    }
    $limited = @($attempts | Where-Object { $_.Status -eq 429 })
    if (
        $limited.Count -gt 0 `
        -and $limited[0].RetryAfter -gt 0 `
        -and $limited[0].Body -match 'RateLimitExceeded'
    ) {
        $first = [array]::IndexOf(@($attempts | ForEach-Object Status), 429) + 1
        Add-Result 'Rate limit login' 'PASS' (
            "429 tu lan #$first, Retry-After=$($limited[0].RetryAfter)s, code RateLimitExceeded"
        )
    } else {
        Add-Result 'Rate limit login' 'FAIL' 'khong thay 429 hop le sau 12 lan login sai'
    }

    # ------------------------------------------------------------------ 6. CI
    Write-Step '6. CI GitHub Actions'
    if ($SkipCi) {
        Add-Result 'CI GitHub Actions' 'SKIP' 'bo qua theo -SkipCi'
    } elseif (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Add-Result 'CI GitHub Actions' 'SKIP' 'chua cai gh CLI; xem tab Actions tren GitHub'
    } else {
        $ci = Invoke-Native {
            gh run list --workflow ci.yml --branch master --limit 5 `
                --json 'conclusion,status,displayTitle,createdAt,url'
        } -Quiet
        $runs = $null
        if ($ci.ExitCode -eq 0) {
            try { $runs = ($ci.Output -join "`n") | ConvertFrom-Json } catch { $runs = $null }
        }
        if (-not $runs) {
            Add-Result 'CI GitHub Actions' 'SKIP' 'gh chua dang nhap hoac khong doc duoc run'
        } else {
            $runs | ForEach-Object {
                Write-Host ("    {0,-10} {1,-10} {2}" -f $_.status, $_.conclusion, $_.displayTitle)
            }
            $last = @($runs | Where-Object { $_.status -eq 'completed' })[0]
            if ($last -and $last.conclusion -eq 'success') {
                Add-Result 'CI GitHub Actions' 'PASS' "run master moi nhat thanh cong: $($last.url)"
            } else {
                Add-Result 'CI GitHub Actions' 'FAIL' "run master moi nhat: $($last.conclusion)"
            }
        }
    }

    if ($LocalCi) {
        Write-Step '6b. Chay lai cac buoc CI tren may (build Release + test)'
        $steps = @(
            @{ Name = 'restore'; Run = { dotnet restore G4_Project.sln } },
            @{ Name = 'build'; Run = { dotnet build G4_Project.sln -c Release --no-restore } },
            @{
                Name = 'backend tests'
                Run = { dotnet run --project tests/G4.Commerce.Tests -c Release --no-build }
            },
            @{ Name = 'js tests'; Run = { node --test (Get-ChildItem tests/*.test.cjs).FullName } }
        )
        $failedStep = $null
        foreach ($step in $steps) {
            Write-Host "    -> $($step.Name)"
            $r = Invoke-Native $step.Run -Quiet
            if ($r.ExitCode -ne 0) {
                $r.Output | Select-Object -Last 20 | ForEach-Object { Write-Host "    $_" }
                $failedStep = $step.Name
                break
            }
        }
        if ($failedStep) {
            Add-Result 'CI tren may' 'FAIL' "buoc $failedStep that bai"
        } else {
            Add-Result 'CI tren may' 'PASS' 'restore, build Release, backend + js tests deu qua'
        }
    }
} catch {
    Add-Result 'Script' 'FAIL' $_.Exception.Message
} finally {
    if ($apiBStopped) {
        Invoke-Native { docker @compose start api-b } -Quiet | Out-Null
    }
    Remove-Item Env:G4_JMETER_BUYER_TOKEN -ErrorAction SilentlyContinue
    $client.Dispose()
    if ($Down) {
        Invoke-Native { docker @compose down } -Quiet | Out-Null
    }
}

Write-Step 'TONG KET'
$results | Format-Table -AutoSize -Wrap
Write-Host 'Khong kiem bang script: Jira (mo board cua team) va K8s (chua co trong repo).'
if (Test-Path $RunDir) {
    Write-Host "Bao cao JMeter: $RunDir\load\index.html va $RunDir\rate-limit\index.html"
}
if (-not $Down) {
    Write-Host "Stack van chay. Tat bang: docker compose -f $ComposeFile down"
}
if (@($results | Where-Object { $_.Result -eq 'FAIL' }).Count -gt 0) { exit 1 }
exit 0
