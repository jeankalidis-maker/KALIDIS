using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;
try {
 int expected=args.Length==1&&int.TryParse(args[0],out int p)?p:0;
 if(args.Length>1||(args.Length==1&&expected<=0))throw new ArgumentException("Optional positive Revit PID only");
 var candidates=Process.GetProcessesByName("Revit").Where(p=>p.MainWindowHandle!=IntPtr.Zero&&(expected==0||p.Id==expected)).ToArray();
 if(candidates.Length!=1)throw new InvalidOperationException("Exactly one Revit window required");
 var process=candidates[0];
 var root=AutomationElement.FromHandle(process.MainWindowHandle);
 if(root.Current.ProcessId!=process.Id)throw new InvalidOperationException("Window owner mismatch");
 var children=new List<object>();
 var walker=TreeWalker.ControlViewWalker;
 for(var el=walker.GetFirstChild(root);el!=null&&children.Count<50;el=walker.GetNextSibling(el)){
  if(el.Current.ProcessId!=process.Id)continue;
  children.Add(new {name=el.Current.Name,controlType=el.Current.ControlType.ProgrammaticName,enabled=el.Current.IsEnabled,offscreen=el.Current.IsOffscreen});
 }
 var modal=root.TryGetCurrentPattern(WindowPattern.Pattern,out object pattern)?(bool?)((WindowPattern)pattern).Current.IsModal:null;
 Console.WriteLine(JsonSerializer.Serialize(new {ok=true,pid=process.Id,title=root.Current.Name,root.Current.IsEnabled,root.Current.IsOffscreen,responding=process.Responding,mainWindowModal=modal,children,readOnly=true,note="Direct children only; absence of a modal flag does not rule out dialogs."}));
} catch(Exception ex){Console.WriteLine(JsonSerializer.Serialize(new {ok=false,error=ex.Message}));Environment.ExitCode=1;}
