// Public shell COM verifies membership; Windows shortcuts create/switch/close desktops.
// Anchors run from a different executable path, excluded by every fixture rule.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Automation;
using Microsoft.Win32;

static partial class DesktopE2E
{
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int current);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid id);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid id);
    }
    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    class VirtualDesktopManager { }
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    sealed class DesktopAnchor
    {
        public Process Process;
        public IntPtr Window;
        public Guid Id;
        public bool Removed;
    }
    static IVirtualDesktopManager shell;
    static DesktopAnchor original;
    static readonly List<DesktopAnchor> Anchors = new List<DesktopAnchor>();
    static readonly List<DesktopAnchor> TemporaryDesktops = new List<DesktopAnchor>();
    static readonly List<object> DesktopScenarios = new List<object>();
    static bool temporaryDesktopsRemoved, originalDesktopRestored;
    static Guid[] originalTopology;
    static int creationRequests;
    static int failedDesktopScenarios;
    static Guid[] DesktopIds()
    {
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops")) {
            var bytes=key==null?null:key.GetValue("VirtualDesktopIDs") as byte[];
            Check(bytes!=null && bytes.Length>0 && bytes.Length%16==0,"Cannot snapshot shell desktop topology before testing");
            return Enumerable.Range(0,bytes.Length/16).Select(i=>new Guid(bytes.Skip(i*16).Take(16).ToArray())).ToArray();
        }
    }
    static Guid CurrentDesktopId()
    {
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops")) {
            var bytes=key==null?null:key.GetValue("CurrentVirtualDesktop") as byte[];
            Check(bytes!=null && bytes.Length==16,"Shell current-desktop diagnostic is unavailable");
            return new Guid(bytes);
        }
    }
    static bool TryNativeDesktop(IntPtr window, out Guid id) { return shell.GetWindowDesktopId(window,out id)==0 && id!=Guid.Empty; }
    static Guid NativeDesktop(IntPtr window)
    {
        Guid id; int hr=shell.GetWindowDesktopId(window,out id);
        Check(hr==0 && id!=Guid.Empty && id!=new Guid("BB64D5B7-4DE3-4AB2-A87C-DB7601AEA7DC") &&
            id!=new Guid("C2DDEA68-66F2-4CF9-8264-1BFD00FBBBAC"),"Shell did not report a concrete desktop for "+window+" (HRESULT="+hr+")");
        return id;
    }
    static bool OnCurrent(IntPtr window)
    {
        int current;
        Check(shell.IsWindowOnCurrentVirtualDesktop(window,out current)==0,"Shell current-desktop query failed for "+window);
        return current!=0;
    }
    static bool Cloaked(IntPtr window)
    {
        int value;
        Check(DwmGetWindowAttribute(window,14,out value,4)==0,"DWM cloak query failed for "+window);
        return value!=0;
    }
    static void RunDesktopAnchor()
    {
        Application.EnableVisualStyles();
        var area=Screen.PrimaryScreen.WorkingArea;
        using(var anchor=new Form { Text="WindowTabs E2E desktop anchor",StartPosition=FormStartPosition.Manual,
            Location=new Point(area.Right-260,area.Bottom-130),Size=new Size(240,100) })
        {
            anchor.Shown+=(s,e)=>ShowWindow(anchor.Handle,4);
            Application.Run(anchor);
        }
    }
    static async Task<DesktopAnchor> StartAnchor()
    {
        var anchor=new DesktopAnchor();
        anchor.Process=Process.Start(new ProcessStartInfo(Path.Combine(Root,"DesktopAnchor.exe"),"--desktop-anchor") {
            WorkingDirectory=Root,UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden });
        Anchors.Add(anchor);
        await Until("Desktop anchor did not start",()=> {
            Check(!anchor.Process.HasExited,"Desktop anchor exited unexpectedly");
            anchor.Window=Windows(anchor.Process.Id).FirstOrDefault(IsWindowVisible);
            return anchor.Window!=IntPtr.Zero;
        });
        await Until("Desktop anchor is not on the active desktop",()=>OnCurrent(anchor.Window) && !Cloaked(anchor.Window));
        await Until("Explorer did not register the anchor's desktop",()=>TryNativeDesktop(anchor.Window,out anchor.Id),10000);
        anchor.Id=NativeDesktop(anchor.Window);
        return anchor;
    }
    static void DesktopChord(int key)
    {
        Check(key==0x44 || key==0x25 || key==0x27 || key==0x73,"Unapproved desktop shortcut");
        Remember("virtual desktop shortcut",new {key});
        // Explorer can take focus during desktop animations. Only these four
        // global shell commands bypass ordinary fixture chords' PID guard.
        int[] keys={0x5B,0x11,key};
        Inject(keys.Select(k=>KeyInput(k,false)).Concat(keys.Reverse().Select(k=>KeyInput(k,true))).ToArray());
    }
    static async Task<DesktopAnchor> CreateDesktop(DesktopAnchor previous)
    {
        Check(OnCurrent(previous.Window) && NativeDesktop(previous.Window)==previous.Id,"Desktop changed before creation");
        var before=DesktopIds();
        creationRequests++;
        DesktopChord(0x44);
        await Until("Win+Ctrl+D did not create exactly one desktop",()=>DesktopIds().Except(before).Count()==1,10000);
        // Record creation before any taskbar registration can fail or be cancelled.
        var created=new DesktopAnchor {Id=DesktopIds().Except(before).Single()};
        TemporaryDesktops.Add(created);
        await Until("Win+Ctrl+D did not activate the created desktop",()=>CurrentDesktopId()==created.Id && !OnCurrent(previous.Window),10000);
        await Task.Delay(500);
        var anchor=await StartAnchor();
        Check(anchor.Id==created.Id,"Anchor is not on the recorded new desktop");
        created.Process=anchor.Process; created.Window=anchor.Window;
        Log("Created temporary desktop "+created.Id);
        return created;
    }
    static async Task SwitchDesktop(DesktopAnchor target, int direction)
    {
        Check(!target.Removed && DesktopIds().Contains(target.Id),"Target desktop no longer exists");
        for(int i=0;CurrentDesktopId()!=target.Id;i++)
        {
            Check(i<64,"Could not reach the recorded desktop with at most 64 directional switches");
            DesktopChord(direction); await Task.Delay(500);
        }
        if(target.Window!=IntPtr.Zero) await Until("Target desktop is still cloaked after switching",()=>OnCurrent(target.Window) && !Cloaked(target.Window) && NativeDesktop(target.Window)==target.Id,10000);
        Remember("desktop reached",new {id=target.Id});
    }
    static List<IntPtr> FixtureStrips()
    {
        var helpers=Helpers();
        return Windows(app.Id).Where(h=>helpers.Contains(GetWindow(h,4)) &&
            (GetWindowLong(h,-20)&0x80000)!=0 && (GetWindowLong(h,-20)&0x20)==0).ToList();
    }
    static IntPtr StripFor(List<IntPtr> members) { return FixtureStrips().Single(h=>members.Contains(GetWindow(h,4))); }
    static IntPtr RootOwner(IntPtr window)
    {
        var seen=new HashSet<IntPtr>();
        while(GetWindow(window,4)!=IntPtr.Zero) {
            Check(seen.Add(window),"Cyclic owner chain"); window=GetWindow(window,4);
        }
        return window;
    }
    static IntPtr ProxyRoot(List<IntPtr> members, bool combined, Guid desktop)
    {
        if(!combined) {
            Check(members.All(h=>GetWindow(h,4)==IntPtr.Zero),"Separate taskbar mode adopted an application owner");
            Check(members.All(h=>NativeDesktop(h)==desktop),"Separate window moved to another desktop");
            return IntPtr.Zero;
        }
        var previews=members.Select(h=>GetWindow(h,4)).ToList();
        Check(previews.All(h=>h!=IntPtr.Zero) && previews.Distinct().Count()==members.Count,"Combined tabs do not have distinct preview owners");
        var roots=members.Select(RootOwner).Distinct().ToList();
        Check(roots.Count==1 && !members.Contains(roots[0]),"Combined group does not share one taskbar root");
        var appWindows=Windows(app.Id);
        Check(previews.Concat(roots).All(appWindows.Contains),"Combined owner does not belong to the staged app");
        Check(previews.All(h=>GetWindow(h,4)==roots[0]),"A preview is not directly owned by its taskbar root");
        Check(NativeDesktop(roots[0])==desktop,"Combined taskbar root moved its group to the wrong desktop");
        return roots[0];
    }
    static bool OwnersReady(List<IntPtr> members, bool combined)
    {
        return members.All(h=>combined ? GetWindow(h,4)!=IntPtr.Zero : GetWindow(h,4)==IntPtr.Zero);
    }
    static void CheckVisibility(List<IntPtr> current, List<IntPtr> other, IntPtr currentStrip, IntPtr otherStrip)
    {
        Check(current.All(h=>OnCurrent(RootOwner(h)) && IsWindowVisible(h) && !Cloaked(h)),"Current-desktop fixture is hidden or misplaced");
        Check(other.All(h=>!OnCurrent(RootOwner(h)) && (!IsWindowVisible(h) || Cloaked(h))),"Other-desktop fixture leaked onto the current desktop");
        Check(IsWindowVisible(currentStrip) && !Cloaked(currentStrip),"Current-desktop strip is hidden");
        Check(!IsWindowVisible(otherStrip) || Cloaked(otherStrip),"Other-desktop strip leaked onto the current desktop");
        Check(FixtureStrips().Count==2,"Desktop switching split or merged the two groups");
    }
    static async Task CheckDesktopGroup(List<IntPtr> current, List<IntPtr> other, IntPtr strip, IntPtr otherStrip)
    {
        await Until("Desktop group visibility did not settle",()=>current.All(h=>!Cloaked(h)) && other.All(h=>!IsWindowVisible(h) || Cloaked(h)));
        CheckVisibility(current,other,strip,otherStrip);
        var selected=GetWindow(strip,4);
        Forms.Single(f=>!f.IsDisposed && f.Handle==selected).Activate();
        await Activated(selected,strip);
        var order=await ReadOrder(strip,current.Count,current);
        for(int i=0;i<12;i++) {
            int index=i%order.Count;
            Chord(0x12,0x31+index); await Activated(order[index],strip); totalSwitches++;
            CheckVisibility(current,other,strip,otherStrip);
        }
    }
    static async Task StartDesktopApp(List<IntPtr> a, List<IntPtr> b, bool combined)
    {
        app=Process.Start(new ProcessStartInfo(Path.Combine(Root,"WindowTabs.exe")) { WorkingDirectory=Root,UseShellExecute=false });
        await Until("Startup did not create exactly one group per desktop",()=>FixtureStrips().Count==2 &&
            FixtureStrips().Count(h=>a.Contains(GetWindow(h,4)))==1 && FixtureStrips().Count(h=>b.Contains(GetWindow(h,4)))==1,15000);
        await Until("Taskbar ownership did not settle",()=>OwnersReady(a,combined) && OwnersReady(b,combined) &&
            ConcreteRoots(a,combined) && ConcreteRoots(b,combined),15000);
        await Until("Startup followers did not finish native placement",()=>SameFrame(a) && SameFrame(b),15000);
        Check(!HasCrash(),"App logged a startup exception");
    }
    static bool ConcreteRoots(List<IntPtr> members, bool combined)
    {
        if(!combined) return true;
        var roots=members.Select(RootOwner).Distinct().ToArray();
        Guid id;
        return roots.Length==1 && !members.Contains(roots[0]) && TryNativeDesktop(roots[0],out id);
    }
    static bool SameFrame(List<IntPtr> members)
    {
        var first=Bounds(members[0]);
        return members.All(h=> {var r=Bounds(h);return r.Left==first.Left && r.Top==first.Top && r.Right==first.Right && r.Bottom==first.Bottom;});
    }
    static bool CheckReleased(List<IntPtr> a, List<IntPtr> b, Guid desktopA, Guid desktopB)
    {
        var snapshot=a.Select(h=>new {hwnd=h.ToInt64(),owner=GetWindow(h,4).ToInt64(),expected=desktopA,actual=NativeDesktop(h)})
            .Concat(b.Select(h=>new {hwnd=h.ToInt64(),owner=GetWindow(h,4).ToInt64(),expected=desktopB,actual=NativeDesktop(h)})).ToArray();
        File.AppendAllText(Path.Combine(Root,"released-windows.jsonl"),Json.Serialize(new {phase,step,windows=snapshot})+Environment.NewLine);
        bool correct=snapshot.All(w=>w.owner==0 && w.actual==w.expected);
        if(!correct) Log("FAIL: Exit changed desktop membership or left an owner: "+Json.Serialize(snapshot));
        return correct;
    }
    static string[] TaskbarButtonNames()
    {
        var bar=AutomationElement.RootElement.FindFirst(TreeScope.Children,
            new PropertyCondition(AutomationElement.ClassNameProperty,"Shell_TrayWnd"));
        Check(bar!=null,"Taskbar automation surface is unavailable");
        return bar.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button))
            .Cast<AutomationElement>().Select(e=>e.Current.Name)
            .Where(n=>n.StartsWith("DesktopE2E",StringComparison.Ordinal) || n.StartsWith("WindowTabs E2E",StringComparison.Ordinal))
            .OrderBy(n=>n,StringComparer.Ordinal).ToArray();
    }
    static async Task<string[]> CaptureTaskbarButtons()
    {
        await Until("Fixture taskbar buttons are missing before grouping",()=>TaskbarButtonNames().Length>0);
        await Task.Delay(300);
        return TaskbarButtonNames();
    }
    static async Task CheckReleasedTaskbar(DesktopAnchor deskA, DesktopAnchor deskB, string[] expectedA, string[] expectedB, Dictionary<IntPtr,int> styles)
    {
        foreach(var item in new[]{new {desktop=deskA,names=expectedA},new {desktop=deskB,names=expectedB}}) {
            var topology=DesktopIds();
            int direction=Array.IndexOf(topology,CurrentDesktopId())<Array.IndexOf(topology,item.desktop.Id)?0x27:0x25;
            await SwitchDesktop(item.desktop,direction);
            await Until("Exit did not restore every fixture taskbar button on "+item.desktop.Id,
                ()=>TaskbarButtonNames().SequenceEqual(item.names));
            Check(styles.All(w=>(GetWindowLong(w.Key,-20)&0x40000)==(w.Value&0x40000)),"Exit changed an application's original APPWINDOW style");
            File.AppendAllText(Path.Combine(Root,"taskbar-restoration.jsonl"),Json.Serialize(new {phase,step,desktop=item.desktop.Id,buttons=TaskbarButtonNames(),stylesRestored=true})+Environment.NewLine);
        }
    }
    static async Task RestoreFixtureDesktops(List<IntPtr> a, List<IntPtr> b, Guid desktopA, Guid desktopB)
    {
        // Retain the failure, then reset only our own windows to run the other
        // matrix cases. The failed exit is not retried.
        foreach(var item in a.Select(h=>new {hwnd=h,id=desktopA}).Concat(b.Select(h=>new {hwnd=h,id=desktopB}))) {
            uint pid; GetWindowThreadProcessId(item.hwnd,out pid);
            Check(pid==Process.GetCurrentProcess().Id && GetWindow(item.hwnd,4)==IntPtr.Zero,"Refusing to reset a foreign or still-owned fixture");
            if(NativeDesktop(item.hwnd)!=item.id) {
                var target=item.id;
                Check(shell.MoveWindowToDesktop(item.hwnd,ref target)==0,"Could not reset fixture desktop after recording a failed exit");
                Remember("reset failed fixture",new {hwnd=item.hwnd.ToInt64(),desktop=item.id});
            }
        }
        await Until("Fixture desktop reset did not settle",()=>a.All(h=>NativeDesktop(h)==desktopA) && b.All(h=>NativeDesktop(h)==desktopB));
    }
    static async Task RunVirtualDesktops()
    {
        Preflight(); GetCursorPos(out savedCursor); inputOwned=true;
        shell=(IVirtualDesktopManager)new VirtualDesktopManager();
        originalTopology=DesktopIds();
        original=await StartAnchor();
        Check(CurrentDesktopId()==original.Id,"Shell diagnostic disagrees with public COM before desktop creation");
        File.WriteAllText(Path.Combine(Root,"desktop-topology.json"),Json.Serialize(new {original=original.Id,desktops=originalTopology}));
        var deskA=await CreateDesktop(original);
        var area=Screen.PrimaryScreen.WorkingArea;
        var a=new[]{NewHelper(new Point(area.Left+60,area.Top+180)),NewHelper(new Point(area.Left+60,area.Top+420),true)}.Select(f=>f.Handle).ToList();
        await Until("A fixtures were not registered on the first temporary desktop",()=>a.All(h=>{Guid id;return TryNativeDesktop(h,out id) && id==deskA.Id;}),10000);
        var taskbarA=await CaptureTaskbarButtons();
        var deskB=await CreateDesktop(deskA);
        var b=new[]{NewHelper(new Point(area.Left+60,area.Top+180)),NewHelper(new Point(area.Left+60,area.Top+420),true)}.Select(f=>f.Handle).ToList();
        await Until("B fixtures were not registered on the second temporary desktop",()=>b.All(h=>{Guid id;return TryNativeDesktop(h,out id) && id==deskB.Id;}),10000);
        var taskbarB=await CaptureTaskbarButtons();
        var fixtureStyles=a.Concat(b).ToDictionary(h=>h,h=>GetWindowLong(h,-20));
        foreach(string grouping in new[]{"auto","rule"}) foreach(bool combined in new[]{false,true})
        {
            phase=grouping+" grouping / combined icons "+combined; step="startup"; Log(phase);
            await SwitchDesktop(deskA,0x25); Fixture(combined,grouping);
            await StartDesktopApp(a,b,combined);
            var stripA=StripFor(a); var stripB=StripFor(b);
            var proxyA=ProxyRoot(a,combined,deskA.Id); var proxyB=ProxyRoot(b,combined,deskB.Id);
            Check(!combined || proxyA!=proxyB,"Groups on different desktops share a taskbar root");
            await CheckDesktopGroup(a,b,stripA,stripB);
            step="switch to B"; await SwitchDesktop(deskB,0x27);
            await CheckDesktopGroup(b,a,stripB,stripA);
            step="new window on B";
            var joining=NewHelper(new Point(area.Left+400,area.Top+280)); var joined=joining.Handle;
            await Until("New B window did not join its own desktop's group",()=>GetWindow(stripB,4)==joined && FixtureStrips().Count==2 && OwnersReady(new List<IntPtr>{joined},combined),15000);
            var expanded=b.Concat(new[]{joined}).ToList();
            Check(ProxyRoot(expanded,combined,deskB.Id)==proxyB,"New B window adopted another taskbar root");
            await CheckDesktopGroup(expanded,a,stripB,stripA);
            step="maximize and restore on B";
            var selected=GetForegroundWindow(); var active=Forms.Single(f=>!f.IsDisposed && f.Handle==selected);
            active.WindowState=FormWindowState.Maximized;
            await Until("Maximized B group did not settle",()=>expanded.All(h=>Forms.Single(f=>!f.IsDisposed && f.Handle==h).WindowState==FormWindowState.Maximized) && Bounds(stripB).Top<=area.Top+5);
            await Task.Delay(300); await Activated(selected,stripB);
            active.WindowState=FormWindowState.Normal;
            await Until("Restored B group did not settle",()=>expanded.All(h=>Forms.Single(f=>!f.IsDisposed && f.Handle==h).WindowState==FormWindowState.Normal) && Bounds(stripB).Top>area.Top+5);
            await Task.Delay(300); await Activated(selected,stripB);
            await CheckDesktopGroup(expanded,a,stripB,stripA);
            step="close joined B window"; joining.Close();
            await Until("Closed B window retained group ownership",()=>!IsWindow(joined) && b.Contains(GetWindow(stripB,4)));
            await CheckDesktopGroup(b,a,stripB,stripA);
            await SwitchDesktop(deskA,0x25); await CheckDesktopGroup(a,b,stripA,stripB);
            Check(ProxyRoot(a,combined,deskA.Id)==proxyA && ProxyRoot(b,combined,deskB.Id)==proxyB,"Desktop switching replaced or moved a taskbar root");
            step="normal exit and restart"; await ExitNormally();
            bool firstExit=CheckReleased(a,b,deskA.Id,deskB.Id);
            if(!firstExit) await RestoreFixtureDesktops(a,b,deskA.Id,deskB.Id);
            await CheckReleasedTaskbar(deskA,deskB,taskbarA,taskbarB,fixtureStyles);
            await StartDesktopApp(a,b,combined); stripA=StripFor(a); stripB=StripFor(b);
            ProxyRoot(a,combined,deskA.Id); ProxyRoot(b,combined,deskB.Id);
            await SwitchDesktop(deskB,0x27); await CheckDesktopGroup(b,a,stripB,stripA);
            await ExitNormally(); bool secondExit=CheckReleased(a,b,deskA.Id,deskB.Id); Check(!HasCrash(),"App logged an exception");
            if(!secondExit) await RestoreFixtureDesktops(a,b,deskA.Id,deskB.Id);
            await CheckReleasedTaskbar(deskA,deskB,taskbarA,taskbarB,fixtureStyles);
            bool passed=firstExit && secondExit;
            if(!passed) failedDesktopScenarios++;
            DesktopScenarios.Add(new {grouping,combinedIcons=combined,status=passed?"passed":"failed",firstExitPreservedDesktop=firstExit,secondExitPreservedDesktop=secondExit,taskbarRestored=true,stylesRestored=true,desktopA=deskA.Id,desktopB=deskB.Id});
            Log((passed?"PASS: ":"FAIL: ")+phase+"; desktop, taskbar buttons and original styles preserved on both exits="+passed);
        }
        Check(failedDesktopScenarios==0,failedDesktopScenarios+" virtual-desktop scenarios failed desktop preservation on Exit; see released-windows.jsonl");
    }
    static async Task CleanupVirtualDesktops()
    {
        // Keep anchors alive until removal is observed, even after a product failure.
        try
        {
            // Cancellation can arrive immediately after Win+Ctrl+D, before the
            // creation wait records its result. Recover only that pending request.
            if(originalTopology!=null && creationRequests>TemporaryDesktops.Count) {
                await Until("Pending desktop creation did not settle before cleanup",()=>
                    DesktopIds().Except(originalTopology).Except(TemporaryDesktops.Select(d=>d.Id)).Count()==creationRequests-TemporaryDesktops.Count,10000);
                var pending=DesktopIds().Except(originalTopology).Except(TemporaryDesktops.Select(d=>d.Id)).ToArray();
                Check(pending.Length<=creationRequests-TemporaryDesktops.Count,"Unexpected desktop topology change during cleanup");
                foreach(var id in pending) TemporaryDesktops.Add(new DesktopAnchor {Id=id});
            }
            foreach(var desktop in TemporaryDesktops.AsEnumerable().Reverse())
            {
                await SwitchDesktop(desktop,0x27);
                Check(CurrentDesktopId()==desktop.Id && !originalTopology.Contains(desktop.Id),"Refusing to close an unverified desktop");
                if(desktop.Window!=IntPtr.Zero) Check(OnCurrent(desktop.Window) && NativeDesktop(desktop.Window)==desktop.Id,"Desktop anchor changed before removal");
                DesktopChord(0x73);
                await Until("Temporary desktop was not removed",()=>!DesktopIds().Contains(desktop.Id),10000);
                desktop.Removed=true; Log("Removed temporary desktop "+desktop.Id);
            }
            temporaryDesktopsRemoved=TemporaryDesktops.Count==2 && TemporaryDesktops.All(d=>d.Removed) && DesktopIds().SequenceEqual(originalTopology);
            if(original!=null) {
                await SwitchDesktop(original,0x25);
                originalDesktopRestored=OnCurrent(original.Window) && NativeDesktop(original.Window)==original.Id;
                Check(originalDesktopRestored,"Original desktop was not restored");
            }
            File.WriteAllText(Path.Combine(Root,"desktop-cleanup.json"),Json.Serialize(new {
                created=TemporaryDesktops.Select(d=>d.Id).ToArray(),removed=TemporaryDesktops.All(d=>d.Removed),
                desktops=originalTopology==null?new Guid[0]:DesktopIds(),originalDesktopRestored}));
        }
        finally
        {
            foreach(var anchor in Anchors) {
                if(!anchor.Process.HasExited) {
                    PostMessage(anchor.Window,0x10,IntPtr.Zero,IntPtr.Zero);
                    if(!anchor.Process.WaitForExit(1000)) anchor.Process.Kill();
                }
                anchor.Process.Dispose();
            }
            if(shell!=null) { Marshal.ReleaseComObject(shell); shell=null; }
        }
    }
}
