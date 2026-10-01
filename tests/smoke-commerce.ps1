param([string]$Backend = 'http://localhost:5251', [string]$Frontend = 'http://localhost:5131')
$ErrorActionPreference = 'Stop'

$password = 'G4@123456'
$tokens = @{}
foreach ($role in @('buyer','seller','shipper','admin')) {
    $login = Invoke-RestMethod -Uri "$Backend/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body (@{ email = "$role@example.test"; password = $password } | ConvertTo-Json)
    if ($login.role -ne $role -or [string]::IsNullOrWhiteSpace($login.token)) { throw "Login failed for $role" }
    $tokens[$role] = $login.token
}

function Invoke-Api([string]$Method, [string]$Path, [string]$Role = 'buyer', $Data = $null) {
    $args = @{ Uri = "$Backend/api/$Path"; Method = $Method; Headers = @{ Authorization = "Bearer $($tokens[$Role])" } }
    if ($Method -eq 'POST') { $args.ContentType = 'application/json'; $args.Body = if ($null -eq $Data) { '{}' } else { $Data | ConvertTo-Json -Depth 6 } }
    try { return Invoke-RestMethod @args }
    catch {
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) { $detail = $_.ErrorDetails.Message }
        throw "$Method api/$Path as $Role failed: $detail"
    }
}

function Assert-Forbidden([string]$Method, [string]$Path, [string]$Role) {
    try {
        Invoke-RestMethod -Uri "$Backend/api/$Path" -Method $Method -Headers @{ Authorization = "Bearer $($tokens[$Role])" } | Out-Null
    }
    catch {
        if ([int]$_.Exception.Response.StatusCode -eq 403) { return }
        throw "Expected 403 for $Role on api/$Path but got $($_.Exception.Message)"
    }
    throw "Expected 403 for $Role on api/$Path but request succeeded"
}

$frontPage = Invoke-WebRequest -Uri "$Frontend/Account/Login" -UseBasicParsing
if ($frontPage.StatusCode -ne 200 -or $frontPage.Content -notmatch 'G4 Marketplace') { throw 'Frontend login page is not ready' }

