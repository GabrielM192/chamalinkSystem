using ChamaLink.Domain.Entities;

namespace ChamaLink.Infrastructure.Services;

// Matches an Excel row's member name against the group's already-registered
// members (by User.FullName). Phone is deliberately ignored - the treasurer's
// manual Excel is missing phones for half the members and has spelling
// variations (BOAZ vs BOAZI, ISHEKELI vs ISHEKERI), so matching by phone
// would either fail or auto-create duplicates (the "Frank Mkoma/Mkoma Frank"
// bug). Matching by normalised name + a confirmation step is the safe path.
//
// This is a pure (testable) helper with no DB dependency: the caller passes
// the candidate members it already loaded, the matcher returns scores.
public static class MemberNameMatcher
{
    // Normalise a name for comparison: uppercase, trim, collapse internal
    // whitespace to single spaces, strip a trailing period. Handles the
    // small inconsistencies between the Excel and what the group's
    // members were registered under.
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var n = name.Trim().ToUpperInvariant().TrimEnd('.');

        // collapse runs of whitespace (incl. non-breaking spaces/tabs) to one
        var sb = new System.Text.StringBuilder(n.Length);
        bool lastWasSpace = false;
        foreach (var ch in n)
        {
            bool isSpace = char.IsWhiteSpace(ch);
            if (isSpace)
            {
                if (!lastWasSpace && sb.Length > 0)
                    sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    // Classic iterative Levenshtein edit distance. Used to score how close
    // two names are (handles single-char swaps/insertions like
    // "BOAZ" vs "BOAZI", "ISHEKELI" vs "ISHEKERI").
    public static int Levenshtein(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b.Length;
        if (string.IsNullOrEmpty(b)) return a.Length;

        int n = a.Length, m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[m];
    }

    // Similarity ratio 0.0 - 1.0 between two (normalised) names.
    public static double Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
        int maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        int dist = Levenshtein(a, b);
        return 1.0 - ((double)dist / maxLen);
    }

    // A scored candidate returned to the caller.
    public readonly record struct ScoredMember(
        Guid MemberId,
        string MemberName,
        int Confidence,        // 0-100
        bool IsExact);

    // Match an Excel name against the group's members.
    // exactThreshold (1.0) => confidence 100. Below that, fuzzy candidates
    // are returned sorted best-first. The caller decides whether the best
    // score is good enough to auto-accept or must be confirmed.
    public static ScoredMember? Match(
        string excelName,
        IReadOnlyList<(GroupMember Member, User? User)> candidates,
        out List<ScoredMember> alternatives)
    {
        alternatives = new List<ScoredMember>();
        var target = Normalize(excelName);
        if (target.Length == 0 || candidates.Count == 0)
            return null;

        var scored = new List<ScoredMember>();
        foreach (var (member, user) in candidates)
        {
            if (user == null) continue;
            var candName = Normalize(user.FullName);
            if (candName.Length == 0) continue;

            double sim = Similarity(target, candName);
            bool exact = sim == 1.0;
            int confidence = (int)Math.Round(sim * 100);
            scored.Add(new ScoredMember(member.Id, user.FullName, confidence, exact));
        }

        if (scored.Count == 0)
            return null;

        // best first
        scored.Sort((x, y) => y.Confidence.CompareTo(x.Confidence));

        // alternatives shown in preview = anything reasonably close (>=60)
        // excluding the top pick, so the treasurer can confirm/pick.
        alternatives = scored.Skip(1).Where(s => s.Confidence >= 60).Take(5).ToList();

        return scored[0];
    }
}
