using ChamaLink.API.Filters;
using ChamaLink.API.Extensions;
using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using ChamaLink.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ChamaLink.API.Controllers;

// Treasury Excel (monthly-summary) import.
//
// This is the treasurer's manual ledger import path - NOT the M-Koba
// statement path. The Excel is a monthly grid (member = row, month =
// column) with no daily dates and no transaction references, so it goes
// through TreasuryImportService which posts clean back-dated ledger
// entries directly (no waterfall, no auto-fine) and matches members by
// name with a confirmation step.
//
// Two-step flow so the treasurer never corrupts data on a wrong guess:
//   1. preview  - parse + match, write NOTHING, return the mapping for
//                 review (exact matches, fuzzy candidates, planned
//                 amounts, deferred MSIBA/SHEREHE).
//   2. commit   - parse + match + post (idempotent via TREAS- refs).
//
// Governance: same ImportApproval policy as the M-Koba upload (defaults
// to Treasurer + Chairperson).
[LegacyEndpoint("/api/truth/{groupId}/manual-payments (kumbukumbu za kabla ya M-Koba, zinaidhinishwa na kiongozi wa pili)")]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TreasuryImportController : ControllerBase
{
    private readonly TreasuryImportService _importService;
    private readonly GroupAuthorizationService _groupAuth;

    public TreasuryImportController(
        TreasuryImportService importService,
        GroupAuthorizationService groupAuth)
    {
        _importService = importService;
        _groupAuth = groupAuth;
    }

    // STEP 1 (dry-run): returns the full mapping the importer WOULD
    // commit. No writes. The treasurer reviews exact matches, resolves
    // fuzzy matches (noted in warnings), then calls commit with
    // memberOverrides for any rows that need manual mapping.
        // FIX (ukaguzi 2026-09-15, M-10): ukomo wa ukubwa wa faili - tazama
    // maelezo kwenye commit endpoint hapo chini.
    [RequestSizeLimit(10 * 1024 * 1024)]
[HttpPost("preview/{groupId}")]
    public async Task<ActionResult<TreasuryImportPreviewDto>> Preview(Guid groupId, IFormFile file)
    {

        await _groupAuth.RequireGovernanceApprovalAsync(User.GetUserId(), groupId, g => g.ImportApproval);

        if (file == null || file.Length == 0)
            return BadRequest("Tafadhali chagua faili la Excel.");

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        using var stream = new MemoryStream(bytes);
        var preview = await _importService.GetPreviewAsync(groupId, stream);
        return Ok(preview);

    }

    // STEP 2 (commit): actually posts the ledger entries.
    //   - file              : the same Excel
    //   - year              : the calendar year the ledger is for (Excel
    //                         has no year column)
    //   - memberOverridesJson : optional JSON object mapping an Excel name
    //                         to a member Id, for rows that preview flagged
    //                         as fuzzy/unmatched. Example:
    //                         {"BOAZ KILEWO":"<guid>","ISHEKELI KASASILA":"<guid>"}
        // FIX (ukaguzi 2026-09-15, M-10): hakuna ukomo wa ukubwa wa faili.
    // Controller inakopi faili nzima kwenye MemoryStream kabla ya kusoma -
    // bila ukomo, mtu anayepakia faili la GB 1 anaweza kuisha RAM ya server
    // (denial of service) kwa ombi moja tu. 10 MB inatosha kabisa kwa
    // statement ya chama; badilisha kama unahitaji zaidi.
    [RequestSizeLimit(10 * 1024 * 1024)]
[HttpPost("commit/{groupId}")]
    public async Task<ActionResult<TreasuryImportResultDto>> Commit(
        Guid groupId,
        IFormFile file,
        [FromForm] int year,
        [FromForm] string? memberOverridesJson)
    {

        await _groupAuth.RequireGovernanceApprovalAsync(User.GetUserId(), groupId, g => g.ImportApproval);

        if (file == null || file.Length == 0)
            return BadRequest("Tafadhali chagua faili la Excel.");

        if (year < 2000 || year > DateTime.UtcNow.Year + 1)
            return BadRequest($"Mwaka '{year}' si sahihi. Toa mwaka wa kawaida (kati ya 2000 na {DateTime.UtcNow.Year + 1}).");

        var overrides = TreasuryImportService.ParseOverrides(memberOverridesJson);

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        using var stream = new MemoryStream(bytes);
        var result = await _importService.CommitAsync(groupId, stream, year, overrides);
        return Ok(result);

    }
}
