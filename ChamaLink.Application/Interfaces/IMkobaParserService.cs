using ChamaLink.Application.DTOs;

namespace ChamaLink.Application.Interfaces;

public interface IMKobaParserService
{
    Task<List<MKobaTransactionItemDto>> ParseStatementAsync(Stream stream);
    /// <summary>Kama ParseStatementAsync + balance na mistari isiyosomeka. (Default kwa stubs za zamani.)</summary>
    async Task<MKobaParseResult> ParseStatementDetailedAsync(Stream stream)
        => new() { Transactions = await ParseStatementAsync(stream) };
}