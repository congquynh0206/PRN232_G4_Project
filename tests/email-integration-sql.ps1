param([ValidateSet('G4_Email_Test_20261005')][string]$Database='G4_Email_Test_20261005')
$ErrorActionPreference='Stop'
$qaRoot=Split-Path -Parent $PSScriptRoot
$qaSqlcmd='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
$qaArtifacts=Join-Path $qaRoot 'artifacts/email-integration'
New-Item -ItemType Directory -Path $qaArtifacts -Force | Out-Null
function Invoke-QaSql([string]$Query) {
    $qaResult=& $qaSqlcmd -S localhost -E -C -I -d $Database -h -1 -W -Q "SET NOCOUNT ON; $Query" -b
    if($LASTEXITCODE -ne 0){throw 'QA SQL command failed'}
    return ($qaResult | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }) -join "`n"
}
$qaTables=@('OrderTable','OrderItem','Payment','Refund','SellerAccount','SellerSettlement','FinancialTransaction','Product','Inventory','User','Address','Promotion','PromotionUsage','OrderPromotionSnapshot','Coupon')
$qaManifestSql=($qaTables | ForEach-Object { "SELECT N'$_', COUNT(*), CONVERT(varchar(64),HASHBYTES('SHA2_256',(SELECT * FROM [$_] ORDER BY [Id] FOR JSON PATH)),2) FROM [$_];" }) -join "`n"
$qaBefore=Invoke-QaSql $qaManifestSql
$qaBefore | Set-Content -LiteralPath (Join-Path $qaArtifacts 'sql-before-manifest.txt')
$qaHasMigration=Invoke-QaSql "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId=N'20261005062401_AddEmailIntegrationDiagnostics';"
if($qaHasMigration -eq '0') {
    $qaSource=Get-Content -LiteralPath (Join-Path $qaRoot 'database/email-integration-migration.sql') -Raw
    $qaFailure=$qaSource.Replace('COMMIT TRANSACTION;',"THROW 50099, 'Intentional QA rollback probe', 1; COMMIT TRANSACTION;")
    $qaProbePath=Join-Path $qaArtifacts 'rollback-probe.sql'
    $qaFailure | Set-Content -LiteralPath $qaProbePath
    & $qaSqlcmd -S localhost -E -C -I -d $Database -i $qaProbePath -b *> (Join-Path $qaArtifacts 'rollback-probe.log')
    if($LASTEXITCODE -eq 0){throw 'Rollback probe did not fail'}
    if((Invoke-QaSql "SELECT CASE WHEN OBJECT_ID(N'IntegrationLog') IS NULL AND COL_LENGTH(N'NotificationOutbox',N'CapturedAt') IS NULL AND NOT EXISTS(SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId=N'20261005062401_AddEmailIntegrationDiagnostics') THEN 1 ELSE 0 END;") -ne '1'){throw 'Schema/history did not roll back'}
}
for($qaPass=1;$qaPass -le 2;$qaPass++) {
    & $qaSqlcmd -S localhost -E -C -I -d $Database -i (Join-Path $qaRoot 'database/email-integration-migration.sql') -b *> (Join-Path $qaArtifacts "migration-pass-$qaPass.log")
    if($LASTEXITCODE -ne 0){throw "Migration pass $qaPass failed"}
}
$qaAfter=Invoke-QaSql $qaManifestSql
$qaAfter | Set-Content -LiteralPath (Join-Path $qaArtifacts 'sql-after-manifest.txt')
if($qaBefore -ne $qaAfter){throw 'Migration changed existing commerce data'}
if((Invoke-QaSql "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId=N'20261005062401_AddEmailIntegrationDiagnostics';") -ne '1'){throw 'Migration history missing or duplicated'}
if((Invoke-QaSql "SELECT COUNT(*) FROM NotificationOutbox WHERE OrderId=100001 AND Status=N'Captured' AND SentAt IS NULL AND CapturedAt='2026-10-04T00:01:00' AND Body=N'Legacy body' AND Attempts=2;") -ne '1'){throw 'Legacy Captured fixture not preserved correctly'}
Write-Output 'SQL migration rollback, restart/idempotency, 15-table manifest and legacy Captured checks passed.'
