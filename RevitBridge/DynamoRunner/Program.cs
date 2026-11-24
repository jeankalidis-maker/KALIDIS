using System.IO;
using System.Reflection;
using System.Runtime.Loader;
internal static class Program {
 [STAThread] static int Main(string[] args){
  if(args.Length!=2)throw new ArgumentException("Provide isolated graph and output XML");
  var graph=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);var folder=Path.GetDirectoryName(graph)!;
  if(!Path.GetFileName(folder).StartsWith("DynamoFixture-",StringComparison.Ordinal)||Path.GetDirectoryName(output)!=folder||File.Exists(output)||!File.Exists(graph))throw new ArgumentException("Dedicated fixture directory required");
  // Only the first-party generated arithmetic fixture or the installed Autodesk plus sample clone.
  var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(graph)));
  var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Autodesk","Revit 2025");
  var dyn=Path.Combine(root,"AddIns","DynamoForRevit");
  var source=Path.Combine(dyn,"NodeHelpSharedDocs","+.dyn");
  if(!File.Exists(source)||hash!=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))))throw new ArgumentException("Graph differs from installed reviewed addition sample");
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