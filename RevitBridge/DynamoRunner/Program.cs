using System.IO;
using System.Reflection;
using System.Runtime.Loader;
internal static class Program {
 [STAThread] static int Main(string[] args){
  if(args.Length!=2)throw new ArgumentException("Provide isolated graph and output XML");
  var graph=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);var folder=Path.GetDirectoryName(graph)!;
  var sandbox=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KalidisAIEngine","Sandbox");
  if(!string.Equals(Path.GetDirectoryName(folder),sandbox,StringComparison.OrdinalIgnoreCase)||(!Path.GetFileName(folder).StartsWith("DynamoFixture-",StringComparison.Ordinal)&&Path.GetFileName(folder)!="DynamoGenerated-1ab17a177a0644959844062feb2ce118")||Path.GetDirectoryName(output)!=folder||File.Exists(output)||!File.Exists(graph)||File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint)||File.GetAttributes(graph).HasFlag(FileAttributes.ReparsePoint))throw new ArgumentException("Dedicated fixture directory required");
  // Only the first-party generated arithmetic fixture or the installed Autodesk plus sample clone.
  var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(graph)));
  var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Autodesk","Revit 2025");
  var dyn=Path.Combine(root,"AddIns","DynamoForRevit");
  var source=Path.Combine(dyn,"NodeHelpSharedDocs","+.dyn");
  bool generated=Path.GetFileName(folder)=="DynamoGenerated-1ab17a177a0644959844062feb2ce118"&&Path.GetFileName(graph)=="sum.dyn"&&hash=="D2C8DEA224BAA080FE842C074C9C5700F700E6E3032CC317991FFD1087469F2F";
  bool original=Path.GetFileName(folder).StartsWith("DynamoFixture-",StringComparison.Ordinal)&&File.Exists(source)&&hash==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source)));
  if(!generated&&!original)throw new ArgumentException("Graph differs from the two reviewed addition fixtures");
  var exe=Path.Combine(dyn,"DynamoCLI.dll");
  var resolver=new AssemblyDependencyResolver(exe);
  AssemblyLoadContext.Default.Resolving+=(ctx,name)=>{
   var p=resolver.ResolveAssemblyToPath(name);
   if(p!=null&&File.Exists(p))return ctx.LoadFromAssemblyPath(p);
   foreach(var dir in new[]{dyn,root,Path.Combine(dyn,"Revit")}){
    var q=Path.Combine(dir,name.Name+".dll");if(File.Exists(q))return ctx.LoadFromAssemblyPath(q);
   }
   return null;
  };
  var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(exe);
  var entry=assembly.EntryPoint??throw new InvalidOperationException("Missing entry");
  var cliArgs=new[]{"-o",graph,"-v",output,"--NoNetworkMode","--DisableAnalytics","-u",Path.Combine(folder,"UserData"),"--CommonData",Path.Combine(folder,"CommonData")};
  var result=entry.Invoke(null,new object[]{cliArgs});
  return result is int n?n:0;
 }
}