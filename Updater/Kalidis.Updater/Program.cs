using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kalidis.Updater;

internal static class Program
{
    private const string RepositoryUrl = "https://github.com/jeankalidis-maker/KALIDIS.git";
    private const string Branch = "main";
    private const string Root = @"C:\KALIDIS\Updater";
    private const string Mirror = @"C:\KALIDIS\Updater\SourceMirror";
    private const string Stage = @"C:\KALIDIS\Updater\Stage";
    private const string Backup = @"C:\KALIDIS\Updater\Backup";
    private const string InstalledDll = @"C:\KALIDIS\RevitBridge\Kalidis.Revit.dll";
    private const string StatePath = @"C:\KALIDIS\Updater\state.json";
    private const string LogPath = @"C:\KALIDIS\Updater\updater.log";

    private const string AgentCommandRepo = @"C:\KALIDIS\Updater\AgentCommandsRepo";
    private const string AgentResultRepo = @"C:\KALIDIS\Updater\AgentResultsRepo";
    private const string AgentCommandBranch = "updater-commands";
    private const string AgentResultBranch = "updater-results";
    private const string AgentCommandFile = "Updater/remoto/comando.json";
    private const string AgentResultFile = "Updater/remoto/resultado.json";
    private const string AgentResultLocal = @"C:\KALIDIS\Updater\AgentResultsRepo\Updater\remoto\resultado.json";
    private const string AgentLedgerPath = @"C:\KALIDIS\Updater\agent-ledger.json";

