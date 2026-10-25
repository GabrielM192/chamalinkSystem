using ChamaLink.Domain.Localization;

namespace ChamaLink.Domain.Exceptions;

/// <summary>
/// Msingi wa makosa YOTE ya biashara (business rule) katika ChamaLink.
///
/// Kwa nini hii ipo:
/// Kabla ya hii, services zilitumia `throw new Exception("...")` kwa kila kitu,
/// na controllers zililazimika kutumia `catch (Exception) -> 400`. Matokeo yake:
///   1. Kosa la server (NullReference, Postgres ilikwama, DI imeshindwa)
///      lilionekana kama "400 Bad Request" - mtumiaji alilaumiwa kwa kosa la mfumo.
///   2. `ex.Message` mbichi (ikiwemo majina ya jedwali/columns za database)
///      ilikuwa inapelekwa kwa client.
///   3. GlobalExceptionMiddleware - ambayo iliandikwa vizuri - haikufikiwa kabisa
///      kwa sababu controllers zilikuwa zimekamata kila kitu kwanza.
///
/// Sasa: service inapotoa `AppException` (au moja ya watoto wake hapa chini),
/// GlobalExceptionMiddleware inaihubadilisha kuwa HTTP status sahihi na ujumbe
/// salama. Makosa yasiyo ya biashara yanaangukia 500 bila maelezo ya ndani.
///
/// Kanuni: Ujumbe wa `AppException` UNAWEZA kuonyeshwa kwa mtumiaji -
/// andika kwa Kiswahili kinachoeleweka, kamwe usiweke taarifa za ndani ya mfumo.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message) : base(message) { Template = message; }
    protected AppException(string message, Exception inner) : base(message, inner) { Template = message; }
    /// <summary>SASISHO14: ujumbe unaotafsirika — Template (Kiswahili chenye {0}) + Args; frontend inatafsiri.</summary>
    protected AppException(Loc message) : base(message.Text) { Template = message.Template; Args = message.Args.ToArray(); }

    /// <summary>Kiolezo cha ujumbe (ufunguo wa tafsiri). Kwa ujumbe usio na vigeu = Message.</summary>
    public string Template { get; }
    public string[] Args { get; } = Array.Empty<string>();
    /// <summary>Maelezo ya ziada yanayotafsirika (mf. orodha ya ukaguzi uliofeli).</summary>
    public IReadOnlyList<LocMessage> Details { get; init; } = Array.Empty<LocMessage>();
}

/// <summary>
/// Ombi la mtumiaji halikukidhi sheria za mfumo -> HTTP 400.
/// Mfano: "Kiasi cha mkopo hakiwezi kuzidi mara 3 ya akiba yako."
/// </summary>
public class ValidationException : AppException
{
    public ValidationException(string message) : base(message) { }
    public ValidationException(Loc message) : base(message) { }
}

/// <summary>
/// Rekodi iliyotafutwa haipo -> HTTP 404.
/// Mfano: "Mkopo haukupatikana."
/// </summary>
public class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(Loc message) : base(message) { }
}

/// <summary>
/// Hakuna kikao halali (refresh token haipo/imekwisha/imefutwa) -> HTTP 401. Frontend inampeleka mtumiaji kwenye login.
/// </summary>
public class UnauthorizedException : AppException
{
    public UnauthorizedException(string message) : base(message) { }
    public UnauthorizedException(Loc message) : base(message) { }
}

/// <summary>
/// Mtumiaji anajulikana ni nani, lakini hana ruhusa -> HTTP 403.
/// Hii ni tofauti na UnauthorizedAccessException ya .NET ambayo
/// GlobalExceptionMiddleware sasa inaipeleka pia kwenye 403 (si 401).
/// </summary>
public class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message) { }
    public ForbiddenException(Loc message) : base(message) { }
}

/// <summary>
/// Mgongano wa hali/data -> HTTP 409.
/// Mfano: "Nafasi ya Chairperson tayari ina mwanachama kwenye kikundi hiki."
/// </summary>
public class ConflictException : AppException
{
    public ConflictException(string message) : base(message) { }
    public ConflictException(Loc message) : base(message) { }
}
