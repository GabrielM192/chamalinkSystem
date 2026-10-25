# ChamaLink: Trust Model and Residual Risks (SASISHO17.2)

This document explains **whom the system trusts, for what, and where that trust ends**. It is the
honest answer to the question "what can people who collude still do?". Every risk listed here has
(a) a test or control that limits it and (b) a plan for reducing it further.

## 1. Sources of truth

| Data | Source of truth | Control |
|---|---|---|
| Contributions/payments after onboarding | M-Koba statement (PDF) | Import is **staged**: one leader uploads, a **different** leader approves (maker ≠ checker); SHA-256 of the file is stored; duplicate transaction refs are rejected; size and page limits apply |
| Historical (pre-M-Koba) data | Manual entry | Allowed only before the group's cutoff date (`ManualEntryCutoff`, a setting); maker ≠ checker; the maker cannot be the beneficiary |
| Member identity | Phone number | A new phone link stays **Pending** until another leader approves it |
| Leadership | Role-change votes | Proposer and target never vote; there is a cooling-off period (`LeaderCoolingOffDays`); every member is notified |
| Welfare payouts | Group policy plus a certificate | Multi-leader chain; evidence is encrypted; the amount is computed by the system |

## 2. Residual risks (stated openly)

### B-1: Bootstrap Sybil (a lone chair with fake accounts)
**Situation:** a group has a chair **only** (no other effective leader) and `LeaderCoolingOffDays = 0`.
The chair can create two fake accounts, approve their membership himself, nominate one as Treasurer, and have the other vote.

**Current controls:**
- When **any** other effective leader exists, only leaders may vote, so fake accounts cannot vote
  (test: `Sybil_Chain_Fails_When_The_Group_Has_A_Real_Second_Leader`).
- With `LeaderCoolingOffDays >= 1`, newly linked accounts cannot vote, and a new leader holds no powers
  until the period ends (test: `Sybil_Chain_At_Bootstrap_Is_Blocked_By_The_CoolingOff_Policy`).
- The audit trail cannot be deleted, and all members are notified (test: `..._Is_The_Documented_Residual_Risk_And_Is_Loud`).

**Recommendation for every group:** set `LeaderCoolingOffDays` to 7 or more and `RoleChangeApprovals` to 2 or more in Settings
**before** adding members. The value is not hard-coded because it is group policy.

### B-2: Authenticity of the M-Koba PDF (two leaders colluding)
**Situation:** two leaders collude: one uploads an edited PDF and the other approves it.
**Current controls:** maker ≠ checker; the file hash is stored (it can be checked against the original later); every transaction carries
a `TransactionRef`; members see their own statements and a mismatch is visible (financial transparency).
**Future plan:** (1) a direct M-Koba/bank API where available; (2) verification of a digital signature/QR on the PDF
if the provider supplies one; (3) automatic comparison of the statement's opening balance against the previous closing balance.

### B-3: The platform itself (server/DB operators)
Whoever controls the server and the DB can change data. Controls: secrets rotation (SECRETS_ROTATION.md),
ProductionGuard, encrypted evidence, and backups. The plan is to make the audit log append-only/hash-chained.

## 3. Supported platform
.NET 8 LTS reaches end of support on **10 Nov 2026**; see `DOTNET10_UPGRADE_PLAN.md`. Every package is on the latest
8.x patch (checked with `dotnet list package --outdated`; newer versions are 10.x only).
