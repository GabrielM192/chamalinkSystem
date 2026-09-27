using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ChamaLink.API.Extensions;

// SECURITY FIX (audit: "Client anaweza kutuma IDs za watu wengine na
// mfumo unaamini hizo IDs" / Broken Access Control - IDOR): controllers
// used to accept "who is performing this action" (IssuedByGroupMemberId,
// RecordedByGroupMemberId, DecidedByGroupMemberId, adminUserId) as a
// plain field/route value from the client, with nothing checking it
// against who actually authenticated. Anyone with a valid token could
// send someone else's ID and the backend would act as that person.
//
// This is the one place that pulls the real, authenticated actor's
// UserId out of the JWT (the "sub" claim AuthService puts in every
// token). Every controller that needs to know "who is calling this"
// must go through here - never accept it as a request parameter again.
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        // ASP.NET Core's JWT handler sometimes remaps the "sub" claim to
        // the long ClaimTypes.NameIdentifier URI depending on configuration,
        // so both forms are checked rather than assuming one.
        var raw = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(raw) || !Guid.TryParse(raw, out var userId))
            throw new UnauthorizedAccessException("Token haina utambulisho sahihi wa mtumiaji.");

        return userId;
    }
}
