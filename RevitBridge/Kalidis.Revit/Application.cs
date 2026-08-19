using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;

namespace Kalidis.Revit;

public class Application : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        application.ControlledApplication.DocumentOpened += OnDocumentOpened;
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
        return Result.Succeeded;
    }

    private static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
    {
        try
        {
            InventoryService.Generate(e.Document);
        }
        catch
        {
            // Nunca bloquear a abertura do Revit por falha no inventário automático.
        }
    }
}
