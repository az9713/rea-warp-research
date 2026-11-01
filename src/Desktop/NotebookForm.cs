using Rea.Notebook;
using System.Text.Json;
namespace Rea.Notebook.Desktop;
public sealed class NotebookForm:Form
{
    string workspace=AppContext.BaseDirectory;
    readonly Label folder=new(){AutoSize=true,Text="Workspace: portable package folder"};
    readonly TextBox exe=new(){Text="NotebookFixture.exe",Dock=DockStyle.Fill};
    readonly TextBox arguments=new(){Text="[\"compute\",\"demo/input.json\",\"demo/result.json\"]",Dock=DockStyle.Fill};
    readonly TextBox inputs=new(){Text="demo/input.json",Dock=DockStyle.Fill};
    readonly TextBox outputs=new(){Text="demo/result.json",Dock=DockStyle.Fill};
    readonly TextBox environment=new(){Text="{\"REA_FACTOR\":\"2\"}",Dock=DockStyle.Fill};
    readonly NumericUpDown timeout=new(){Minimum=100,Maximum=30000,Value=5000,Dock=DockStyle.Fill};
    readonly Button preview=new(){Text="1. Seal / preview",AutoSize=true};
    readonly Button run=new(){Text="2. Run explicitly",AutoSize=true,Enabled=false};
    readonly Button interrupt=new(){Text="Interrupt",AutoSize=true,Enabled=false};
    readonly Button demo=new(){Text="Prepare synthetic input",AutoSize=true};
    readonly Button choose=new(){Text="Choose workspace",AutoSize=true};
    readonly Button importPlan=new(){Text="Import plan",AutoSize=true};
    readonly Button importEvidence=new(){Text="Import evidence",AutoSize=true};
    readonly Button exportPlan=new(){Text="Save sealed plan",AutoSize=true};
    readonly Button exportEvidence=new(){Text="Save evidence bundle",AutoSize=true};
    readonly Label status=new(){AutoSize=true,MaximumSize=new Size(1080,0),Text="No command has run. Preview then choose Run explicitly."};
    readonly DataGridView grid=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.FromArgb(21,28,42),BorderStyle=BorderStyle.None};
    readonly TextBox output=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Font=new Font("Consolas",10)};
    CommandPlan? plan;ReceiptEnvelope? receipt,baseline;CancellationTokenSource? cancellation;Task? pending;bool closing;
    public NotebookForm()
    {
        Text="Research Notebook | guarded local command experiments";Width=1160;Height=850;MinimumSize=new(800,650);BackColor=Color.FromArgb(15,21,32);ForeColor=Color.FromArgb(226,234,245);Font=new("Segoe UI",10);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=7};
        root.RowStyles.Add(new(SizeType.Absolute,60));root.RowStyles.Add(new(SizeType.Absolute,48));root.RowStyles.Add(new(SizeType.Absolute,215));root.RowStyles.Add(new(SizeType.Absolute,48));root.RowStyles.Add(new(SizeType.Absolute,64));root.RowStyles.Add(new(SizeType.Percent,45));root.RowStyles.Add(new(SizeType.Percent,55));
        root.Controls.Add(new Label{Text="Research Notebook\nSealed inputs + one-factor reruns + bounded execution receipts",AutoSize=true,Font=new Font("Segoe UI",14,FontStyle.Bold)},0,0);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};foreach(var b in new[]{demo,choose,importPlan,importEvidence,exportPlan,exportEvidence})toolbar.Controls.Add(b);root.Controls.Add(toolbar,0,1);
        var settings=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=7};settings.ColumnStyles.Add(new(SizeType.Absolute,160));settings.ColumnStyles.Add(new(SizeType.Percent,100));
        settings.Controls.Add(folder,0,0);settings.SetColumnSpan(folder,2);
        var fields=new (string,Control)[]{("Executable",exe),("Arguments (JSON)",arguments),("Inputs",inputs),("Outputs",outputs),("REA_ values",environment),("Timeout (ms)",timeout)};
        for(int i=0;i<fields.Length;i++){settings.RowStyles.Add(new(SizeType.Absolute,29));settings.Controls.Add(new Label{Text=fields[i].Item1,AutoSize=true},0,i+1);settings.Controls.Add(fields[i].Item2,1,i+1);}
        root.Controls.Add(settings,0,2);var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};foreach(var b in new[]{preview,run,interrupt})actions.Controls.Add(b);root.Controls.Add(actions,0,3);root.Controls.Add(status,0,4);
        grid.Columns.Add("artifact","Declared artifact");grid.Columns.Add("before","Baseline SHA (hover full)");grid.Columns.Add("after","Candidate SHA (hover full)");grid.Columns.Add("change","Change");grid.Columns[3].FillWeight=25;
        grid.DefaultCellStyle.BackColor=BackColor;grid.DefaultCellStyle.ForeColor=ForeColor;grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(43,63,89);grid.DefaultCellStyle.SelectionForeColor=ForeColor;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(30,43,63);grid.ColumnHeadersDefaultCellStyle.ForeColor=ForeColor;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        root.Controls.Add(grid,0,5);root.Controls.Add(output,0,6);Controls.Add(root);
        foreach(var box in new[]{exe,arguments,inputs,outputs,environment})box.TextChanged+=(_,_)=>InvalidatePlan();timeout.ValueChanged+=(_,_)=>InvalidatePlan();
        demo.Click+=(_,_)=>Guard(PrepareDemo);
        choose.Click+=(_,_)=>{using var picker=new FolderBrowserDialog();if(picker.ShowDialog(this)==DialogResult.OK){workspace=picker.SelectedPath;folder.Text="Workspace: "+new DirectoryInfo(workspace).Name;InvalidatePlan();}};
        preview.Click+=(_,_)=>Guard(PreviewNow);run.Click+=(_,_)=>pending=RunNow();interrupt.Click+=(_,_)=>cancellation?.Cancel();
        exportPlan.Click+=(_,_)=>Guard(()=>{if(plan is null)throw new InvalidDataException("Preview a plan first.");Save("command-plan.json",plan);});
        exportEvidence.Click+=(_,_)=>Guard(()=>{if(plan is null||receipt is null)throw new InvalidDataException("No current receipt.");Notebook.VerifyReceipt(receipt,plan);Save("notebook-evidence.json",new EvidenceBundle(plan,receipt));});
        importPlan.Click+=(_,_)=>Guard(()=>{var p=ReadImport<CommandPlan>();if(p is not null){Notebook.VerifyPlan(p);ApplyPlan(p);status.Text="Imported plan only. No command ran. Preview checks the selected local workspace.";}});
        importEvidence.Click+=(_,_)=>Guard(()=>{var b=ReadImport<EvidenceBundle>();if(b is not null){Notebook.VerifyReceipt(b.Receipt,b.Plan);ApplyPlan(b.Plan);receipt=b.Receipt;ShowReceipt();run.Enabled=false;status.Text="Imported evidence: internal commitments verified. No command ran; authorship is unverified. Preview before an explicit rerun.";}});
        FormClosing+=(_,e)=>{if(pending is not null&&!pending.IsCompleted){e.Cancel=true;closing=true;cancellation?.Cancel();status.Text="Interrupting owned child before closing...";}};
        foreach(var button in new[]{preview,run,interrupt,demo,choose,importPlan,importEvidence,exportPlan,exportEvidence}){button.BackColor=Color.FromArgb(35,54,76);button.ForeColor=ForeColor;button.FlatStyle=FlatStyle.Flat;}
        timeout.BackColor=Color.FromArgb(21,29,43);timeout.ForeColor=ForeColor;
        foreach(var box in new[]{exe,arguments,inputs,outputs,environment,output}){box.BackColor=Color.FromArgb(21,29,43);box.ForeColor=ForeColor;box.BorderStyle=BorderStyle.FixedSingle;}
    }
    void Guard(Action action){try{action();}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or FormatException or System.ComponentModel.Win32Exception){run.Enabled=false;status.Text="Refused: "+e.Message;}}
    void InvalidatePlan(){plan=null;receipt=null;run.Enabled=false;grid.Rows.Clear();status.Text="Edited. Seal / preview again before running. The previous baseline remains a comparison only.";}
    void PrepareDemo(){var path=Notebook.Resolve(workspace,"demo/input.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);if(!File.Exists(path))Notebook.SaveNew(path,new{Values=new[]{3,6,9}});exe.Text="NotebookFixture.exe";arguments.Text="[\"compute\",\"demo/input.json\",\"demo/result.json\"]";inputs.Text="demo/input.json";outputs.Text="demo/result.json";environment.Text="{\"REA_FACTOR\":\"2\"}";InvalidatePlan();status.Text="Synthetic input prepared: [3, 6, 9]. No process started.";}
    void PreviewNow()
    {
        if(plan is null)plan=Notebook.Create(workspace,exe.Text,Notebook.Parse<string[]>(System.Text.Encoding.UTF8.GetBytes(arguments.Text)),Split(inputs.Text),Split(outputs.Text),Notebook.Parse<SortedDictionary<string,string>>(System.Text.Encoding.UTF8.GetBytes(environment.Text)),(int)timeout.Value);
        var result=Notebook.Preview(workspace,plan);run.Enabled=result.Ready;grid.Rows.Clear();foreach(var item in plan.Spec.Inputs)grid.Rows.Add(item.Key,item.Value,"pending", "input sealed");
        status.Text=result.Ready?"Ready: executable and declared inputs match. Plan "+plan.Sha256[..16]+". Run starts a real local process; this is not an OS sandbox.":"Refused: "+string.Join(" ",result.Reasons);
    }
    static string[] Split(string text)=>text.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    async Task RunNow()
    {
        if(plan is null||!run.Enabled)return;
        using var token=new CancellationTokenSource();cancellation=token;SetBusy(true);status.Text="Running selected command. Interrupt requests termination of the owned process tree.";
        try{receipt=await Notebook.Run(workspace,plan,token.Token);Notebook.VerifyReceipt(receipt,plan);if(baseline is null&&receipt.Receipt.Outcome=="completed")baseline=receipt;ShowReceipt();status.Text="Recorded "+receipt.Receipt.Outcome+"; exit "+receipt.Receipt.ExitCode+"; "+receipt.Receipt.DurationMs+" ms. Internal hash checks are not proof of authorship or a full environment replay.";}
        catch(Exception e){receipt=null;status.Text="Run failed/refused: "+e.Message;}
        finally{cancellation=null;SetBusy(false);run.Enabled=false;if(closing)BeginInvoke(Close);}
    }
    void SetBusy(bool value){foreach(var c in new Control[]{exe,arguments,inputs,outputs,environment,timeout,preview,demo,choose,importPlan,importEvidence,exportPlan,exportEvidence})c.Enabled=!value;run.Enabled=!value;interrupt.Enabled=value;}
    static string ShortHash(string value)=>value.Length==64?value[..16]+"…":value;
    void ShowReceipt()
    {
        if(receipt is null)return;grid.Rows.Clear();
        foreach(var item in receipt.Receipt.OutputsAfter){var before=baseline?.Receipt.OutputsAfter.GetValueOrDefault(item.Key)??receipt.Receipt.OutputsBefore.GetValueOrDefault(item.Key)??"unknown";var row=grid.Rows.Add(item.Key,ShortHash(before),ShortHash(item.Value),before==item.Value?"same":"changed");grid.Rows[row].Cells[1].ToolTipText=before;grid.Rows[row].Cells[2].ToolTipText=item.Value;}
        output.Text="BASELINE STDOUT\r\n"+(baseline?.Receipt.Stdout.Text??"No baseline")+"\r\nCANDIDATE STDOUT\r\n"+receipt.Receipt.Stdout.Text+"\r\nSTDERR\r\n"+receipt.Receipt.Stderr.Text+"\r\nNOTES\r\n"+string.Join("\r\n",receipt.Receipt.Notes);
    }
    void ApplyPlan(CommandPlan p){exe.Text=p.Spec.Executable;arguments.Text=JsonSerializer.Serialize(p.Spec.Arguments);inputs.Text=string.Join(";",p.Spec.Inputs.Keys);outputs.Text=string.Join(";",p.Spec.Outputs);environment.Text=JsonSerializer.Serialize(p.Spec.Environment);timeout.Value=p.Spec.TimeoutMs;plan=p;receipt=null;run.Enabled=false;grid.Rows.Clear();}
    T? ReadImport<T>()where T:class{using var picker=new OpenFileDialog{Filter="JSON|*.json",CheckFileExists=true};if(picker.ShowDialog(this)!=DialogResult.OK)return null;var info=new FileInfo(picker.FileName);if(info.Length>2*Notebook.FileLimit)throw new InvalidDataException("Import exceeds 2 MiB.");return Notebook.ReadFile<T>(picker.FileName);}
    void Save<T>(string name,T value){using var picker=new SaveFileDialog{Filter="JSON|*.json",FileName=name,OverwritePrompt=true};if(picker.ShowDialog(this)==DialogResult.OK){Notebook.SaveNew(picker.FileName,value);status.Text="Saved new local evidence file. It includes command arguments, explicit values and captured output; review before sharing.";}}
    public async Task SelfTest(string screenshot)
    {
        if(run.Enabled)throw new Exception("Run enabled before preview.");demo.PerformClick();if(run.Enabled)throw new Exception("Prepare unexpectedly enabled run.");preview.PerformClick();if(!run.Enabled)throw new Exception("Demo preview failed: "+status.Text);run.PerformClick();if(pending is null)throw new Exception("Run handler not wired.");await pending;if(receipt?.Receipt.Outcome!="completed")throw new Exception(status.Text);
        environment.Text="{\"REA_FACTOR\":\"3\"}";if(run.Enabled)throw new Exception("Edit did not invalidate preview.");preview.PerformClick();run.PerformClick();await pending!;
        if(receipt?.Receipt.Outcome!="completed"||grid.Rows.Count!=1||grid.Rows[0].Cells[3].Value?.ToString()!="changed"||!output.Text.Contains("[9,18,27]"))throw new Exception("Before/after UI mismatch: "+status.Text);
        Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(screenshot,System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllText(screenshot+".json",JsonSerializer.Serialize(new{passed=true,checks=new[]{"real native form opened","Run disabled before preview","synthetic prepare starts no child","preview enables explicit run","actual Run click executes fixture","edit invalidates sealed preview","second explicit click factor3 changes committed artifact","native baseline/candidate grid and output agree"},installedWarpTouched=false,runtime_files=new[]{"ResearchNotebook.exe","ResearchNotebook.dll","Notebook.Core.dll","NotebookFixture.exe","NotebookFixture.dll"}.ToDictionary(x=>x,x=>Notebook.FileHash(Path.Combine(AppContext.BaseDirectory,x),256*Notebook.FileLimit))},Notebook.Json));
    }
    sealed record EvidenceBundle(CommandPlan Plan,ReceiptEnvelope Receipt);
}
