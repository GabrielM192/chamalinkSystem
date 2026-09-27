using ChamaLink.Application.DTOs;
using ChamaLink.Domain;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Infrastructure.Services;

/// <summary>
/// Reporting service. Reads from ComplianceSnapshots and ledger.
/// Monthly matrix shows ledger view, compliance summary shows status view.
/// </summary>
public class ComplianceReportService
{
    private readonly ApplicationDbContext _context;

    public ComplianceReportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ComplianceSummaryRowDto>> GetComplianceSummaryAsync(Guid groupId)
    {
        var allSnapshots = await _context.ComplianceSnapshots.Where(s => s.GroupId == groupId).ToListAsync();
        if (allSnapshots.Count == 0) return new List<ComplianceSummaryRowDto>();

        var latestSnapshots = allSnapshots.GroupBy(s => s.GroupMemberId).Select(g => g.OrderByDescending(s => s.Month).First()).ToList();
        var memberIds = latestSnapshots.Select(s => s.GroupMemberId).ToList();

        var members = await _context.GroupMembers.Include(m => m.User).Where(m => memberIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

        return latestSnapshots.Select(s =>
        {
            members.TryGetValue(s.GroupMemberId, out var member);
            return new ComplianceSummaryRowDto(
                s.GroupMemberId,
                member?.UserId ?? Guid.Empty,
                member?.User?.FullName ?? "Member",
                member?.User?.PhoneNumber ?? string.Empty,
                s.Month,
                s.TotalMissedMonths,
                s.ConsecutiveMissedMonths,
                s.OutstandingFineAmount,
                s.OutstandingContributionDebt,
                s.Status.ToString());
        })
        .OrderByDescending(r => r.ConsecutiveMissedMonths)
        .ThenByDescending(r => r.OutstandingContributionDebt)
        .ToList();
    }

    public async Task<MonthlyMatrixDto> GetMonthlyMatrixAsync(Guid groupId, int? year = null)
    {
        var group = await _context.Groups.FirstOrDefaultAsync(g => g.Id == groupId)
            ?? throw new Domain.Exceptions.NotFoundException("Group not found.");

        int targetYear = year ?? DateTime.UtcNow.Year;
        var now = DateTime.UtcNow;
        int lastMonth = targetYear == now.Year ? now.Month : 12;
        var yearStart = new DateTime(targetYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd = new DateTime(targetYear, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        decimal joiningFeeTarget = group.Settings?.Financial.JoiningFee ?? 0m;

        var members = await _context.GroupMembers.Where(m => m.GroupId == groupId)
            .OrderBy(m => m.MemberNumber)
            .Select(m => new { m.Id, m.UserId, m.MemberNumber, m.JoinedAt, m.Status, Name = m.User != null ? m.User.FullName : "Member" })
            .ToListAsync();

        var entries = await _context.LedgerEntries
            .Where(l => l.GroupId == groupId && l.CreatedAt >= yearStart && l.CreatedAt <= yearEnd)
            .Select(l => new { l.UserId, l.Type, l.Amount, l.CreatedAt, l.ReferenceNo })
            .ToListAsync();

        var dto = new MonthlyMatrixDto
        {
            GroupId = groupId,
            GroupName = group.Name,
            Year = targetYear,
            Months = Enumerable.Range(1, lastMonth).ToList()
        };

        foreach (var member in members)
        {
            var row = new MatrixMemberRowDto
            {
                GroupMemberId = member.Id,
                UserId = member.UserId,
                MemberName = member.Name,
                MemberNumber = member.MemberNumber,
                Status = member.Status.ToString(),
                IsNonActive = member.Status == MemberStatus.Inactive
            };

            var memberEntries = entries.Where(e => e.UserId == member.UserId).ToList();

            for (int m = 1; m <= lastMonth; m++)
            {
                var monthStart = new DateTime(targetYear, m, 1, 0, 0, 0, DateTimeKind.Utc);
                var monthEnd = monthStart.AddMonths(1);
                var monthEntries = memberEntries.Where(e => e.CreatedAt >= monthStart && e.CreatedAt < monthEnd).ToList();

                decimal contribution = monthEntries.Where(e => e.Type == TransactionType.Contribution).Sum(e => e.Amount);
                decimal finePaid = monthEntries.Where(e => e.Type == TransactionType.FinePayment).Sum(e => e.Amount);
                decimal loanRepayment = monthEntries.Where(e => e.Type == TransactionType.LoanRepayment).Sum(e => e.Amount);
                decimal toJoining = monthEntries.Where(e => e.Type == TransactionType.JoiningFee).Sum(e => e.Amount);
                decimal toDebt = monthEntries.Where(e => e.Type == TransactionType.Contribution && e.ReferenceNo != null && e.ReferenceNo.EndsWith("-DENI")).Sum(e => e.Amount);
                decimal toSavings = monthEntries.Where(e => e.Type == TransactionType.Contribution && e.ReferenceNo != null && e.ReferenceNo.EndsWith("-AKIBA")).Sum(e => e.Amount);
                decimal events = monthEntries.Where(e => e.Type == TransactionType.EventContribution || e.Type == TransactionType.WelfareTopUp).Sum(e => e.Amount);

                var cell = new MatrixMonthCellDto
                {
                    Month = m,
                    Contribution = contribution,
                    FinePaid = finePaid,
                    LoanRepayment = loanRepayment,
                    TotalPaid = contribution + finePaid + loanRepayment,
                    HadFine = finePaid > 0,
                    ToJoiningFee = toJoining,
                    ToDebt = toDebt,
                    ToSavings = toSavings,
                    Missed = contribution == 0 && finePaid == 0 && loanRepayment == 0 && member.JoinedAt < monthEnd && (m < lastMonth || targetYear < now.Year)
                };

                row.Months.Add(cell);
                row.EventTotal += events;
                row.JoiningFeePaid += toJoining;
            }

            decimal jfPaidAllTime = await _context.LedgerEntries
                .Where(l => l.GroupId == groupId && l.UserId == member.UserId && l.Type == TransactionType.JoiningFee)
                .SumAsync(l => (decimal?)l.Amount) ?? 0m;

            row.JoiningFeePaid = jfPaidAllTime;
            row.JoiningFeeTarget = joiningFeeTarget;
            row.JoiningFeeDebt = Math.Max(0m, joiningFeeTarget - jfPaidAllTime);
            row.GrandTotal = row.Months.Sum(c => c.TotalPaid) + row.JoiningFeePaid + row.EventTotal;

            dto.Members.Add(row);
        }

        dto.MonthTotals = dto.Months.Select(m => dto.Members.Sum(r => r.Months[m - 1].TotalPaid)).ToList();
        dto.TotalJoiningFees = dto.Members.Sum(r => r.JoiningFeePaid);
        dto.TotalEvents = dto.Members.Sum(r => r.EventTotal);
        dto.TotalLoanRepayments = dto.Members.Sum(r => r.Months.Sum(c => c.LoanRepayment));
        dto.GrandTotal = dto.Members.Sum(r => r.GrandTotal);

        dto.CurrentSavings = entries.Where(e => e.Type == TransactionType.Contribution || e.Type == TransactionType.JoiningFee || e.Type == TransactionType.FinePayment).Sum(e => e.Amount)
            - entries.Where(e => e.Type == TransactionType.LoanDisbursement || e.Type == TransactionType.WelfareDeduction || e.Type == TransactionType.ShareOut).Sum(e => e.Amount);

        return dto;
    }

    public async Task<ComplianceTrendDto?> GetComplianceTrendAsync(Guid groupId, Guid groupMemberId)
    {
        var member = await _context.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == groupMemberId && m.GroupId == groupId);
        if (member == null) return null;

        var snapshots = await _context.ComplianceSnapshots.Where(s => s.GroupMemberId == groupMemberId).OrderBy(s => s.Month).ToListAsync();
        var points = snapshots.Select(s => new ComplianceTrendPointDto(
            s.Month, s.ExpectedContribution, s.PaidContribution, s.FineIssuedAmount, s.FinePaidAmount,
            s.OutstandingFineAmount, s.TotalMissedMonths, s.ConsecutiveMissedMonths, s.OutstandingContributionDebt, s.Status.ToString())).ToList();

        return new ComplianceTrendDto(groupMemberId, member.User?.FullName ?? "Member", points);
    }
}
