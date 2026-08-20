using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kalidis.Revit;

/// <summary>
/// Coordena a execução lógica dos comandos remotos.
/// Proteções:
/// - anti-replay persistente por ID;
/// - detecção de colisão (mesmo ID com conteúdo diferente);
/// - hash SHA-256 do comando;
/// - ledger persistente dos últimos comandos;
/// - auditoria JSONL;
/// - comandos mutáveis exigem ID explícito.
/// </summary>
public static class CommandReplayGuard
{
    private const string CommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string ResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private const string LedgerPath = @"C:\KALIDIS\Bridge\execucao-ledger.json";
    private const string AuditPath = @"C:\KALIDIS\Bridge\auditoria.jsonl";
    private const int MaxLedgerEntries = 1000;

    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly HashSet<string> ReadOnlyActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "listar", "info", "inventario", "listar_comandos_revit",
        "snapshot_ambiente", "snapshot_elemento", "analisar_proximidade",
        "detectar_aberturas_sem_cuba"
    };

    public static bool TryReadCurrent(out CommandEnvelope? command, out string? error)
    {
        command = null;
        error = null;
        try
        {
            if (!File.Exists(CommandPath)) return false;
            string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(raw)) return false;

            using JsonDocument json = JsonDocument.Parse(raw);
            JsonElement root = json.RootElement;
            bool active = !root.TryGetProperty("ativo", out JsonElement activeEl) || activeEl.ValueKind != JsonValueKind.False;
            string? id = GetString(root, "id")?.Trim();
            string? action = GetString(root, "acao")?.Trim();
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

            command = new CommandEnvelope(id, action, hash, raw, active, IsReadOnly(action));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryAcquire(CommandEnvelope command, out string? reason)
    {
        reason = null;
        if (!command.Active)
        {
            reason = "Comando inativo.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(command.Action))
        {
            reason = "Comando sem ação.";
            WriteGuardFailure(command, reason);
            return false;
        }

        if (!command.ReadOnly && string.IsNullOrWhiteSpace(command.Id))
        {
            reason = "Comandos que alteram o modelo exigem um ID único.";
            WriteGuardFailure(command, reason);
            return false;
        }

        // Leituras sem ID continuam permitidas, mas não entram no ledger.
        if (string.IsNullOrWhiteSpace(command.Id)) return true;

        lock (Sync)
        {
            List<LedgerEntry> ledger = LoadLedger();
            LedgerEntry? existing = ledger.LastOrDefault(x => string.Equals(x.Id, command.Id, StringComparison.Ordinal));
            if (existing != null)
            {
                if (!string.Equals(existing.Hash, command.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"COLISÃO DE ID: '{command.Id}' já foi usado com outro conteúdo.";
                    WriteGuardFailure(command, reason);
                    AppendAudit(command, "rejeitado", false, reason);
                    return false;
                }

                reason = $"Comando '{command.Id}' já registrado como '{existing.Status}' e não será reexecutado.";
                AppendAudit(command, "replay_bloqueado", false, reason);
                return false;
            }

            ledger.Add(new LedgerEntry(
                command.Id!, command.Hash, command.Action!, "in_progress",
                DateTime.UtcNow, null, null));
            SaveLedger(ledger);
            AppendAudit(command, "adquirido", true, "Comando adquirido para execução.");
            return true;
        }
    }

    public static void Complete(CommandEnvelope command, bool success, string? message)
    {
        if (string.IsNullOrWhiteSpace(command.Id))
        {
            AppendAudit(command, "concluido_sem_id", success, message ?? string.Empty);
            return;
        }

        lock (Sync)
        {
            List<LedgerEntry> ledger = LoadLedger();
            int index = ledger.FindLastIndex(x => string.Equals(x.Id, command.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                LedgerEntry old = ledger[index];
                ledger[index] = old with
                {
                    Status = success ? "completed" : "failed_or_unknown",
                    CompletedUtc = DateTime.UtcNow,
                    Message = message
                };
                SaveLedger(ledger);
            }
            AppendAudit(command, "finalizado", success, message ?? string.Empty);
        }
    }

    public static bool TryReadMatchingResult(CommandEnvelope command, out bool success, out string? message)
    {
        success = false;
        message = null;
        try
        {
            if (!File.Exists(ResultPath)) return false;
            string raw = File.ReadAllText(ResultPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(raw)) return false;
            using JsonDocument json = JsonDocument.Parse(raw);
            JsonElement root = json.RootElement;
            string? resultId = GetString(root, "id")?.Trim();
            if (!string.Equals(resultId, command.Id, StringComparison.Ordinal)) return false;
            success = root.TryGetProperty("sucesso", out JsonElement okEl) && okEl.ValueKind == JsonValueKind.True;
            message = GetString(root, "mensagem") ?? GetString(root, "erro");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void WriteGuardFailure(CommandEnvelope command, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
            object result = new
            {
                id = command.Id,
                sucesso = false,
                mensagem = message,
                quantidade = 0,
                elementIds = Array.Empty<long>(),
                erro = "KALIDIS_EXECUTION_GUARD",
                dados = new { action = command.Action, commandHash = command.Hash }
            };
            File.WriteAllText(ResultPath,
                JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch { }
    }

    private static bool IsReadOnly(string? action)
        => !string.IsNullOrWhiteSpace(action) && ReadOnlyActions.Contains(action);

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static List<LedgerEntry> LoadLedger()
    {
        try
        {
            if (!File.Exists(LedgerPath)) return new List<LedgerEntry>();
            return JsonSerializer.Deserialize<List<LedgerEntry>>(File.ReadAllText(LedgerPath, Encoding.UTF8), JsonOptions)
                   ?? new List<LedgerEntry>();
        }
        catch
        {
            return new List<LedgerEntry>();
        }
    }

    private static void SaveLedger(List<LedgerEntry> ledger)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LedgerPath)!);
            if (ledger.Count > MaxLedgerEntries)
                ledger = ledger.Skip(ledger.Count - MaxLedgerEntries).ToList();
            File.WriteAllText(LedgerPath,
                JsonSerializer.Serialize(ledger, JsonOptions) + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch { }
    }

    private static void AppendAudit(CommandEnvelope command, string phase, bool success, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AuditPath)!);
            object row = new
            {
                utc = DateTime.UtcNow,
                commandId = command.Id,
                action = command.Action,
                commandHash = command.Hash,
                phase,
                success,
                message
            };
            File.AppendAllText(AuditPath,
                JsonSerializer.Serialize(row) + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch { }
    }

    public sealed record CommandEnvelope(
        string? Id,
        string? Action,
        string Hash,
        string Raw,
        bool Active,
        bool ReadOnly);

    public sealed record LedgerEntry(
        string Id,
        string Hash,
        string Action,
        string Status,
        DateTime StartedUtc,
        DateTime? CompletedUtc,
        string? Message);
}
