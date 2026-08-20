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
            // Rede/Git fica fora da API do Revit e apenas agenda trabalho em background.
            RemoteBridgeService.Tick();

            if (sender is not UIApplication uiApp) return;

            if (!CommandReplayGuard.TryReadCurrent(out CommandReplayGuard.CommandEnvelope? command, out _) || command == null)
                return;
            if (!command.Active) return;
            if (!CommandReplayGuard.TryAcquire(command, out _)) return;

            bool routed = false;
            string? routingError = null;

            try
            {
                // IMPORTANTE: exatamente um serviço processa cada comando.
                // O anti-replay global decide se o comando pode rodar; os serviços
                // não são mais encadeados no mesmo Idling.
                if (SmartBatchBridgeService.IsCommand())
                {
                    SmartBatchBridgeService.TryProcess(uiApp);
                    routed = true;
                }
                else if (FastBatchBridgeService.IsBatchCommand())
                {
                    FastBatchBridgeService.TryProcess(uiApp);
                    routed = true;
                }
                else if (BancadaCubaBridgeService.IsCommand())
                {
                    BancadaCubaBridgeService.TryProcess(uiApp);
                    routed = true;
                }
                else if (IsGeometryCommand())
                {
                    GeometryBridgeService.TryProcess(uiApp);
                    routed = true;
                }
                else if (MaxBridgeService.IsCommand())
                {
                    MaxBridgeService.TryProcess(uiApp);
                    routed = true;
                }
                else
                {
                    BridgeService.TryProcess(uiApp);
                    routed = true;
                }
            }
            catch (Exception ex)
            {
                routingError = ex.Message;
            }

            if (!routed)
            {
                string message = routingError ?? "Nenhum serviço aceitou o comando.";
                CommandReplayGuard.WriteGuardFailure(command, message);
                CommandReplayGuard.Complete(command, false, message);
                return;
            }

            if (CommandReplayGuard.TryReadMatchingResult(command, out bool success, out string? resultMessage))
            {
                CommandReplayGuard.Complete(command, success, resultMessage);
            }
            else
            {
                string message = routingError ??
                    "O serviço foi roteado, mas não publicou um resultado com o mesmo ID. Execução bloqueada contra replay; use novo ID após diagnóstico.";
                CommandReplayGuard.WriteGuardFailure(command, message);
                CommandReplayGuard.Complete(command, false, message);
            }
        }
        catch
        {
            // O bridge nunca deve derrubar a interface por exceção gerenciada.
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
