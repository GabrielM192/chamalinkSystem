using System.Globalization;
using System.Text.Json;
using ChamaLink.Application.DTOs;
using ChamaLink.Application.Interfaces;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// V2 — Single Allocation Engine path. No more ComputeSplit duplicate logic.
/// Every payment goes through AllocationEngine → FinancialEvent → Ledger → Audit.
/// </summary>
public class TreasuryImportService
{
    private readonly ApplicationDbContext _context;
    private readonly AccountResolverService _accountResolver;
    private readonly ITreasuryExcelParserService _parser;
    private readonly DebtService _debtService;
    private readonly LoanService _loanService;
    private readonly AllocationEngine _allocationEngine;
    private readonly BusinessRuleEngine _businessRuleEngine;
    private readonly FinancialPositionService _financialPositionService;
    private readonly ObligationLedgerService _obligationLedgerService;
    private readonly AuditService _auditService;

    public TreasuryImportService(
        ApplicationDbContext context,
        AccountResolverService accountResolver,
        ITreasuryExcelParserService parser,
        DebtService debtService,
        LoanService loanService,
        AllocationEngine allocationEngine,
        BusinessRuleEngine businessRuleEngine,
        FinancialPositionService financialPositionService,
        ObligationLedgerService obligationLedgerService,
        AuditService auditService)
    {
        _context = context;
        _accountResolver = accountResolver;
        _parser = parser;
        _debtService = debtService;
        _loanService = loanService;
        _allocationEngine = allocationEngine;
        _businessRuleEngine = businessRuleEngine;
        _financialPositionService = financialPositionService;
        _obligationLedgerService = obligationLedgerService;
        _auditService = auditService;
    }

