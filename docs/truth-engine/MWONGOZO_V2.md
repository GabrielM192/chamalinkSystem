# Injini ya Ukweli v2 — Mwongozo wa Viongozi na Developer

## 1. Kanuni ya msingi
**Data ghafi pekee ndiyo inahifadhiwa. Taarifa zinahesabiwa upya kila mara.**
Mfumo hauhifadhi tena "10k mchango + 5k kianzio" wakati wa import. Unahifadhi mstari wa PDF
kama ulivyo (reference, saa, namba ya simu, kiasi, balance). Kila unapoomba taarifa, injini
inacheza upya malipo yote kwa mpangilio wa muda. Ukibadilisha kanuni au ukarekebisha kosa,
taarifa zote zinajirekebisha zenyewe. Hakuna data iliyoharibika inayobaki.

| Chanzo | Nani anaingiza | Inahesabiwa lini |
|---|---|---|
| M-Koba PDF (`MkobaRawTransactions`) | Kiongozi anapakia PDF | Mara moja, ikishaunganishwa na namba ya simu |
| Namba ya simu ↔ mwanachama (`MemberPhones`) | Kiongozi | Mara moja |
| Manual: pesa ya mkononi au Kianzio (`FinancialEvents`, Source=Manual) | Kiongozi A anaomba | **Baada ya kiongozi B kuidhinisha**, na tarehe ≤ `ManualEntryCutoff` |
| Pengo la PDF (`MkobaStatementGaps`) | Mfumo unagundua; kiongozi A anapendekeza mwenyewe | Baada ya kiongozi B kuidhinisha |
| Msamaha wa faini au nyongeza ya muda (`TruthAdjustments`) | Kiongozi A | Baada ya kiongozi B kuidhinisha |
| Mikopo (`Loans`) | Mfumo wa mikopo uliopo | Ratiba sawa hadi `DueDate` |

## 2. Fomula (kwa kila mwanachama, kwa mpangilio wa muda)
1. **Deadline ya mwezi M** = tarehe `DueDateDay` (5) ya mwezi M+1, saa 23:59:59, + `GracePeriodDays` (Ukonga = 0),
   + siku za nyongeza zilizoidhinishwa kwa mwezi huo. Ukilipa kufikia tarehe 5 hutozwi faini. Kuanzia tarehe 6 unatozwa.
2. **Deadline ikifika:** kwanza akiba inalipia (rejesho la mkopo, kisha mchango). Kama bado kuna upungufu,
   faini ya 5,000 inatozwa. Faini haitozwi kama msamaha umeidhinishwa. Kwa Ukonga
   (`FineOnPartialPayment=false`), faini haitozwi pia kama mchango ulilipwa kwa sehemu.
3. **Kila malipo yanagawanywa hivi:** Kianzio (kwa mwanachama mpya au manual iliyowekwa alama ya JoinFee) →
   marejesho ya mkopo, la zamani kwanza → mchango na faini ya mwezi wa zamani kabisa → Kianzio → **akiba**.
4. **Kwenye msiba au sherehe:** deni la Kianzio linakatwa kwanza. Tumia `GET /api/truth/member/{id}/welfare-deduction?amount=`.

## 3. Hatua za kurekebisha Ukonga (kwa mpangilio)
```bash
# 0. BACKUP — lazima
pg_dump -Fc -d chamalink -f backup_$(date +%F).dump
# 1. Migrations (angalia docs/truth-engine/SCHEMA_YA_DB.md)
dotnet ef database update -p ChamaLink.Infrastructure -s ChamaLink.API
```
| # | Endpoint | Maelezo |
|---|---|---|
| 2 | `PUT /api/truth/{groupId}/settings` | `{"contributionStartDate":"2026-01-01","dueDateDay":5,"gracePeriodDays":0,"fineOnPartialPayment":false,"manualEntryCutoff":"2026-08-31T23:59:59","foundingMembersCutoff":"2026-03-31T23:59:59"}` |
| 3 | `POST /api/truth/{groupId}/reset?dryRun=true` | Inaonyesha kitakachofutwa bila kufuta |
| 4 | `POST /api/truth/{groupId}/reset?dryRun=false&confirm=Ukonga mkoba wing` | Inafuta data za fedha za majaribio. Wanachama, policy, namba za simu na audit havifutwi |
| 5 | `POST /api/truth/{groupId}/import-mkoba` (file=PDF) | Inahifadhi mistari 191 ghafi. Ripoti inaonyesha mapengo 2 (Japhet, Tito) na namba za simu ambazo hazijaunganishwa |
| 6 | `GET /api/truth/{groupId}/phones` → `POST .../phones {memberId, phone}` | Kila namba iliyoonekana M-Koba inaunganishwa na mwanachama wake |
| 7 | `POST .../gaps/{gapId}/resolve {memberId, evidence}` → kiongozi mwingine: `POST .../gaps/{gapId}/decision {approve:true}` | Kwa Japhet na Tito. Ushahidi ni SMS au statement ya Vodacom |
| 8 | `POST .../manual-payments {memberId, amount, occurredAt, isJoinFee:true, reason}` → kiongozi mwingine: `.../decision` | Mf. Kianzio cha Frank 10,000 alichompa mtunza-hazina mkononi |
| 9 | `POST .../adjustments {memberId, kind:"FineWaiver", year, month, reason}` → `.../decision` | Mf. Abdulla na Fauzia, Apr, kama viongozi walisamehe |
| 10 | `GET /api/truth/group/{groupId}` · `GET /api/truth/{groupId}/unattributed` | Taarifa ya ukweli pamoja na pesa ambayo mwenyewe hajulikani |

**Muhimu:** baada ya hapa, usitumie tena import ya zamani (`/api/mkoba-import`) wala
`/api/debug/reclassify/preview`. Zinaandika mgawanyo wa zamani. Tumia `/api/truth/...` pekee.

## 4. Ulinzi dhidi ya udanganyifu
- **Maker-checker:** aliyeomba hawezi kuidhinisha ombi lake mwenyewe. Hii inatumika kwa manual, mapengo na msamaha.
- **Manual baada ya `ManualEntryCutoff` inakataliwa.** Kuanzia hapo M-Koba pekee ndiyo ukweli.
- **Kianzio hakiwezi kuzidi kiasi kilichobaki,** hata ukijumlisha maombi yanayosubiri idhini. Hii inazuia kurudufu.
- **Reference moja inahesabiwa mara moja tu.** PDF zinazopishana (mf. ya Jul na ya Aug) ni salama kupakia.
- **Namba moja ya simu = mwanachama mmoja.**
- **Mnyororo wa balance unakaguliwa kila import.** Mstari uliokosekana kwenye PDF unaonekana kama pengo, haupotei kimya.
  PDF kamili ikipakiwa baadaye, pengo lisiloidhinishwa linajiondoa lenyewe.
- **Kila hatua ya kuandika inarekodiwa kwenye `AuditEvents`.**
