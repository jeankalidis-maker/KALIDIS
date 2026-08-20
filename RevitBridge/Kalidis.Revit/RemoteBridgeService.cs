using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Kalidis.Revit;

/// <summary>
/// Ponte remota KALIDIS via GitHub.
/// - Lê comandos publicados em Bridge/remoto/comando.json (repositório público).
/// - Entrega o comando para o bridge local já existente.
/// - Publica o resultado local em Bridge/remoto/resultado.json usando o Git local.
///
/// Nenhuma chamada à API do Revit é feita em thread de fundo. A execução no modelo
/// continua sendo feita pelo BridgeService/MaxBridgeService no evento Idling.
/// </summary>
public static class RemoteBridgeService
{
    private const string RemoteCommandUrl = "https://raw.githubusercontent.com/jeankalidis-maker/KALIDIS/main/Bridge/remoto/comando.json";
    private const string LocalCommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string LocalResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private const string StatePath = @"C:\KALIDIS\Bridge\remoto.estado.json";
    private const string LogPath = @"C:\KALIDIS\Bridge\remoto.log";
    private const string RepoPath = @"C:\Users\naose\KALIDIS";
    private const string RepoResultPath = @"C:\Users\naose\KALIDIS\Bridge\remoto\resultado.json";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private static readonly object Sync = new();
    private static DateTime _nextPollUtc = DateTime.MinValue;
    private static bool _pollRunning;
    private static DateTime _lastResultWriteUtc = DateTime.MinValue;
    private static string? _lastRemoteCommandId;
    private static string? _lastPushedResultId;

    public static void EnsureFiles()
    {
        Directory.CreateDirectory(@"C:\KALIDIS\Bridge");
        Directory.CreateDirectory(Path.GetDirectoryName(RepoResultPath)!);

        if (!File.Exists(StatePath))
            WriteState("iniciado", null, null);
    }

    /// <summary>
    /// Deve ser chamado no Idling. Apenas agenda I/O de rede/Git em background.
    /// </summary>
    public static void Tick()
    {
        EnsureFiles();

        lock (Sync)
        {
            if (_pollRunning || DateTime.UtcNow < _nextPollUtc)
                return;

            _pollRunning = true;
            _nextPollUtc = DateTime.UtcNow.AddMilliseconds(500);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await PullRemoteCommandAsync();
                await PublishLocalResultAsync();
            }
            catch (Exception ex)
            {
                WriteState("erro", _lastRemoteCommandId, ex.Message);
            }
            finally
            {
                lock (Sync) _pollRunning = false;
            }
        });
    }

    private static async Task PullRemoteCommandAsync()
    {
        string raw;
        try
        {
            raw = await Http.GetStringAsync(RemoteCommandUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        catch (Exception ex)
        {
            WriteState("sem_conexao_comando", _lastRemoteCommandId, ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(raw)) return;

        using JsonDocument json = JsonDocument.Parse(raw);
        JsonElement root = json.RootElement;

        bool ativo = !root.TryGetProperty("ativo", out JsonElement ativoEl) || ativoEl.ValueKind != JsonValueKind.False;
        string? id = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;

        if (!ativo || string.IsNullOrWhiteSpace(id) || string.Equals(id, _lastRemoteCommandId, StringComparison.Ordinal))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(LocalCommandPath)!);
        File.WriteAllText(LocalCommandPath, raw.Trim() + Environment.NewLine, new UTF8Encoding(false));
        _lastRemoteCommandId = id;
        WriteState("comando_recebido", id, null);
    }

    private static async Task PublishLocalResultAsync()
    {
        if (!File.Exists(LocalResultPath)) return;

        DateTime writeUtc = File.GetLastWriteTimeUtc(LocalResultPath);
        if (writeUtc <= _lastResultWriteUtc) return;

        string raw = await File.ReadAllTextAsync(LocalResultPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(raw)) return;

        string? id = null;
        try
        {
            using JsonDocument json = JsonDocument.Parse(raw);
            if (json.RootElement.TryGetProperty("id", out JsonElement idEl))
                id = idEl.GetString();
        }
        catch (Exception ex)
        {
            WriteState("resultado_json_invalido", null, ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            WriteState("resultado_sem_id", null, null);
            return;
        }

        if (string.Equals(id, _lastPushedResultId, StringComparison.Ordinal))
        {
            _lastResultWriteUtc = writeUtc;
            return;
        }

        if (!Directory.Exists(Path.Combine(RepoPath, ".git")))
        {
            WriteState("resultado_local_sem_repo_git", id, $"Repositório local não encontrado em {RepoPath}");
            return;
        }

        WriteState("resultado_detectado", id, null);

        try
        {
            await RunGitAsync("pull --rebase --autostash");

            Directory.CreateDirectory(Path.GetDirectoryName(RepoResultPath)!);
            await File.WriteAllTextAsync(RepoResultPath, raw.Trim() + Environment.NewLine, new UTF8Encoding(false));
            await RunGitAsync("add Bridge/remoto/resultado.json");

            (int diffCode, string diffOutput) = await RunProcessAsync(
                "git",
                $"-C \"{RepoPath}\" diff --cached --quiet -- Bridge/remoto/resultado.json");

            if (diffCode == 1)
            {
                await RunGitAsync($"commit -m \"bridge: resultado {SanitizeForCommit(id)}\"");
                await RunGitAsync("push");
            }
            else if (diffCode != 0)
            {
                throw new InvalidOperationException($"git diff --cached falhou ({diffCode}): {diffOutput}");
            }

            _lastPushedResultId = id;
            _lastResultWriteUtc = writeUtc;
            WriteState("resultado_publicado", id, null);
        }
        catch (Exception ex)
        {
            WriteState("erro_publicar_resultado", id, ex.Message);
        }
    }

    private static string SanitizeForCommit(string value)
        => value.Replace("\"", "").Replace("\r", " ").Replace("\n", " ");

    private static async Task<string> RunGitAsync(string arguments)
    {
        (int code, string output) = await RunProcessAsync("git", $"-C \"{RepoPath}\" {arguments}");
        if (code != 0)
            throw new InvalidOperationException($"git {arguments} falhou ({code}): {output}");
        return output;
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = RepoPath
        };

        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = (await stdout) + Environment.NewLine + (await stderr);
        return (process.ExitCode, output.Trim());
    }

    private static void WriteState(string status, string? id, string? erro)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            var state = new
            {
                versao = "1.0-remoto-rapido",
                status,
                comandoId = id,
                ultimoResultadoPublicado = _lastPushedResultId,
                atualizadoEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                erro
            };
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StatePath, json + Environment.NewLine, new UTF8Encoding(false));
            AppendLog(status, id, erro);
        }
        catch
        {
        }
    }

    private static void AppendLog(string status, string? id, string? erro)
    {
        try
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {status} | id={id ?? "-"} | erro={erro ?? "-"}";
            File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
        }
    }
}
