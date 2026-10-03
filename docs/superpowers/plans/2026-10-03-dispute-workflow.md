# Dispute workflow implementation plan

> Use superpowers:executing-plans to implement the approved conversation specification.

**Goal:** Buyer/seller negotiate with immutable evidence before admin intervention; demo deadlines are 45 seconds. Format every Razor view for readability.

**Architecture:** Extend the existing Dispute entity and add append-only DisputeEntry history. DisputeService owns transitions, authorization and deadlines; existing finance/return services remain the only owners of money movement and refund/return processing. SQL Server remains the runtime data source.

**Spec:** User-approved conversation on 2026-10-03: mandatory opening description/links, append-only supplements, seller response required before admin or deadline, automatic escalation for seller silence, buyer silence closes and releases hold, paginated role tabs/modal details, Vietnamese labels, formatted Views.

## Global constraints

- Seller and buyer response windows: 45 seconds, configurable; worker processes every 5 seconds.
- Evidence requires description and at least one HTTP(S) URL; submissions cannot be edited/deleted.
- Only one active case per order; evidence does not extend deadlines; seller cannot reset a pending proposal deadline.
- Admin sees only escalated cases and their outcomes; decision requires a reason.
- Refund failure leaves the case open and funds reserved; successful completion closes once.
- No page reload or scroll reset on updates; paginated primary lists.
- Preserve existing work; no automatic database migration, commit or push.

## Tasks

1. Write workflow tests covering opening/validation/ownership, immutable history, 45-second timeouts, agreement/refund retry, return flow, admin gate and repeat holds. Run RED; implement domain/contracts/service/EF mapping and run GREEN.
2. Add guarded controllers, order badges, legacy endpoint gates, worker and SQL migration; verify build and migration SQL.
3. Add buyer/seller/admin dispute UI with countdown, evidence links, proposals, decisions, modal detail and periodic updates. Test Vietnamese state/URL/countdown rendering; check JS syntax.
4. Format every Razor view without changing existing semantics; update run/authorization/finance docs and smoke scenario. Build Razor and run all suites.
5. Fresh whole-change review; fix material issues, verify and report migration/runtime limits.

## Review focus

- Timer racing with response or refund, and concurrent API submissions.
- Legacy fund-hold endpoints bypassing evidence or admin escalation.
- Refund/return failures causing early closure or releasing another order's money.
- Evidence URLs and untrusted descriptions rendered safely.
- Auto refresh disturbing a modal, a form, pagination or scroll position.
