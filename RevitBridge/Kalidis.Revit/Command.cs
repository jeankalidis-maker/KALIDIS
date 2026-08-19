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

        int paredes = CountInstances(doc, BuiltInCategory.OST_Walls);
        int pisos = CountInstances(doc, BuiltInCategory.OST_Floors);
        int portas = CountInstances(doc, BuiltInCategory.OST_Doors);
        int janelas = CountInstances(doc, BuiltInCategory.OST_Windows);
        int ambientes = CountInstances(doc, BuiltInCategory.OST_Rooms);
        int mobiliario = CountInstances(doc, BuiltInCategory.OST_Furniture);
        int equipamentos = CountInstances(doc, BuiltInCategory.OST_MechanicalEquipment);
        int instanciasFamilia = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .GetElementCount();

        StringBuilder sb = new();
        sb.AppendLine("Conexão com o Revit OK.");
        sb.AppendLine();
        sb.AppendLine($"Projeto ativo:\n{arquivo}");
        sb.AppendLine();
        sb.AppendLine("LEITURA DO MODELO");
        sb.AppendLine($"Paredes: {paredes}");
        sb.AppendLine($"Pisos: {pisos}");
        sb.AppendLine($"Portas: {portas}");
        sb.AppendLine($"Janelas: {janelas}");
        sb.AppendLine($"Ambientes: {ambientes}");
        sb.AppendLine($"Mobiliário: {mobiliario}");
        sb.AppendLine($"Equipamentos mecânicos: {equipamentos}");
        sb.AppendLine($"Instâncias de famílias: {instanciasFamilia}");
        sb.AppendLine();
        sb.AppendLine("KALIDIS Revit Bridge v0.2");

        TaskDialog.Show("KALIDIS - Leitura do Modelo", sb.ToString());
        return Result.Succeeded;
    }

    private static int CountInstances(Document doc, BuiltInCategory category)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .GetElementCount();
    }
}
