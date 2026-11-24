using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Kalidis.AIEngine;

// Independent prototype: loopback only, explicit allowlist and test-copy writes.
// All Revit API operations run on the ExternalEvent UI thread.
public sealed class Application : IExternalApplication
{
    private HttpListener? listener;
    private ExternalEvent? evt;
    private Handler? handler;
    private CancellationTokenSource? stop;
    private byte[] token = Array.Empty<byte>();
    private static readonly string Home = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KalidisAIEngine");
    public Result OnStartup(UIControlledApplication app)
    {
        try
        {
            Directory.CreateDirectory(Home);
            token = RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(Path.Combine(Home, "token.txt"), Convert.ToHexString(token));
            handler = new Handler();
            evt = ExternalEvent.Create(handler);
            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:7895/");
            listener.Start();
            stop = new CancellationTokenSource();
            _ = Listen(stop.Token);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Home, "startup-error.txt"), ex.ToString());
            listener?.Close(); evt?.Dispose();
            return Result.Failed;
        }
    }
    public Result OnShutdown(UIControlledApplication app)
    {
        stop?.Cancel(); listener?.Close(); handler?.Cancel(); evt?.Dispose();
        try { File.Delete(Path.Combine(Home, "token.txt")); } catch { }
        return Result.Succeeded;
    }
    private async Task Listen(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext c;
            try { c = await listener!.GetContextAsync(); }
            catch { break; }
            _ = Serve(c, ct);
        }
    }
    private bool Auth(HttpListenerRequest r)
    {
        string? h = r.Headers["Authorization"];
        if (h == null || !h.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        try { return CryptographicOperations.FixedTimeEquals(token, Convert.FromHexString(h[7..])); }
        catch { return false; }
    }
    private async Task Serve(HttpListenerContext c, CancellationToken ct)
    {
        try
        {
            if (!Auth(c.Request)) { await Reply(c, 401, new { ok=false, error="unauthorized" }); return; }
            if (c.Request.HttpMethod == "GET" && c.Request.Url?.AbsolutePath == "/health")
            { await Reply(c, 200, new { ok=true, version="0.3.0", transport="loopback", engine="ExternalEvent" }); return; }
            if (c.Request.HttpMethod != "POST" || c.Request.Url?.AbsolutePath != "/execute")
            { await Reply(c, 404, new { ok=false, error="not_found" }); return; }
            if (c.Request.ContentLength64 < 1 || c.Request.ContentLength64 > 262144)
            { await Reply(c, 413, new { ok=false, error="invalid_body_size" }); return; }
            using var sr = new StreamReader(c.Request.InputStream, Encoding.UTF8);
            using var json = JsonDocument.Parse(await sr.ReadToEndAsync(ct));
            var job = new Job(json.RootElement.Clone());
            if (!handler!.TryQueue(job))
            { await Reply(c, 409, new { ok=false, error="busy" }); return; }
            try
            {
                var raised = evt!.Raise();
                if (raised != ExternalEventRequest.Accepted && raised != ExternalEventRequest.Pending)
                { job.Cancel(); handler.Remove(job); await Reply(c, 503, new { ok=false, error="event_not_accepted" }); return; }
                var result = await job.Done.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
                await Reply(c, 200, result);
            }
            catch (TimeoutException)
            {
                bool canceled = job.Cancel();
                if (canceled) handler.Remove(job);
                await Reply(c, 504, new { ok=false, error=canceled ? "canceled_before_execution" : "execution_state_unknown_do_not_retry" });
            }
        }
        catch (Exception ex) { try { await Reply(c, 400, new { ok=false, error=ex.Message }); } catch { } }
        finally { try { c.Response.Close(); } catch { } }
    }
    private static async Task Reply(HttpListenerContext c, int status, object payload)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        c.Response.StatusCode = status; c.Response.ContentType = "application/json; charset=utf-8";
        c.Response.ContentLength64 = data.Length;
        await c.Response.OutputStream.WriteAsync(data);
    }
    internal sealed class Job(JsonElement request)
    {
        internal JsonElement Request = request;
        internal TaskCompletionSource<object> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int state;
        internal bool Start() => Interlocked.CompareExchange(ref state, 1, 0) == 0;
        internal bool Cancel() => Interlocked.CompareExchange(ref state, 2, 0) == 0;
    }
    internal sealed class Handler : IExternalEventHandler
    {
        private readonly object gate = new();
        private Job? pending;
        private bool running;
        internal bool TryQueue(Job j) { lock(gate) { if (pending != null || running) return false; pending=j; return true; } }
        internal void Remove(Job j) { lock(gate) { if (ReferenceEquals(pending,j)) pending=null; } }
        internal void Cancel() { lock(gate) { pending?.Cancel(); pending=null; } }
        public string GetName() => "Kalidis AI Engine";
        public void Execute(UIApplication app)
        {
            Job? j;
            lock(gate) { j=pending; pending=null; if(j==null || !j.Start()) return; running=true; }
            try { j.Done.TrySetResult(Run(app,j.Request)); }
            catch(Exception ex) { j.Done.TrySetResult(new { ok=false,error=ex.Message }); }
            finally { lock(gate) running=false; }
        }
        private static object Run(UIApplication app, JsonElement r)
        {
            var watch=Stopwatch.StartNew();
            string action=r.GetProperty("action").GetString() ?? "";
            if(action=="catalog")
                return new { ok=true, implemented=new[]{"catalog","snapshot","test_wall","batch","export_family"}.Concat(AdvancedOperations.Actions).Concat(ProjectOperations.Actions).Concat(InspectionOperations.Actions).Concat(ExtensionOperations.Actions).Concat(VisualOperations.Actions).Concat(KenLPPorts.Actions).ToArray(), nativeCommands=Enum.GetNames<PostableCommand>(), note="Native names are discoverable, not validated as executable by this prototype." };
            if(action=="extensions") return ExtensionOperations.List(Home);
            if(action=="new_test_project" || action=="open_test_project") return ProjectOperations.NewOrOpen(app,r,action,Path.Combine(Home,"Sandbox"));
            var d=app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document");
            if(action=="snapshot")
            {
                var levels=new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>()
                    .Select(x=>new { id=x.Id.Value,name=x.Name,elevationMm=UnitUtils.ConvertFromInternalUnits(x.Elevation,UnitTypeId.Millimeters) }).ToArray();
                var walls=new FilteredElementCollector(d).OfClass(typeof(WallType)).Cast<WallType>()
                    .Select(x=>new { id=x.Id.Value,name=x.Name,kind=x.Kind.ToString() }).ToArray();
                var families=new FilteredElementCollector(d).OfClass(typeof(Family)).Cast<Family>()
                    .Select(x=>new { id=x.Id.Value,name=x.Name }).ToArray();
                return new { ok=true, title=d.Title, path=d.PathName, version=app.Application.VersionNumber, workshared=d.IsWorkshared, family=d.IsFamilyDocument, levels, wallTypes=walls, families, elapsedMs=watch.ElapsedMilliseconds };
            }
            if(KenLPPorts.Actions.Contains(action)) return KenLPPorts.Read(d,r,action);
            if(InspectionOperations.Actions.Contains(action)) return InspectionOperations.Read(app,d,r,action);
            if(action=="elements") return ProjectOperations.Elements(d,r);
            if(action=="element_info") return ProjectOperations.Info(d,r);
            if(new[]{"types","native_status","library","family_info"}.Contains(action)) return AdvancedOperations.Read(app,d,r,action);
            if(action!="test_wall" && action!="batch" && action!="export_family" && !AdvancedOperations.Actions.Contains(action) && !ProjectOperations.Actions.Contains(action) && action!="run_extension" && !VisualOperations.Actions.Contains(action)) throw new InvalidOperationException("Unknown action");
            // Writes only to explicitly designated local test copy, never cloud/workshared/unsaved documents.
            string root=Path.GetFullPath(Path.Combine(Home,"Sandbox"))+Path.DirectorySeparatorChar;
            string expected=Path.GetFullPath(r.GetProperty("expectedPath").GetString() ?? "");
            string actual=Path.GetFullPath(d.PathName);
            if(d.IsReadOnly || d.IsWorkshared || d.IsFamilyDocument || d.IsModelInCloud ||
                !expected.Equals(actual,StringComparison.OrdinalIgnoreCase) ||
                !actual.StartsWith(root,StringComparison.OrdinalIgnoreCase) ||
                !actual.EndsWith(".rvt",StringComparison.OrdinalIgnoreCase) || !File.Exists(actual) ||
                (File.GetAttributes(actual)&FileAttributes.ReparsePoint)!=0 || (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0 ||
                !string.Equals(Path.GetDirectoryName(actual),root.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Write refused: open a local test copy in the engine Sandbox");
            if(VisualOperations.Actions.Contains(action)) return VisualOperations.Run(app,d,r,action,root);
            if(action=="run_extension") return ExtensionOperations.Run(d,r,Home);
            if(action=="edit_elements") return ProjectOperations.Edit(d,r);
            if(action=="export_project") return ProjectOperations.Export(d,r,root);
            if(action=="batch") return ModelOperations.Batch(d,r);
            if(AdvancedOperations.Actions.Contains(action)) return AdvancedOperations.Write(app,d,r,action,root);
            if(action=="export_family") return ModelOperations.ExportFamily(d,r,root);
            var level=d.GetElement(new ElementId(r.GetProperty("levelId").GetInt64())) as Level ?? throw new InvalidOperationException("Invalid level");
            var type=d.GetElement(new ElementId(r.GetProperty("wallTypeId").GetInt64())) as WallType ?? throw new InvalidOperationException("Invalid wall type");
            if(type.Kind!=WallKind.Basic) throw new InvalidOperationException("Basic wall type required");
            bool dryRun=!r.TryGetProperty("commit",out var v)||v.ValueKind!=JsonValueKind.True;
            double mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
            using var tx=new Transaction(d,"Kalidis AI Engine - test wall");
            if(tx.Start()!=TransactionStatus.Started) throw new InvalidOperationException("Cannot start transaction");
            try
            {
                var wall=Wall.Create(d,Line.CreateBound(new XYZ(0,0,level.Elevation),new XYZ(mm(3000),0,level.Elevation)),type.Id,level.Id,mm(3000),0,false,false);
                long id=wall.Id.Value;
                d.Regenerate();
                var status=dryRun?tx.RollBack():tx.Commit();
                bool ok=dryRun?status==TransactionStatus.RolledBack:status==TransactionStatus.Committed;
                return new { ok,dryRun,status=status.ToString(),elementId=id,exists=d.GetElement(new ElementId(id))!=null,elapsedMs=watch.ElapsedMilliseconds };
            }
            catch { if(tx.GetStatus()==TransactionStatus.Started) tx.RollBack(); throw; }
        }
    }
}
