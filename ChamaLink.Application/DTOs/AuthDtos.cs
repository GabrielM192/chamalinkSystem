using System.ComponentModel.DataAnnotations;

namespace ChamaLink.Application.DTOs;

// SECURITY FIX (audit 2.1: "DTOs hazina validation rules"): these fields
// used to accept anything at all - empty names, malformed emails, blank
// passwords. [ApiController] makes ASP.NET Core check these attributes
// automatically and return 400 before the request ever reaches AuthService,
// so a bad request never gets as far as a database query.
public record RegisterDto(
    [Required(ErrorMessage = "Jina kamili linahitajika.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Jina lazima liwe kati ya herufi 2 na 200.")]
    string FullName,

    [Required(ErrorMessage = "Barua pepe inahitajika.")]
    [EmailAddress(ErrorMessage = "Barua pepe si sahihi.")]
    string Email,

    [Required(ErrorMessage = "Namba ya simu inahitajika.")]
    [StringLength(20, MinimumLength = 9, ErrorMessage = "Namba ya simu si sahihi.")]
    string PhoneNumber,

    [Required(ErrorMessage = "Neno la siri linahitajika.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Neno la siri liwe angalau herufi 6.")]
    string Password
);

public record LoginDto(
    [Required(ErrorMessage = "Barua pepe inahitajika.")]
    [EmailAddress(ErrorMessage = "Barua pepe si sahihi.")]
    string Email,

    [Required(ErrorMessage = "Neno la siri linahitajika.")]
    string Password
);

public record AuthResponseDto(
    string Token,
    Guid UserId,
    string FullName,
    string Email
);