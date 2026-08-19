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

        TaskDialog.Show(
            "KALIDIS Revit Bridge v0.1",
            $"Conexão com o Revit OK.\n\nProjeto ativo:\n{arquivo}\n\nPróximo passo: leitura de elementos e parâmetros.");

        return Result.Succeeded;
    }
}
