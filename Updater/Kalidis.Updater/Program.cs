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
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public static async Task<int> Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Stage);
        Directory.CreateDirectory(Backup);
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
        await EnsureMirrorAsync();

        string remoteSha = (await RunAsync("git", $"-C \"{Mirror}\" ls-remote origin refs/heads/{Branch}", Mirror)).Output
            .Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

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

        WriteState("atualizando_codigo", remoteSha, null);
        await MustRunAsync("git", $"-C \"{Mirror}\" fetch --depth 1 origin {Branch}", Mirror);
        await MustRunAsync("git", $"-C \"{Mirror}\" reset --hard FETCH_HEAD", Mirror);

        string project = Path.Combine(Mirror, "RevitBridge", "Kalidis.Revit", "Kalidis.Revit.csproj");
        if (!File.Exists(project)) throw new FileNotFoundException("Projeto Revit nao encontrado.", project);

        WriteState("compilando", remoteSha, null);
        ProcessResult build = await RunAsync("dotnet", $"build \"{project}\"", Path.GetDirectoryName(project)!);
        if (build.ExitCode != 0)
        {
            WriteState("falha_compilacao", remoteSha, build.Output);
            Log("Build falhou para " + remoteSha + Environment.NewLine + build.Output);
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
                }
                catch (Exception rollbackEx)
                {
                    WriteState("falha_rollback", commit, ex.Message + " | rollback: " + rollbackEx.Message);
                    Log("Falha no rollback: " + rollbackEx);
                }
            }
            else
            {
                WriteState("falha_instalacao", commit, ex.Message);
            }
        }

        await Task.CompletedTask;
    }

    private static bool IsRevitRunning()
        => Process.GetProcessesByName("Revit").Length > 0;

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
        UpdaterState state = new(
            Status: status,
            TargetCommit: targetCommit,
            InstalledCommit: installedCommit ?? old?.InstalledCommit,
            DllSha256: dllHash ?? old?.DllSha256,
            UpdatedAt: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Error: error);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void TrimBackups(int keep)
    {
        foreach (FileInfo f in new DirectoryInfo(Backup).GetFiles("Kalidis.Revit.*.dll").OrderByDescending(f => f.CreationTimeUtc).Skip(keep))
        {
            try { f.Delete(); } catch { }
        }
    }

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}{Environment.NewLine}", new UTF8Encoding(false)); }
        catch { }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record ProcessResult(int ExitCode, string Output);
    private sealed record UpdaterState(string Status, string? TargetCommit, string? InstalledCommit, string? DllSha256, string UpdatedAt, string? Error);
}