    public async Task<TreasuryImportPreviewDto> GetPreviewAsync(Guid groupId, Stream stream, int? year = null)
    {
        var excelRows = await _parser.ParseAsync(stream);
        var (group, members) = await LoadGroupAndMembersAsync(groupId);
        var candidates = members.Select(m => (m, m.User)).ToList();
        var policy = await _businessRuleEngine.GetActivePolicyAsync(groupId, DateTime.UtcNow);

        var dto = new TreasuryImportPreviewDto
        {
            GroupId = groupId,
            TotalRows = excelRows.Count
        };

        foreach (var row in excelRows)
        {
            var previewRow = new TreasuryImportPreviewRowDto
            {
                RowNumber = row.RowNumber,
                ExcelName = row.ExcelName,
                MonthlyAmounts = row.MonthlyAmounts,
                JoiningFee = row.JoiningFee,
                Msiba = row.Msiba,
                Sherehe = row.Sherehe,
                IsNonActive = row.IsNonActive
            };

            var best = MemberNameMatcher.Match(row.ExcelName, candidates, out var alts);
            if (best is { IsExact: true } exact)
            {
                previewRow.MatchedMemberId = exact.MemberId;
                previewRow.MatchedMemberName = exact.MemberName;
                previewRow.MatchConfidence = 100;
                previewRow.IsExactMatch = true;
                dto.ExactMatchedRows++;
            }
            else if (best is { } fuzzy)
            {
                previewRow.MatchedMemberName = fuzzy.MemberName;
                previewRow.MatchConfidence = fuzzy.Confidence;
                previewRow.Candidates = alts.Select(a => new TreasuryMatchCandidateDto
                {
                    MemberId = a.MemberId,
                    MemberName = a.MemberName,
                    Confidence = a.Confidence
                }).ToList();
                dto.FuzzyMatchedRows++;
                previewRow.Warnings.Add($"Name not exact match - best: '{fuzzy.MemberName}' ({fuzzy.Confidence}%).");
            }
            else
            {
                previewRow.MatchConfidence = 0;
                dto.UnmatchedRows++;
                previewRow.Warnings.Add("Name not found - will be skipped.");
            }

            int previewYear = year ?? DateTime.UtcNow.Year;
            var previewLoans = previewRow.MatchedMemberId is Guid pmid
                ? await _context.Loans.Where(l => l.GroupMemberId == pmid && l.Status == LoanStatus.Active).ToListAsync()
                : new List<Loan>();

            // Get financial position for preview (simplified) - oldest-first
            var position = new MemberFinancialPositionDto
            {
                SavingsBalance = 0,
                AvailableSavings = 0, // haijulikani kwenye hakikisho — hatubuni akiba ya mwanachama
                ContributionDebt = 0,
                OutstandingFine = 0,
                JoiningFeeBalance = 0
            };
            List<ObligationItem> previewQueue = new();
            if (previewRow.MatchedMemberId.HasValue)
            {
                try
                {
                    var previewAsOf = new DateTime(previewYear, 12, 31, 23, 59, 59, DateTimeKind.Utc);
                    var pos = await _financialPositionService.CalculatePositionAsync(previewRow.MatchedMemberId.Value, previewAsOf);
                    position = pos;
                    previewQueue = await _obligationLedgerService.GetOutstandingQueueWithPolicyAsync(previewRow.MatchedMemberId.Value, policy, previewAsOf);
                }
                catch { /* use default for preview */ }
            }

            // For preview running queue simulation (in-memory oldest-first like CommitAsync)
            var runningQueue = previewQueue.Select(q => new ObligationItem
            {
                Year = q.Year, Month = q.Month, MonthName = q.MonthName, DueDate = q.DueDate,
                ContributionDue = q.ContributionDue, ContributionPaid = q.ContributionPaid,
                FineDue = q.FineDue, FinePaid = q.FinePaid,
                JoinFeeDue = q.JoinFeeDue, JoinFeePaid = q.JoinFeePaid,
                IsOverdue = q.IsOverdue, IsFirstMonth = q.IsFirstMonth
            }).ToList();

            for (int m = 0; m < 12; m++)
            {
                var amt = row.MonthlyAmounts[m];
                var split = new TreasuryPlannedSplitDto
                {
                    Month = m + 1,
                    MonthName = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m + 1).ToUpperInvariant(),
                    TotalAmount = amt
                };

                if (!amt.HasValue || amt.Value <= 0)
                {
                    split.SplitNote = "unpaid (*)";
                }
                else
                {
                    // Use oldest-first allocation for preview too
                    var allocation = _allocationEngine.AllocateOldestFirst(
                        paymentAmount: amt.Value,
                        paymentDate: new DateTime(previewYear, m + 1, 1),
                        outstandingQueue: runningQueue.Count > 0 ? runningQueue : new List<ObligationItem>
                        {
                            new ObligationItem { Year = previewYear, Month = m+1, MonthName = $"{m+1:00}", DueDate = new DateTime(previewYear, m+1, 5), ContributionDue = policy.MonthlyContribution, IsOverdue = false }
                        },
                        policy: policy,
                        activeLoans: previewLoans,
                        year: previewYear,
                        month: m + 1);

                    split.Contribution = allocation.Allocations.Where(a => a.Target == "Contribution").Sum(a => a.Amount);
                    split.Fine = allocation.Allocations.Where(a => a.Target == "Fine").Sum(a => a.Amount);
                    split.RepaymentApplied = allocation.Allocations.Where(a => a.Target == "LoanRepayment").Sum(a => a.Amount);
                    split.JoiningFeeApplied = allocation.Allocations.Where(a => a.Target == "JoiningFee").Sum(a => a.Amount);
                    split.DebtCleared = allocation.Allocations.Where(a => a.Target == "Debt").Sum(a => a.Amount);
                    split.Savings = allocation.Allocations.Where(a => a.Target == "Savings").Sum(a => a.Amount);

                    var notes = new List<string>();
                    if (split.Contribution > 0) notes.Add($"contribution {split.Contribution:N0}");
                    if (split.RepaymentApplied > 0) notes.Add($"loan repayment {split.RepaymentApplied:N0}");
                    if (split.Fine > 0) notes.Add($"fine {split.Fine:N0}");
                    if (split.DebtCleared > 0) notes.Add($"debt {split.DebtCleared:N0}");
                    if (split.JoiningFeeApplied > 0) notes.Add($"join {split.JoiningFeeApplied:N0}");
                    if (split.Savings > 0) notes.Add($"savings {split.Savings:N0}");
                    split.SplitNote = notes.Count > 0 ? string.Join(" + ", notes) : $"contribution {split.Contribution:N0}";

                }

                previewRow.PlannedSplits.Add(split);
            }

            if (row.Msiba.HasValue || row.Sherehe.HasValue)
                previewRow.Warnings.Add("Welfare payouts (MSIBA/SHEREHE) now handled by Welfare Engine V2.");

            dto.Rows.Add(previewRow);
        }

        if (dto.UnmatchedRows > 0)
            dto.Warnings.Add($"{dto.UnmatchedRows} rows unmatched - need memberOverrides before commit.");

