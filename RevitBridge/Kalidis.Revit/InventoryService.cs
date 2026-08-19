using System.Text;
using Autodesk.Revit.DB;

namespace Kalidis.Revit;

public static class InventoryService
{
    private const string ReportFolder = @"C:\KALIDIS\Reports";
    private const string InventoryPath = @"C:\KALIDIS\Reports\inventario_modelo.csv";
    private const string StatePath = @"C:\KALIDIS\Reports\estado_modelo.txt";

    public static InventoryResult Generate(Document doc)
    {
        Directory.CreateDirectory(ReportFolder);

        var elements = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements();

        var categorized = elements
            .Where(e => e.Category != null)
            .ToList();

        var categories = categorized
            .GroupBy(e => e.Category!.Name)
            .Select(g => new CategoryCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .ToList();

        var familyTypes = categorized
            .OfType<FamilyInstance>()
            .GroupBy(fi => new
            {
                Category = fi.Category?.Name ?? "<sem categoria>",
                Family = fi.Symbol?.FamilyName ?? "<sem família>",
                Type = fi.Symbol?.Name ?? "<sem tipo>"
            })
            .Select(g => new FamilyTypeCount(g.Key.Category, g.Key.Family, g.Key.Type, g.Count()))
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Family)
            .ThenBy(x => x.Type)
            .ToList();

        var csv = new StringBuilder();
        csv.AppendLine("SECAO;CATEGORIA;FAMILIA;TIPO;QUANTIDADE");

        foreach (var category in categories)
            csv.AppendLine($"CATEGORIA;{Esc(category.Name)};;;{category.Count}");

        foreach (var ft in familyTypes)
            csv.AppendLine($"FAMILIA_TIPO;{Esc(ft.Category)};{Esc(ft.Family)};{Esc(ft.Type)};{ft.Count}");

        File.WriteAllText(InventoryPath, csv.ToString(), new UTF8Encoding(true));

        string documentPath = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : doc.PathName;
        string state =
            $"KALIDIS Revit Bridge v0.4{Environment.NewLine}" +
            $"GeradoEm={DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
            $"Projeto={documentPath}{Environment.NewLine}" +
            $"ElementosComCategoria={categorized.Count}{Environment.NewLine}" +
            $"Categorias={categories.Count}{Environment.NewLine}" +
            $"InstanciasFamilias={categorized.OfType<FamilyInstance>().Count()}{Environment.NewLine}" +
            $"FamiliasTiposDistintos={familyTypes.Count}{Environment.NewLine}" +
            $"Inventario={InventoryPath}{Environment.NewLine}";

        File.WriteAllText(StatePath, state, new UTF8Encoding(true));

        return new InventoryResult(
            categorized.Count,
            categories.Count,
            categorized.OfType<FamilyInstance>().Count(),
            familyTypes.Count,
            categories.Take(15).ToList(),
            InventoryPath,
            StatePath);
    }

    private static string Esc(string value)
    {
        value ??= string.Empty;
        return value.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
    }
}

public record CategoryCount(string Name, int Count);
public record FamilyTypeCount(string Category, string Family, string Type, int Count);
public record InventoryResult(
    int ElementsWithCategory,
    int CategoryCount,
    int FamilyInstances,
    int DistinctFamilyTypes,
    IReadOnlyList<CategoryCount> TopCategories,
    string InventoryPath,
    string StatePath);
