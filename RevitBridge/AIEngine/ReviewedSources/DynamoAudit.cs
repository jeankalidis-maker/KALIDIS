using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Text.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Collections;
using Kalidis.AIEngine;
namespace Kalidis.DynamoAudit;
public sealed class DynamoAudit : IReviewedOperation {
 public string Name=>"dynamo_audit";
 private static ExternalEvent? evt;
 private static Handler? handler;
 public object Execute(Document d,JsonElement a){
  string mode=a.GetProperty("mode").GetString()??"";
  if(mode=="status")return new{status=handler?.State??"idle",result=handler?.Result};
  if(mode!="start")throw new ArgumentException("start or status");
  if(!a.TryGetProperty("executeReadOnlyGraph",out var permit)||permit.ValueKind!=JsonValueKind.True)throw new ArgumentException("Explicit executeReadOnlyGraph=true required; event is outside rollback");
  if(handler?.State=="queued"||handler?.State=="running")throw new InvalidOperationException("Already queued");
  // Fixed installed, hash-reviewed, read-only graph. No arbitrary path or node input.
  var root=Path.Combine(Path.GetDirectoryName(typeof(Document).Assembly.Location)!,"AddIns","DynamoForRevit");
  var graph=Path.Combine(root,"NodeHelpSharedDocs","Revit.Application.Document.Current.dyn");
  var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(graph)));
  if(hash!="9F9BE8BF25B3DB37B4557117AE77787ACF8B9DEBA5AA031994F845D34C9C0F38")throw new InvalidOperationException("Installed graph differs from reviewed graph");
  if(evt==null){handler=new Handler();evt=ExternalEvent.Create(handler);}
  handler!.Path=d.PathName;handler.Graph=graph;handler.Root=root;handler.State="queued";handler.Result=null;
  var raised=evt.Raise();
  if(raised!=ExternalEventRequest.Accepted){handler.State="rejected";throw new InvalidOperationException("Event refused: "+raised);}
  return new{status="queued",note="Read-only graph queued outside rollback; poll status, do not resubmit"};
 }
 private sealed class Handler:IExternalEventHandler {
  internal string Path="",Graph="",Root="",State="idle";
  internal object? Result;
  public string GetName()=>"Kalidis fixed Dynamo document audit";
  public void Execute(UIApplication app){
   State="running";
   try{
    var doc=app.ActiveUIDocument?.Document??throw new InvalidOperationException("No active document");
    if(doc.PathName!=Path||doc.IsModifiable||doc.IsFamilyDocument||doc.IsWorkshared||doc.IsReadOnly)throw new InvalidOperationException("Document changed or transaction open");
    var context=AssemblyLoadContext.Default;
    Assembly? Resolve(AssemblyLoadContext c,AssemblyName n){
     if(n.Name==null||n.Name.Contains('/')||n.Name.Contains('\\'))return null;
     foreach(var dir in new[]{System.IO.Path.Combine(Root,"Revit"),Root}){
      var p=System.IO.Path.Combine(dir,n.Name+".dll");if(File.Exists(p))return c.LoadFromAssemblyPath(p);
     }return null;
    }
    context.Resolving+=Resolve;
    try{
     var asm=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x=>x.GetName().Name=="DynamoRevitDS")??context.LoadFromAssemblyPath(System.IO.Path.Combine(Root,"Revit","DynamoRevitDS.dll"));
     var type=asm.GetType("Dynamo.Applications.DynamoRevit",true)!;
     var modelProperty=type.GetProperty("RevitDynamoModel",BindingFlags.Public|BindingFlags.Static)??throw new InvalidOperationException("Model property missing");
     var model=modelProperty.GetValue(null);
     if(model==null){
      var dataType=asm.GetType("Dynamo.Applications.DynamoRevitCommandData",true)!;
      var data=Activator.CreateInstance(dataType)!;
      dataType.GetProperty("Application")!.SetValue(data,app);
      dataType.GetProperty("JournalData")!.SetValue(data,new Dictionary<string,string>{{"dynShowUI","false"},{"dynAutomation","true"},{"dynPath",Graph},{"dynPathExecute","false"},{"dynForceManualRun","true"}});
      var result=type.GetMethod("ExecuteCommand",new[]{dataType})!.Invoke(Activator.CreateInstance(type),new[]{data});
      if(result?.ToString()!="Succeeded")throw new InvalidOperationException("Dynamo initialization failed");
      model=modelProperty.GetValue(null)??throw new InvalidOperationException("No Dynamo model");
     }
     var workspace=model.GetType().GetProperty("CurrentWorkspace")!.GetValue(model)!;
     var currentPath=workspace.GetType().GetProperty("FileName")?.GetValue(workspace) as string;
     if(!string.Equals(currentPath,Graph,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Existing workspace differs; no execution");
     if(workspace.GetType().GetProperty("HasUnsavedChanges")?.GetValue(workspace) is not bool changed||changed)throw new InvalidOperationException("Workspace changed or state unavailable");
     var checkNodes=((IEnumerable)workspace.GetType().GetProperty("Nodes")!.GetValue(workspace)!).Cast<object>().ToArray();
     var expected=new[]{"2afbe0f3-e899-4247-b426-75096fb094f3","8a2cd538-6ef4-41e1-b99f-633f8065c5bf","dee5c877-a330-481a-9134-55384f0cea32"};
     if(checkNodes.Length!=3||!checkNodes.Select(n=>n.GetType().GetProperty("GUID")?.GetValue(n)?.ToString()).OrderBy(x=>x).SequenceEqual(expected.OrderBy(x=>x))||checkNodes.Any(n=>n.GetType().FullName!="Dynamo.Graph.Nodes.ZeroTouch.DSFunction"))throw new InvalidOperationException("Unexpected graph nodes");
     // Reload the hash-reviewed installed graph, never execute edited in-memory nodes.
     var open=model.GetType().GetMethods().FirstOrDefault(m=>m.Name=="OpenFileFromPath"&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType==typeof(string)&&m.GetParameters()[1].ParameterType==typeof(bool))??throw new InvalidOperationException("Open API unavailable");
     open.Invoke(model,new object[]{Graph,true});
     workspace=model.GetType().GetProperty("CurrentWorkspace")!.GetValue(model)!;
     var run=workspace.GetType().GetMethod("Run",Type.EmptyTypes)??throw new InvalidOperationException("Run API unavailable");
     run.Invoke(workspace,null);
     var engine=model.GetType().GetProperty("EngineController")!.GetValue(model);
     var nodes=(IEnumerable)workspace.GetType().GetProperty("Nodes")!.GetValue(workspace)!;
     var rows=new List<object>();bool verified=false;
     foreach(var node in nodes){
      var t=node.GetType();var guid=t.GetProperty("GUID")?.GetValue(node)?.ToString();var mirror=t.GetProperty("CachedValue")?.GetValue(node);
      if(mirror==null&&engine!=null){var m=t.GetMethods().FirstOrDefault(m=>m.Name=="GetValue"&&m.GetParameters().Length==2);mirror=m?.Invoke(node,new object[]{0,engine});}
      var value=mirror?.GetType().GetProperty("Data")?.GetValue(mirror);
      var display=value?.ToString()??mirror?.ToString();
      rows.Add(new{guid,value=display,type=value?.GetType().FullName});
      if(guid=="dee5c877-a330-481a-9134-55384f0cea32"&&value!=null&&string.Equals(display,doc.ActiveView.Discipline.ToString(),StringComparison.OrdinalIgnoreCase))verified=true;
     }
     Result=new{commandResult="Run invoked",document=doc.Title,pid=Environment.ProcessId,nodes=rows,expectedDiscipline=doc.ActiveView.Discipline.ToString(),verified};
     State=verified?"verified":"executed_unverified";
    }finally{context.Resolving-=Resolve;}
   }catch(Exception ex){State="failed";Result=new{error=ex.ToString()};}
  }
 }
}