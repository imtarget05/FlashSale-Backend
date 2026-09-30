# ERRATA — Azure live-state evidence

**Status:** current. Supersedes the `control_plane_fqdn=null` reading in
`docs/evidence/e2e/azure-live-state.json` as originally generated.

**Issued:** 2026-09-30
**Scope:** classification correction only. No historical evidence file was
deleted, edited, or re-stamped.

---

## ERRATUM 1 — `control_plane_fqdn = null` was OUR BUG, not an Azure gap

### What the earlier evidence claimed

`docs/evidence/e2e/azure-live-state.json` recorded, for all three apps:

```
control_plane_fqdn        : null
control_plane_fqdn_is_null : true
ingress_object_present    : false
external_ingress          : null
```

and the accompanying prose asserted that "`az containerapp show` reports
fqdn=null and an empty ingress object for every app". The file classified this
as a control-plane/environment discrepancy and leaned on the HTTP probe to
override it.

### Root cause

**A wrong JSON path in our own probe script.**

`tools/e2e/probe_azure_live.py` read:

```python
live_fqdn = props.get("fqdn")          # WRONG — always null
ingress   = props.get("ingress") or {}  # WRONG — always null
```

The real Container Apps fields live one level deeper, under `configuration`:

```
properties.configuration.ingress.fqdn
properties.configuration.ingress.external
```

`properties.fqdn` and `properties.ingress` **do not exist** on a Container App
resource. They are null for every healthy app, deployed or not, so reading them
can never produce a signal — it can only ever produce `null`.

### Verification

Same command, both paths, same app, same moment:

```
properties.fqdn                        = None
properties.ingress                     = None
properties.configuration.ingress.fqdn   = "ca-maia-api.wittysand-b748274c.eastasia.azurecontainerapps.io"
properties.configuration.ingress.external = True
```

Azure was reporting the FQDN correctly the entire time. We were reading a field
that is always null and interpreting the emptiness as an outage signal.

### Corrected classification

| | |
|---|---|
| **Was** | `CONTROL_PLANE_METADATA_MISMATCH` — implied an Azure-side gap |
| **Is** | `MEASUREMENT_DEFECT` — a bug in our probe script |
| **Not** | `APPLICATION_UNAVAILABLE` |

The apps were, and are, reachable. See `current-live-state.json`.

### Fix

`tools/e2e/probe_azure_live.py` now reads the correct path, and records **both**
the correct field and the incorrect one:

```json
"control_plane_fqdn_source": "properties.configuration.ingress.fqdn",
"control_plane_fqdn": "ca-maia-api.wittysand-b748274c.eastasia.azurecontainerapps.io",
"incorrect_path_fqdn": null,
"incorrect_path_fqdn_note": "properties.fqdn is always null for a Container App; reading it previously produced a false 'control plane reports null' claim"
```

Carrying `incorrect_path_fqdn` in the output is deliberate: if the field is ever
repadded at the wrong level by a future Azure API change, the regression is
visible in the evidence file instead of hiding.

---

## ERRATUM 2 — HTTP `000` was scale-from-zero, not unavailability

During re-verification a short-timeout probe returned `000` (connection failure)
for all three apps, which looked like a fresh outage. It was not.

The Consumption plan scales to **0 replicas** when idle. A request that arrives
at a cold app gets a connection error until the cold start completes.

Measured on the same endpoints:

| Attempt | Timeout | MAIA `/health` |
|---|---|---|
| First | 12 s | `000` (exit 28, timeout) |
| Retry, sequential | 90 s | `200` in 0.44 s |

Sequential probing with an adequate timeout returns `200` on all three apps.

**Canonical rule adopted: health evidence is collected by SEQUENTIAL requests
with a timeout long enough to cover a cold start, never by firing parallel
short-timeout probes.** A single `000` against a consumption-plan app is not
evidence of failure; it is evidence of a cold start that was not waited out.

---

## ERRATUM 3 — the "test oracle was initially wrong" framing

`docs/checkpoints/portfolio-7-of-8.md` and the older probe notes describe this
as a case where "the test oracle was initially wrong". That description is
imprecise and flattering to the tooling.

The oracle did not disagree with reality. **The oracle was correct and the
instrument was broken**, and the instrument's own output (`fqdn: null`) was
believed over the observation (HTTP 200). The lesson is sharper than "the oracle
was wrong":

> An instrument that can only ever return a null is not a weak oracle; it is not
> an oracle at all. Before trusting a signal, check that it is capable of
> returning the opposite value.

This is now case 6 in `docs/INTERVIEW-CASEBOOK.md`.

---

## What was NOT changed

- No evidence file was deleted, renamed, or re-stamped.
- The `status=VERIFIED` claim for these deployments was correct all along and is
  now supported by evidence that agrees with the control plane.
- No Azure configuration was modified. This was a read-only investigation.

## Standing decision on public `/docs`

All three public APIs serve Swagger UI (`/docs` → HTTP 200). FastAPI is
constructed with default `docs_url` in every service, so **there is currently no
environment gate** — it is exposed by framework default, not by a deliberate
choice.

That distinction matters. "Exposed because nobody turned it off" is not an
architecture decision; it is the absence of one. The decision recorded here:

> **Demo environment:** Swagger is intentionally exposed for portfolio review.
> Production hardening is a documented gap, not an implemented control.

Follow-up owed (not implemented now, because no failure mode justifies breaking
the demo): gate `docs_url` behind an explicit env flag that defaults to on for
the portfolio demo and off for production. Tracked in
`docs/ACCEPTANCE-HISTORY.md`, not in code, until the flag is actually written.
