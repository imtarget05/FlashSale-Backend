# Backup storage environment values (Portfolio Mode).
# IP allowlist: the storage firewall runs default_action = Deny, so the operator
# egress IP must be listed or every upload/download/restore fails with 403.
# NOTE: Azure expects a bare IP for a single host (no "/32" suffix) or a CIDR
# with a 0-30 prefix; "1.2.3.4/32" is rejected by the provider.
# Update it when the workstation egress IP changes (ADR-009).
allowed_ip_rules = ["115.76.179.191"]

# Retention (ADR-008): current blobs live 30 days, non-current versions 90 days.
retention_current_days    = 30
retention_noncurrent_days = 90
