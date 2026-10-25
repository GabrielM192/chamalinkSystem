# ChamaLink — Kubadilisha siri (Secrets Rotation Runbook) · SASISHO17

> Kipengele **M** cha ripoti ya ukaguzi wa usalama ya SASISHO16. Hati hii inaeleza **siri zipi zipo, zinakaa wapi, na
> jinsi ya kuzibadilisha bila kupoteza data wala kuwafungia watumiaji kimakosa**.

## 1. Kanuni za msingi

1. **Hakuna siri kwenye git.** `appsettings.json` na `appsettings.Development.json` zina vishika-nafasi tu.
   Siri halisi zinawekwa kama *environment variables* (`Jwt__Key=...`) au kwenye msimamizi wa siri (systemd
   `EnvironmentFile=` yenye ruhusa `0600`, Docker/K8s secrets, Azure Key Vault, AWS Secrets Manager, n.k.).
2. **ProductionGuard** (`ChamaLink.API/Security/ProductionGuard.cs`) inakataa kuanzisha API nje ya `Development`
   ikiwa: `Jwt:Key` ni dhaifu au ni ufunguo uliovuja wa sandbox; `Benefits:EvidenceEncryptionKey` haipo au si byte 32;
   `Benefits:EvidencePath`/`MaxEvidenceBytes` hazipo; CORS ina localhost au si https; swichi hatari
   (`Truth:AllowReset`, `Legacy:*`, `Errors:IncludeExceptionDetails`) ziko `true`.
3. Kila badiliko la siri liandikwe kwenye daftari la mabadiliko (tarehe, nani, sababu) — **si thamani yenyewe**.
4. Ratiba: badilisha **mara moja** ukishuku uvujaji; vinginevyo kila **miezi 6** (JWT, DB) na kila **miezi 12** (ufunguo wa vyeti).

## 2. Orodha ya siri

| Siri | Config key | Inatumika kwa | Ukibadilisha… |
|---|---|---|---|
| Ufunguo wa JWT | `Jwt:Key` | Kusaini access tokens (dakika 15) | Access tokens zote za zamani zinakataliwa papo hapo; refresh cookie (siku 14) bado inafanya kazi → watumiaji hawalazimiki kuingia tena |
| Nenosiri la DB | `ConnectionStrings:DefaultConnection` | PostgreSQL | API lazima ianzishwe upya na nenosiri jipya |
| Ufunguo wa vyeti | `Benefits:EvidenceEncryptionKey` | AES-256-GCM ya vyeti + PDF za M-Koba kwenye diski | Faili za zamani zinasomwa kwa `Benefits:EvidenceEncryptionPreviousKeys` |
| Refresh tokens | (DB: `RefreshTokens`, zimehifadhiwa kama SHA-256) | Vikao | `RevokeAllSessionsAsync` / `TokenVersion++` |

## 3. Taratibu

### 3.1 `Jwt:Key`
```bash
NEW=$(openssl rand -base64 64 | tr -d '\n')
# weka kwenye EnvironmentFile ya huduma (mf. /etc/chamalink/api.env, chmod 600, mmiliki: chamalink)
sudo sed -i "s|^Jwt__Key=.*|Jwt__Key=${NEW}|" /etc/chamalink/api.env
sudo systemctl restart chamalink-api
```
* Matokeo: access tokens zilizopo zinakataliwa (401) → frontend inatumia refresh cookie kupata token mpya kimya kimya.
* **Kama ufunguo ULIVUJA**: pia futa vikao vyote: `UPDATE "Users" SET "TokenVersion" = "TokenVersion" + 1;`
  na `UPDATE "RefreshTokens" SET "RevokedAt" = now() WHERE "RevokedAt" IS NULL;` — kila mtu ataingia upya.
* Ufunguo wa sandbox `ChamaLinkSandboxSecretKey2024…` umeorodheshwa ndani ya ProductionGuard na **hautakubaliwa kamwe**.

### 3.2 Nenosiri la PostgreSQL
```bash
NEWPW=$(openssl rand -base64 32 | tr -d '/+=\n')
sudo -u postgres psql -c "ALTER ROLE chamalink WITH PASSWORD '${NEWPW}';"
sudo sed -i "s|Password=[^;]*|Password=${NEWPW}|" /etc/chamalink/api.env
sudo systemctl restart chamalink-api
```
* Mtumiaji wa DB wa API **asiwe superuser**; apewe ruhusa za schema ya ChamaLink tu. Migrations zifanywe na mtumiaji tofauti.
* Funga `pg_hba.conf` kwa IP ya server ya API tu, `scram-sha-256`.

### 3.3 `Benefits:EvidenceEncryptionKey` (bila kupoteza faili)
Muundo wa faili: `CLENC1 | nonce | tag | ciphertext`, AAD = njia ya faili (faili haliwezi kuhamishwa kwa kesi nyingine).
SASISHO17 inaruhusu **funguo nyingi za kufungua** (`Benefits:EvidenceEncryptionPreviousKeys`, base64 zimetenganishwa kwa koma);
**kusimba** kunatumia ufunguo wa sasa pekee.

```bash
OLD=$(grep '^Benefits__EvidenceEncryptionKey=' /etc/chamalink/api.env | cut -d= -f2-)
NEW=$(openssl rand -base64 32 | tr -d '\n')
sudo sed -i "s|^Benefits__EvidenceEncryptionKey=.*|Benefits__EvidenceEncryptionKey=${NEW}|" /etc/chamalink/api.env
echo "Benefits__EvidenceEncryptionPreviousKeys=${OLD}" | sudo tee -a /etc/chamalink/api.env >/dev/null
sudo systemctl restart chamalink-api
```
* Faili mpya → ufunguo mpya. Faili za zamani → zinafunguka kwa ufunguo wa zamani. Hash (SHA-256 ya maudhui) inakaguliwa kila upakuaji.
* Ufunguo wa zamani **usifutwe** mpaka faili zote za zamani zimesimbwa upya (awamu ijayo: zana ya re-encrypt) au mpaka sera ya
  kuhifadhi kumbukumbu (miaka 7) imekwisha.
* **Hifadhi nakala ya funguo zote nje ya server** (mf. password manager ya mwenyekiti + mtunza-hazina). Ufunguo ukipotea, vyeti vimepotea.

### 3.4 Vikao vya mtumiaji mmoja (simu imeibiwa)
`AuthService.RevokeAllSessionsAsync(userId)` → `TokenVersion++` + refresh tokens zote zinafutwa. Access token ya zamani inakataliwa
kwenye ombi linalofuata (TokenVersion inakaguliwa dhidi ya DB).

## 4. Baada ya kubadilisha — ukaguzi
1. `journalctl -u chamalink-api | grep ProductionGuard` → hakuna `Critical`.
2. Ingia kama kiongozi; fungua cheti cha kesi ya zamani (inathibitisha PreviousKeys) na pakia kipya.
3. `SELECT count(*) FROM "AuditEvents" WHERE "Timestamp" > now() - interval '1 hour';` — shughuli zinaendelea kurekodiwa.
4. Andika kwenye daftari: tarehe, siri ipi, nani alifanya, sababu.

## 5. Ikitokea uvujaji (incident)
1. Badilisha siri husika (§3) **mara moja**; kwa JWT pia futa vikao vyote.
2. Hamisha logs za `AuditEvents` na `LoginFailures` za siku 30 kwa uchunguzi (zinalindwa na hash-chain).
3. Wajulishe viongozi wote; angalia vitendo vya fedha (`ManualPayment`, `Adjustment`, `StatementImport`, `BenefitCase`) vya kipindi husika.
