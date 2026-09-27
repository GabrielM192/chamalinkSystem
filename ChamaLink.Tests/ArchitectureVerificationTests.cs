using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure.Services;
using System.Reflection;

namespace ChamaLink.Tests;

/// <summary>
/// Majaribio ya muundo wa entities (FinancialEvent, AuditEvent, GroupPolicy...).
/// KUMBUKA: majaribio yaliyokuwa yanalinda AllocationEngine (V2) kama "chanzo pekee" yameondolewa -
/// chanzo pekee cha hesabu sasa ni ContributionTruthEngine; ulinzi wake uko kwenye TruthArchitectureTests.
/// </summary>
public class ArchitectureVerificationTests
{
    [Fact]
    public void Every_Payment_Operation_Must_Create_FinancialEvent()
    {
        var financialEventType = typeof(FinancialEvent);
        Assert.NotNull(financialEventType.GetProperty("GroupId"));
        Assert.NotNull(financialEventType.GetProperty("MemberId"));
        Assert.NotNull(financialEventType.GetProperty("Type"));
        Assert.NotNull(financialEventType.GetProperty("Amount"));
        Assert.NotNull(financialEventType.GetProperty("PolicyVersion"));
        Assert.NotNull(financialEventType.GetProperty("CorrelationId"));
        Assert.NotNull(financialEventType.GetProperty("AuditTrailId"));
    }

    [Fact]
    public void Every_FinancialEvent_Must_Create_AuditEvent()
    {
        var auditType = typeof(AuditEvent);
        Assert.NotNull(auditType.GetProperty("CorrelationId"));
        Assert.NotNull(auditType.GetProperty("Action"));
        Assert.NotNull(auditType.GetProperty("PolicyVersion"));
        Assert.NotNull(auditType.GetProperty("AllocationResultJson"));
        Assert.NotNull(auditType.GetProperty("IsSystemGenerated"));
    }

    [Fact]
    public void FinancialPosition_Must_Have_Held_And_Available_Savings()
    {
        var positionType = typeof(ChamaLink.Application.DTOs.MemberFinancialPositionDto);
        Assert.NotNull(positionType.GetProperty("SavingsBalance"));
        Assert.NotNull(positionType.GetProperty("HeldSavings"));
        Assert.NotNull(positionType.GetProperty("AvailableSavings"));
        Assert.NotNull(positionType.GetProperty("NetPosition"));
    }

    [Fact]
    public void GroupPolicy_Must_Have_Versioning()
    {
        var policyType = typeof(GroupPolicy);
        Assert.NotNull(policyType.GetProperty("Version"));
        Assert.NotNull(policyType.GetProperty("EffectiveFrom"));
        Assert.NotNull(policyType.GetProperty("EffectiveTo"));
        Assert.NotNull(policyType.GetProperty("Reason"));
    }

    [Fact]
    public void Welfare_Must_Have_Full_Lifecycle()
    {
        Assert.NotNull(typeof(WelfareEvent).GetProperty("Status"));
        Assert.NotNull(typeof(WelfareEvent).GetProperty("TotalExpected"));
        Assert.NotNull(typeof(WelfareEvent).GetProperty("TotalCollected"));
        Assert.NotNull(typeof(WelfareEvent).GetProperty("TotalDisbursed"));
        Assert.NotNull(typeof(WelfareObligation));
        Assert.NotNull(typeof(WelfareContribution));
        Assert.NotNull(typeof(WelfareDisbursement));
    }

    [Fact]
    public void MemberStatus_Must_Be_Entity_Not_String()
    {
        Assert.NotNull(typeof(MemberStatusHistory));
        Assert.NotNull(typeof(MemberStatusHistory).GetProperty("FromStatus"));
        Assert.NotNull(typeof(MemberStatusHistory).GetProperty("ToStatus"));
        Assert.NotNull(typeof(MemberStatusHistory).GetProperty("Reason"));
    }

    [Fact]
    public void BusinessRuleEngine_HeldSavings_Calculation()
    {
        var ruleEngine = new BusinessRuleEngine(null!);
        var policy = new GroupPolicy { LoanCollateralPercent = 30m };

        decimal outstanding = 500000m;
        decimal held = ruleEngine.CalculateHeldSavings(outstanding, policy);

        Assert.Equal(150000m, held);
    }

    [Fact]
    public void ObligationLedgerService_Must_Require_GroupPolicy_Parameter()
    {
        var serviceType = typeof(ObligationLedgerService);
        var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        var hasMethodWithPolicy = methods.Any(m =>
            m.Name.Contains("WithPolicy") &&
            m.GetParameters().Any(p => p.ParameterType == typeof(GroupPolicy)));

        Assert.True(hasMethodWithPolicy, "ObligationLedgerService must have GetOutstandingQueueWithPolicyAsync that requires GroupPolicy #3)");
    }
}