$rolePages = [ordered]@{
    buyer = 'Order Summary'
    seller = '<span class="eyebrow">Seller</span>'
    shipper = '<span class="eyebrow">Shipper</span>'
    admin = '<span class="eyebrow">Admin</span>'
}
foreach ($role in $rolePages.Keys) {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $loginPage = Invoke-WebRequest -Uri "$Frontend/Account/Login" -WebSession $session -UseBasicParsing
    $antiForgeryToken = [regex]::Match(
        $loginPage.Content,
        'name="__RequestVerificationToken" type="hidden" value="([^"]+)"'
    ).Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($antiForgeryToken)) { throw 'Frontend login form has no antiforgery token' }
    $rolePage = Invoke-WebRequest -Uri "$Frontend/Account/Login" -Method Post -WebSession $session `
        -ContentType 'application/x-www-form-urlencoded' -UseBasicParsing `
        -Body @{ Email = "$role@example.test"; Password = $password; __RequestVerificationToken = $antiForgeryToken }
    if ($rolePage.StatusCode -ne 200 -or $rolePage.Content -notmatch [regex]::Escape($rolePages[$role])) {
        throw "Frontend login did not reach the $role page"
    }
    if ($role -eq 'buyer') {
        $deniedPage = Invoke-WebRequest -Uri "$Frontend/Seller" -WebSession $session -UseBasicParsing
        if ($deniedPage.Content -notmatch 'login-card') { throw 'Buyer accessed the seller page' }
    }
}
Assert-Forbidden 'GET' 'addresses' 'seller'
Assert-Forbidden 'GET' 'seller/finance' 'buyer'
$addresses = Invoke-Api 'GET' 'addresses'
if (@($addresses).Count -lt 2) { throw 'SQL seed data is missing buyer addresses' }
$editedAddress = Invoke-Api 'POST' "addresses/$($addresses[1].id)/edit" 'buyer' @{ fullName = 'Buyer'; street = '2 Updated Example Street'; city = 'Ho Chi Minh City'; state = 'HCM'; country = 'Vietnam' }
if ($editedAddress.street -ne '2 Updated Example Street') { throw 'Address edit failed' }
$cart = Invoke-Api 'GET' 'catalog/random?count=2'
if ($cart.Count -ne 2) { throw 'Random cart did not return two products' }
$checkoutKey = [guid]::NewGuid().ToString('N')
$request = @{ addressId = $addresses[0].id; items = @($cart | ForEach-Object { @{ productId = $_.productId; quantity = 1 } }); couponCode = 'G4SAVE10'; checkoutKey = $checkoutKey }
$quote = Invoke-Api 'POST' 'quote' 'buyer' $request
$order = Invoke-Api 'POST' 'orders' 'buyer' $request
$duplicateOrder = Invoke-Api 'POST' 'orders' 'buyer' $request
if ($order.id -ne $duplicateOrder.id -or $order.totalPrice -ne $quote.total) { throw 'Checkout is not idempotent or total differs' }
$payment = Invoke-Api 'POST' "orders/$($order.id)/pay/card" 'buyer' @{ number = '4111111111111111'; expiry = '12/30'; key = [guid]::NewGuid().ToString('N') }
if ($payment.status -ne 'Succeeded') { throw 'Fake card payment failed' }
Invoke-Api 'POST' "seller/orders/$($order.id)/prepare" 'seller' | Out-Null
$shipment = Invoke-Api 'POST' "seller/orders/$($order.id)/ship" 'seller'
if (!$shipment.trackingNumber) { throw 'Tracking number missing' }
foreach ($status in @('PickedUp','InTransit','OutForDelivery','Delivered')) {
    $event = Invoke-Api 'POST' "shipments/$($shipment.id)/events" 'shipper' @{ status = $status; eventId = [guid]::NewGuid().ToString('N'); location = 'Distribution hub' }
    if (!$event.applied) { throw "Tracking event $status was rejected" }
}
$detail = Invoke-Api 'GET' "orders/$($order.id)"
if ($detail.status -ne 'Delivered') { throw 'Order did not reach Delivered' }
if ($null -eq $detail.settlement -or $detail.settlement.platformFeeAmount -le 0 -or $detail.settlement.netAmount -ge $detail.settlement.grossAmount) { throw 'Seller settlement or platform fee is missing' }
Invoke-Api 'POST' "orders/$($order.id)/fund-hold" 'buyer' @{ reason = 'Smoke dispute hold' } | Out-Null
$heldDetail = Invoke-Api 'GET' "orders/$($order.id)" 'buyer'
if ($heldDetail.settlement.status -ne 'OnHold') { throw 'Fund hold did not mark the settlement On Hold' }
Invoke-Api 'POST' "orders/$($order.id)/fund-hold/resolve" 'admin' @{ releaseToSeller = $false } | Out-Null
$return = Invoke-Api 'POST' "orders/$($order.id)/returns" 'buyer' @{ reason = 'Not as described' }
Invoke-Api 'POST' 'carrier/fail-next' 'seller' @{ orderId = $order.id; direction = 'Return'; count = 3 } | Out-Null
Invoke-Api 'POST' "seller/returns/$($return.id)/approve" 'seller' | Out-Null
$returnDetail = Invoke-Api 'GET' "orders/$($order.id)"
$returnShipment = @($returnDetail.shipments | Where-Object { $_.direction -eq 'Return' })[0]
if ($returnShipment.status -ne 'ShipmentCreationFailed' -or $returnDetail.returnRequest.status -ne 'Approved') { throw 'Return carrier failure was not handled' }
$failedReturnId = $returnShipment.id
Invoke-Api 'POST' "seller/returns/$($return.id)/ship" 'seller' | Out-Null
$returnDetail = Invoke-Api 'GET' "orders/$($order.id)"
$returnShipment = @($returnDetail.shipments | Where-Object { $_.direction -eq 'Return' })[0]
if ($returnShipment.id -ne $failedReturnId -or $returnDetail.returnRequest.status -ne 'ReturnShipping') { throw 'Return retry did not reuse the correct shipment' }
if (@($returnDetail.shipments | Where-Object { $_.direction -eq 'Outbound' })[0].id -ne $shipment.id) { throw 'Return retry changed outbound shipment' }
if (!$returnShipment.trackingNumber) { throw 'Return tracking number missing' }
foreach ($status in @('PickedUp','InTransit','OutForDelivery','Delivered')) {
    $event = Invoke-Api 'POST' "shipments/$($returnShipment.id)/events" 'shipper' @{ status = $status; eventId = [guid]::NewGuid().ToString('N'); location = 'Return hub' }
    if (!$event.applied) { throw "Return tracking event $status was rejected" }
}
Invoke-Api 'POST' "seller/returns/$($return.id)/receive" 'seller' | Out-Null
$refund = Invoke-Api 'POST' "seller/orders/$($order.id)/refund" 'seller' @{ reason = 'return' }
if ($refund.status -ne 'Succeeded' -or $refund.amount -ne $order.totalPrice) { throw 'Refund failed or wrong amount' }
$repeatRefund = Invoke-Api 'POST' "seller/orders/$($order.id)/refund" 'seller' @{ reason = 'return' }
if ($repeatRefund.id -ne $refund.id) { throw 'Refund duplicated' }
if ((Invoke-Api 'GET' "orders/$($order.id)").status -ne 'Closed') { throw 'Refunded order did not close' }
$finance = Invoke-Api 'GET' 'seller/finance' 'seller'
if (@($finance.transactions | Where-Object { $_.orderId -eq $order.id -and $_.type -eq 'PlatformFee' }).Count -ne 1) { throw 'Platform fee ledger entry is missing or duplicated' }
if (@($finance.transactions | Where-Object { $_.orderId -eq $order.id -and $_.type -eq 'Refund' }).Count -ne 1) { throw 'Refund ledger entry is missing or duplicated' }

