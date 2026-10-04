# Return automation, shipping weight and shipment claims

> **For agentic workers:** Implement inline with superpowers:executing-plans and a final independent review. Leave all changes uncommitted for the user to review in Sourcetree.

**Goal:** Automatically refund ordinary returns, price shipping by actual kilograms, and restrict tracking updates to the shipper who claimed the shipment.

**Architecture:** Extend the existing return, checkout and shipping services. SQL Server transactions and concurrency checks protect claims, receipt decisions and refunds. Preserve existing orders and financial records with an additive EF migration.

**Tech Stack:** .NET 8, EF Core 8 / SQL Server, Razor, vanilla JavaScript, executable commerce tests and Node UI tests.

**Spec:** `docs/BUSINESS_REQUIREMENTS.md` sections 3.1, 6 and 7; user-approved scope in this conversation.

## Global constraints

- Work in the existing checkout on master; no automatic branch, stage, commit, merge or push.
- Receipt timeout: 48 hours normally, configurable 45 seconds in Development, measured from return tracking Delivered.
- Preserve the existing full-order refund policy; differentiated return charges and partial refunds are outside this automation change.
- Actual kilograms, without rounding to whole kg: Hanoi-local 2 + max(0,w-1)*0.5; other routes 5 + max(0,w-1). Round final USD fee to two decimals.
- Require positive product weight and a seller pickup address for new checkout; save order/item weight and both endpoint address snapshots. Old orders keep their stored amounts.
- Missing legacy weights remain missing. Provide seller editing and explicit demo-fixture setup rather than inventing weights in a migration.
- Each outbound/return shipment is claimed separately and atomically. Only its assigned shipper may submit tracking.

## Review focus

- Return timeout and seller issue reporting must serialize against refunds; an escalated dispute stops automatic receipt/refund.
- Ordinary return holds must not be imported as legacy disputes or released early by the finance worker.
- Gateway failures retain held funds and a retryable return, without duplicate provider refunds or financial entries.
- Product/address edits after checkout must not change historical weight, shipping or pickup/delivery snapshots.
- Concurrent claims, repeated claims and access to another shipper's order must not expose tracking actions or payment data.

## Task 1: Ordinary return automation

Files: ReturnService, IReturnService, DisputeService/IDisputeService, ReturnsController, worker, finance guards, order detail DTOs, seller/buyer UI, commerce tests.

- [x] RED: approval holds funds; receiving a delivered return automatically refunds and repeated receipt does not refund twice.
- [x] Implement receipt deadline, bounded worker retries and escalated seller return-issue evidence, using the existing dispute audit and hold.
- [x] Verify timeout before/after boundary, issue stops refund, failed refund retries and existing dispute tests pass.

## Task 2: Weight-based shipping

Files: Product/order/item models, OrderPricing, CheckoutService, seller shipping settings API/UI, catalog DTO, buyer UI, EF mapping/migration, tests.

- [x] RED: 1.2kg is $2.10 Hanoi-local or $5.20 otherwise; zero/missing weight rejected and snapshots preserved.
- [x] Implement decimal kilograms, Hanoi route classification from seller/buyer addresses and seller product-weight/pickup configuration.
- [x] Verify fractional weights, country/region normalization, historical amounts and legacy-data compatibility.

## Task 3: Shipper claims

Files: ShippingInfo, ShipmentService/IShipmentService, ShippingController, OrdersController, shipper Razor/JS, migration, smoke test.

- [x] RED: unclaimed tracking is rejected, claim is idempotent for the owner and rejects another shipper.
- [x] Implement pending/mine filters, atomic SQL conditional claim, assigned-only tracking/detail, both endpoint snapshots.
- [x] Verify outbound and return independent assignment, terminal shipment restrictions and concurrency/query translation.

## Verification

- `dotnet run --project tests/G4.Commerce.Tests --no-restore --configuration Release`
- `node --test tests/*.test.cjs`
- `dotnet build G4_Project.sln --no-restore --configuration Release -p:UseAppHost=false`
- Generate and inspect additive migration / SQL script; document manual demo setup and smoke scenarios.

## Progress

- Planning: scope recorded; user authorized implementation directly in this conversation.
- Implementation: all three features delivered in the current master checkout; no stage/commit/branch/push.
- Independent review found concurrent finance release, overlapping ordinary-return/dispute holds and stale manual/worker receipt risks. Fixed all three and added regressions.
- Verification: commerce checks pass; 11 Node UI tests pass; Release solution build passes with zero warnings/errors. SQL migration applied to local CloneEbayDB and demo product weights/pickup seeded without inventory reset.
- SQL integration: fresh isolated G4_ReturnShipping_Test_20261003 baseline/migrations/seed passed smoke including real 45-second ordinary auto-refund, finance hold/refund, negotiation and separately claimed outbound/return tracking.
- Browser/API QA: concurrent SQL claims return one 200/one 409; wrong-owner tracking/detail is denied; seller settings save, 1.2kg Hanoi/other quotes, UI claim/tracking, seller return issue and admin seller-win resolution pass. Desktop/mobile screenshots inspected. Fixed mobile checkout overflow and description-field/dialog ID collision with a regression test.
- Guide: docs/RETURNS_AND_SHIPPING.md contains migration, demo setup, API routes and manual test steps. Existing requirements/run/finance/dispute/role docs updated to match.

