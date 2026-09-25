param([string]$Backend = 'http://localhost:5251', [string]$Frontend = 'http://localhost:5131')
$ErrorActionPreference = 'Stop'

function Invoke-Demo([string]$Method, [string]$Path, [string]$Role = 'buyer', $Data = $null) {
    $args = @{ Uri = "$Backend/api/demo/$Path"; Method = $Method; Headers = @{ 'X-Demo-Role' = $Role } }
    if ($Method -eq 'POST') { $args.ContentType = 'application/json'; $args.Body = if ($null -eq $Data) { '{}' } else { $Data | ConvertTo-Json -Depth 6 } }
    return Invoke-RestMethod @args
}

$frontPage = Invoke-WebRequest -Uri $Frontend -UseBasicParsing
if ($frontPage.StatusCode -ne 200 -or $frontPage.Content -notmatch 'Order Summary') { throw 'Frontend page is not ready' }
Invoke-Demo 'POST' 'bootstrap' | Out-Null
$addresses = Invoke-Demo 'GET' 'addresses'
$editedAddress = Invoke-Demo 'POST' "addresses/$($addresses[1].id)/edit" 'buyer' @{ fullName = 'Demo Buyer'; street = '2 Updated Demo Street'; city = 'Ho Chi Minh City'; state = 'HCM'; country = 'Vietnam' }
if ($editedAddress.street -ne '2 Updated Demo Street') { throw 'Address edit failed' }
$cart = Invoke-Demo 'GET' 'catalog/random?count=2'
if ($cart.Count -ne 2) { throw 'Random cart did not return two products' }
$checkoutKey = [guid]::NewGuid().ToString('N')
$request = @{ addressId = $addresses[0].id; items = @($cart | ForEach-Object { @{ productId = $_.productId; quantity = 1 } }); couponCode = 'G4SAVE10'; checkoutKey = $checkoutKey }
$quote = Invoke-Demo 'POST' 'quote' 'buyer' $request
$order = Invoke-Demo 'POST' 'orders' 'buyer' $request
$duplicateOrder = Invoke-Demo 'POST' 'orders' 'buyer' $request
if ($order.id -ne $duplicateOrder.id -or $order.totalPrice -ne $quote.total) { throw 'Checkout is not idempotent or total differs' }
$payment = Invoke-Demo 'POST' "orders/$($order.id)/pay/card" 'buyer' @{ number = '4111111111111111'; expiry = '12/30'; key = [guid]::NewGuid().ToString('N') }
if ($payment.status -ne 'Succeeded') { throw 'Fake card payment failed' }
Invoke-Demo 'POST' "seller/orders/$($order.id)/prepare" 'seller' | Out-Null
$shipment = Invoke-Demo 'POST' "seller/orders/$($order.id)/ship" 'seller'
if (!$shipment.trackingNumber) { throw 'Tracking number missing' }
foreach ($status in @('PickedUp','InTransit','OutForDelivery','Delivered')) {
    $event = Invoke-Demo 'POST' "shipments/$($shipment.id)/events" 'seller' @{ status = $status; eventId = [guid]::NewGuid().ToString('N'); location = 'Demo hub' }
    if (!$event.applied) { throw "Tracking event $status was rejected" }
}
$detail = Invoke-Demo 'GET' "orders/$($order.id)"
if ($detail.status -ne 'Delivered') { throw 'Order did not reach Delivered' }
$return = Invoke-Demo 'POST' "orders/$($order.id)/returns" 'buyer' @{ reason = 'Not as described' }
Invoke-Demo 'POST' "seller/returns/$($return.id)/approve" 'seller' | Out-Null
$returnDetail = Invoke-Demo 'GET' "orders/$($order.id)"
$returnShipment = @($returnDetail.shipments | Where-Object { $_.direction -eq 'Return' })[0]
if (!$returnShipment.trackingNumber) { throw 'Return tracking number missing' }
foreach ($status in @('PickedUp','InTransit','OutForDelivery','Delivered')) {
    $event = Invoke-Demo 'POST' "shipments/$($returnShipment.id)/events" 'seller' @{ status = $status; eventId = [guid]::NewGuid().ToString('N'); location = 'Return hub' }
    if (!$event.applied) { throw "Return tracking event $status was rejected" }
}
Invoke-Demo 'POST' "seller/returns/$($return.id)/receive" 'seller' | Out-Null
$refund = Invoke-Demo 'POST' "seller/orders/$($order.id)/refund" 'seller' @{ reason = 'return' }
if ($refund.status -ne 'Succeeded' -or $refund.amount -ne $order.totalPrice) { throw 'Refund failed or wrong amount' }
$repeatRefund = Invoke-Demo 'POST' "seller/orders/$($order.id)/refund" 'seller' @{ reason = 'return' }
if ($repeatRefund.id -ne $refund.id) { throw 'Refund duplicated' }
if ((Invoke-Demo 'GET' "orders/$($order.id)").status -ne 'Closed') { throw 'Refunded order did not close' }

