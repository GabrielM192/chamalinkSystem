# Mpango wa kuhamia .NET 10 LTS · SASISHO17 (kipengele L)

## Kwa nini
* **.NET 8 LTS inaisha msaada tarehe 10 Novemba 2026** — baada ya hapo hakuna marekebisho ya usalama.
  Leo (Oktoba 2026) ChamaLink iko kwenye .NET 8.0.x yenye vifurushi vya karibuni (hakuna vulnerable package:
  `dotnet list package --vulnerable --include-transitive` = safi kwenye API na Tests).
* .NET 10 ni LTS (msaada hadi Novemba 2028), EF Core 10, Npgsql 10.

## Ratiba inayopendekezwa
| Hatua | Lini | Kazi | Kigezo cha kupita |
|---|---|---|---|
| 0 | sasa (S17) | Vifurushi vyote 8.0.x vya karibuni; test packages mpya; JWT 8.x | `dotnet list package --vulnerable` safi; tests zote kupita |
| 1 | wiki 1 | Tawi `net10`: `TargetFramework` → `net10.0` kwa miradi 5; SDK `global.json` 10.0.x | build bila onyo jipya la usalama |
| 2 | wiki 1 | Vifurushi: `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.Extensions.*` → 10.0.x; `Npgsql.EntityFrameworkCore.PostgreSQL` → 10.x | restore safi |
| 3 | wiki 2 | Kukagua mabadiliko yanayovunja: EF Core 10 (tafsiri za LINQ, `ExecuteUpdate` API mpya ya lambda), ASP.NET Core 10 (rate limiter, `IExceptionHandler`, OpenAPI), Npgsql 10 (`DateTime` Kind, JSON) | tests 255+ kupita; `NoHardcodedRules`, `TruthArchitecture` kupita |
| 4 | wiki 2 | QA kamili kwenye PG (full17.sql + migrations), pentest phase9, UI smoke EN/SW | matokeo sawa na .NET 8 (ripoti za Frank/Tunganege zinalingana kabisa) |
| 5 | wiki 3 | Staging siku 7 kwa data ya majaribio; kisha production (blue/green, rollback = picha ya .NET 8) | hakuna hitilafu mpya kwenye logs |
| **Mwisho** | **kabla ya 1 Nov 2026** | Production iko .NET 10 | — |

## Hatari na kinga
* **Tafsiri za tarehe (EAT/Kind=Utc)**: Npgsql inakataa `Kind=Unspecified` — majaribio ya `UtcOnlyInterceptor` yanaendelea kulinda.
* **Advisory locks (DbTx)**: SQL ghafi `pg_advisory_xact_lock(bigint)` haibadiliki; jaribio la P1-10 linathibitisha funguo.
* **ExecuteUpdate**: API ya EF 10 inakubali lambda ya kawaida — `AuthService.RefreshAsync` na mengine yarekebishwe kama kuna onyo.
* **Rollback**: hifadhi build ya .NET 8 na dump ya DB kabla ya kuhama; migrations za S17 hazitegemei toleo la runtime.

## Frontend
* `npm audit` = 0 baada ya `npm audit fix` (brace-expansion, source-map-js). Endesha `npm audit` kwenye kila PR (CI).
* Panga kuhamia Vite/React toleo jipya la LTS pamoja na hatua ya 4.

## CI inayopendekezwa (kuzuia kurudi kwa tatizo)
```yaml
- run: dotnet list package --vulnerable --include-transitive | tee vuln.txt && ! grep -q "has the following vulnerable packages" vuln.txt
- run: dotnet test --no-build
- run: npm ci && npm audit --audit-level=high && npm run lint && npm run build && npm run i18n:check
```