    private const string SelfStage = @"C:\KALIDIS\Updater\SelfStage";
    private const string SelfHelper = @"C:\KALIDIS\Updater\self-update.cmd";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    public static async Task<int> Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Stage);
        Directory.CreateDirectory(Backup);
        Directory.CreateDirectory(SelfStage);
        Log("Kalidis.Updater iniciado.");

        if (args.Any(a => string.Equals(a, "--once", StringComparison.OrdinalIgnoreCase)))
        {
            await RunCycleAsync();
            return 0;
        }

        while (true)
        {
            try { await RunCycleAsync(); }
            catch (Exception ex) { WriteState("erro", null, ex.Message); Log("ERRO: " + ex); }
            await Task.Delay(PollInterval);
        }
    }

    private static async Task RunCycleAsync()
    {
        await ProcessAgentCommandAsync();
        await EnsureMirrorAsync();

        string remoteSha = await GetRemoteMainShaAsync();
        if (string.IsNullOrWhiteSpace(remoteSha))
        {
            WriteState("sem_conexao", null, "Nao foi possivel obter SHA remoto.");
            return;
        }

        string? installedSha = ReadState()?.InstalledCommit;
        if (string.Equals(installedSha, remoteSha, StringComparison.OrdinalIgnoreCase))
        {
            WriteState("atualizado", remoteSha, null);
            return;
        }

        await UpdateRevitBridgeAsync(remoteSha);
        await TryStageSelfUpdateAsync(remoteSha);
    }

    private static async Task<string> GetRemoteMainShaAsync()
    {
        ProcessResult r = await RunAsync("git", $"-C \"{Mirror}\" ls-remote origin refs/heads/{Branch}", Mirror);
        if (r.ExitCode != 0) return string.Empty;
        return r.Output.Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    private static async Task UpdateRevitBridgeAsync(string remoteSha)
    {
        WriteState("atualizando_codigo", remoteSha, null);
        await MustRunAsync("git", $"-C \"{Mirror}\" fetch --depth 1 origin {Branch}", Mirror);
        await MustRunAsync("git", $"-C \"{Mirror}\" reset --hard FETCH_HEAD", Mirror);

        string project = Path.Combine(Mirror, "RevitBridge", "Kalidis.Revit", "Kalidis.Revit.csproj");
        if (!File.Exists(project)) throw new FileNotFoundException("Projeto Revit nao encontrado.", project);

        WriteState("compilando", remoteSha, null);
        ProcessResult build = await RunAsync("dotnet", $"build \"{project}\"", Path.GetDirectoryName(project)!);
        if (build.ExitCode != 0)
        {
            WriteState("falha_compilacao", remoteSha, Tail(build.Output, 12000));
            Log("Build falhou para " + remoteSha + Environment.NewLine + build.Output);
            await PublishDiagnosticAsync("build_revit_falhou", false, Tail(build.Output, 12000), new { commit = remoteSha });
            return;
        }

        string builtDll = Path.Combine(Mirror, "RevitBridge", "Kalidis.Revit", "bin", "Debug", "net8.0-windows", "Kalidis.Revit.dll");
        if (!File.Exists(builtDll)) throw new FileNotFoundException("DLL compilada nao encontrada.", builtDll);

        string stagedDll = Path.Combine(Stage, $"Kalidis.Revit.{remoteSha[..Math.Min(12, remoteSha.Length)]}.dll");
        File.Copy(builtDll, stagedDll, true);
        string stagedHash = Sha256File(stagedDll);
        WriteState("pronto_para_instalar", remoteSha, null, stagedHash);

        if (IsRevitRunning())
        {
            WriteState("aguardando_revit_fechar", remoteSha, null, stagedHash);
            return;
        }

        await InstallAsync(stagedDll, remoteSha, stagedHash);
    }

    private static async Task TryStageSelfUpdateAsync(string remoteSha)
    {
        try
        {
            string project = Path.Combine(Mirror, "Updater", "Kalidis.Updater", "Kalidis.Updater.csproj");
            if (!File.Exists(project)) return;

            string output = Path.Combine(SelfStage, remoteSha[..Math.Min(12, remoteSha.Length)]);
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);

            ProcessResult publish = await RunAsync("dotnet", $"publish \"{project}\" -c Release -r win-x64 --self-contained false -o \"{output}\"", Path.GetDirectoryName(project)!);
            if (publish.ExitCode != 0)
            {
                Log("Self-update build falhou: " + publish.Output);
                await PublishDiagnosticAsync("build_updater_falhou", false, Tail(publish.Output, 12000), new { commit = remoteSha });
                return;
            }

            string exe = Path.Combine(output, "Kalidis.Updater.exe");
            if (!File.Exists(exe)) return;

            string currentExe = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentExe)) return;
            string installDir = Path.GetDirectoryName(currentExe)!;
            string stagedHash = Sha256File(exe);
            string currentHash = File.Exists(currentExe) ? Sha256File(currentExe) : string.Empty;
            if (string.Equals(stagedHash, currentHash, StringComparison.OrdinalIgnoreCase)) return;

            string helper = $"@echo off\r\n" +
                            $"set PID={Environment.ProcessId}\r\n" +
                            $":wait\r\n" +
                            $"tasklist /FI \"PID eq %PID%\" 2>NUL | find \"%PID%\" >NUL\r\n" +
                            $"if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                            $"xcopy /E /Y /I \"{output}\\*\" \"{installDir}\\\" >nul\r\n" +
                            $"start \"\" /min \"{Path.Combine(installDir, "Kalidis.Updater.exe")}\"\r\n" +
                            $"del \"%~f0\"\r\n";
            File.WriteAllText(SelfHelper, helper, new UTF8Encoding(false));
            Log($"Self-update agendado. commit={remoteSha} hash={stagedHash}");

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" /min \"{SelfHelper}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log("Falha ao preparar self-update: " + ex);
        }
    }

    private static async Task InstallAsync(string stagedDll, string commit, string stagedHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstalledDll)!);
        string backupFile = Path.Combine(Backup, $"Kalidis.Revit.{DateTime.Now:yyyyMMdd-HHmmss}.dll");

        try
        {
            if (File.Exists(InstalledDll)) File.Copy(InstalledDll, backupFile, true);

            string tempInstall = InstalledDll + ".new";
            File.Copy(stagedDll, tempInstall, true);
            if (!string.Equals(Sha256File(tempInstall), stagedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Hash da DLL temporaria divergiu do artefato compilado.");

            File.Move(tempInstall, InstalledDll, true);
            if (!string.Equals(Sha256File(InstalledDll), stagedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Hash da DLL instalada divergiu do esperado.");

            TrimBackups(8);
            WriteState("instalado", commit, null, stagedHash, commit);
            Log($"DLL instalada com sucesso. commit={commit} hash={stagedHash}");
            await PublishDiagnosticAsync("revit_bridge_instalado", true, "DLL instalada com sucesso.", new { commit, dllSha256 = stagedHash });
        }
        catch (Exception ex)
        {
            Log("Falha na instalacao: " + ex);
            if (File.Exists(backupFile))
            {
                try
                {
                    File.Copy(backupFile, InstalledDll, true);
                    WriteState("rollback_executado", commit, ex.Message);
                    Log("Rollback executado: " + backupFile);
                    await PublishDiagnosticAsync("rollback_executado", false, ex.Message, new { commit, backupFile });
                }
                catch (Exception rollbackEx)
                {
                    WriteState("falha_rollback", commit, ex.Message + " | rollback: " + rollbackEx.Message);
                    Log("Falha no rollback: " + rollbackEx);
                    await PublishDiagnosticAsync("falha_rollback", false, ex.Message + " | " + rollbackEx.Message, new { commit });
                }
            }
            else
            {
                WriteState("falha_instalacao", commit, ex.Message);
                await PublishDiagnosticAsync("falha_instalacao", false, ex.Message, new { commit });
            }
        }
    }

    private static async Task ProcessAgentCommandAsync()
    {
        try
        {
            await EnsureAgentRepoAsync(AgentCommandRepo, AgentCommandBranch);
            ProcessResult ls = await RunAsync("git", $"-C \"{AgentCommandRepo}\" ls-remote origin refs/heads/{AgentCommandBranch}", AgentCommandRepo);
            if (ls.ExitCode != 0) return;
            string sha = ls.Output.Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sha)) return;

            await MustRunAsync("git", $"-C \"{AgentCommandRepo}\" fetch --depth 1 origin {AgentCommandBranch}", AgentCommandRepo);
            ProcessResult show = await RunAsync("git", $"-C \"{AgentCommandRepo}\" show FETCH_HEAD:{AgentCommandFile}", AgentCommandRepo);
            if (show.ExitCode != 0 || string.IsNullOrWhiteSpace(show.Output)) return;

            AgentCommand? command = JsonSerializer.Deserialize<AgentCommand>(show.Output, JsonOptions);
            if (command == null || !command.Ativo || string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Acao)) return;
            if (WasAgentCommandProcessed(command.Id)) return;

            AgentResult result = await ExecuteAgentCommandAsync(command);
            MarkAgentCommandProcessed(command.Id);
            await PublishAgentResultAsync(result);
        }
        catch (Exception ex)
        {
            Log("Falha no canal do agente: " + ex);
        }
    }

    private static async Task<AgentResult> ExecuteAgentCommandAsync(AgentCommand c)
    {
        string action = c.Acao.Trim().ToLowerInvariant();
        try
        {
            switch (action)
            {
                case "status":
                    return Ok(c.Id, "Status coletado.", new { state = ReadState(), revitAberto = IsRevitRunning(), updaterPid = Environment.ProcessId });
                case "ler_log":
                    return Ok(c.Id, "Log coletado.", new { log = ReadLastLines(LogPath, Math.Clamp(c.Linhas ?? 120, 10, 500)) });
                case "revit_status":
                    return Ok(c.Id, "Status do Revit coletado.", new { aberto = IsRevitRunning(), processos = Process.GetProcessesByName("Revit").Select(p => new { p.Id, p.ProcessName }).ToArray() });
                case "git_status":
                    await EnsureMirrorAsync();
                    ProcessResult gs = await RunAsync("git", $"-C \"{Mirror}\" status --short --branch", Mirror);
                    return gs.ExitCode == 0 ? Ok(c.Id, "Git status coletado.", new { output = gs.Output }) : Fail(c.Id, gs.Output);
                case "forcar_atualizacao":
                    await EnsureMirrorAsync();
                    string remoteSha = await GetRemoteMainShaAsync();
                    if (string.IsNullOrWhiteSpace(remoteSha)) return Fail(c.Id, "Nao foi possivel obter SHA remoto.");
                    await UpdateRevitBridgeAsync(remoteSha);
                    return Ok(c.Id, "Ciclo de atualizacao executado.", new { targetCommit = remoteSha, state = ReadState() });
                case "diagnostico":
                    return Ok(c.Id, "Diagnostico coletado.", new
                    {
                        state = ReadState(),
                        revitAberto = IsRevitRunning(),
                        log = ReadLastLines(LogPath, Math.Clamp(c.Linhas ?? 160, 20, 500)),
                        installedDllExists = File.Exists(InstalledDll),
                        installedDllSha256 = File.Exists(InstalledDll) ? Sha256File(InstalledDll) : null
                    });
                default:
                    return Fail(c.Id, $"Acao nao permitida: {c.Acao}. A whitelist e fechada.");
            }
        }
        catch (Exception ex)
        {
            return Fail(c.Id, ex.ToString());
        }
    }

    private static async Task PublishDiagnosticAsync(string kind, bool success, string message, object data)
    {
        string id = $"diag-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        await PublishAgentResultAsync(new AgentResult(id, success, kind + ": " + message, data, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
    }

    private static async Task PublishAgentResultAsync(AgentResult result)
    {
        await EnsureAgentRepoAsync(AgentResultRepo, AgentResultBranch);
        await MustRunAsync("git", $"-C \"{AgentResultRepo}\" fetch --depth 1 origin {AgentResultBranch}", AgentResultRepo);
        await MustRunAsync("git", $"-C \"{AgentResultRepo}\" reset --hard FETCH_HEAD", AgentResultRepo);

        string? dir = Path.GetDirectoryName(AgentResultLocal);
        if (dir != null) Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine;
        string tmp = AgentResultLocal + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, AgentResultLocal, true);

        await MustRunAsync("git", $"-C \"{AgentResultRepo}\" add {AgentResultFile}", AgentResultRepo);
        ProcessResult diff = await RunAsync("git", $"-C \"{AgentResultRepo}\" diff --cached --quiet -- {AgentResultFile}", AgentResultRepo);
        if (diff.ExitCode == 1)
        {
            string safeId = result.Id.Replace("\"", "").Replace("\r", " ").Replace("\n", " ");
            await MustRunAsync("git", $"-C \"{AgentResultRepo}\" commit -m \"agent: resultado {safeId}\"", AgentResultRepo);
            ProcessResult push = await RunAsync("git", $"-C \"{AgentResultRepo}\" push origin HEAD:{AgentResultBranch}", AgentResultRepo);
            if (push.ExitCode != 0)
            {
                await MustRunAsync("git", $"-C \"{AgentResultRepo}\" pull --rebase origin {AgentResultBranch}", AgentResultRepo);
                await MustRunAsync("git", $"-C \"{AgentResultRepo}\" push origin HEAD:{AgentResultBranch}", AgentResultRepo);
            }
        }
    }

    private static async Task EnsureAgentRepoAsync(string path, string branch)
    {
        if (Directory.Exists(Path.Combine(path, ".git"))) return;
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await MustRunAsync("git", $"clone --branch {branch} --single-branch --depth 1 \"{RepositoryUrl}\" \"{path}\"", Root);
    }

    private static bool WasAgentCommandProcessed(string id)
    {
        try
        {
            if (!File.Exists(AgentLedgerPath)) return false;
            string[] ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(AgentLedgerPath), JsonOptions) ?? Array.Empty<string>();
            return ids.Contains(id, StringComparer.Ordinal);
        }
        catch { return false; }
    }

    private static void MarkAgentCommandProcessed(string id)
    {
        try
        {
            List<string> ids = new();
            if (File.Exists(AgentLedgerPath))
                ids.AddRange(JsonSerializer.Deserialize<string[]>(File.ReadAllText(AgentLedgerPath), JsonOptions) ?? Array.Empty<string>());
            if (!ids.Contains(id, StringComparer.Ordinal)) ids.Add(id);
            if (ids.Count > 500) ids = ids.Skip(ids.Count - 500).ToList();
            File.WriteAllText(AgentLedgerPath, JsonSerializer.Serialize(ids, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { }
    }

    private static bool IsRevitRunning() => Process.GetProcessesByName("Revit").Length > 0;

    private static async Task EnsureMirrorAsync()
    {
        if (Directory.Exists(Path.Combine(Mirror, ".git"))) return;
        if (Directory.Exists(Mirror)) Directory.Delete(Mirror, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Mirror)!);
        await MustRunAsync("git", $"clone --branch {Branch} --single-branch --depth 1 \"{RepositoryUrl}\" \"{Mirror}\"", Root);
    }

    private static async Task MustRunAsync(string file, string args, string cwd)
    {
        ProcessResult r = await RunAsync(file, args, cwd);
        if (r.ExitCode != 0) throw new InvalidOperationException($"{file} {args} falhou ({r.ExitCode}): {r.Output}");
    }

    private static async Task<ProcessResult> RunAsync(string file, string args, string cwd)
    {
        using Process p = new();
        p.StartInfo = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        p.Start();
        Task<string> stdout = p.StandardOutput.ReadToEndAsync();
        Task<string> stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new ProcessResult(p.ExitCode, ((await stdout) + Environment.NewLine + (await stderr)).Trim());
    }

    private static string Sha256File(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    private static UpdaterState? ReadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            return JsonSerializer.Deserialize<UpdaterState>(File.ReadAllText(StatePath), JsonOptions);
        }
        catch { return null; }
    }

    private static void WriteState(string status, string? targetCommit, string? error, string? dllHash = null, string? installedCommit = null)
    {
        UpdaterState? old = ReadState();
        UpdaterState state = new(status, targetCommit, installedCommit ?? old?.InstalledCommit, dllHash ?? old?.DllSha256, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), error);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void TrimBackups(int keep)
    {
        foreach (FileInfo f in new DirectoryInfo(Backup).GetFiles("Kalidis.Revit.*.dll").OrderByDescending(f => f.CreationTimeUtc).Skip(keep))
        {
            try { f.Delete(); } catch { }
        }
    }

    private static string ReadLastLines(string path, int count)
    {
        try
        {
            if (!File.Exists(path)) return string.Empty;
            return string.Join(Environment.NewLine, File.ReadLines(path).TakeLast(count));
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static string Tail(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[^max..];

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}{Environment.NewLine}", new UTF8Encoding(false)); }
        catch { }
    }

    private static AgentResult Ok(string id, string message, object data) => new(id, true, message, data, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    private static AgentResult Fail(string id, string message) => new(id, false, message, null, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private sealed record ProcessResult(int ExitCode, string Output);
    private sealed record UpdaterState(string Status, string? TargetCommit, string? InstalledCommit, string? DllSha256, string UpdatedAt, string? Error);
    private sealed record AgentCommand(bool Ativo, string Id, string Acao, int? Linhas);
    private sealed record AgentResult(string Id, bool Sucesso, string Mensagem, object? Dados, string ExecutadoEm);
}