$single = Invoke-Demo 'GET' 'catalog/random?count=1'
$cancelRequest = @{ addressId = $addresses[0].id; items = @(@{ productId = $single[0].productId; quantity = 1 }); checkoutKey = [guid]::NewGuid().ToString('N') }
$cancelOrder = Invoke-Demo 'POST' 'orders' 'buyer' $cancelRequest
$declined = Invoke-Demo 'POST' "orders/$($cancelOrder.id)/pay/card" 'buyer' @{ number = '4000000000000002'; expiry = '12/30'; key = [guid]::NewGuid().ToString('N') }
if ($declined.status -ne 'Declined') { throw 'Declined card did not fail' }
$cancelled = Invoke-Demo 'POST' "orders/$($cancelOrder.id)/cancel" 'buyer'
if ($cancelled.status -ne 'Cancelled') { throw 'Unpaid cancel failed' }

$single = Invoke-Demo 'GET' 'catalog/random?count=1'
$paidCancelRequest = @{ addressId = $addresses[0].id; items = @(@{ productId = $single[0].productId; quantity = 1 }); checkoutKey = [guid]::NewGuid().ToString('N') }
$paidCancelOrder = Invoke-Demo 'POST' 'orders' 'buyer' $paidCancelRequest
Invoke-Demo 'POST' "orders/$($paidCancelOrder.id)/pay/card" 'buyer' @{ number = '4111111111111111'; expiry = '12/30'; key = [guid]::NewGuid().ToString('N') } | Out-Null
Invoke-Demo 'POST' "orders/$($paidCancelOrder.id)/cancel" 'buyer' | Out-Null
$rejectedCancel = Invoke-Demo 'POST' "seller/orders/$($paidCancelOrder.id)/cancel/decision" 'seller' @{ approve = $false; reason = 'Packed already' }
if ($rejectedCancel.status -ne 'Paid') { throw 'Rejected cancellation did not restore Paid' }
Invoke-Demo 'POST' "orders/$($paidCancelOrder.id)/cancel" 'buyer' | Out-Null
$acceptedCancel = Invoke-Demo 'POST' "seller/orders/$($paidCancelOrder.id)/cancel/decision" 'seller' @{ approve = $true }
if ($acceptedCancel.status -ne 'Cancelled') { throw 'Paid cancellation was not accepted' }
$cancelDetail = Invoke-Demo 'GET' "orders/$($paidCancelOrder.id)"
if (@($cancelDetail.refunds | Where-Object { $_.status -eq 'Succeeded' }).Count -ne 1) { throw 'Paid cancellation did not refund once' }

$single = Invoke-Demo 'GET' 'catalog/random?count=1'
$deliveryRequest = @{ addressId = $addresses[0].id; items = @(@{ productId = $single[0].productId; quantity = 1 }); checkoutKey = [guid]::NewGuid().ToString('N') }
$deliveryOrder = Invoke-Demo 'POST' 'orders' 'buyer' $deliveryRequest
Invoke-Demo 'POST' "orders/$($deliveryOrder.id)/pay/card" 'buyer' @{ number = '4111111111111111'; expiry = '12/30'; key = [guid]::NewGuid().ToString('N') } | Out-Null
Invoke-Demo 'POST' "seller/orders/$($deliveryOrder.id)/prepare" 'seller' | Out-Null
Invoke-Demo 'POST' 'carrier/fail-next' 'seller' @{ orderId = $deliveryOrder.id; direction = 'Outbound'; count = 3 } | Out-Null
$failedLabel = Invoke-Demo 'POST' "seller/orders/$($deliveryOrder.id)/ship" 'seller'
if ($failedLabel.status -ne 'ShipmentCreationFailed') { throw 'Carrier failure was not handled' }
$deliveryShipment = Invoke-Demo 'POST' "seller/orders/$($deliveryOrder.id)/ship" 'seller'
if (!$deliveryShipment.trackingNumber -or $deliveryShipment.id -ne $failedLabel.id) { throw 'Carrier retry created a duplicate shipment' }
foreach ($status in @('PickedUp','InTransit','OutForDelivery','DeliveryFailed','ReturningToSender','ReturnedToSeller')) {
    $event = Invoke-Demo 'POST' "shipments/$($deliveryShipment.id)/events" 'seller' @{ status = $status; eventId = [guid]::NewGuid().ToString('N'); note = 'Demo event' }
    if (!$event.applied) { throw "Delivery failure event $status was rejected" }
}
$deliveryRefund = Invoke-Demo 'POST' "seller/orders/$($deliveryOrder.id)/refund" 'seller' @{ reason = 'delivery-failed' }
if ($deliveryRefund.status -ne 'Succeeded' -or (Invoke-Demo 'GET' "orders/$($deliveryOrder.id)").status -ne 'Closed') { throw 'Delivery failure refund failed' }

Write-Output "PASS: frontend, checkout, card success/decline, tracking, returns, refunds, cancel, carrier retry, delivery failure"
