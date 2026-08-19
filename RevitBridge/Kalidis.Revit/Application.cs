using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Kalidis.Revit;

public class Application : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        application.ControlledApplication.DocumentOpened += OnDocumentOpened;
        application.Idling += OnIdling;
        BridgeService.EnsureFiles();
        RemoteBridgeService.EnsureFiles();
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
        application.Idling -= OnIdling;
        return Result.Succeeded;
    }

    private static void OnDocumentOpened(object? sender, DocumentOpenedEventArgs e)
    {
        try
        {
            InventoryService.Generate(e.Document);
            BridgeService.EnsureFiles();
            RemoteBridgeService.EnsureFiles();
        }
        catch
        {
            // Nunca bloquear a abertura do Revit por falha no inventário/bridge.
        }
    }

    private static void OnIdling(object? sender, IdlingEventArgs e)
    {
        try
        {
            // Rede/Git roda em background; as chamadas à API do Revit continuam
            // exclusivamente no contexto seguro do Idling abaixo.
            RemoteBridgeService.Tick();

            if (sender is UIApplication uiApp)
            {
                BridgeService.TryProcess(uiApp);
                MaxBridgeService.TryProcess(uiApp);
            }
        }
        catch
        {
            // Bridge não pode travar a interface do Revit.
        }
    }
}
