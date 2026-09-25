# G4 Checkout and Shipping MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the standalone one-seller demo specified in the approved design, from random order summary to payment, tracking, returns, and refunds.

**Architecture:** Keep the existing ASP.NET Core API, MVC frontend, EF Core, and SQL Server. Add domain services behind payment and shipping interfaces. Seed demo buyer/seller/products, expose narrow backend endpoints, and render the workflow in MVC. PayPal calls sandbox; card and carrier are explicit simulators.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core 8, SQL Server, Razor MVC, PayPal REST sandbox, local mail capture.

**Spec:** `docs/superpowers/specs/2026-09-24-g4-checkout-shipping-mvp-design.md`

## Global Constraints

- One seller per order; buyer selects 1-5 random product lines.
- USD only; shipping $2 same state / $5 different state; payment expiry 15 minutes; return window 7 days.
- PayPal sandbox and fake credit card; no real card data stored.
- Whole-order return and whole-order refund only.
- Existing `InitialCreate.Up()` is empty: create a clean database from the supplied SQL baseline before applying new migrations. Never run the baseline script over an existing populated database.
- Preserve the pre-existing `backend/backend.csproj` change.

## Review Focus

- Duplicate checkout submission must not reserve inventory twice.
- PayPal timeout must not become a false payment failure or allow a second capture.
- Shipment retry must not create a second tracking number.
- A late tracking event must not roll shipment state backward.
- Refund retry must never exceed the captured amount.

---

### Task 1: Domain rules and tests

**Files:** Create `backend/Checkout/OrderPricing.cs`, `backend/Checkout/OrderState.cs`, `backend/Checkout/ShippingState.cs`, `backend/Checkout/FakeCardProcessor.cs`; create `tests/G4.Mvp.Tests/` test harness.

**Interfaces:** Pricing consumes line prices, quantities, coupon percent, and same-state flag; returns subtotal, discount, shipping, and total. State functions accept current state and requested action; return next state or reject.

- [ ] Write tests for exact amount calculation, invalid quantity, duplicate payment success, shipment transitions, late event rejection, and card scenarios.
- [ ] Run tests and confirm failure for missing behavior.
- [ ] Implement the smallest pure services satisfying tests.
- [ ] Run all tests and the solution build.

### Task 2: Database foundation and demo seed

**Files:** Modify existing model partial classes and `ApplicationDbContext`; create an EF migration for MVP fields/tables; create demo seed service and local database setup instructions.

**Interfaces:** Database stores order snapshots, payment attempts, shipment events, returns, refunds, notification outbox, and seeded demo actors/products.

- [ ] Write database-level tests for unique idempotency IDs, required data, and seed idempotency.
- [ ] Run tests to observe the missing schema/seed behavior.
- [ ] Add schema and seed data; use SQL baseline once for a fresh database.
- [ ] Run tests and verify the resulting schema if SQL Server access is available.

### Task 3: Checkout and payments

**Files:** Create API controllers and services for random cart, quote, create order, card payment, PayPal create/capture/reconcile, payment expiry, and refund provider abstraction.

**Interfaces:** JSON API yields cart, quote, order details, and payment result; all mutations have idempotency keys and state validation.

- [ ] Write API/service tests for quote tampering, inventory reservation, duplicate requests, expired order, card outcomes, PayPal uncertain outcome, and provider callback replay.
- [ ] Run tests and verify the missing behavior fails.
- [ ] Implement backend flow; configure PayPal sandbox credentials through configuration/secrets only.
- [ ] Run tests and solution build.

### Task 4: Shipping, cancellations, returns, refunds, email

**Files:** Create shipping simulator API/client, tracking and retry service, cancellation/return/refund service, notification outbox worker, and API endpoints.

**Interfaces:** Seller and demo controls drive legal events; buyer reads timeline and requests cancellation/return; payment adapter refunds original capture.

- [ ] Write tests for retry uniqueness, event order, cancellation windows, whole-order return, refund cap, and notification deduplication.
- [ ] Run tests and verify missing behavior fails.
- [ ] Implement services, worker, and endpoints.
- [ ] Run tests and solution build.

### Task 5: MVC demo UI

**Files:** Modify `frontend/Program.cs` and shared layout; create checkout, orders, tracking, returns, seller/demo controllers and Razor views; add focused CSS.

**Interfaces:** MVC calls backend API by configured base URL; browser actions use role-aware demo session and post mutations to backend.

- [ ] Write meaningful UI/controller smoke tests for order summary, disabled payment when invalid, tracking states, and role switch.
- [ ] Run tests and observe missing behavior.
- [ ] Implement the screens and responsive layout.
- [ ] Run tests, build, and manual end-to-end browser checks.

### Task 6: End-to-end verification and handoff

**Files:** Update README/demo instructions and the design document only if actual behavior differs; preserve a list of deliberate scope cuts.

- [ ] Run clean build and full test suite.
- [ ] Exercise success, failure, expiry, retry, delivery, return, and refund paths against the running app.
- [ ] Review changed files and confirm no secrets or card data entered source/logs.
- [ ] Report working flows, environment prerequisites, limitations, and any unverified integration steps.
