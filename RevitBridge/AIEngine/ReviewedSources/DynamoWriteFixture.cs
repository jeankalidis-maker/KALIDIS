using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Text.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Collections;
using Kalidis.AIEngine;
namespace Kalidis.DynamoWriteFixture;
public sealed class DynamoWriteFixture : IReviewedOperation {
 public string Name=>"dynamo_write_fixture";
 private static ExternalEvent? evt;
 private static Handler? handler;
 public object Execute(Document d,JsonElement a){
  string mode=a.GetProperty("mode").GetString()??"";
  if(mode=="status")return new{status=handler?.State??"idle",result=handler?.Result};
  if(mode!="start")throw new ArgumentException("start or status");
  if(!a.TryGetProperty("executeFixtureGraph",out var permit)||permit.ValueKind!=JsonValueKind.True)throw new ArgumentException("Explicit executeReadOnlyGraph=true required; event is outside rollback");
  if(handler?.State=="queued"||handler?.State=="running")throw new InvalidOperationException("Already queued");
  var id=a.GetProperty("id").GetInt64();
  var e=d.GetElement(new ElementId(id)) as FamilyInstance??throw new ArgumentException("Fixture instance required");
  if(e.Category?.Id.Value!=(long)BuiltInCategory.OST_Furniture||e.Host!=null||e.Pinned||e.GroupId!=ElementId.InvalidElementId||e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()!="Kalidis Dynamo fixture pending")throw new ArgumentException("Only marked independent fixture");
  var root=Path.Combine(Path.GetDirectoryName(typeof(Document).Assembly.Location)!,"AddIns","DynamoForRevit");
  var graph=Path.Combine(root,"NodeHelpSharedDocs","Revit.Application.Document.Current.dyn");
  var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(graph)));
  if(hash!="9F9BE8BF25B3DB37B4557117AE77787ACF8B9DEBA5AA031994F845D34C9C0F38")throw new InvalidOperationException("Installed graph differs");
  if(evt==null){handler=new Handler();evt=ExternalEvent.Create(handler);}
  handler!.Id=id;handler.Path=d.PathName;handler.Graph=graph;handler.Root=root;handler.State="queued";handler.Result=null;
  var raised=evt.Raise();
  if(raised!=ExternalEventRequest.Accepted){handler.State="rejected";throw new InvalidOperationException("Event refused: "+raised);}
  return new{status="queued",note="Fixture write graph queued outside rollback; poll status, do not resubmit"};
 }
 private sealed class Handler:IExternalEventHandler {
  internal string Path="",Graph="",Root="",State="idle";
  internal object? Result;internal long Id;
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
     // Generate a first-party fixture graph with a fixed script and numeric element ID.
     var open=model.GetType().GetMethods().FirstOrDefault(m=>m.Name=="OpenFileFromPath"&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType==typeof(string)&&m.GetParameters()[1].ParameterType==typeof(bool))??throw new InvalidOperationException("Open API unavailable");
     var element=doc.GetElement(new ElementId(Id)) as FamilyInstance??throw new InvalidOperationException("Fixture disappeared");
     var p=element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
     if(p==null||p.IsReadOnly||p.AsString()!="Kalidis Dynamo fixture pending")throw new InvalidOperationException("Stale fixture");
     string folder=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,"DynamoFixture-"+Guid.NewGuid().ToString("N"));
     Directory.CreateDirectory(folder);string file=System.IO.Path.Combine(folder,"write.dyn");
     var nodeId=Guid.NewGuid().ToString("N");var portId=Guid.NewGuid().ToString("N");
     string script="import clr, os\nclr.AddReference('RevitAPI')\nclr.AddReference('RevitServices')\nfrom Autodesk.Revit.DB import ElementId, BuiltInParameter, Transaction\nfrom RevitServices.Persistence import DocumentManager\nfrom System import Int64\ndoc=DocumentManager.Instance.CurrentDBDocument\ne=doc.GetElement(ElementId(Int64("+Id.ToString(System.Globalization.CultureInfo.InvariantCulture)+")))\np=e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)\nif p.AsString() != 'Kalidis Dynamo fixture pending': raise Exception('Stale fixture')\nt=Transaction(doc, 'Kalidis Dynamo fixture')\nt.Start()\ntry:\n if not p.Set('Kalidis Dynamo graph verified'): raise Exception('Set failed')\n t.Commit()\nexcept:\n t.RollBack()\n raise\nOUT='Kalidis Dynamo graph verified'\n";
     var graphObject=new{Uuid=Guid.NewGuid().ToString(),IsCustomNode=false,Description="Fixed fixture write",Name="Kalidis Dynamo fixture",ElementResolver=new{ResolutionMap=new Dictionary<string,string>()},Inputs=Array.Empty<object>(),Outputs=Array.Empty<object>(),Nodes=new[]{new{ConcreteType="PythonNodeModels.PythonNode, PythonNodeModels",Code=script,Engine="CPython3",EngineName="CPython3",VariableInputPorts=true,Id=nodeId,NodeType="PythonScriptNode",Inputs=Array.Empty<object>(),Outputs=new[]{new{Id=portId,Name="OUT",Description="Result",UsingDefaultValue=false,Level=2,UseLevels=false,KeepListStructure=false}},Replication="Disabled",Description="Fixed fixture write"}},Connectors=Array.Empty<object>(),Dependencies=Array.Empty<object>(),NodeLibraryDependencies=Array.Empty<object>(),Bindings=Array.Empty<object>(),View=new{Dynamo=new{ScaleFactor=1,HasRunWithoutCrash=true,Version="3.3.0",RunType="Manual",RunPeriod="1000"},NodeViews=new[]{new{Id=nodeId,Name="Python Script",IsSetAsInput=false,IsSetAsOutput=false,Excluded=false,ShowGeometry=false,X=0,Y=0}},Annotations=Array.Empty<object>(),X=0,Y=0,Zoom=1}};
     File.WriteAllText(file,JsonSerializer.Serialize(graphObject));
     var digest=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
     open.Invoke(model,new object[]{file,true});
     workspace=model.GetType().GetProperty("CurrentWorkspace")!.GetValue(model)!;
     var nodes=((IEnumerable)workspace.GetType().GetProperty("Nodes")!.GetValue(workspace)!).Cast<object>().ToArray();
     if(nodes.Length!=1||nodes[0].GetType().FullName!="PythonNodeModels.PythonNode"||nodes[0].GetType().GetProperty("Script")?.GetValue(nodes[0]) is string actualScript&&actualScript!=script)throw new InvalidOperationException("Unexpected fixture node");
     if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))!=digest)throw new InvalidOperationException("Fixture graph changed");
     workspace.GetType().GetMethod("Run",Type.EmptyTypes)!.Invoke(workspace,null);
     var current=doc.GetElement(new ElementId(Id)).get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString();
     bool verified=current=="Kalidis Dynamo graph verified"&&!doc.IsModifiable;
     Result=new{id=Id,graph=file,sha256=digest,after=current,verified,pid=Environment.ProcessId};
     State=verified?"verified":"executed_unverified";
     if(verified)open.Invoke(model,new object[]{Graph,true});
    }finally{context.Resolving-=Resolve;}
   }catch(Exception ex){State="failed";Result=new{error=ex.ToString()};}
  }
 }
}