using System.Text;
using System.Text.Json;

namespace Kalidis.Revit;

/// <summary>
/// Proteção persistente contra reexecução do mesmo comando remoto.
/// O relay pode regravar comando.json mais de uma vez; o ID lógico do comando
/// é a fonte de verdade para impedir movimentos/rotações relativos duplicados.
/// </summary>
public static class CommandReplayGuard
{
    private const string CommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string StatePath = @"C:\KALIDIS\Bridge\ultimo-comando-processado.id";

    public static string? CurrentCommandId()
    {
        try
        {
            if (!File.Exists(CommandPath)) return null;
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(CommandPath, Encoding.UTF8));
            if (!json.RootElement.TryGetProperty("id", out JsonElement idEl) || idEl.ValueKind != JsonValueKind.String)
                return null;
            string? id = idEl.GetString();
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static bool IsAlreadyProcessed(string? commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return false;
        try
        {
            if (!File.Exists(StatePath)) return false;
            string last = File.ReadAllText(StatePath, Encoding.UTF8).Trim();
            return string.Equals(last, commandId, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static void MarkProcessed(string? commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, commandId.Trim() + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
            // Nunca bloquear o Revit por falha ao persistir a proteção anti-replay.
        }
    }
}
