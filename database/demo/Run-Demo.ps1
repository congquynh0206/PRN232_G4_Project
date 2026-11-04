param(
    [ValidateSet('Api', 'Frontend')]
    [string]$App = 'Api',
    [string]$SqlServer = 'localhost',
    [ValidatePattern('^G4_Demo[A-Za-z0-9_]*$')]
    [string]$DatabaseName = 'G4_Demo',
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $candidate = Get-Item -LiteralPath $PSScriptRoot
    while ($null -ne $candidate -and -not (Test-Path -LiteralPath (Join-Path $candidate.FullName 'G4_Project.sln'))) {
        $candidate = $candidate.Parent
    }
    if ($null -eq $candidate) {
        throw 'Khong tim thay repo. Hay them -ProjectRoot duong_dan_G4_Project.'
    }
    $ProjectRoot = $candidate.FullName
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $ProjectRoot 'G4_Project.sln'))) {
    throw 'ProjectRoot phai tro toi repo G4_Project.'
}

# This public fixture key is for synthetic demo identities only. Never use for real identities.
# All configuration is scoped to this PowerShell process/child; no appsettings or User Secrets edits.
$settings = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    DOTNET_ENVIRONMENT = 'Development'
    SellerIdentity__HmacKey = 'AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA='
    SellerIdentity__KeyId = 'v1'
    Returns__WindowSeconds = '7200'
    Returns__ReceiptSeconds = '7200'
    Disputes__SellerResponseSeconds = '7200'
    Disputes__BuyerResponseSeconds = '7200'
    Disputes__ReturnReceiptSeconds = '7200'
    AddressChanges__ConfirmationSeconds = '7200'
    Finance__Levels__1__HoldSeconds = '1800'
    Finance__Levels__2__HoldSeconds = '900'
    Finance__LevelPromotion__EvaluationSeconds = '86400'
    Finance__Debt__DueSeconds = '3600'
    Finance__Debt__WarningBeforeSeconds = '300'
    Finance__WriteOff__AfterSeconds = '7200'
    Finance__WriteOff__InactivitySeconds = '1800'
    Finance__WriteOff__Enabled = 'true'
    Mail__Mode = 'Capture'
    ConnectionStrings__DefaultConnection = "Server=$SqlServer;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True;"
    ApiSettings__BaseUrl = 'http://localhost:5257'
    Frontend__PublicBaseUrl = 'http://localhost:5137'
    Carrier__BaseUrl = 'http://localhost:5257'
    Realtime__PublicApiBaseUrl = 'http://localhost:5257'
    Realtime__AllowedOrigins__0 = 'http://localhost:5137'
    Realtime__AllowedOrigins__1 = 'http://127.0.0.1:5137'
}
$previous = @{}
foreach ($entry in $settings.GetEnumerator()) {
    $previous[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
}
try {
    if ($App -eq 'Api') {
        Write-Host "Demo API: http://localhost:5257 - database $DatabaseName (Windows Authentication)"
        & dotnet run --project (Join-Path $ProjectRoot 'backend/G4.Api/G4.Api.csproj') `
            --no-launch-profile -- --urls 'http://localhost:5257'
    }
    else {
        Write-Host 'Demo frontend: http://localhost:5137'
        & dotnet run --project (Join-Path $ProjectRoot 'frontend/frontend.csproj') `
            --no-launch-profile -- --urls 'http://localhost:5137'
    }
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE"
    }
}
finally {
    foreach ($name in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
