// Independent black-box driver: no reference to WindowTabs or service injection.
// Real entry point, foreign HWNDs, SendInput, foreground/owner checks, normal Exit.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class DesktopE2E
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct GuiThread { public int Size,Flags; public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret; public Rect CaretRect; }
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Key { public ushort Code,Scan; public uint Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] struct Data { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Key Key; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Data Data; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread, ref GuiThread info);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int size);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.IDispatch)] out object accessible);
    [DllImport("oleacc.dll")] static extern int AccessibleChildren(Accessibility.IAccessible container, int start, int count, [Out] object[] children, out int obtained);
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint kind);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder text, int size, out uint needed);
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly List<Form> Forms = new List<Form>();
    static readonly List<object> Measurements = new List<object>();
    static readonly List<object> ResourceSamples = new List<object>();
    static readonly Queue<object> RecentActions = new Queue<object>();
    static Process app;
    static string phase = "preflight";
    static string step = "";
    static int completed, totalSwitches;
    static double switchingSeconds;
    static int clickOrdinal;
    static Point savedCursor;
    static bool inputOwned;
    static void Check(bool ok, string text) { if (!ok) throw new Exception(phase + " / " + step + ": " + text); }
    static void Log(string text) { Console.WriteLine(DateTime.UtcNow.ToString("o") + " " + text); }
    static void Remember(string action, object details)
    {
        RecentActions.Enqueue(new { utc=DateTime.UtcNow.ToString("o"),phase,step,action,details,foreground=GetForegroundWindow().ToInt64() });
        while(RecentActions.Count>40) RecentActions.Dequeue();
    }
    static bool HasCrash() { var path=Path.Combine(Root,"WindowTabsCrash.log"); return File.Exists(path) && new FileInfo(path).Length>0; }
    static void Diagnose()
    {
        if(app==null || app.HasExited) return;
        var all=Windows(app.Id).Concat(Helpers()).Select(h=> new { hwnd=h.ToInt64(),owner=GetWindow(h,4).ToInt64(),visible=IsWindowVisible(h),style=GetWindowLong(h,-16),extendedStyle=GetWindowLong(h,-20),windowClass=Class(h),bounds=Bounds(h) });
        File.WriteAllText(Path.Combine(Root,"windows.json"),Json.Serialize(all.ToArray()));
    }
    static string Class(IntPtr h) { var s=new StringBuilder(256); GetClassName(h,s,s.Capacity); return s.ToString(); }
    static List<IntPtr> Windows(int pid)
    {
        var result=new List<IntPtr>();
        EnumWindows((h,d) => { uint p; GetWindowThreadProcessId(h,out p); if(p==pid) result.Add(h); return true; },IntPtr.Zero);
        return result;
    }
    static List<IntPtr> Helpers() { return Forms.Where(f=>!f.IsDisposed).Select(f=>f.Handle).ToList(); }
    static List<IntPtr> Strips()
    {
        var helpers=Helpers();
        return Windows(app.Id).Where(h=>IsWindowVisible(h) && helpers.Contains(GetWindow(h,4)) &&
            (GetWindowLong(h,-20)&0x80000)!=0 && (GetWindowLong(h,-20)&0x20)==0).ToList();
    }
    static Rect Bounds(IntPtr h) { Rect r; Check(GetWindowRect(h,out r),"Cannot read HWND bounds"); return r; }
    static Point TabPoint(IntPtr strip, int index, int count)
    {
        var r=Bounds(strip);
        int width=Math.Min((r.Right-r.Left)/count,(int)Math.Round(160.0*GetDpiForWindow(strip)/96));
        return new Point(r.Left+index*width+width/2,r.Top+(r.Bottom-r.Top)/2);
    }
    static void Inject(params Input[] inputs)
    {
        Check(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(Input)))==inputs.Length,"SendInput rejected input (locked desktop/UIPI?)");
    }
    static Input MouseInput(uint flags, int x=0, int y=0) { return new Input { Data=new Data { Mouse=new Mouse { X=x,Y=y,Flags=flags } } }; }
    static Input KeyInput(int code, bool up) { return new Input { Type=1,Data=new Data { Key=new Key { Code=(ushort)code,Flags=(up?2u:0u) | (code>=0x21 && code<=0x28 ? 1u:0u) } } }; }
    static void Move(Point p)
    {
        var v=SystemInformation.VirtualScreen;
        Inject(MouseInput(0xC001,(int)Math.Round((p.X-v.Left)*65535.0/(v.Width-1)),(int)Math.Round((p.Y-v.Top)*65535.0/(v.Height-1))));
    }
    static async Task Click(IntPtr strip, int index, int count)
    {
        var p=TabPoint(strip,index,count);
        // Repeated clicks at the same position within the system double-click
        // interval intentionally open Rename. Keep this a single-click scenario.
        p.X += (++clickOrdinal%2==0 ? 1 : -1) * (SystemInformation.DoubleClickSize.Width+2);
        Remember("click",new { strip=strip.ToInt64(),index,count,x=p.X,y=p.Y });
        Check(WindowFromPoint(p)==strip,"Tab is occluded; refusing to click another window");
        Move(p); await Task.Delay(20);
        Inject(MouseInput(2)); await Task.Delay(20); Inject(MouseInput(4));
        uint pid; uint thread=GetWindowThreadProcessId(strip,out pid);
        await Until("Tab click did not release mouse capture",()=> { var info=new GuiThread { Size=Marshal.SizeOf(typeof(GuiThread)) }; return GetGUIThreadInfo(thread,ref info) && info.Capture==IntPtr.Zero; });
    }
    static void Chord(params int[] keys)
    {
        Remember("chord",new { keys });
        uint pid; GetWindowThreadProcessId(GetForegroundWindow(),out pid);
        Check(pid==Process.GetCurrentProcess().Id || (app!=null && pid==app.Id),"Foreground belongs to another application; refusing keyboard input");
        var input=keys.Select(k=>KeyInput(k,false)).Concat(keys.Reverse().Select(k=>KeyInput(k,true))).ToArray();
        Inject(input);
    }
    static async Task Until(string description, Func<bool> condition, int timeout=5000)
    {
        var watch=Stopwatch.StartNew();
        while(!condition())
        {
            Check(app==null || !app.HasExited,"Application exited unexpectedly");
            Check(watch.ElapsedMilliseconds<timeout,description);
            await Task.Delay(10);
        }
    }
    static async Task Activated(IntPtr expected, IntPtr strip)
    {
        Remember("verify activation",new { expected=expected.ToInt64(),strip=strip.ToInt64(),owner=GetWindow(strip,4).ToInt64() });
        try { await Until("Wrong foreground or strip owner after switch",()=>GetForegroundWindow()==expected && GetWindow(strip,4)==expected && IsWindowVisible(strip),2000); }
        catch { Log("expected="+expected+" foreground="+GetForegroundWindow()+" owner="+GetWindow(strip,4)); throw; }
        Check(WindowFromPoint(TabPoint(strip,0,3))==strip,"Active window covers its tab strip");
    }
    static async Task Drag(IntPtr source, IntPtr target, int targetCount)
    {
        var start=TabPoint(source,0,1); var end=TabPoint(target,0,targetCount);
        Remember("drag",new { source=source.ToInt64(),target=target.ToInt64(),start,end });
        Check(WindowFromPoint(start)==source && WindowFromPoint(end)==target,"Drag endpoints are occluded");
        Move(start); Inject(MouseInput(2));
        try
        {
            await Task.Delay(50);
            for(int i=1;i<=20;i++) { Move(new Point(start.X+(end.X-start.X)*i/20,start.Y+(end.Y-start.Y)*i/20)); await Task.Delay(20); }
            await Task.Delay(100);
        }
        finally { Inject(MouseInput(4)); }
    }
    static async Task<List<IntPtr>> ReadOrder(IntPtr strip, int count, IEnumerable<IntPtr> expected)
    {
        var order=new List<IntPtr>();
        for(int i=0;i<count;i++)
        {
            await Click(strip,i,count);
            await Until("Clicked tab did not become the strip owner",()=>GetWindow(strip,4)==GetForegroundWindow() && expected.Contains(GetForegroundWindow()));
            // Wait for event-driven ownership and rendering to settle, then verify uniqueness.
            await Task.Delay(50);
            order.Add(GetForegroundWindow());
        }
        Check(order.Distinct().Count()==count && new HashSet<IntPtr>(order).SetEquals(expected),"Missing, duplicated, or stale tab after grouping");
        return order;
    }
    static int[] Resources()
    {
        app.Refresh();
        var counts=new[]{(int)GetGuiResources(app.Handle,0),(int)GetGuiResources(app.Handle,1),app.HandleCount};
        Check(counts.All(n=>n>0),"Native resource counters are unavailable");
        return counts;
    }
    static void CheckGrowth(int[] before, int[] after)
    {
        Check(after[0]<=before[0]+256,"GDI growth exceeded the 256-object budget: "+before[0]+" -> "+after[0]);
        Check(after[1]<=before[1]+128,"USER growth exceeded the 128-object budget: "+before[1]+" -> "+after[1]);
        Check(after[2]<=before[2]+64,"Handle growth exceeded the 64-handle budget: "+before[2]+" -> "+after[2]);
    }
    static async Task<int[]> SwitchMany(IntPtr strip, List<IntPtr> order, int count)
    {
        var timings=new List<double>();
        // Warm caches before measuring growth; do not force GC inside the application.
        for(int i=0;i<30;i++) { step="warmup "+i; int n=i%order.Count; Chord(0x12,0x31+n); await Activated(order[n],strip); }
        await Task.Delay(250);
        int[] before=Resources();
        for(int i=0;i<count;i++)
        {
            step="switch "+i+" method "+(i%4);
            int current=order.IndexOf(GetForegroundWindow());
            Check(current>=0,"Foreground left the helper group (desktop input interference)");
            int target=(current+(i%4==3?order.Count-1:1))%order.Count;
            var timer=Stopwatch.StartNew();
            switch(i%4)
            {
                case 0: await Click(strip,target,order.Count); break;
                case 1: Chord(0x12,0x31+target); break;
                case 2: Chord(0x11,0x12,0x7A); break; // configured Ctrl+Alt+F11: next
                default: Chord(0x11,0x12,0x7B); break; // Ctrl+Alt+F12: previous
            }
            await Activated(order[target],strip);
            timings.Add(timer.Elapsed.TotalMilliseconds); totalSwitches++;
            Check(Strips().Count==1,"Switch created an extra group");
            if(i%50==49) { ResourceSamples.Add(new { phase,verifiedSwitches=totalSwitches,resources=Resources() }); Log("Verified switches: "+totalSwitches); }
        }
        // A burst sends input without awaiting each switch; compare final focus to a model.
        for(int i=0;i<20;i++) { Chord(0x12,0x31+i%order.Count); await Task.Delay(15); }
        await Activated(order[19%order.Count],strip);
        await Task.Delay(500);
        int[] after=Resources();
        timings.Sort();
        Measurements.Add(new { phase, count, p50Ms=timings[timings.Count/2],p95Ms=timings[(int)(timings.Count*.95)],maxMs=timings.Last(),before,after });
        // This external process cannot force GC in the application. Icon finalizers
        // make short-window counts fluctuate; use a bounded headroom budget and
        // retain raw measurements, not an assertion that every resource is a leak.
        CheckGrowth(before,after);
        return before;
    }
    // A menu item's bounds on screen from the window's MSAA tree, or null when it has none by that name.
    static Rect? MenuItemRect(IntPtr window, string name)
    {
        var iid=new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); // IAccessible
        object found;
        if(AccessibleObjectFromWindow(window,0xFFFFFFFC,ref iid,out found)!=0) return null; // OBJID_CLIENT
        var menu=found as Accessibility.IAccessible;
        if(menu==null) return null;
        int count; try { count=menu.accChildCount; } catch(COMException) { return null; }
        var children=new object[count]; int obtained;
        if(count==0 || AccessibleChildren(menu,0,count,children,out obtained)!=0) return null;
        foreach(var child in children.Take(obtained))
        {
            var entry=child as Accessibility.IAccessible;
            if(entry==null) continue;
            try
            {
                var text=entry.get_accName(0);
                if(text==null || text.Replace("&","")!=name) continue;
                int left,top,width,height; entry.accLocation(out left,out top,out width,out height,0);
                return new Rect{ Left=left, Top=top, Right=left+width, Bottom=top+height };
            }
            catch(COMException) { }
        }
        return null;
    }
    static async Task ExitNormally()
    {
        // Exercise the shipped NotifyIcon context-menu Exit handler. This bypasses
        // Explorer icon discovery only, using Framework NotifyIcon's tray callback.
        Check(AllowSetForegroundWindow(app.Id),"Cannot grant the tray-menu foreground permission");
        var candidates=Windows(app.Id).Where(h=>!IsWindowVisible(h) && Class(h).StartsWith("WindowsForms10.")).ToList();
        foreach(var h in candidates) PostMessage(h,0x800,IntPtr.Zero,new IntPtr(0x205));
        // The menu is a WinForms strip in the app's theme, not a system menu: its items are
        // found by name through MSAA, which reaches across processes.
        var popup=IntPtr.Zero; var item=new Rect();
        await Until("Tray context menu did not open",()=>{
            foreach(var h in Windows(app.Id).Where(IsWindowVisible))
            {
                var found=MenuItemRect(h,"Exit WindowTabs");
                if(found.HasValue) { popup=h; item=found.Value; return true; }
            }
            return false;
        });
        await Until("Tray menu did not acquire keyboard focus",()=>Windows(app.Id).Contains(GetForegroundWindow()));
        Log("Tray Exit item at "+item.Left+","+item.Top+" "+(item.Right-item.Left)+"x"+(item.Bottom-item.Top));
        var point=new Point((item.Left+item.Right)/2,(item.Top+item.Bottom)/2);
        Check(WindowFromPoint(point)==popup,"Exit menu is occluded");
        Move(point); Inject(MouseInput(2)); await Task.Delay(30); Inject(MouseInput(4));
        var timer=Stopwatch.StartNew();
        while(!app.HasExited) { Check(timer.ElapsedMilliseconds<10000,"Normal Exit did not terminate the process"); await Task.Delay(20); }
        Check(app.ExitCode==0,"Normal Exit returned "+app.ExitCode);
        Check(Windows(app.Id).Count==0,"Native windows survived process exit");
        Check(Helpers().All(IsWindow),"Exiting WindowTabs closed a helper application");
        app.Dispose(); app=null;
    }
    static void Fixture()
    {
        var own=Process.GetCurrentProcess().MainModule.FileName;
        File.WriteAllText(Path.Combine(Root,"WindowTabsSettings.json"),Json.Serialize(new {
            enableTabbingByDefault=false, includedPaths=new[]{own},autoGroupingPaths=new string[0],
            runAtStartup=false,replaceAltTab=false,combineIconsInTaskbar=false,hideInactiveTabs=false,
            enableCtrlNumberHotKey=true,numberHotKeyModifier="Alt",enableNumberLeader=true,enableHoverActivate=false,autoHideMode="Never",alignment="Left",language="en",
            hotKeys=new { nextTab=0x67A, prevTab=0x67B, numberLeader=0x453 },
            tabAppearance=new { tabMaxWidth=160,tabHeight=28,tabOverlap=0,tabHeightOffset=0 }
        }));
    }
    static void Preflight()
    {
        Check(Environment.UserInteractive && Process.GetCurrentProcess().SessionId!=0,"Run the runner in an interactive user session, not as a Windows service");
        Check(Process.GetProcessesByName("WindowTabs").Length==0,"Existing WindowTabs instance; exit it first");
        var desk=OpenInputDesktop(0,false,1);
        Check(desk!=IntPtr.Zero,"Desktop is locked or inaccessible");
        try { var name=new StringBuilder(256); uint needed; Check(GetUserObjectInformation(desk,2,name,512,out needed) && name.ToString()=="Default","Use the unlocked Default desktop"); }
        finally { CloseDesktop(desk); }
        foreach(int key in new[]{1,2,0x10,0x11,0x12}) Check(GetAsyncKeyState(key)>=0,"Release mouse and modifier keys before testing");
        Check(Screen.PrimaryScreen.WorkingArea.Width>=1000 && Screen.PrimaryScreen.WorkingArea.Height>=700,"Desktop must provide at least 1000x700 physical pixels");
        Check(Marshal.SizeOf(typeof(Input))==28,"Incorrect x86 INPUT layout");
    }
    static async Task Run(int switches, int cycles, int durationMinutes)
    {
        await Until("Release mouse and modifier keys before testing",()=>new[]{1,2,0x10,0x11,0x12}.All(key=>GetAsyncKeyState(key)>=0),10000);
        Preflight(); GetCursorPos(out savedCursor); inputOwned=true; Fixture();
        var area=Screen.PrimaryScreen.WorkingArea;
        for(int cycle=0;cycle<cycles;cycle++)
        {
            phase="cycle "+(cycle+1)+" startup"; Log(phase);
            var positions=new[]{new Point(area.Left+40,area.Top+90),new Point(area.Left+400,area.Top+310),new Point(area.Left+60,area.Top+530)};
            // Retain surviving foreign HWNDs across app restarts, then replenish
            // the helper closed by the previous cycle.
            Forms.RemoveAll(f=>f.IsDisposed);
            while(Forms.Count<3) {
                var helper=new Form();
                helper.Menu=new MainMenu(new[]{new MenuItem("&File",new[]{new MenuItem("&Test")})});
                Forms.Add(helper);
            }
            for(int i=0;i<3;i++)
            {
                var f=Forms[i]; f.Text="WindowTabs E2E "+(char)('A'+i);
                f.StartPosition=FormStartPosition.Manual; f.WindowState=FormWindowState.Normal;
                f.Location=positions[i]; f.Size=new Size(560,150);
                f.BackColor=new[]{Color.LightBlue,Color.LightGreen,Color.LightPink}[i];
                f.Show(); ShowWindow(f.Handle,4); // Override the launcher's initial SW_HIDE.
            }
            app=Process.Start(new ProcessStartInfo(Path.Combine(Root,"WindowTabs.exe")) { WorkingDirectory=Root,UseShellExecute=false });
            await Until("Actual startup did not discover all three helper windows",()=>Strips().Count==3,15000);
            Check(!HasCrash(),"App logged an exception during startup");
            var handles=Helpers();
            phase="cycle "+(cycle+1)+" drag grouping";
            IntPtr source=Strips().Single(h=>GetWindow(h,4)==handles[0]);
            IntPtr target=Strips().Single(h=>GetWindow(h,4)==handles[1]);
            await Drag(source,target,1);
            await Until("First drag did not merge groups",()=>Strips().Count==2);
            await ReadOrder(target,2,handles.Take(2));
            source=Strips().Single(h=>GetWindow(h,4)==handles[2]);
            await Drag(source,target,2);
            await Until("Second drag did not merge groups",()=>Strips().Count==1);
            var order=await ReadOrder(target,3,handles);
            phase="Alt number menu masking";
            int selected=(order.IndexOf(GetForegroundWindow())+1)%order.Count;
            Chord(0x12,0x31+selected);
            await Activated(order[selected],target);
            await Task.Delay(150);
            uint process; uint thread=GetWindowThreadProcessId(order[selected],out process);
            var gui=new GuiThread { Size=Marshal.SizeOf(typeof(GuiThread)) };
            Check(GetGUIThreadInfo(thread,ref gui) && (gui.Flags & 4)==0,"Alt number activated the application menu");
            phase="Number leader";
            Chord(0x12,0x53);
            await Task.Delay(100);
            selected=(selected+1)%order.Count;
            Chord(0x31+selected);
            await Activated(order[selected],target);
            phase="cycle "+(cycle+1)+" frequent switching";
            var switching=Stopwatch.StartNew();
            int[] switchingBaseline=null;
            // Keep one application/group alive for the requested duration in
            // each cycle, then exercise normal exit and restart as usual.
            do
            {
                var baseline=await SwitchMany(target,order,switches);
                if(switchingBaseline==null) switchingBaseline=baseline;
                // A slow leak must not disappear by resetting the baseline on
                // every chunk in a prolonged run.
                CheckGrowth(switchingBaseline,Resources());
            }
            while(switching.Elapsed.TotalSeconds < durationMinutes*60.0/cycles);
            switchingSeconds+=switching.Elapsed.TotalSeconds;
            phase="cycle "+(cycle+1)+" maximized switching";
            Forms.Single(f=>!f.IsDisposed && f.Handle==GetForegroundWindow()).WindowState=FormWindowState.Maximized;
            await Until("Maximized group did not move the tabs inside the window",()=>Bounds(target).Top<=area.Top+5);
            await SwitchMany(target,order,30);
            Forms.Single(f=>!f.IsDisposed && f.Handle==GetForegroundWindow()).WindowState=FormWindowState.Normal;
            await Until("Restored group did not move back below the monitor edge",()=>Bounds(target).Top>area.Top+5);
            phase="cycle "+(cycle+1)+" close active and continue";
            IntPtr closing=GetForegroundWindow();
            Forms.Single(f=>!f.IsDisposed && f.Handle==closing).Close();
            order.Remove(closing);
            await Until("Closed window retained strip ownership",()=>!IsWindow(closing) && order.Contains(GetWindow(target,4)));
            await ReadOrder(target,2,order);
            await SwitchMany(target,order,30);
            phase="cycle "+(cycle+1)+" normal exit";
            await ExitNormally();
            var saved=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Root,"WindowTabsSettings.json")));
            Check(saved.ContainsKey("version") && !string.IsNullOrEmpty((string)saved["version"]),"Startup did not persist its settings version");
            Check((string)saved["alignment"]=="Left" && !(bool)saved["enableTabbingByDefault"],"Fixture settings were lost");
            Check(!HasCrash(),"Application logged an exception during the cycle");
            completed++; Log("PASS: cycle "+completed+"; original entry point, drag grouping, switching, close and normal exit");
            // The next cycle reuses the saved portable configuration and reacquires the real singleton.
        }
    }
    [STAThread] static int Main(string[] args)
    {
        if((args.Length!=3 && args.Length!=4) || args[0]!="--run") { Console.Error.WriteLine("Use Run-DesktopE2E.ps1 -Interactive"); return 2; }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        int code=1;
        using(var context=new ApplicationContext())
        using(var start=new Timer { Interval=50 })
        {
            start.Tick+=async (s,e)=> {
                start.Stop();
                try { await Run(int.Parse(args[1]),int.Parse(args[2]),args.Length==4?int.Parse(args[3]):0); code=0; }
                catch(Exception error) { Console.Error.WriteLine(error); File.WriteAllText(Path.Combine(Root,"failure.txt"),error.ToString()); Diagnose(); }
                finally
                {
                    if(inputOwned) { Inject(MouseInput(4),KeyInput(0x11,true),KeyInput(0x12,true)); Move(savedCursor); }
                    if(app!=null) { if(!app.HasExited) app.Kill(); app.WaitForExit(); app.Dispose(); }
                    foreach(var f in Forms) f.Dispose();
                    File.WriteAllText(Path.Combine(Root,"result.json"),Json.Serialize(new { status=code==0?"passed":"failed",phase,step,completedCycles=completed,verifiedSwitches=totalSwitches,switchingSeconds,measurements=Measurements,resourceSamples=ResourceSamples,recentActions=RecentActions.ToArray() }));
                    context.ExitThread();
                }
            };
            start.Start(); Application.Run(context);
        }
        return code;
    }
}
