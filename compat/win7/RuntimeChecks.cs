#if WIN7_TESTS
using System;using System.Diagnostics;using System.IO;using System.Threading;using System.Windows.Forms;
namespace QuickReplyLite {
internal static class LegacyRuntimeChecks {
    internal static void Run() {
        int ui=Thread.CurrentThread.ManagedThreadId, ticks=0;
        using(Form window=new Form { Text="Win7 compatibility checks", Width=320, Height=200 })
        using(System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer {Interval=10}) {
            window.Show(); Application.DoEvents();timer.Tick+=delegate{ticks++;};timer.Start();
            using(var token=new Win7Cancellation()) {LegacyUi.Delay(100,token);Check(ticks>0,"delay keeps window messages responsive");}
            ticks=0;int worker=0;int value=LegacyUi.Run(delegate {worker=Thread.CurrentThread.ManagedThreadId;Thread.Sleep(100);return 42;});
            Check(value==42 && worker!=ui && ticks>0,"file work runs off-thread while UI messages continue");
            bool failed=false;try{LegacyUi.Run<int>(delegate{throw new InvalidDataException("worker failure");});}catch(IOException e){failed=e.InnerException is InvalidDataException;}
            Check(failed,"worker failure returns safely to the caller");
            using(var token=new Win7Cancellation()) {
                token.Cancel();bool stopped=false;try{LegacyUi.Delay(1000,token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"cancelled send stops before waiting");
            }
            using(var token=new Win7Cancellation())
            using(System.Windows.Forms.Timer cancel=new System.Windows.Forms.Timer {Interval=30}) {
                cancel.Tick+=delegate{token.Cancel();cancel.Stop();};cancel.Start();var time=Stopwatch.StartNew();bool stopped=false;
                try{LegacyUi.Delay(1000,token);}catch(OperationCanceledException){stopped=true;}
                Check(stopped && time.ElapsedMilliseconds<800,"closing or cancelling during a wait stops pending send steps");
            }
            timer.Stop();window.Close();
        }
        Check(Thread.CurrentThread.ManagedThreadId==ui && Thread.CurrentThread.GetApartmentState()==ApartmentState.STA,"clipboard and input remain on the original STA thread");
        Console.WriteLine("All 6 legacy UI scheduling checks passed.");
    }
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);Console.WriteLine("PASS legacy runtime: "+name);}
}
}
#endif
