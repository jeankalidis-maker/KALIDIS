using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kalidis.Revit;

/// <summary>
/// Ponte remota KALIDIS via GitHub.
/// - Comandos: branch exclusiva bridge-commands, detectada por SHA via git ls-remote.
/// - Resultados: branch exclusiva bridge-results, publicada por clone local separado.
/// - Deduplicação de resultado por SHA-256 do conteúdo, não por timestamp.
/// - Escrita atômica do resultado remoto para evitar arquivo parcial/vazio.
/// - Sincroniza o clone de resultados com origin antes de cada publicação.
///
/// Nenhuma chamada à API do Revit é feita em thread de fundo. A execução no modelo
/// continua sendo feita no contexto seguro do Idling.
/// </summary>
public static class RemoteBridgeService
{
    private const string RepositoryUrl = "https://github.com/jeankalidis-maker/KALIDIS.git";
    private const string LocalCommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string LocalResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private const string StatePath = @"C:\KALIDIS\Bridge\remoto.estado.json";
    private const string LogPath = @"C:\KALIDIS\Bridge\remoto.log";

    private const string CommandRepoPath = @"C:\KALIDIS\BridgeCommandsRepo";
    private const string CommandBranch = "bridge-commands";
    private const string CommandFilePath = "Bridge/remoto/comando.json";

    private const string ResultRepoPath = @"C:\KALIDIS\BridgeResultsRepo";
    private const string RepoResultPath = @"C:\KALIDIS\BridgeResultsRepo\Bridge\remoto\resultado.json";
    private const string ResultBranch = "bridge-results";

    private static readonly object Sync = new();
    private static DateTime _nextPollUtc = DateTime.MinValue;
    private static bool _pollRunning;
    private static string? _lastRemoteCommandId;
    private static string? _lastRemoteCommandSha;
    private static string? _lastPushedResultId;
    private static string? _lastPushedResultHash;

    public static void EnsureFiles()
    {
        Directory.CreateDirectory(@"C:\KALIDIS\Bridge");
        Directory.CreateDirectory(Path.GetDirectoryName(CommandRepoPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ResultRepoPath)!);

        if (!File.Exists(StatePath))
            WriteState("iniciado", null, null);
    }

    public static void Tick()
    {
        EnsureFiles();

        lock (Sync)
        {
            if (_pollRunning || DateTime.UtcNow < _nextPollUtc)
                return;

            _pollRunning = true;
            _nextPollUtc = DateTime.UtcNow.AddSeconds(1);
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
        try
        {
            await EnsureCommandRepoAsync();

            (int lsCode, string lsOutput) = await RunProcessAsync(
                "git",
                $"-C \"{CommandRepoPath}\" ls-remote origin refs/heads/{CommandBranch}",
                CommandRepoPath);

            if (lsCode != 0)
            {
                WriteState("sem_conexao_comando", _lastRemoteCommandId, lsOutput);
                return;
            }

            string? remoteSha = ParseLsRemoteSha(lsOutput);
            if (string.IsNullOrWhiteSpace(remoteSha))
            {
                WriteState("branch_comandos_sem_sha", _lastRemoteCommandId, lsOutput);
                return;
            }

            if (string.Equals(remoteSha, _lastRemoteCommandSha, StringComparison.OrdinalIgnoreCase))
                return;

            await RunCommandGitAsync($"fetch --depth 1 origin {CommandBranch}");

            (int showCode, string raw) = await RunProcessAsync(
                "git",
                $"-C \"{CommandRepoPath}\" show FETCH_HEAD:{CommandFilePath}",
                CommandRepoPath);

            if (showCode != 0 || string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException($"Não foi possível ler {CommandFilePath} na branch {CommandBranch}: {raw}");

            using JsonDocument json = JsonDocument.Parse(raw);
            JsonElement root = json.RootElement;

            bool ativo = !root.TryGetProperty("ativo", out JsonElement ativoEl) || ativoEl.ValueKind != JsonValueKind.False;
            string? id = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;

            _lastRemoteCommandSha = remoteSha;

            if (!ativo || string.IsNullOrWhiteSpace(id))
            {
                WriteState("comando_ignorado", id, null);
                return;
            }

            if (string.Equals(id, _lastRemoteCommandId, StringComparison.Ordinal))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(LocalCommandPath)!);
            string commandTmp = LocalCommandPath + ".tmp";
            await File.WriteAllTextAsync(commandTmp, raw.Trim() + Environment.NewLine, new UTF8Encoding(false));
            File.Move(commandTmp, LocalCommandPath, true);

            _lastRemoteCommandId = id;
            WriteState("comando_recebido_rapido", id, null);
        }
        catch (Exception ex)
        {
            WriteState("erro_ler_comando", _lastRemoteCommandId, ex.Message);
        }
    }

    private static async Task EnsureCommandRepoAsync()
    {
        if (Directory.Exists(Path.Combine(CommandRepoPath, ".git")))
            return;

        if (Directory.Exists(CommandRepoPath) && Directory.EnumerateFileSystemEntries(CommandRepoPath).Any())
            throw new InvalidOperationException($"A pasta {CommandRepoPath} existe, mas não é um repositório Git vazio/válido.");

        Directory.CreateDirectory(CommandRepoPath);

        (int initCode, string initOutput) = await RunProcessAsync(
            "git",
            $"-C \"{CommandRepoPath}\" init",
            CommandRepoPath);

        if (initCode != 0)
            throw new InvalidOperationException($"git init do canal de comandos falhou ({initCode}): {initOutput}");

        (int remoteCode, string remoteOutput) = await RunProcessAsync(
            "git",
            $"-C \"{CommandRepoPath}\" remote add origin \"{RepositoryUrl}\"",
            CommandRepoPath);

        if (remoteCode != 0)
            throw new InvalidOperationException($"git remote add origin falhou ({remoteCode}): {remoteOutput}");

        WriteState("canal_comandos_pronto", _lastRemoteCommandId, null);
    }

    private static string? ParseLsRemoteSha(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        string firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        string[] parts = firstLine.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0].Trim() : null;
    }

