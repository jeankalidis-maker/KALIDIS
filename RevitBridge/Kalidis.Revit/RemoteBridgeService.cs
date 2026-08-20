using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Kalidis.Revit;

/// <summary>
/// Ponte remota KALIDIS via GitHub.
/// - Lê comandos em main/Bridge/remoto/comando.json.
/// - Entrega o comando ao bridge local.
/// - Publica resultados em uma branch exclusiva (bridge-results), usando um clone
///   local separado para não disputar o repositório de desenvolvimento nem exigir
///   pull/rebase a cada resposta.
///
/// Nenhuma chamada à API do Revit é feita em thread de fundo. A execução no modelo
/// continua sendo feita no contexto seguro do Idling.
/// </summary>
public static class RemoteBridgeService
{
    private const string RepositoryUrl = "https://github.com/jeankalidis-maker/KALIDIS.git";
    private const string RemoteCommandUrl = "https://raw.githubusercontent.com/jeankalidis-maker/KALIDIS/main/Bridge/remoto/comando.json";
    private const string LocalCommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string LocalResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private const string StatePath = @"C:\KALIDIS\Bridge\remoto.estado.json";
    private const string LogPath = @"C:\KALIDIS\Bridge\remoto.log";

    // Clone EXCLUSIVO para resultados. Não toca em C:\Users\naose\KALIDIS.
    private const string ResultRepoPath = @"C:\KALIDIS\BridgeResultsRepo";
    private const string RepoResultPath = @"C:\KALIDIS\BridgeResultsRepo\Bridge\remoto\resultado.json";
    private const string ResultBranch = "bridge-results";

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
        Directory.CreateDirectory(Path.GetDirectoryName(ResultRepoPath)!);

        if (!File.Exists(StatePath))
            WriteState("iniciado", null, null);
    }

    /// <summary>
    /// Chamado no Idling. Rede/Git ficam em background.
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

        WriteState("resultado_detectado", id, null);

        try
        {
            await EnsureResultRepoAsync();

            Directory.CreateDirectory(Path.GetDirectoryName(RepoResultPath)!);
            await File.WriteAllTextAsync(RepoResultPath, raw.Trim() + Environment.NewLine, new UTF8Encoding(false));

            await RunResultGitAsync("add Bridge/remoto/resultado.json");

            (int diffCode, string diffOutput) = await RunProcessAsync(
                "git",
                $"-C \"{ResultRepoPath}\" diff --cached --quiet -- Bridge/remoto/resultado.json",
                ResultRepoPath);

            if (diffCode == 1)
            {
                await RunResultGitAsync($"commit -m \"bridge: resultado {SanitizeForCommit(id)}\"");

                try
                {
                    await RunResultGitAsync($"push origin HEAD:{ResultBranch}");
                }
                catch
                {
                    // Recuperação rara caso a branch exclusiva tenha sido alterada externamente.
                    await RunResultGitAsync($"pull --rebase origin {ResultBranch}");
                    await RunResultGitAsync($"push origin HEAD:{ResultBranch}");
                }
            }
            else if (diffCode != 0)
            {
                throw new InvalidOperationException($"git diff --cached falhou ({diffCode}): {diffOutput}");
            }

            _lastPushedResultId = id;
            _lastResultWriteUtc = writeUtc;
            WriteState("resultado_publicado_rapido", id, null);
        }
        catch (Exception ex)
        {
            // Não marca como processado: haverá retry automático no próximo Tick.
            WriteState("erro_publicar_resultado", id, ex.Message);
        }
    }

    private static async Task EnsureResultRepoAsync()
    {
        if (Directory.Exists(Path.Combine(ResultRepoPath, ".git")))
            return;

        if (Directory.Exists(ResultRepoPath) && Directory.EnumerateFileSystemEntries(ResultRepoPath).Any())
            throw new InvalidOperationException($"A pasta {ResultRepoPath} existe, mas não é um repositório Git vazio/valido.");

        Directory.CreateDirectory(Path.GetDirectoryName(ResultRepoPath)!);
        WriteState("criando_clone_resultados", _lastRemoteCommandId, null);

        (int code, string output) = await RunProcessAsync(
            "git",
            $"clone --branch {ResultBranch} --single-branch --depth 1 \"{RepositoryUrl}\" \"{ResultRepoPath}\"",
            @"C:\KALIDIS");

        if (code != 0)
            throw new InvalidOperationException($"git clone da branch {ResultBranch} falhou ({code}): {output}");

        WriteState("clone_resultados_pronto", _lastRemoteCommandId, null);
    }

    private static string SanitizeForCommit(string value)
        => value.Replace("\"", "").Replace("\r", " ").Replace("\n", " ");

    private static async Task<string> RunResultGitAsync(string arguments)
    {
        (int code, string output) = await RunProcessAsync(
            "git",
            $"-C \"{ResultRepoPath}\" {arguments}",
            ResultRepoPath);

        if (code != 0)
            throw new InvalidOperationException($"git {arguments} falhou ({code}): {output}");

        return output;
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory)
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
            WorkingDirectory = workingDirectory
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
                versao = "1.1-remoto-branch-resultados",
                status,
                comandoId = id,
                ultimoResultadoPublicado = _lastPushedResultId,
                branchResultados = ResultBranch,
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
