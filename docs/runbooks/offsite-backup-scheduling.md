# Runbook — off-site backup scheduling (Phase 3B)

## What is scheduled

`scripts/backup/scheduler/run-offsite-backup-scheduled.sh` → calls
`scripts/backup/run-offsite-backup.sh` (local pg_dump → validate → upload 3
objects to Azure → verify). Log:

```text
~/Library/Application Support/flashsale-offsite-backup/repo/logs/offsite-backup.log
```

## Install / switch / remove (macOS launchd)

```bash
# daily 02:30 local — the portfolio schedule
./scripts/backup/scheduler/install-offsite-scheduler.sh --daily

# every 90 seconds — EVIDENCE / TEST ONLY, unload afterwards
./scripts/backup/scheduler/install-offsite-scheduler.sh --test

# remove all agents
./scripts/backup/scheduler/install-offsite-scheduler.sh --unload

launchctl list | grep offsite        # confirm what is loaded
```

Prerequisites: `.env` in the repo root (git-ignored) containing
`POSTGRES_PASSWORD`; Docker Desktop running; `az login` valid (Entra ID).

## Why an install step exists (macOS TCC)

launchd agents cannot read `~/Downloads` (macOS privacy protection) — the naive
job dies with `Operation not permitted`. Therefore `--daily/--test` stage the
scripts into `~/Library/Application Support/flashsale-offsite-backup/repo/`
(byte-identical `rsync` copy, refreshed on each install) and point the plist at
that copy. The copy only contains backup scripts + `.env`, never keys.

Also required in the wrapper: an explicit `PATH` (`/opt/homebrew/bin`,
`/usr/local/bin`) because launchd starts jobs with a minimal environment —
otherwise `docker: command not found` (observed and fixed during the drill).

## RPO guarantee — read this before claiming anything

| Statement | Truth |
|---|---|
| "Daily backup runs at 02:30" | TRUE while the scheduler host is powered on and awake |
| "RPO ≤ 24h" | **CONDITIONAL**: only while this development machine stays on/awake; if the laptop sleeps for 3 days, the newest backup is 3 days old |
| "RPO ≤ 24h unconditionally" | FALSE today |

Mitigations available on macOS but not implemented (portfolio scope): `pmset`
wake schedules, `launchd` `StartCalendarInterval` + `Wake` behaviour, or moving
the job to a cloud scheduler/CI. The structural fix is a Kubernetes CronJob in
the cloud phase with a managed identity, which removes the laptop dependency
entirely (Phase 3C/7+, not this phase).

## Verification after any schedule change

```bash
launchctl list | grep offsite
tail -20 "$HOME/Library/Application Support/flashsale-offsite-backup/repo/logs/offsite-backup.log"
az storage blob list -c postgres-backups --account-name stflashsalebackup \
  --auth-mode login --prefix postgres/ --query '[?ends_with(name, `.dump`)].name' -o tsv
```

Expected: one new `postgres/YYYY/MM/DD/flashsale_<UTC>Z.dump` (+ `.sha256`,
`.metadata.json`) per scheduled run, each with a distinct timestamp.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Backup log shows `AuthorizationFailure` / `403` on every upload after a network or ISP change | workstation egress IP changed; the storage firewall runs `default_action = Deny` with an explicit allowlist (ADR-009) | get the current egress IP (`curl -s https://api.ipify.org`), put it in `infrastructure/terraform/backup-storage/terraform.tfvars` (`allowed_ip_rules`), re-apply that root. Emergency: `az storage account update -n stflashsalebackup -g rg-flashsale-data-protection --default-action Allow` (ARM control plane is not firewalled) |
| `docker: command not found` in log | launchd's minimal PATH | wrapper already exports `/opt/homebrew/bin:/usr/local/bin`; verify it was staged by a fresh `--daily` install |
| `Operation not permitted` | macOS TCC when reading `~/Downloads` | reinstall via `install-offsite-scheduler.sh` so scripts run from `~/Library/Application Support/…` |
| `KeyBasedAuthenticationNotPermitted` | an `az` call without `--auth-mode login` | add the flag; Shared Key is disabled by design |
