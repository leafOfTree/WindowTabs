// Opt-in desktop benchmark: shipped entry point, isolated real browser profile,
// native clicks, foreground/owner timing and verified visible desktop pixels.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class DesktopLatency
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit,Size=24)] struct Data { [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Data Data; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder name,int length,out uint needed);
    static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static readonly Color[] Colors={Color.FromArgb(36,103,166),Color.FromArgb(39,139,83),Color.FromArgb(175,62,103)};
    static readonly List<Process> Children=new List<Process>();
    static readonly List<object> ChildIdentities=new List<object>();
    static readonly List<IntPtr> BrowserWindows=new List<IntPtr>();
    static readonly List<IntPtr> HelperWindows=new List<IntPtr>();
    static readonly List<object> Clicks=new List<object>();
    static readonly List<object> Maximizes=new List<object>();
    static readonly List<object> Summaries=new List<object>();
    static Process app;
    static string phase="preflight";
    static Point savedCursor;
    static bool ownsInput;
    static readonly string FixtureTag="WT latency "+new DirectoryInfo(Root).Name+" ";
    static int clickOrdinal;
    static void Check(bool condition,string message) { if(!condition) throw new Exception(phase+": "+message); }
    static void Log(string message) { Console.WriteLine(DateTime.UtcNow.ToString("o")+" "+message); }
    static Rect Bounds(IntPtr hwnd) { Rect r; Check(GetWindowRect(hwnd,out r),"Cannot read window bounds"); return r; }
    static string Title(IntPtr hwnd) { var text=new StringBuilder(1024); GetWindowText(hwnd,text,text.Capacity); return text.ToString(); }
    static List<IntPtr> Windows(Func<IntPtr,bool> predicate)
    {
        var windows=new List<IntPtr>(); EnumWindows((h,d)=>{if(predicate(h)) windows.Add(h); return true;},IntPtr.Zero); return windows;
    }
    static bool InProcess(IntPtr hwnd,int pid) { uint actual; GetWindowThreadProcessId(hwnd,out actual); return actual==pid; }
    static List<IntPtr> Strips(IEnumerable<IntPtr> windows)
    {
        return Windows(h=>InProcess(h,app.Id) && IsWindowVisible(h) && windows.Contains(GetWindow(h,4)) &&
            (GetWindowLong(h,-20)&0x80000)!=0 && (GetWindowLong(h,-20)&0x20)==0);
    }
    static async Task Until(string message,Func<bool> predicate,int timeout=15000)
    {
        var clock=Stopwatch.StartNew();
        while(!predicate()) { Check(app==null || !app.HasExited,"WindowTabs exited unexpectedly"); Check(clock.ElapsedMilliseconds<timeout,message); await Task.Delay(2); }
    }
    static Process Start(string path,string args)
    {
        var child=Process.Start(new ProcessStartInfo(path,args){WorkingDirectory=Root,UseShellExecute=false}); Children.Add(child);
        try { ChildIdentities.Add(new {pid=child.Id,path,startUtcTicks=child.StartTime.ToUniversalTime().Ticks}); }
        catch(InvalidOperationException) { /* A browser launch proxy can exit immediately. */ }
        File.WriteAllText(Path.Combine(Root,"children.json"),Json.Serialize(ChildIdentities));
        return child;
    }
    static Input MouseInput(uint flags,int x=0,int y=0) { return new Input{Data=new Data{Mouse=new Mouse{X=x,Y=y,Flags=flags}}}; }
    static void Inject(params Input[] inputs) { Check(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(Input)))==inputs.Length,"Input rejected"); }
    static void Move(Point p)
    {
        var v=SystemInformation.VirtualScreen;
        Inject(MouseInput(0xC001,(int)Math.Round((p.X-v.Left)*65535.0/(v.Width-1)),(int)Math.Round((p.Y-v.Top)*65535.0/(v.Height-1))));
    }
    static Point TabPoint(IntPtr strip,int index,int count)
    {
        var r=Bounds(strip); int width=Math.Min((r.Right-r.Left)/count,(int)Math.Round(160.0*GetDpiForWindow(strip)/96));
        return new Point(r.Left+index*width+width/2+(++clickOrdinal%2==0?1:-1)*(SystemInformation.DoubleClickSize.Width+2),r.Top+(r.Bottom-r.Top)/2);
    }
    static Point ContentPoint(IntPtr hwnd) { var r=Bounds(hwnd); return new Point((r.Left+r.Right)/2,(r.Top+r.Bottom)/2); }
    static bool VisibleColor(IntPtr hwnd,Color expected)
    {
        var p=ContentPoint(hwnd);
        if(GetAncestor(WindowFromPoint(p),2)!=hwnd) return false;
        using(var image=new Bitmap(3,3)) using(var g=Graphics.FromImage(image)) {
            g.CopyFromScreen(p.X-1,p.Y-1,0,0,image.Size);
            for(int x=0;x<3;x++) for(int y=0;y<3;y++) { var actual=image.GetPixel(x,y); if(Math.Abs(actual.R-expected.R)>3 || Math.Abs(actual.G-expected.G)>3 || Math.Abs(actual.B-expected.B)>3) return false; }
        }
        return true;
    }
    static double Percentile(List<double> values,double q) { var sorted=values.OrderBy(x=>x).ToArray(); return sorted[Math.Min(sorted.Length-1,(int)Math.Ceiling(sorted.Length*q)-1)]; }
    static void Summary(string name,List<double> values)
    {
        var result=new { name,count=values.Count,p50Ms=Percentile(values,.5),p95Ms=Percentile(values,.95),maxMs=values.Max() };
        Summaries.Add(result); Log(Json.Serialize(result));
    }
    static async Task<Tuple<IntPtr,double,double>> Click(IntPtr strip,int index,int count,IList<IntPtr> expected,IList<Color> colors,IntPtr expectedTarget)
    {
        Check(expected.Contains(GetForegroundWindow()),"Foreground left the test group; stopping input");
        var point=TabPoint(strip,index,count); Check(WindowFromPoint(point)==strip,"Tab is occluded; refusing to click another window");
        Move(point); await Task.Delay(20);
        Check(WindowFromPoint(point)==strip,"Tab moved before input");
        Check(GetForegroundWindow()!=expectedTarget,"Measurement requires a change of visible window");
        var clock=Stopwatch.StartNew(); Inject(MouseInput(2),MouseInput(4));
        IntPtr target=IntPtr.Zero;
        await Until("Clicked tab did not activate its expected window",()=>{ target=GetForegroundWindow(); return target==expectedTarget && GetWindow(strip,4)==target; });
        double foreground=clock.Elapsed.TotalMilliseconds;
        int colour=expected.IndexOf(target);
        await Until("Target content not visible",()=>VisibleColor(target,colors[colour]));
        return Tuple.Create(target,foreground,clock.Elapsed.TotalMilliseconds);
    }
    static async Task BrowserSwitches(string browser,int switches)
    {
        phase="real browser startup";
        string profile=Path.Combine(Root,"browser-profile");
        for(int i=0;i<3;i++) {
            string file=Path.Combine(Root,"page-"+i+".html");
            File.WriteAllText(file,"<!doctype html><title>"+FixtureTag+i+"</title><body style=\"margin:0;background:rgb("+Colors[i].R+","+Colors[i].G+","+Colors[i].B+");color:white\"><h1>WindowTabs latency fixture "+i+"</h1></body>");
            Start(browser,"--user-data-dir=\""+profile+"\" --no-first-run --app=\""+new Uri(file).AbsoluteUri+"\"");
            int n=i;
            await Until("Browser fixture did not open",()=>Windows(h=>IsWindowVisible(h) && Title(h)==FixtureTag+n).Count==1);
            BrowserWindows.Add(Windows(h=>IsWindowVisible(h) && Title(h)==FixtureTag+i).Single());
        }
        var area=Screen.PrimaryScreen.WorkingArea;
        foreach(var h in BrowserWindows) SetWindowPos(h,IntPtr.Zero,area.Left+100,area.Top+140,800,500,0x14);
        await Until("Browser windows did not auto-group",()=>Strips(BrowserWindows).Count==1);
        var strip=Strips(BrowserWindows).Single();
        SetForegroundWindow(GetWindow(strip,4));
        await Until("Cannot activate browser fixture",()=>BrowserWindows.Contains(GetForegroundWindow()));
        var order=new List<IntPtr>();
        await Task.Delay(750);
        Log("Browser grouping ready: "+Json.Serialize(new {strip=strip.ToInt64(),bounds=Bounds(strip),dpi=GetDpiForWindow(strip),windows=BrowserWindows.Select(h=>new {hwnd=h.ToInt64(),title=Title(h),bounds=Bounds(h)}).ToArray()}));
        // Discover tab order from native ownership, independently of title sorting.
        for(int i=0;i<3;i++) {
            var p=TabPoint(strip,i,3); Check(WindowFromPoint(p)==strip,"Discovery tab occluded"); Move(p); await Task.Delay(30); Inject(MouseInput(2),MouseInput(4));
            await Task.Delay(150);
            await Until("Discovery click failed",()=>BrowserWindows.Contains(GetForegroundWindow()) && GetWindow(strip,4)==GetForegroundWindow());
            var h=GetForegroundWindow(); Log("Discovery index="+i+" foreground="+h+" title="+Title(h)); order.Add(h);
            await Until("Browser fixture pixels unavailable",()=>VisibleColor(h,Colors[BrowserWindows.IndexOf(h)]));
        }
        Check(order.Distinct().Count()==3,"Tab order discovery returned duplicates");
        for(int state=0;state<2;state++) {
            phase=state==0?"browser normal clicks":"browser maximized clicks";
            if(state==1) { PostMessage(GetForegroundWindow(),0x112,new IntPtr(0xF030),IntPtr.Zero); await Until("Browser group did not maximize",()=>BrowserWindows.All(IsZoomed)); await Task.Delay(500); }
            var foregroundValues=new List<double>(); var frameValues=new List<double>();
            for(int i=-6;i<switches;i++) {
                int index=(order.IndexOf(GetForegroundWindow())+1)%3;
                var result=await Click(strip,index,3,BrowserWindows,Colors,order[index]);
                if(i>=0) { foregroundValues.Add(result.Item2); frameValues.Add(result.Item3); Clicks.Add(new { phase,sample=i,target=BrowserWindows.IndexOf(result.Item1),foregroundMs=result.Item2,visiblePixelMs=result.Item3 }); }
                await Task.Delay(50);
            }
            Summary(phase+" foreground",foregroundValues); Summary(phase+" visible pixels",frameValues);
        }
        foreach(var h in BrowserWindows) PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero);
        await Until("Browser fixtures did not close",()=>BrowserWindows.All(h=>!IsWindow(h))); BrowserWindows.Clear();
    }
    static async Task GroupMaximize(int samples,int slowMs)
    {
        phase="group helper startup";
        string own=Process.GetCurrentProcess().MainModule.FileName;
        for(int i=0;i<20;i++) {
            var child=Start(own,"--helper "+i);
            await Until("Helper did not open",()=>Windows(h=>InProcess(h,child.Id) && IsWindowVisible(h)).Count==1);
            HelperWindows.Add(Windows(h=>InProcess(h,child.Id) && IsWindowVisible(h)).Single());
            if(i!=4 && i!=19) continue;
            int count=i+1;
            await Until("Helper windows did not auto-group",()=>Strips(HelperWindows).Count==1);
            var strip=Strips(HelperWindows).Single();
            // New process activation and group adoption arrive asynchronously.
            // Select the settled foreground owner, rather than an owner snapshot
            // taken while the last helper was still joining the group.
            await Task.Delay(500);
            await Until("Helper group did not become foreground",()=>HelperWindows.Contains(GetForegroundWindow()) && GetWindow(strip,4)==GetForegroundWindow());
            var top=GetForegroundWindow();
            var slow=HelperWindows.First(h=>h!=top);
            foreach(int delay in new[]{0,slowMs}) {
                SendMessage(slow,0x804E,new IntPtr(delay),IntPtr.Zero);
                phase=count+" windows maximize, slow="+delay+"ms";
                var groupValues=new List<double>(); var topValues=new List<double>(); var pixels=new List<double>();
                for(int n=-1;n<samples;n++) {
                    Check(HelperWindows.Contains(GetForegroundWindow()),"Foreground left helper group; stopping benchmark");
                    PostMessage(top,0x112,new IntPtr(0xF120),IntPtr.Zero);
                    await Until("Helper group did not restore",()=>HelperWindows.All(h=>!IsZoomed(h) && !IsIconic(h)));
                    await Task.Delay(350);
                    SendMessage(slow,0x804F,IntPtr.Zero,IntPtr.Zero); // reset handler count
                    var clock=Stopwatch.StartNew(); PostMessage(top,0x112,new IntPtr(0xF030),IntPtr.Zero);
                    await Until("Leader did not maximize",()=>IsZoomed(top)); double leader=clock.Elapsed.TotalMilliseconds;
                    await Until("Followers did not maximize",()=>HelperWindows.All(IsZoomed)); double group=clock.Elapsed.TotalMilliseconds;
                    await Until("Maximized leader content not visible",()=>VisibleColor(top,Colors[HelperWindows.IndexOf(top)%3])); double frame=clock.Elapsed.TotalMilliseconds;
                    int requests=SendMessage(slow,0x8050,IntPtr.Zero,IntPtr.Zero).ToInt32();
                    if(n>=0) { topValues.Add(leader); groupValues.Add(group); pixels.Add(frame); Maximizes.Add(new { count,delayMs=delay,sample=n,leaderMs=leader,allMaximizedMs=group,visiblePixelMs=frame,slowPositionRequests=requests }); }
                    await Task.Delay(100);
                }
                Summary(phase+" leader",topValues); Summary(phase+" all native states",groupValues); Summary(phase+" visible leader pixels",pixels);
            }
            SendMessage(slow,0x804E,IntPtr.Zero,IntPtr.Zero);
            PostMessage(top,0x112,new IntPtr(0xF120),IntPtr.Zero);
            await Until("Final group restore failed",()=>HelperWindows.All(h=>!IsZoomed(h) && !IsIconic(h))); await Task.Delay(350);
        }
    }
    sealed class Helper : Form
    {
        int delay,requests;
        public Helper(int index) { Text="WT group latency "+index; StartPosition=FormStartPosition.Manual; var a=Screen.PrimaryScreen.WorkingArea; Location=new Point(a.Left+100,a.Top+140); Size=new Size(800,500); BackColor=Colors[index%3]; }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x804E) { delay=message.WParam.ToInt32(); return; }
            if(message.Msg==0x804F) { requests=0; return; }
            if(message.Msg==0x8050) { message.Result=new IntPtr(requests); return; }
            if(message.Msg==0x46) { requests++; if(delay>0) Thread.Sleep(delay); }
            base.WndProc(ref message);
        }
    }
    static async Task Run(string browser,int switches,int samples,int slowMs)
    {
        Check(Process.GetProcessesByName("WindowTabs").Length==0,"Existing WindowTabs instance; exit it first");
        Check(Environment.UserInteractive,"Interactive desktop required"); var desktop=OpenInputDesktop(0,false,1); Check(desktop!=IntPtr.Zero,"Desktop is locked");
        try { var name=new StringBuilder(256); uint needed; Check(GetUserObjectInformation(desktop,2,name,512,out needed) && name.ToString()=="Default","Unlocked Default desktop required"); } finally { CloseDesktop(desktop); }
        // Production rules select executable paths. Refuse existing browser
        // windows so auto-grouping cannot adopt the user's normal profile.
        var browserPids=new HashSet<int>();
        foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(browser))) using(process) browserPids.Add(process.Id);
        Check(!Windows(h=>IsWindowVisible(h) && browserPids.Any(pid=>InProcess(h,pid))).Any(),"Existing browser windows would match production grouping rules; close them first or use another browser");
        Check(Screen.PrimaryScreen.WorkingArea.Width>=1000 && Screen.PrimaryScreen.WorkingArea.Height>=700,"Desktop must provide 1000x700 physical pixels");
        Check(Marshal.SizeOf(typeof(Input))==28,"Incorrect x86 input layout");
        await Until("Release mouse and modifier keys",()=>new[]{1,2,0x10,0x11,0x12}.All(k=>GetAsyncKeyState(k)>=0));
        GetCursorPos(out savedCursor); ownsInput=true;
        string own=Process.GetCurrentProcess().MainModule.FileName;
        File.WriteAllText(Path.Combine(Root,"WindowTabsSettings.json"),Json.Serialize(new {
            enableTabbingByDefault=false,includedPaths=new[]{browser,own},autoGroupingPaths=new[]{browser,own},runAtStartup=false,replaceAltTab=false,combineIconsInTaskbar=false,hideInactiveTabs=false,
            enableCtrlNumberHotKey=false,enableHoverActivate=false,autoHideMode="Never",alignment="Left",language="en",tabAppearance=new{tabMaxWidth=160,tabHeight=28,tabOverlap=0,tabHeightOffset=0}
        }));
        app=Start(Path.Combine(Root,"WindowTabs.exe"),"");
        await BrowserSwitches(browser,switches); await GroupMaximize(samples,slowMs);
        string crash=Path.Combine(Root,"WindowTabsCrash.log");
        Check(!File.Exists(crash) || new FileInfo(crash).Length==0,"WindowTabs wrote a crash log");
        Log("PASS: real browser clicks, verified visible pixels and controlled grouped maximize delays");
    }
    [STAThread] static int Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); Application.EnableVisualStyles();
        if(args.Length==2 && args[0]=="--helper") { using(var helper=new Helper(int.Parse(args[1]))) Application.Run(helper); return 0; }
        if(args.Length!=5 || args[0]!="--run") return 2;
        int code=1;
        using(var context=new ApplicationContext()) using(var start=new System.Windows.Forms.Timer{Interval=50}) {
            start.Tick+=async(s,e)=>{
                start.Stop();
                try { await Run(args[1],int.Parse(args[2]),int.Parse(args[3]),int.Parse(args[4])); code=0; }
                catch(Exception error) {
                    Console.Error.WriteLine(error); File.WriteAllText(Path.Combine(Root,"failure.txt"),error.ToString());
                    if(app!=null && !app.HasExited) File.WriteAllText(Path.Combine(Root,"windows.json"),Json.Serialize(new {foreground=GetForegroundWindow().ToInt64(),windows=Windows(h=>InProcess(h,app.Id) || BrowserWindows.Contains(h) || HelperWindows.Contains(h)).Select(h=>new{hwnd=h.ToInt64(),owner=GetWindow(h,4).ToInt64(),visible=IsWindowVisible(h),title=Title(h),bounds=Bounds(h),dpi=GetDpiForWindow(h)}).ToArray()}));
                }
                finally {
                    foreach(var h in BrowserWindows.Concat(HelperWindows)) if(IsWindow(h)) PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero);
                    if(ownsInput) { Inject(MouseInput(4)); Move(savedCursor); }
                    // Only children started by this driver are eligible for forced cleanup.
                    foreach(var child in Children) { try { if(!child.WaitForExit(3000)) { child.Kill(); child.WaitForExit(); } } catch(InvalidOperationException) {} child.Dispose(); }
                    float dpi; using(var graphics=Graphics.FromHwnd(IntPtr.Zero)) dpi=graphics.DpiX;
                    File.WriteAllText(Path.Combine(Root,"result.json"),Json.Serialize(new {status=code==0?"passed":"failed",phase,clicks=Clicks,maximizes=Maximizes,summaries=Summaries,screen=Screen.PrimaryScreen.Bounds,dpi,sampling="Stopwatch from native mouse down; foreground/strip owner; visible 3x3 desktop pixels. Polling and capture overhead included. Not photon latency or full-frame completion."}));
                    context.ExitThread();
                }
            };
            start.Start(); Application.Run(context);
        }
        return code;
    }
}