$single = Invoke-Api 'GET' 'catalog/random?count=1'
$cancelRequest = @{ addressId = $addresses[0].id; items = @(@{ productId = $single[0].productId; quantity = 1 }); checkoutKey = [guid]::NewGuid().ToString('N') }
$cancelOrder = Invoke-Api 'POST' 'orders' 'buyer' $cancelRequest
$declined = Invoke-Api 'POST' "orders/$($cancelOrder.id)/pay/card" 'buyer' @{ number = '4000000000000002'; expiry = '12/30'; key = [guid]::NewGuid().ToString('N') }
if ($declined.status -ne 'Declined') { throw 'Declined card did not fail' }
$cancelled = Invoke-Api 'POST' "orders/$($cancelOrder.id)/cancel" 'buyer'
if ($cancelled.status -ne 'Cancelled') { throw 'Unpaid cancel failed' }

$single = Invoke-Api 'GET' 'catalog/random?count=1'
$paidCancelRequest = @{ addressId = $addresses[0].id; items = @(@{ productId = $single[0].productId; quantity = 1 }); checkoutKey = [guid]::NewGuid().ToString('N') }
$paidCancelOrder = Invoke-Api 'POST' 'orders' 'buyer' $paidCancelRequest
Invoke-Api 'POST' "orders/$($paidCancelOrder.id)/pay/card" 'buyer' @{ number = '4111111111111111'; expiry = '12/30'; key = [guid]::NewGuid().ToString('N') } | Out-Null
Invoke-Api 'POST' "orders/$($paidCancelOrder.id)/cancel" 'buyer' | Out-Null
$rejectedCancel = Invoke-Api 'POST' "seller/orders/$($paidCancelOrder.id)/cancel/decision" 'seller' @{ approve = $false; reason = 'Packed already' }
if ($rejectedCancel.status -ne 'Paid') { throw 'Rejected cancellation did not restore Paid' }
Invoke-Api 'POST' "orders/$($paidCancelOrder.id)/cancel" 'buyer' | Out-Null
$acceptedCancel = Invoke-Api 'POST' "seller/orders/$($paidCancelOrder.id)/cancel/decision" 'seller' @{ approve = $true }
if ($acceptedCancel.status -ne 'Cancelled') { throw 'Paid cancellation was not accepted' }
$cancelDetail = Invoke-Api 'GET' "orders/$($paidCancelOrder.id)"
if (@($cancelDetail.refunds | Where-Object { $_.status -eq 'Succeeded' }).Count -ne 1) { throw 'Paid cancellation did not refund once' }

$single = Invoke-Api 'GET' 'catalog/random?count=1'
$deliveryRequest = @{ addressId = $addresses[0].id; items = @(@{ productId = $single[0].productId; quantity = 1 }); checkoutKey = [guid]::NewGuid().ToString('N') }
$deliveryOrder = Invoke-Api 'POST' 'orders' 'buyer' $deliveryRequest
Invoke-Api 'POST' "orders/$($deliveryOrder.id)/pay/card" 'buyer' @{ number = '4111111111111111'; expiry = '12/30'; key = [guid]::NewGuid().ToString('N') } | Out-Null
Invoke-Api 'POST' "seller/orders/$($deliveryOrder.id)/prepare" 'seller' | Out-Null
Invoke-Api 'POST' 'carrier/fail-next' 'seller' @{ orderId = $deliveryOrder.id; direction = 'Outbound'; count = 3 } | Out-Null
$failedLabel = Invoke-Api 'POST' "seller/orders/$($deliveryOrder.id)/ship" 'seller'
if ($failedLabel.status -ne 'ShipmentCreationFailed') { throw 'Carrier failure was not handled' }
$deliveryShipment = Invoke-Api 'POST' "seller/orders/$($deliveryOrder.id)/ship" 'seller'
if (!$deliveryShipment.trackingNumber -or $deliveryShipment.id -ne $failedLabel.id) { throw 'Carrier retry created a duplicate shipment' }
foreach ($status in @('PickedUp','InTransit','OutForDelivery','DeliveryFailed','ReturningToSender','ReturnedToSeller')) {
    $event = Invoke-Api 'POST' "shipments/$($deliveryShipment.id)/events" 'shipper' @{ status = $status; eventId = [guid]::NewGuid().ToString('N'); note = 'Shipping event' }
    if (!$event.applied) { throw "Delivery failure event $status was rejected" }
}
$deliveryRefund = Invoke-Api 'POST' "seller/orders/$($deliveryOrder.id)/refund" 'seller' @{ reason = 'delivery-failed' }
if ($deliveryRefund.status -ne 'Succeeded' -or (Invoke-Api 'GET' "orders/$($deliveryOrder.id)").status -ne 'Closed') { throw 'Delivery failure refund failed' }

if (@(Invoke-Api 'GET' 'shipper/shipments' 'shipper').Count -lt 1) { throw 'Shipper cannot list shipments' }
Write-Output "PASS: login, role tokens, buyer checkout, seller workflow, shipper tracking, admin hold decision, finance, returns and refunds"
