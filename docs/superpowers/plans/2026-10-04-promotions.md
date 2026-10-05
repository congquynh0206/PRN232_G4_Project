# Promotions Implementation Plan

> For agentic workers: use the approved spec and TDD, with independent pricing/UI work coordinated through the interfaces below and one final whole-change review.

**Goal:** Implement all five seller promotions, admin coupons, checkout and funding-aware settlement/refund.

**Architecture:** Shared Promotion management and immutable order snapshots. Pure domain pricing chooses valid combinations; SQL transactions reserve coupon limits; successful payments consume once and cancellation/expiry releases once.

**Tech Stack:** .NET 8, EF Core SQL Server, Razor, vanilla JavaScript, Node tests and Playwright.

**Spec:** ../specs/2026-10-04-promotions-design.md

## Global constraints

- Existing checkout, existing branch; no commits, no primary database changes during development.
- Preserve legacy orders, coupon usage, images and configured finance fees.
- USD decimal rounding; one seller/order; snapshot prices at creation; one coupon/order.
- Zero buyer payable settles internally; never send a zero charge/refund to PayPal.
- Full five types: Sale, Volume, Order, Coupon, Shipping.

## Review focus

- Coupon last-slot races and repeated payment/callback must never consume twice.
- Funding subsidy refund must reverse seller gross and platform subsidy without refunding buyer extra.
- Legacy pending/paid orders and codes must continue to work after migration.
- Form fields hidden by promotion type must not prevent submission or leak obsolete values.
- Quote changes must require buyer review before creating a differently priced order.

## Tasks

- [x] Domain pricing: tests RED, implement Sale/Volume/Order/Coupon/Shipping with allocation, combination and zero totals; tests GREEN.
- [x] Persistence/management: ownership/validation tests RED, Promotion/targets/tiers/usage/snapshots/audit, versioned CRUD/list/options API; tests GREEN.
- [x] Checkout lifecycle: tests RED, quote/fingerprint/snapshots/reserve/consume/release and funding-aware finance/refund; tests GREEN.
- [x] Seller/admin UI and buyer discount presentation: Node tests RED, shared promotion panel, conditional forms/list/filter, checkout breakdown and immutable details; tests GREEN.
- [x] Migration: generate additive EF migration, legacy coupon conversion/usage, inspect and apply on QA DB only.
- [x] Build/suites and live SQL/browser QA: all five forms, stack rules, concurrent last coupon, old code compatibility, payment/refund and mobile.
- [x] Read-only fresh reviewer; fix material findings and rerun affected checks.

## Shared interfaces

Domain `G4.Domain.Rules`: PromotionPriceLine, PromotionRule, PromotionTierRule, PromotionPriceResult, AppliedPromotion, PromotionLinePrice; `PromotionPricing.Calculate(lines, rules, shippingBase, couponCode)`.

Management JSON paths `/seller/promotions` or `/admin/promotions`: `/page`, `/options`, `/{id}`, POST root, POST `/{id}/edit`, POST `/{id}/state`. Page query page/pageSize/filter/search/type; admin owner filter optional. Detail/list item uses id/name/type/code/fundingSource/sellerId/value/isPercent/freeShipping/cap/minSubtotal/minQuantity/startAt/endAt/isPaused/adminPaused/version/maxUsage/maxUsagePerBuyer/budget/productIds/categoryIds/tiers/status/usedCount/reservedCount/spent/reservedAmount. Type labels Vietnamese in UI. Tier object minQuantity/percent. State body paused/reason/version.

Options returns products (id,title,categoryId,sellerId), categories(id,name), sellers(id,name); seller options own products only. All times UTC; datetime-local converted explicitly.

Quote retains subtotal/discount/shipping/total/totalWeightKg, adds shippingBase/shippingDiscount/sellerDiscount/platformSubsidy/sellerGross/pricingFingerprint/lines/promotions. CheckoutRequest adds ExpectedTotal and PricingFingerprint optional only for legacy compatibility; new UI always sends both.

## Execution ledger

- User explicitly requested “ok code đi” after reviewing the spec; execute continuously without another design/plan confirmation.
- Ruling: stay in existing checkout and leave all changes uncommitted, matching the user's established workflow.
- Independent pricing and presentation can proceed in parallel after agreeing these interfaces; persistence/payment integration remains coordinated by root.
- RED/GREEN demonstrated for pricing, ownership/validation, checkout lifecycle/address/PayPal, finance and frontend payloads/rendering.
- QA restored the historical backup into `G4_Promotions_Test_20261004`: all 47 original orders retained as schema 0, all 10 successful historical coupon uses mapped; no primary database changes.
- SQL migration rerun is idempotent; invalid coupon preflight throws 51001 and rolls back the surrounding transaction/history.
- Live SQL passed last-slot/count and budget races, same checkout key concurrency, two card payments, paused snapshots, funding/refund and internal zero totals.
- Follow-up SQL passed parallel cancellation, cancellation concurrent with checkout and worker expiry restoring stock/usage. Restock is an atomic SQL increment.
- Browser passed all five forms, role oversight/history, buyer funded quotes and invalid-coupon guard at desktop/mobile; no overflow or script errors. Node suite: 39/39 passed.
- Fresh review findings corrected: lock order, stale PayPal responses/unresolved attempt selection, address contents in fingerprint, zero-benefit coupon identity and fail-fast deployment script.
- Implementation tradeoff: one transaction-owned reservation gate serializes promotion management and checkout/order mutations in this simulation. Standalone finance also locks by seller; payment transactions use ReadCommitted and per-order application locks, coupon creation/reservation uses Serializable.
- Main database deployment remains a separate approval step per spec section 8. Manual test instructions: `docs/PROMOTIONS_TEST_GUIDE.md`.
- User separately approved main deployment: “Có, chạy migration vào database chính”. Created and verified COPY_ONLY/CHECKSUM backup, applied `AddPromotions` to `CloneEbayDB`, confirmed six new tables and the existing coupon mapping. All before/after data hashes and stock/counts match; primary still has 0 orders and no QA data.
- Stopped only the verified QA server helpers after verification to release DLL locks. QA database and screenshots remain available; existing app configuration is unchanged.
