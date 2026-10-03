# Progress: 2026-10-03-dispute-workflow.md

- Baseline: clean Git status, commerce tests pass. Branch codex/dispute-workflow created.
- Ruling: implement in the current checkout on a feature branch so the user's current project receives the approved changes; no additional worktree consent requested.
- Ruling: retain real configurable defaults but use 45-second Development deadlines and 5-second worker ticks; all UI displays actual configured deadline.
- Pre-flight: DisputeService consumes finance hold/refund APIs. Repeated cases require per-case hold keys and the latest hold amount; existing signatures stay compatible through optional parameters.
- Pre-flight: old fund-hold routes must delegate to the new guarded workflow so they cannot bypass evidence or negotiation.
- Completed: domain/contracts/service, append-only history guard, owner checks, legacy endpoint gates, order badges, notifications and EF migration with SQL review script.
- Completed: buyer/seller/admin paginated dispute tabs, modal actions/details, countdown, immutable-evidence notice and incremental order-row updates including cases removed by the open filter.
- Completed: all 15 Razor Views formatted vertically; preserved brand letter spacing with inline-flex. Run/architecture/role/finance docs updated and DISPUTES.md added; smoke scenario now negotiates return/refund with evidence.
- Fresh review found: external carrier timeout rolling back agreement; one failing case aborting maintenance; return-window crossing rejecting a timely opened dispute. Fixed by committing acceptance first, isolating retries and honoring a persisted return agreement. Active disputes block automatic order closure.
- Review follow-up found: sequential external calls delaying deadlines. Fixed by processing deadlines first and bounding the whole agreement batch (Development 3s; default 5s). Regression uses a genuinely stalled gateway that honors cancellation.
- Additional fixes: refund entry points serialize on SQL order lock before calling provider; per-case hold keys support reopening; finance hold descriptions fit the existing 500-character column while full evidence remains intact.
- Verification: Release solution build passed with UseAppHost=false, 0 warnings/errors; console commerce/dispute checks passed; 8 JavaScript tests passed; JS and PowerShell smoke syntax checked. Migration SQL generated successfully. Final model/diff checks recorded below.
- Runtime limits: migration not applied to the user's SQL Server; no live SQL/browser smoke run or real PayPal call performed. Existing running applications were not terminated. No commit or push.
- Final checks: EF reports no pending model changes; diff --check, all JavaScript syntax checks and PowerShell smoke parser pass; console suite rerun after long-evidence regression passes.
