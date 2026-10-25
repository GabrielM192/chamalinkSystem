using System.Text.Json;
using ChamaLink.API.Controllers;
using ChamaLink.API.Middleware;
using ChamaLink.Domain;
using ChamaLink.Domain.Entities;
using ChamaLink.Infrastructure.Services;
using ChamaLink.Infrastructure.Services.Benefits;
using ChamaLink.Infrastructure.Services.Truth;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace ChamaLink.Tests;

/// <summary>
/// SASISHO16: uthabiti wa bidhaa na usafi wa majibu —
///   sera ya mikopo (inaonekana lini, nani anaona nini), ilani za taarifa (code + kiolezo; mwanachama haoni za ndani),
///   ziada ya makato ya mafao (drift), CSV injection, ujumbe wa uthibitishaji (framework haivuji), neno la siri, kuficha simu.
/// </summary>
public class Sasisho16Tests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, Web);

    // ------------------------------------------------------------------ sera ya mikopo
    [Fact]
    public async Task Loans_Disabled_Without_Any_Loan_Is_Hidden_And_Member_Sees_No_Internal_List()
    {
        var w = await LoanApplicationTests.Build();
        var s = await w.Db.TruthGroupSettings.SingleAsync();
        s.LoansEnabled = false; s.LoanInterestPercent = null; s.LoanRepaymentMonths = null;
        await w.Db.SaveChangesAsync();

        var member = Json(await w.Loans.PolicyAsync(w.GroupId, w.U("M1")));
        Assert.False(member.GetProperty("visible").GetBoolean());
        Assert.False(member.GetProperty("hasActivity").GetBoolean());
        Assert.Equal(0, member.GetProperty("missing").GetArrayLength());

        var leader = Json(await w.Loans.PolicyAsync(w.GroupId, w.U("Chair")));
        Assert.True(leader.GetProperty("isLeader").GetBoolean());
        var missing = leader.GetProperty("missing").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(2, missing.Count);
        // maelezo ya kibinadamu — si majina ya sehemu za code (LoanInterestPercent n.k.)
        Assert.DoesNotContain(missing, m => m.Contains("Loan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Loans_Disabled_With_An_Old_Loan_Stays_Visible_So_Debt_Is_Never_Hidden()
    {
        var w = await LoanApplicationTests.Build();
        var s = await w.Db.TruthGroupSettings.SingleAsync();
        s.LoansEnabled = false;
        w.Db.Loans.Add(new Loan
        {
            Id = Guid.NewGuid(), GroupId = w.GroupId, GroupMemberId = w.M("M1"), UserId = w.U("M1"), PrincipalAmount = 50_000m,
            Purpose = "Mkopo wa zamani", DisbursedAt = DateTime.UtcNow.AddMonths(-2), DueDate = DateTime.UtcNow.AddMonths(1), Status = LoanStatus.Active
        });
        await w.Db.SaveChangesAsync();

        var member = Json(await w.Loans.PolicyAsync(w.GroupId, w.U("M2")));
        Assert.False(member.GetProperty("enabled").GetBoolean());
        Assert.True(member.GetProperty("hasActivity").GetBoolean());
        Assert.True(member.GetProperty("visible").GetBoolean());
    }

    // ------------------------------------------------------------------ ilani za taarifa
    [Fact]
    public async Task Data_Quality_Notices_Are_For_Leaders_Only_And_Carry_A_Stable_Code()
    {
        var w = await LoanApplicationTests.Build();
        var uid = Guid.NewGuid(); var mid = Guid.NewGuid();
        w.Db.Users.Add(new User { Id = uid, FullName = "Bila Simu", PhoneNumber = "" });
        w.Db.GroupMembers.Add(new GroupMember { Id = mid, GroupId = w.GroupId, UserId = uid, MemberNumber = "NP", Role = GroupRole.Member, Status = MemberStatus.Active, JoinedAt = DateTime.UtcNow.AddMonths(-1) });
        await w.Db.SaveChangesAsync();

        var truth = await w.Truth.GetMemberTruthAsync(mid);
        var n = Assert.Single(truth.Notices, x => x.Code == "NO_APPROVED_PHONE");
        Assert.Equal(TruthLedgerService.TruthNotice.KindDataQuality, n.Kind);

        var asLeader = Json(TruthController.ToDto(truth, includeAllocations: true, viewerIsLeader: true));
        Assert.Contains(asLeader.GetProperty("notices").EnumerateArray(), x => x.GetProperty("code").GetString() == "NO_APPROVED_PHONE"
                                                                             && !string.IsNullOrEmpty(x.GetProperty("tpl").GetString()));

        var asMember = Json(TruthController.ToDto(truth, includeAllocations: true, viewerIsLeader: false));
        Assert.Equal(0, asMember.GetProperty("notices").GetArrayLength());
        Assert.Equal(0, asMember.GetProperty("warnings").GetArrayLength());
        Assert.Equal(0, asMember.GetProperty("rejected").GetArrayLength());
    }

    // ------------------------------------------------------------------ mafao: ziada ya makato
    [Fact]
    public void Deduction_Drift_Is_Reported_When_Beneficiary_Paid_Debt_After_Voting_Started()
    {
        var c = new BenefitCase { CaseNumber = "T-1", GrossAmount = 500_000m, JoinFeeDeducted = 40_000m, ArrearsDeducted = 30_000m, LoanArrearsDeducted = 0m, NetAmount = 430_000m };
        var d = BenefitService.ComputeDrift(c, current: 40_000m);
        Assert.NotNull(d);
        Assert.Equal(70_000m, d!.Frozen);
        Assert.Equal(30_000m, d.Excess);
        Assert.Contains("430,000", d.Message);       // kiasi kilichoidhinishwa hakibadilishwi kimya
        Assert.False(string.IsNullOrEmpty(d.MessageLoc.Tpl));

        Assert.Null(BenefitService.ComputeDrift(c, current: 70_000m));   // deni halijabadilika
        Assert.Null(BenefitService.ComputeDrift(c, current: 90_000m));   // deni limeongezeka: makato yaliyofungwa bado yanatosha
        Assert.Null(BenefitService.ComputeDrift(new BenefitCase { GrossAmount = 1m }, current: 0m)); // hakukuwa na makato
    }

    // ------------------------------------------------------------------ CSV injection
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "'=HYPERLINK(\"http://x\")")]
    [InlineData("+255712", "'+255712")]
    [InlineData("-5", "'-5")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tx", "'\tx")]
    [InlineData("Frank Mkoma", "Frank Mkoma")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Csv_Cells_Cannot_Become_Spreadsheet_Formulas(string? input, string expected)
        => Assert.Equal(expected, BenefitService.CsvSafe(input));

    // ------------------------------------------------------------------ uthibitishaji wa ombi
    [Theory]
    [InlineData("Barua pepe si sahihi.", true)]
    [InlineData("The Email field is required.", false)]
    [InlineData("The JSON value could not be converted to System.Decimal. Path: $.amount | LineNumber: 0 | BytePositionInLine: 20.", false)]
    [InlineData("The value '<script>' is not valid for Amount.", false)]
    [InlineData("'x' is invalid after a value.", false)]
    [InlineData("A non-empty request body is required.", false)]
    public void Only_Our_Own_Validation_Messages_Reach_The_Client(string message, bool shown)
        => Assert.Equal(shown, RequestValidation.OwnMessage(new ModelError(message)) is not null);

    [Fact]
    public void Validation_Message_From_An_Exception_Is_Never_Shown()
        => Assert.Null(RequestValidation.OwnMessage(new ModelError(new FormatException("Input string was not in a correct format."), "Thamani")));

    [Theory]
    [InlineData("$.amount", "amount")]
    [InlineData("req.Items[3].Amount", "amount")]
    [InlineData("$", "")]
    [InlineData("PhoneNumber", "phoneNumber")]
    [InlineData("req", "")]
    [InlineData("dto", "")]
    public void Field_Names_Are_Stripped_Of_Internal_Paths(string key, string expected)
        => Assert.Equal(expected, RequestValidation.FieldName(key));

    // ------------------------------------------------------------------ neno la siri
    [Theory]
    [InlineData("1234567")]          // fupi
    [InlineData("12345678")]         // mfululizo
    [InlineData("abcdefgh")]         // mfululizo
    [InlineData("password")]         // la kawaida
    [InlineData("Chamalink123")]     // la kawaida (herufi kubwa hazisaidii)
    [InlineData("aaaaaaaaaaaa")]     // herufi moja
    [InlineData("ababababab")]       // herufi mbili
    public void Weak_Passwords_Are_Rejected(string pw)
        => Assert.NotNull(PasswordPolicy.Validate(pw, "mtu@test.tz", "255700000001", 8));

    [Fact]
    public void Password_Containing_Email_Or_Phone_Is_Rejected_And_Long_Phrase_Is_Accepted()
    {
        Assert.NotNull(PasswordPolicy.Validate("juma.kibaha2026", "juma.kibaha@test.tz", null, 8));
        Assert.NotNull(PasswordPolicy.Validate("x712805984y", "a@b.tz", "255712805984", 8));
        Assert.NotNull(PasswordPolicy.Validate(new string('k', 129) + "1a", "a@b.tz", null, 8)); // zaidi ya 128
        Assert.Null(PasswordPolicy.Validate("mvua ya vuli imefika", "frank@test.tz", "255712805984", 8));
        Assert.Equal(8, PasswordPolicy.MinLength(null));
        Assert.Equal(8, PasswordPolicy.MinLength(4));     // config haiwezi kudhoofisha chini ya 8
        Assert.Equal(12, PasswordPolicy.MinLength(12));
    }

    // ------------------------------------------------------------------ kuficha simu
    [Theory]
    [InlineData("255742674429", "25574••••429")]
    [InlineData("+255 742 674 429", "25574••••429")]
    [InlineData("0742", "••••")]
    public void Phones_Are_Masked_For_Viewers_Without_Rights(string phone, string expected)
        => Assert.Equal(expected, PhoneMask.Mask(phone));
}
