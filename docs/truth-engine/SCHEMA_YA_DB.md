# Schema ya DB ya Injini ya Ukweli — chanzo ni MIGRATIONS pekee

`ukonga_policy.sql` (SQL iliyoandikwa kwa mkono) imeondolewa (TATHMINI 2026-09-26 #8):
ilikuwa nakala ya pili ya schema ambayo ingeweza kutofautiana na migrations.

Migrations zinazohusika:

| Migration | Inaongeza |
|---|---|
| `20260925150000_TruthEngineSettings` | safu za cutoff kwenye GroupPolicies |
| `20260925170000_TruthEngineRawData` | TruthGroupSettings, MkobaRawTransactions, MemberPhones, MkobaStatementGaps, TruthAdjustments |

## Kutumia
```bash
# Njia ya kawaida
dotnet ef database update -p ChamaLink.Infrastructure -s ChamaLink.API

# Ukihitaji SQL (mf. DBA aipitie kwanza) — itengenezwe KUTOKA kwenye migrations, isiandikwe kwa mkono:
dotnet ef migrations script 20260923100000_UnifyPolicyDueDate --idempotent -p ChamaLink.Infrastructure -s ChamaLink.API -o truth.sql
```

## Mipangilio ya kikundi (Ukonga)
Haiwekwi kwa SQL tena. Viongozi wanaiweka kwenye **Mipangilio** (frontend) au
`PUT /api/truth/{groupId}/settings`: mchango 10,000, faini 5,000, Kianzio 50,000, siku ya mwisho 5,
nyongeza 0, waanzilishi hadi 31 Machi 2026, faini kwa malipo pungufu = hapana.

## Endpoints za zamani
Zimefungwa (410 Gone) kwa `[LegacyEndpoint]`. Kuziwasha kwa muda (uhamisho wa data tu):
`appsettings.json` → `"Legacy": { "Endpoints": true }` au `export Legacy__Endpoints=true`.
