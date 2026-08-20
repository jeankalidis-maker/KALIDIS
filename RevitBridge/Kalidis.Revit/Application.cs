using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Kalidis.Revit;

public class Application : IExternalApplication
{
    private const string CommandPath = @"C:\KALIDIS\Bridge\comando.json";

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
                if (FastBatchBridgeService.IsBatchCommand())
                {
                    FastBatchBridgeService.TryProcess(uiApp);
                }
                else if (BancadaCubaBridgeService.IsCommand())
                {
                    BancadaCubaBridgeService.TryProcess(uiApp);
                }
                else if (IsGeometryCommand())
                {
                    GeometryBridgeService.TryProcess(uiApp);
                }
                else
                {
                    BridgeService.TryProcess(uiApp);
                    MaxBridgeService.TryProcess(uiApp);
                }
            }
        }
        catch
        {
            // Bridge não pode travar a interface do Revit.
        }
    }

    private static bool IsGeometryCommand()
    {
        try
        {
            if (!File.Exists(CommandPath)) return false;
            string raw = File.ReadAllText(CommandPath);
            return raw.Contains("snapshot_ambiente", StringComparison.OrdinalIgnoreCase) ||
                   raw.Contains("snapshot_elemento", StringComparison.OrdinalIgnoreCase) ||
                   raw.Contains("analisar_proximidade", StringComparison.OrdinalIgnoreCase) ||
                   raw.Contains("detectar_aberturas_sem_cuba", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
