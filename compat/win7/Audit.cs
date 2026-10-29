using System;using System.IO;using System.Linq;using System.Reflection.PortableExecutable;using System.Reflection.Metadata;
class Win7Audit {
static int count;static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
static void Main(string[] args){string folder=args[0];foreach(string name in new[]{"QuickReplyLite.exe","ExcelDataReader.dll","ICSharpCode.SharpZipLib.dll"}){
using(var pe=new PEReader(File.OpenRead(Path.Combine(folder,name)))){var md=pe.GetMetadataReader();Check(md.MetadataVersion.StartsWith("v2.0"),name+" targets CLR2");Check(pe.PEHeaders.CoffHeader.Machine==Machine.I386 && (pe.PEHeaders.CorHeader.Flags & (CorFlags.Requires32Bit|CorFlags.Prefers32Bit))==0,name+" supports x86 and x64");
foreach(var h in md.AssemblyReferences){var a=md.GetAssemblyReference(h);var n=md.GetString(a.Name);Check(a.Version.Major<4 || n=="ExcelDataReader",name+" reference "+n+" "+a.Version+" needs no .NET4");}
if(name.EndsWith(".exe")){var types=md.TypeDefinitions.Select(h=>md.GetString(md.GetTypeDefinition(h).Name)).ToArray();foreach(var n in new[]{"Program","MainForm","PhraseEditor","WindowDockingSession","ExcelLibraryReader","LibraryTransfer","LegacyUi"})Check(types.Contains(n),"app retains "+n);Check(!types.Any(n=>n.Contains("ActivationGate")||n.Contains("LicenseVerifier")),"app is activation-free");
foreach(var h in md.TypeReferences){var t=md.GetTypeReference(h);Check(!md.GetString(t.Namespace).StartsWith("System.Threading.Tasks"),"no TPL runtime dependency");}
foreach(var h in md.MemberReferences){var m=md.GetMemberReference(h);Check(md.GetString(m.Name)!="get_DeviceDpi","no .NET4.7 DeviceDpi API");}
var imports=md.MethodDefinitions.Select(h=>md.GetMethodDefinition(h)).Where(m=>(m.Attributes&System.Reflection.MethodAttributes.PinvokeImpl)!=0).Select(m=>md.GetString(m.GetImport().Name)).ToArray();Check(imports.Contains("GetWindowLongW")&&imports.Contains("SetWindowLongW")&&imports.Contains("GetWindowLongPtrW"),"window styles support both process architectures");
}
}}
string config=File.ReadAllText(Path.Combine(folder,"QuickReplyLite.exe.config"));Check(config.Contains("v2.0.50727")&&!config.Contains("v4.0")&&!config.Contains("v4.8"),"startup selects Win7 built-in CLR2 only");Console.WriteLine("All "+count+" Win7 binary compatibility assertions passed.");}
}