    private static async Task PublishLocalResultAsync()
    {
        if (!File.Exists(LocalResultPath)) return;

        string raw;
        try
        {
            raw = await File.ReadAllTextAsync(LocalResultPath, Encoding.UTF8);
        }
        catch (IOException)
        {
            // Serviço do Revit pode estar concluindo a escrita neste exato instante.
            // O próximo Tick tentará novamente.
            return;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            WriteState("resultado_local_vazio_ignorado", null, null);
            return;
        }

        string? id;
        try
        {
            using JsonDocument json = JsonDocument.Parse(raw);
            id = json.RootElement.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;
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

        string normalized = raw.Trim() + Environment.NewLine;
        string resultHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();

        if (string.Equals(resultHash, _lastPushedResultHash, StringComparison.OrdinalIgnoreCase))
            return;

        WriteState("resultado_detectado", id, null);

        try
        {
            await EnsureResultRepoAsync();
            await SyncResultRepoAsync();

            Directory.CreateDirectory(Path.GetDirectoryName(RepoResultPath)!);
            string tmpPath = RepoResultPath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, normalized, new UTF8Encoding(false));
            File.Move(tmpPath, RepoResultPath, true);

            // Segurança adicional: nunca stage/push se o arquivo final estiver vazio.
            FileInfo info = new(RepoResultPath);
            if (!info.Exists || info.Length == 0)
                throw new InvalidOperationException("Resultado remoto ficou vazio antes do stage; publicação cancelada.");

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
                    // Se outra publicação avançou a branch, sincroniza e reaplica o resultado local.
                    await SyncResultRepoAsync();
                    await File.WriteAllTextAsync(tmpPath, normalized, new UTF8Encoding(false));
                    File.Move(tmpPath, RepoResultPath, true);
                    await RunResultGitAsync("add Bridge/remoto/resultado.json");

                    (int retryDiff, string retryOutput) = await RunProcessAsync(
                        "git",
                        $"-C \"{ResultRepoPath}\" diff --cached --quiet -- Bridge/remoto/resultado.json",
                        ResultRepoPath);

                    if (retryDiff == 1)
                        await RunResultGitAsync($"commit -m \"bridge: resultado {SanitizeForCommit(id)} retry\"");
                    else if (retryDiff != 0)
                        throw new InvalidOperationException($"git diff retry falhou ({retryDiff}): {retryOutput}");

                    await RunResultGitAsync($"push origin HEAD:{ResultBranch}");
                }
            }
            else if (diffCode != 0)
            {
                throw new InvalidOperationException($"git diff --cached falhou ({diffCode}): {diffOutput}");
            }

            _lastPushedResultId = id;
            _lastPushedResultHash = resultHash;
            WriteState("resultado_publicado_hash", id, null);
        }
        catch (Exception ex)
        {
            WriteState("erro_publicar_resultado", id, ex.Message);
        }
    }

    private static async Task SyncResultRepoAsync()
    {
        await RunResultGitAsync($"fetch origin {ResultBranch}");
        await RunResultGitAsync($"reset --hard origin/{ResultBranch}");
        await RunResultGitAsync("clean -f -- Bridge/remoto/resultado.json.tmp");
    }

    private static async Task EnsureResultRepoAsync()
    {
        if (Directory.Exists(Path.Combine(ResultRepoPath, ".git")))
            return;

        if (Directory.Exists(ResultRepoPath) && Directory.EnumerateFileSystemEntries(ResultRepoPath).Any())
            throw new InvalidOperationException($"A pasta {ResultRepoPath} existe, mas não é um repositório Git vazio/válido.");

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

    private static async Task<string> RunCommandGitAsync(string arguments)
    {
        (int code, string output) = await RunProcessAsync(
            "git",
            $"-C \"{CommandRepoPath}\" {arguments}",
            CommandRepoPath);

        if (code != 0)
            throw new InvalidOperationException($"git {arguments} falhou ({code}): {output}");

        return output;
    }

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
                versao = "1.3-remoto-result-hash-atomic",
                status,
                comandoId = id,
                ultimoComandoSha = _lastRemoteCommandSha,
                ultimoResultadoPublicado = _lastPushedResultId,
                ultimoResultadoHash = _lastPushedResultHash,
                branchComandos = CommandBranch,
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
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | status={status} | id={id ?? "-"} | commandSha={_lastRemoteCommandSha ?? "-"} | resultHash={_lastPushedResultHash ?? "-"} | erro={erro ?? "-"}";
            File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
        }
    }
}
