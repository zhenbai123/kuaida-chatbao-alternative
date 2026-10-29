from pathlib import Path
import argparse,subprocess,shutil,re,json,hashlib
p=argparse.ArgumentParser();p.add_argument('--dotnet');p.add_argument('--compiler',required=True);p.add_argument('--refs',required=True);p.add_argument('--excel',required=True);p.add_argument('--zip',required=True);p.add_argument('--output',default='release/win7');p.add_argument('--verify-build',action='store_true');a=p.parse_args()
r=Path(__file__).resolve().parents[2];refs=Path(a.refs);out=(r/a.output).resolve();out.mkdir(parents=True,exist_ok=True);gen=r/'tools/win7-generated';gen.mkdir(parents=True,exist_ok=True)
def convert(s,name):
 s=s.replace('using System.Threading.Tasks;','').replace('async Task ','void ').replace('async void ','void ').replace('async delegate','delegate').replace('await ','')
 s=s.replace('Task.Run(', 'LegacyUi.Run(').replace('Task.Delay(', 'LegacyUi.Delay(').replace('CancellationTokenSource','Win7Cancellation').replace('CancellationToken','Win7Cancellation')
 s=s.replace('String.IsNullOrWhiteSpace','LegacyText.IsBlank').replace('String.Join(','LegacyText.Join(').replace('String.Concat(','LegacyText.Concat(').replace('Path.Combine(','LegacyIO.Combine(')
 s=s.replace('json.CopyTo(content);','LegacyIO.Copy(json, content);').replace('stream.Flush(true);','LegacyIO.Flush(stream);')
 s=s.replace('JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false, true, "  ")','JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), false)')
 s=re.sub(r'\b([A-Za-z_][A-Za-z0-9_]*)\.CopyTo\(([A-Za-z_][A-Za-z0-9_]*)\);',r'LegacyIO.Copy(\1, \2);',s)
 s=s.replace('DeviceDpi','LegacyUi.Dpi(this)')
 s=s.replace('1.8.2.0','1.8.3.0').replace('快答·开源版（免激活）','快答·Win7兼容版（免激活）').replace('快答 · 开源版（免激活）','快答 · Win7兼容版（免激活）')
 if name=='WindowsWindowDocking.cs':
  s=s.replace('private static extern IntPtr GetWindowLongPtr(', 'private static extern IntPtr GetWindowLongPtr64(').replace('private static extern IntPtr SetWindowLongPtr(', 'private static extern IntPtr SetWindowLongPtr64(')
  point=s.index('        internal WindowsWindowDockingHost(')
  s=s[:point]+'''        [DllImport("user32.dll", EntryPoint="GetWindowLongW")] private static extern int GetWindowLong32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongW")] private static extern int SetWindowLong32(IntPtr window, int index, int value);
        private static IntPtr GetWindowLongPtr(IntPtr w, int i) { return IntPtr.Size == 8 ? GetWindowLongPtr64(w,i) : new IntPtr(GetWindowLong32(w,i)); }
        private static IntPtr SetWindowLongPtr(IntPtr w, int i, IntPtr v) { return IntPtr.Size == 8 ? SetWindowLongPtr64(w,i,v) : new IntPtr(SetWindowLong32(w,i,v.ToInt32())); }
'''+s[point:]
 assert not re.search(r'\basync\b|\bawait\b|\bTask\b|DeviceDpi|String.IsNullOrWhiteSpace|\.CopyTo\(',s),name
 return s
for file in (r/'src').glob('*.cs'):
 (gen/file.name).write_text(convert(file.read_text(encoding='utf-8-sig'),file.name),encoding='utf-8-sig')
for file in [r/'compat/win7/Runtime.cs',r/'compat/win7/ZipArchive.cs',r/'compat/win7/RuntimeChecks.cs']:shutil.copy2(file,gen/file.name)
manifest=(r/'app.manifest').read_text().replace('<supportedOS Id=', '<supportedOS Id="{35138b9a-5d96-4fbd-8e2d-a2440225f93a}" /><supportedOS Id=',1);(gen/'app.manifest').write_text(manifest)
libs=['mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Runtime.Serialization','System.ServiceModel.Web','System.Xml','System.Data','Accessibility']
cmd=([a.dotnet] if a.dotnet else [])+[a.compiler,'-nologo','-noconfig','-nostdlib+','-langversion:5','-target:winexe','-platform:anycpu','-optimize+','-codepage:65001','-win32manifest:'+str(gen/'app.manifest'),'-win32icon:'+str(r/'assets/app.ico'),'-out:'+str(out/'QuickReplyLite.exe')]
cmd+=['-reference:'+str(refs/(lib+'.dll')) for lib in libs]+['-reference:'+str(Path(a.excel).resolve()),'-reference:'+str(Path(a.zip).resolve())]+[str(f) for f in sorted(gen.glob('*.cs'))]
subprocess.run(cmd,check=True)
shutil.copy2(a.excel,out/'ExcelDataReader.dll');shutil.copy2(a.zip,out/'ICSharpCode.SharpZipLib.dll')
(out/'QuickReplyLite.exe.config').write_text('<?xml version="1.0" encoding="utf-8"?><configuration><startup><supportedRuntime version="v2.0.50727" /></startup></configuration>')
print('Win7 CLR2 / AnyCPU build:',out)

if a.verify_build:
 tests=r/'tools/win7-tests-generated';tests.mkdir(parents=True,exist_ok=True)
 for file in (r/'tests').glob('*.cs'):
  content=file.read_text(encoding='utf-8-sig')
  content=content.replace('        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);','')
  (tests/file.name).write_text(convert(content,file.name),encoding='utf-8-sig')
 testcmd=[part for part in cmd if not part.startswith(('-out:','-target:'))]
 testcmd+=['-define:WIN7_TESTS','-target:exe','-main:Tests','-out:'+str(out/'Win7CoreChecks.exe')]+[str(f) for f in tests.glob('*.cs')]
 subprocess.run(testcmd,check=True)
 for name,entry in [('Screenshots','QuickReplyLite.ScreenshotProgram'),('WindowDockingSmoke','QuickReplyLite.WindowDockingSmoke')]:
  file=r/'scripts'/(name+'.cs');generated=tests/file.name;content=convert(file.read_text(encoding='utf-8-sig'),file.name)
  if name=='Screenshots': content=content.replace('Application.SetCompatibleTextRenderingDefault(false);','Application.SetCompatibleTextRenderingDefault(false); LegacyRuntimeChecks.Run();',1)
  generated.write_text(content,encoding='utf-8-sig')
  harnesscmd=[part for part in cmd if not part.startswith(('-out:','-target:'))]+['-define:WIN7_TESTS','-target:exe','-main:'+entry,'-out:'+str(out/(name+'.exe')),str(generated)]
  subprocess.run(harnesscmd,check=True)

 if a.verify_build:
  x86cmd=[part for part in testcmd if not part.startswith(('-out:','-platform:'))]+['-platform:x86','-out:'+str(out/'Win7CoreChecks-x86.exe')]
  subprocess.run(x86cmd,check=True)
  file=tests/'WindowDockingSmoke.cs'
  x86cmd=[part for part in cmd if not part.startswith(('-out:','-target:','-platform:'))]+['-target:exe','-platform:x86','-main:QuickReplyLite.WindowDockingSmoke','-out:'+str(out/'WindowDockingSmoke-x86.exe'),str(file)]
  subprocess.run(x86cmd,check=True)
