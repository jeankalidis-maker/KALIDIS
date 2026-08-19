using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Kalidis.Revit;

[Transaction(TransactionMode.Manual)]
public class Command : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        UIDocument uiDoc = commandData.Application.ActiveUIDocument;

        if (uiDoc == null)
        {
            TaskDialog.Show("KALIDIS", "Nenhum projeto Revit está aberto.");
            return Result.Cancelled;
        }

        Document doc = uiDoc.Document;
        string arquivo = string.IsNullOrWhiteSpace(doc.PathName)
            ? doc.Title + " (ainda não salvo)"
            : doc.PathName;

        var elementos = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements();

        var porCategoria = elementos
            .Where(e => e.Category != null)
            .GroupBy(e => e.Category!.Name)
            .Select(g => new { Categoria = g.Key, Quantidade = g.Count() })
            .OrderByDescending(x => x.Quantidade)
            .ThenBy(x => x.Categoria)
            .ToList();

        var familias = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .GroupBy(fi => new
            {
                Familia = fi.Symbol?.Family?.Name ?? "(sem família)",
                Tipo = fi.Symbol?.Name ?? "(sem tipo)",
                Categoria = fi.Category?.Name ?? "(sem categoria)"
            })
            .Select(g => new
            {
                g.Key.Categoria,
                g.Key.Familia,
                g.Key.Tipo,
                Quantidade = g.Count()
            })
            .OrderBy(x => x.Categoria)
            .ThenBy(x => x.Familia)
            .ThenBy(x => x.Tipo)
            .ToList();

        string pastaRelatorios = @"C:\KALIDIS\Reports";
        Directory.CreateDirectory(pastaRelatorios);
        string caminhoCsv = Path.Combine(pastaRelatorios, "inventario_modelo.csv");

        StringBuilder csv = new();
        csv.AppendLine("SECAO;CATEGORIA;FAMILIA;TIPO;QUANTIDADE");

        foreach (var item in porCategoria)
            csv.AppendLine($"CATEGORIA;{Esc(item.Categoria)};;;{item.Quantidade}");

        foreach (var item in familias)
            csv.AppendLine($"FAMILIA;{Esc(item.Categoria)};{Esc(item.Familia)};{Esc(item.Tipo)};{item.Quantidade}");

        File.WriteAllText(caminhoCsv, csv.ToString(), new UTF8Encoding(true));

        int totalComCategoria = porCategoria.Sum(x => x.Quantidade);
        int totalFamilias = familias.Sum(x => x.Quantidade);

        StringBuilder resumo = new();
        resumo.AppendLine("Conexão com o Revit OK.");
        resumo.AppendLine();
        resumo.AppendLine($"Projeto ativo:\n{arquivo}");
        resumo.AppendLine();
        resumo.AppendLine("INVENTÁRIO COMPLETO");
        resumo.AppendLine($"Elementos com categoria: {totalComCategoria}");
        resumo.AppendLine($"Categorias encontradas: {porCategoria.Count}");
        resumo.AppendLine($"Instâncias de famílias: {totalFamilias}");
        resumo.AppendLine($"Famílias/tipos distintos: {familias.Count}");
        resumo.AppendLine();
        resumo.AppendLine("TOP 15 CATEGORIAS:");

        foreach (var item in porCategoria.Take(15))
            resumo.AppendLine($"{item.Categoria}: {item.Quantidade}");

        resumo.AppendLine();
        resumo.AppendLine($"Relatório salvo em:\n{caminhoCsv}");
        resumo.AppendLine();
        resumo.AppendLine("KALIDIS Revit Bridge v0.3");

        TaskDialog.Show("KALIDIS - Inventário Completo", resumo.ToString());
        return Result.Succeeded;
    }

    private static string Esc(string value)
    {
        string v = value.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
        return v;
    }
}
