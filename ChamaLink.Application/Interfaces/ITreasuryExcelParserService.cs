using ChamaLink.Application.DTOs;

namespace ChamaLink.Application.Interfaces;

// Parses a treasurer's monthly-grid Excel ledger (rows = members,
// columns = JAN..DEC + KIANZIO + MSIBA + SHEREHE + MAWASILIANO) into a
// list of normalized TreasuryExcelRowDto rows. Uses ExcelDataReader
// (already referenced by ChamaLink.Application). The parser is purely
// structural: it does NOT match members or write anything - that is
// TreasuryImportService's job. This mirrors IMKobaParserService's split
// (parse -> DTOs -> controller/service processes).
public interface ITreasuryExcelParserService
{
    Task<List<TreasuryExcelRowDto>> ParseAsync(Stream stream);
}
