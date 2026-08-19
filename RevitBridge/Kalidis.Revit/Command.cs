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
        InventoryResult result = InventoryService.Generate(doc);

        string arquivo = string.IsNullOrWhiteSpace(doc.PathName)
            ? doc.Title + " (ainda não salvo)"
            : doc.PathName;

        StringBuilder resumo = new();
        resumo.AppendLine("Conexão com o Revit OK.");
        resumo.AppendLine();
        resumo.AppendLine($"Projeto ativo:\n{arquivo}");
        resumo.AppendLine();
        resumo.AppendLine("INVENTÁRIO COMPLETO");
        resumo.AppendLine($"Elementos com categoria: {result.ElementsWithCategory}");
        resumo.AppendLine($"Categorias encontradas: {result.CategoryCount}");
        resumo.AppendLine($"Instâncias de famílias: {result.FamilyInstances}");
        resumo.AppendLine($"Famílias/tipos distintos: {result.DistinctFamilyTypes}");
        resumo.AppendLine();
        resumo.AppendLine("TOP 15 CATEGORIAS:");

        foreach (var item in result.TopCategories)
            resumo.AppendLine($"{item.Name}: {item.Count}");

        resumo.AppendLine();
        resumo.AppendLine($"Inventário:\n{result.InventoryPath}");
        resumo.AppendLine($"Estado:\n{result.StatePath}");
        resumo.AppendLine();
        resumo.AppendLine("V0.4: inventário também é gerado automaticamente ao abrir um RVT.");

        TaskDialog.Show("KALIDIS - Inventário Completo", resumo.ToString());
        return Result.Succeeded;
    }
}
