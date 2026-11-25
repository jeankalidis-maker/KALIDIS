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
    internal static readonly string SessionId=Guid.NewGuid().ToString("N");
    private static long revision;
    internal static long ModelRevision=>Interlocked.Read(ref revision);
    private static void OnModelChanged(object? sender,Autodesk.Revit.DB.Events.DocumentChangedEventArgs args)=>Interlocked.Increment(ref revision);
    private static void OnDocumentOpened(object? sender,Autodesk.Revit.DB.Events.DocumentOpenedEventArgs args)=>Interlocked.Increment(ref revision);
    private static void OnDocumentClosed(object? sender,Autodesk.Revit.DB.Events.DocumentClosedEventArgs args)=>Interlocked.Increment(ref revision);
    private HttpListener? listener;
    private ExternalEvent? evt;
    private Handler? handler;
    private CancellationTokenSource? stop;
    private Task? listenerTask;
    private byte[] token = Array.Empty<byte>();
    private static readonly string Home = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KalidisAIEngine");
    public Result OnStartup(UIControlledApplication app)
    {
        try
        {
            Directory.CreateDirectory(Home);
            token = RandomNumberGenerator.GetBytes(32);
            handler = new Handler();
            evt = ExternalEvent.Create(handler);
            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:7895/");
            listener.Start();
            File.WriteAllText(Path.Combine(Home, "token.txt"), Convert.ToHexString(token));
            stop = new CancellationTokenSource();
            // Never capture Revit/WPF synchronization context for HTTP I/O.
            var cancellation=stop.Token;
            listenerTask=Task.Run(()=>Listen(cancellation),cancellation);
            app.ControlledApplication.DocumentChanged+=OnModelChanged;
            app.ControlledApplication.DocumentOpened+=OnDocumentOpened;
            app.ControlledApplication.DocumentClosed+=OnDocumentClosed;
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
        app.ControlledApplication.DocumentChanged-=OnModelChanged;
        app.ControlledApplication.DocumentOpened-=OnDocumentOpened;
        app.ControlledApplication.DocumentClosed-=OnDocumentClosed;
        stop?.Cancel(); listener?.Close(); handler?.Cancel(); evt?.Dispose();
        try { var file=Path.Combine(Home,"token.txt");if(File.Exists(file)&&File.ReadAllText(file).Trim()==Convert.ToHexString(token))File.Delete(file); } catch { }
        return Result.Succeeded;
    }
    private async Task Listen(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext c;
            try { c = await listener!.GetContextAsync().ConfigureAwait(false); }
            catch { break; }
            _ = Task.Run(()=>Serve(c,ct),CancellationToken.None);
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
            { await Reply(c, 200, new { ok=true, version="0.3.1", transport="loopback", engine="ExternalEvent", queue=handler?.Status(), listenerRunning=listenerTask is {IsCompleted:false}, pid=Environment.ProcessId }); return; }
            if(c.Request.HttpMethod=="POST"&&c.Request.Url?.AbsolutePath=="/cancel_batch"){
                if(c.Request.ContentLength64<1||c.Request.ContentLength64>1024){await Reply(c,413,new{ok=false,error="invalid_body_size"});return;}
                using var reader=new StreamReader(c.Request.InputStream,Encoding.UTF8);
                using var payload=JsonDocument.Parse(await reader.ReadToEndAsync(ct).ConfigureAwait(false));
                var id=payload.RootElement.GetProperty("jobId").GetString();
                if(!Guid.TryParseExact(id,"N",out _)){await Reply(c,400,new{ok=false,error="invalid_job_id"});return;}
                bool accepted=handler!.RequestCancellation(id!);
                await Reply(c,accepted?200:409,new{ok=accepted,state=accepted?"cancellation_requested":"not_active_or_commit_started",note="Request only; await original batch result and verify rollback."});return;
            }
            if (c.Request.HttpMethod != "POST" || c.Request.Url?.AbsolutePath != "/execute")
            { await Reply(c, 404, new { ok=false, error="not_found" }); return; }
            if (c.Request.ContentLength64 < 1 || c.Request.ContentLength64 > 262144)
            { await Reply(c, 413, new { ok=false, error="invalid_body_size" }); return; }
            using var sr = new StreamReader(c.Request.InputStream, Encoding.UTF8);
            using var json = JsonDocument.Parse(await sr.ReadToEndAsync(ct).ConfigureAwait(false));
            var job = new Job(json.RootElement.Clone());
            if (!handler!.TryQueue(job))
            { await Reply(c, 409, new { ok=false, error="busy" }); return; }
            try
            {
                var raised = evt!.Raise();
                if (raised != ExternalEventRequest.Accepted && raised != ExternalEventRequest.Pending)
                { job.Cancel(); handler.Remove(job); await Reply(c, 503, new { ok=false, error="event_not_accepted" }); return; }
                var result = await job.Done.Task.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
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
        await c.Response.OutputStream.WriteAsync(data).ConfigureAwait(false);
    }
    internal sealed class Job(JsonElement request)
    {
        internal JsonElement Request = request;
        internal TaskCompletionSource<object> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly BatchCancellation Cancellation=new();
        internal string? BatchId=>Request.TryGetProperty("action",out var a)&&a.ValueKind==JsonValueKind.String&&a.GetString()=="batch"&&Request.TryGetProperty("jobId",out var id)&&id.ValueKind==JsonValueKind.String?id.GetString():null;
        private int state;
        internal bool Start() => Interlocked.CompareExchange(ref state, 1, 0) == 0;
        internal bool Cancel() => Interlocked.CompareExchange(ref state, 2, 0) == 0;
    }
    internal sealed class Handler : IExternalEventHandler
    {
        private readonly object gate = new();
        private Job? pending,active;
        internal bool RequestCancellation(string id){
            lock(gate){
                var j=active??pending;
                return j!=null&&j.BatchId==id&&j.Cancellation.Request();
            }
        }
        private bool running;
        private long started,completed,canceled;
        private string? activeAction,lastAction;
        private DateTime? activeSince,lastFinished;
        internal object Status(){lock(gate)return new{pending=pending!=null,running,started,completed,canceled,activeAction,activeSince,lastAction,lastFinished};}
        internal bool TryQueue(Job j) { lock(gate) { if (pending != null || running) return false; pending=j; return true; } }
        internal void Remove(Job j) { lock(gate) { if (ReferenceEquals(pending,j)){pending=null;canceled++;} } }
        internal void Cancel() { lock(gate) { pending?.Cancel(); pending=null; } }
        public string GetName() => "Kalidis AI Engine";
        public void Execute(UIApplication app)
        {
            Job? j;
            lock(gate) { j=pending; pending=null; if(j==null || !j.Start()) return; running=true;active=j;started++;activeSince=DateTime.UtcNow;activeAction=j.Request.TryGetProperty("action",out var name)&&name.ValueKind==JsonValueKind.String?name.GetString():null; }
            try { j.Done.TrySetResult(Run(app,j.Request,j.Cancellation)); }
            catch(Exception ex) { j.Done.TrySetResult(new { ok=false,error=ex.Message }); }
            finally { j.Cancellation.Finish();lock(gate){active=null;running=false;completed++;lastAction=activeAction;lastFinished=DateTime.UtcNow;activeAction=null;activeSince=null;} }
        }
        private static object Run(UIApplication app, JsonElement r,BatchCancellation cancellation)
        {
            var watch=Stopwatch.StartNew();
            string action=r.GetProperty("action").GetString() ?? "";
            if(action=="catalog")
                return new { ok=true, implemented=new[]{"catalog","snapshot","project_context","curtain_wall","test_wall","batch","export_family"}.Concat(AdvancedOperations.Actions).Concat(ProjectOperations.Actions).Concat(InspectionOperations.Actions).Concat(ExtensionOperations.Actions).Concat(VisualOperations.Actions).Concat(KenLPPorts.Actions).Concat(KenLPAdditionalPorts.Actions).Concat(FamilyEditOperations.Actions).Concat(HorizunAudit.Actions).Concat(StairOperations.Actions).ToArray(), cooperativeBatchCancel=true, groupedFamilyCuts=true, nativeCommands=Enum.GetNames<PostableCommand>(), note="Native names are discoverable, not validated as executable by this prototype." };
            if(action=="extensions") return ExtensionOperations.List(Home);
            if(action=="new_test_project" || action=="open_test_project") return ProjectOperations.NewOrOpen(app,r,action,Path.Combine(Home,"Sandbox"));
            var d=app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document");
            if(action=="project_context") return ContextOperations.Read(app,d,r);
            if(action=="snapshot") return SnapshotOperations.Read(app,d);
            if(HorizunAudit.Actions.Contains(action)) return HorizunAudit.Read(d,r,action);
            if(KenLPPorts.Actions.Contains(action)) return KenLPPorts.Read(d,r,action);
            if(KenLPAdditionalPorts.Actions.Contains(action)) return KenLPAdditionalPorts.Read(d,r,action);
            if(InspectionOperations.Actions.Contains(action)) return InspectionOperations.Read(app,d,r,action);
            if(action=="elements") return ProjectOperations.Elements(d,r);
            if(action=="element_info") return ProjectOperations.Info(d,r);
            if(new[]{"types","native_status","library","family_info"}.Contains(action)) return AdvancedOperations.Read(app,d,r,action);
            if(action!="curtain_wall" && action!="test_wall" && action!="batch" && action!="export_family" && !AdvancedOperations.Actions.Contains(action) && !ProjectOperations.Actions.Contains(action) && action!="run_extension" && !VisualOperations.Actions.Contains(action) && !FamilyEditOperations.Actions.Contains(action) && !StairOperations.Actions.Contains(action)) throw new InvalidOperationException("Unknown action");
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
            if(action=="curtain_wall") return CurtainOperations.Run(d,r);
            if(action=="straight_stair") return StairOperations.Run(d,r);
            if(action=="edit_family_copy") return FamilyEditOperations.Run(d,r,root);
            if(VisualOperations.Actions.Contains(action)) return VisualOperations.Run(app,d,r,action,root);
            if(action=="run_extension") return ExtensionOperations.Run(d,r,Home);
            if(action=="edit_elements") return ProjectOperations.Edit(d,r);
            if(action=="export_project") return ProjectOperations.Export(d,r,root);
            if(action=="batch"){
                bool hasSession=r.TryGetProperty("expectedSession",out var session);
                bool hasRevision=r.TryGetProperty("expectedRevision",out var expectedRevision);
                if(hasSession!=hasRevision)throw new ArgumentException("Both expectedSession and expectedRevision are required together");
                if(hasSession&&(session.GetString()!=SessionId||expectedRevision.GetInt64()!=ModelRevision))
                    throw new InvalidOperationException("Stale model context: refresh snapshot before planning");
                if(r.TryGetProperty("jobId",out var jobId)&&!Guid.TryParseExact(jobId.GetString(),"N",out _))throw new ArgumentException("jobId must be a 32-character GUID");
                return ModelOperations.Batch(d,r,cancellation);
            }
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