        return dto;
    }

    public async Task<TreasuryImportResultDto> CommitAsync(Guid groupId, Stream stream, int year, Dictionary<string, Guid>? memberOverrides)
    {
        var excelRows = await _parser.ParseAsync(stream);
        var (group, members) = await LoadGroupAndMembersAsync(groupId);
        var candidates = members.Select(m => (m, m.User)).ToList();
        var memberById = members.ToDictionary(m => m.Id);
        var policy = await _businessRuleEngine.GetActivePolicyAsync(groupId, new DateTime(year, 1, 1));

        int dueDateDay = policy.DueDateDay;

        var result = new TreasuryImportResultDto { TotalRows = excelRows.Count };

        var overrides = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (memberOverrides != null)
        {
            foreach (var kv in memberOverrides)
                overrides[MemberNameMatcher.Normalize(kv.Key)] = kv.Value;
        }

        using var tx = await _context.Database.BeginTransactionAsync();

        foreach (var row in excelRows)
        {
            Guid? resolvedMemberId = null;
            var best = MemberNameMatcher.Match(row.ExcelName, candidates, out _);
            if (best is { IsExact: true } exact) resolvedMemberId = exact.MemberId;
            else if (overrides.TryGetValue(MemberNameMatcher.Normalize(row.ExcelName), out var overrideId)) resolvedMemberId = overrideId;

            if (!resolvedMemberId.HasValue || !memberById.TryGetValue(resolvedMemberId.Value, out var member))
            {
                result.UnmatchedRowsSkipped++;
                result.SkippedUnmatched.Add($"Row {row.RowNumber}: '{row.ExcelName}' - unmatched, skipped.");
                continue;
            }

            var savingsAccount = await _accountResolver.GetOrCreateAccountAsync(member.Id, AccountType.Savings);
            string memberRef = !string.IsNullOrWhiteSpace(member.MemberNumber) ? member.MemberNumber : row.RowNumber.ToString("000", CultureInfo.InvariantCulture);
            string code = string.IsNullOrWhiteSpace(group.Code) ? "GROUP" : group.Code.ToUpperInvariant().Trim();

            // Get outstanding queue once, then maintain in-memory to avoid stale DB query bug
            // Previously: position = CalculatePositionAsync each month but SaveChanges only at end -> stale
            // Build the complete year queue once. Starting at 31 Dec of the
            // previous year omitted the current year's obligations, causing
            // legitimate Excel payments to be routed to savings.
            var startAsOf = new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            var outstandingQueue = await _obligationLedgerService.GetOutstandingQueueWithPolicyAsync(member.Id, policy, startAsOf);
            var position = await _financialPositionService.CalculatePositionAsync(member.Id, startAsOf);

            // In-memory running totals for this import run
            var runningLedger = new List<LedgerEntry>();

            for (int m = 0; m < 12; m++)
            {
                var amount = row.MonthlyAmounts[m];
                if (!amount.HasValue || amount.Value <= 0) continue;

                string monthAbbr = new DateTime(year, m + 1, 1).ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
                int day = Math.Min(dueDateDay, DateTime.DaysInMonth(year, m + 1));
                var date = new DateTime(year, m + 1, day, 0, 0, 0, DateTimeKind.Utc);
                var monthStart = new DateTime(year, m + 1, 1, 0, 0, 0, DateTimeKind.Utc);
                string monthName = date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

                var activeLoans = await _context.Loans.Where(l => l.GroupMemberId == member.Id && l.Status == LoanStatus.Active).ToListAsync();

                // Use oldest-first allocation with in-memory queue to avoid stale DB bug
                // Previously: Allocate with position that was queried from DB but not seeing unsaved entries
                var allocation = _allocationEngine.AllocateOldestFirst(
                    paymentAmount: amount.Value,
                    paymentDate: date,
                    outstandingQueue: outstandingQueue,
                    policy: policy,
                    activeLoans: activeLoans,
                    year: year,
                    month: m + 1);

                var correlationId = allocation.CorrelationId;

                // Create FinancialEvent for payment received
                var paymentEvent = new FinancialEvent
                {
                    Id = Guid.NewGuid(),
                    GroupId = groupId,
                    MemberId = member.Id,
                    Type = FinancialEventType.PaymentReceived,
                    Amount = amount.Value,
                    OccurredAt = date,
                    CreatedBy = Guid.Empty,
                    Source = EventSource.ExcelImport,
                    SourceReference = $"TREAS-{code}-{year}-{monthAbbr}-{memberRef}",
                    PolicyVersion = policy.Version,
                    CorrelationId = correlationId
                };
                _context.FinancialEvents.Add(paymentEvent);

                // Create ledger entries from allocation
                foreach (var alloc in allocation.Allocations)
                {
                    string refNo = alloc.Target switch
                    {
                        "Contribution" => $"TREAS-{code}-{year:0000}-{monthAbbr}-{memberRef}",
                        "LoanRepayment" => $"TREAS-{code}-{year:0000}-{monthAbbr}-{memberRef}-REJESHO-{alloc.LoanId}",
                        "Fine" => $"TREAS-{code}-{year:0000}-{monthAbbr}-{memberRef}-FINE",
                        "JoiningFee" => $"TREAS-{code}-{year:0000}-KIANZIO-{memberRef}",
                        "Debt" => $"TREAS-{code}-{year:0000}-{monthAbbr}-{memberRef}-DENI",
                        "Savings" => $"TREAS-{code}-{year:0000}-{monthAbbr}-{memberRef}-AKIBA",
                        _ => $"TREAS-{code}-{year:0000}-{monthAbbr}-{memberRef}-{alloc.Target}"
                    };

                    bool exists = await _context.LedgerEntries.AnyAsync(l => l.GroupId == groupId && l.UserId == member.UserId && l.ReferenceNo == refNo);
                    if (exists)
                    {
                        result.EntriesSkippedDuplicate++;
                        continue;
                    }

                    var (type, desc) = alloc.Target switch
                    {
                        "Contribution" => (TransactionType.Contribution, $"Contribution {monthName} (treasurer Excel)"),
                        "LoanRepayment" => (TransactionType.LoanRepayment, $"Loan repayment {monthName} (treasurer Excel)"),
                        "Fine" => (TransactionType.FinePayment, $"Fine payment {monthName} (treasurer Excel)"),
                        "JoiningFee" => (TransactionType.JoiningFee, $"Joining fee {monthName} (treasurer Excel)"),
                        "Debt" => (TransactionType.Contribution, $"Excess -> old debt {monthName} (treasurer Excel)"),
                        "Savings" => (TransactionType.Savings, $"Excess savings {monthName} (treasurer Excel)"),
                        _ => (TransactionType.Savings, $"{alloc.Target} {monthName} (treasurer Excel)")
                    };

                    // Handle loan repayment via LoanService
                    if (alloc.Target == "LoanRepayment" && alloc.LoanId.HasValue)
                    {
                        decimal repaid = await _loanService.ApplyRepaymentFromImportAsync(member.Id, member.UserId, groupId, alloc.Amount, date, refNo.Replace($"-REJESHO-{alloc.LoanId}", ""));
                        if (repaid > 0)
                        {
                            result.RepaymentsPosted++;
                            result.TotalRepaymentsAmount += repaid;
                        }
                    }
                    else if (alloc.Target == "Debt")
                    {
                        decimal cleared = await _debtService.ClearWithPaymentAsync(member.Id, alloc.Amount, groupId);
                        if (cleared > 0)
                        {
                            _context.LedgerEntries.Add(new LedgerEntry
                            {
                                Id = Guid.NewGuid(),
                                GroupId = groupId,
                                UserId = member.UserId,
                                AccountId = savingsAccount.Id,
                                Amount = cleared,
                                Type = type,
                                ReferenceNo = refNo,
                                Description = desc,
                                CreatedAt = date
                            });
                            result.DebtClearancesPosted++;
                            result.TotalDebtClearedAmount += cleared;
                        }
                    }
                    else
                    {
                        _context.LedgerEntries.Add(new LedgerEntry
                        {
                            Id = Guid.NewGuid(),
                            GroupId = groupId,
                            UserId = member.UserId,
                            AccountId = savingsAccount.Id,
                            Amount = alloc.Amount,
                            Type = type,
                            ReferenceNo = refNo,
                            Description = desc,
                            CreatedAt = date
                        });

                        if (alloc.Target == "Contribution")
                        {
                            result.ContributionsPosted++;
                            result.TotalContributionsAmount += alloc.Amount;
                            member.TotalContributionsCount++;
                        }
                        else if (alloc.Target == "Savings")
                        {
                            member.AdvanceBalance += alloc.Amount;
                            result.SavingsPosted++;
                            result.TotalSavingsAmount += alloc.Amount;
                        }
                        else if (alloc.Target == "Fine")
                        {
                            // Create fine entity if needed
                            bool fineExists = await _context.Fines.AnyAsync(f => f.GroupMemberId == member.Id && f.Period == monthStart && f.ReasonType == FineReasonType.LateMonthlyContribution);
                            if (!fineExists)
                            {
                                var fineAccount = await _accountResolver.GetOrCreateAccountAsync(member.Id, AccountType.Fine);
                                var fineEntity = new Fine
                                {
                                    Id = Guid.NewGuid(),
                                    GroupId = groupId,
                                    GroupMemberId = member.Id,
                                    UserId = member.UserId,
                                    Amount = alloc.Amount,
                                    ReasonType = FineReasonType.LateMonthlyContribution,
                                    Reason = $"Late fine {monthName} (treasurer Excel)",
                                    Period = monthStart,
                                    IssuedAt = date,
                                    AmountPaid = alloc.Amount,
                                    Status = FineStatus.Paid,
                                    PaidAt = date
                                };
                                _context.Fines.Add(fineEntity);
                                _context.LedgerEntries.Add(new LedgerEntry
                                {
                                    Id = Guid.NewGuid(),
                                    GroupId = groupId,
                                    UserId = member.UserId,
                                    AccountId = fineAccount.Id,
                                    Amount = alloc.Amount,
                                    Type = TransactionType.FineIssue,
                                    ReferenceNo = refNo,
                                    Description = $"Late fine {monthName} (treasurer Excel)",
                                    CreatedAt = date
                                });
                            }
                            result.FinesPosted++;
                            result.TotalFinesAmount += alloc.Amount;
                        }
                        else if (alloc.Target == "JoiningFee")
                        {
                            result.JoiningFeesPosted++;
                            result.TotalJoiningFeesAmount += alloc.Amount;
                        }
                    }

                    // FinancialEvent for each allocation
                    var allocEventType = alloc.Target switch
                    {
                        "Contribution" => FinancialEventType.ContributionPaid,
                        "LoanRepayment" => FinancialEventType.LoanRepaymentPaid,
                        "Fine" => FinancialEventType.FinePaid,
                        "JoiningFee" => FinancialEventType.JoiningFeePaid,
                        "Debt" => FinancialEventType.DebtCleared,
                        "Savings" => FinancialEventType.SavingsDeposited,
                        _ => FinancialEventType.SavingsDeposited
                    };

                    _context.FinancialEvents.Add(new FinancialEvent
                    {
                        Id = Guid.NewGuid(),
                        GroupId = groupId,
                        MemberId = member.Id,
                        Type = allocEventType,
                        Amount = alloc.Amount,
                        OccurredAt = date,
                        CreatedBy = Guid.Empty,
                        Source = EventSource.ExcelImport,
                        SourceReference = refNo,
                        PolicyVersion = policy.Version,
                        CorrelationId = correlationId
                    });
                }

                // Audit trail for this month
                await _auditService.CreateAsync(
                    groupId, member.Id, Guid.Empty, "Treasurer", member.MemberNumber,
                    AuditAction.PaymentReceived, "FinancialEvent", paymentEvent.Id,
                    null, new { Amount = amount.Value, Month = monthName, Allocation = allocation },
                    EventSource.ExcelImport, $"TREAS-{code}-{year}-{monthAbbr}-{memberRef}",
                    new { Year = year, Month = m + 1, Amount = amount.Value, Member = member.User?.FullName },
                    policy.Version, allocation, $"Treasurer import {monthName}", correlationId);

                // Update position in-memory for next iteration (no DB query)
                position.ContributionDebt = outstandingQueue.Sum(q => q.ContributionOutstanding);
                position.OutstandingFine = outstandingQueue.Sum(q => q.FineOutstanding);
                position.JoiningFeeBalance = outstandingQueue.Sum(q => q.JoinFeeOutstanding);
                position.TotalPaidContributions += allocation.Allocations.Where(a => a.Target == "Contribution" || a.Target == "Debt").Sum(a => a.Amount);
                position.TotalFinesPaid += allocation.Allocations.Where(a => a.Target == "Fine").Sum(a => a.Amount);
            }

            // Joining fee explicit column - prevent double-count (M-Koba already paid KIANZIO via waterfall)
            // mf. mwanachama alilipa 60k kwa M-Koba = 10k mchango + 50k KIANZIO; KIANZIO 50k ya Excel ingehesabiwa mara mbili.
            if (row.JoiningFee.HasValue && row.JoiningFee.Value > 0)
            {
                // Check how much JoiningFee already paid according to queue (from M-Koba etc)
                decimal alreadyPaidJoinFee = outstandingQueue.Sum(q => q.JoinFeePaid);
                // Also check ledger directly for safety
                try
                {
                    var ledgerJoinFee = await _context.LedgerEntries
                        .Where(l => l.GroupId == groupId && l.UserId == member.UserId && l.Type == TransactionType.JoiningFee)
                        .SumAsync(l => (decimal?)l.Amount) ?? 0m;
                    if (ledgerJoinFee > alreadyPaidJoinFee) alreadyPaidJoinFee = ledgerJoinFee;
                }
                catch { }

                decimal joinFeeTarget = policy.JoiningFee;
                decimal remainingJoinFee = Math.Max(0, joinFeeTarget - alreadyPaidJoinFee);
                decimal toPost = Math.Min(row.JoiningFee.Value, remainingJoinFee);

                if (toPost <= 0)
                {
                    result.Warnings.Add($"Row {row.RowNumber}: '{row.ExcelName}' KIANZIO {row.JoiningFee.Value:N0} skipped - already paid {alreadyPaidJoinFee:N0}/{joinFeeTarget:N0} via M-Koba (prevent double-count).");
                    result.EntriesSkippedDuplicate++;
                }
                else
                {
                    if (toPost < row.JoiningFee.Value)
                        result.Warnings.Add($"Row {row.RowNumber}: '{row.ExcelName}' KIANZIO {row.JoiningFee.Value:N0} reduced to {toPost:N0} - {alreadyPaidJoinFee:N0} already paid via M-Koba.");

                    string refNo = $"TREAS-{code}-{year:0000}-KIANZIO-{memberRef}";
                    bool exists = await _context.LedgerEntries.AnyAsync(l => l.GroupId == groupId && l.UserId == member.UserId && l.ReferenceNo == refNo);
                    if (!exists)
                    {
                        int day = Math.Min(dueDateDay, DateTime.DaysInMonth(year, 1));
                        var date = new DateTime(year, 1, day, 0, 0, 0, DateTimeKind.Utc);
                        _context.LedgerEntries.Add(new LedgerEntry
                        {
                            Id = Guid.NewGuid(),
                            GroupId = groupId,
                            UserId = member.UserId,
                            AccountId = savingsAccount.Id,
                            Amount = toPost,
                            Type = TransactionType.JoiningFee,
                            ReferenceNo = refNo,
                            Description = $"Joining fee {year} (treasurer Excel) - {alreadyPaidJoinFee:N0} already paid, posting {toPost:N0} remaining double-count prevention)",
                            CreatedAt = date
                        });
                        result.JoiningFeesPosted++;
                        result.TotalJoiningFeesAmount += toPost;
                    }
                    else result.EntriesSkippedDuplicate++;
                }
            }

            if (row.IsNonActive && member.Status != MemberStatus.Inactive && member.Status != MemberStatus.Exited)
            {
                member.Status = MemberStatus.Inactive;
                result.MembersMarkedInactive++;
            }

            if (row.Msiba.HasValue && row.Msiba.Value > 0)
                result.MsibaShereheDeferred.Add($"{row.ExcelName} row {row.RowNumber}: MSIBA {row.Msiba.Value:N0} now handled by Welfare Engine V2");
            if (row.Sherehe.HasValue && row.Sherehe.Value > 0)
                result.MsibaShereheDeferred.Add($"{row.ExcelName} row {row.RowNumber}: SHEREHE {row.Sherehe.Value:N0} now handled by Welfare Engine V2");
        }

        await _context.SaveChangesAsync();
        await tx.CommitAsync();

        if (result.MsibaShereheDeferred.Count > 0)
            result.Warnings.Add($"{result.MsibaShereheDeferred.Count} welfare entries now handled by Welfare Engine V2.");
        if (result.EntriesSkippedDuplicate > 0)
            result.Warnings.Add($"{result.EntriesSkippedDuplicate} duplicate entries skipped (safe to re-run).");

        return result;
    }

    private async Task<(Group group, List<GroupMember> members)> LoadGroupAndMembersAsync(Guid groupId)
    {
        var group = await _context.Groups.Include(g => g.Settings).FirstOrDefaultAsync(g => g.Id == groupId)
            ?? throw new InvalidOperationException("Group not found.");
        var members = await _context.GroupMembers.Include(m => m.User).Where(m => m.GroupId == groupId).ToListAsync();
        return (group, members);
    }

    public static Dictionary<string, Guid>? ParseOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var doc = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (doc == null) return null;
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in doc)
            if (Guid.TryParse(kv.Value, out var id)) result[kv.Key] = id;
        return result.Count == 0 ? null : result;
    }
}
