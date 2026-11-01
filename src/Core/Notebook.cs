using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rea.Notebook;

public sealed record CommandSpec(int Schema, string Executable, string ExecutableSha256,
    string[] Arguments, SortedDictionary<string,string> Inputs, string[] Outputs,
    SortedDictionary<string,string> Environment, int TimeoutMs);
public sealed record CommandPlan(CommandSpec Spec, string Sha256);
public sealed record StreamCapture(string Base64, string Sha256, int Bytes, bool Truncated)
{
    [JsonIgnore] public string Text => Encoding.UTF8.GetString(Convert.FromBase64String(Base64));
}
public sealed record ExecutionReceipt(int Schema, string PlanSha256, string Outcome, int? ExitCode,
    long DurationMs, bool ParentExitConfirmed, StreamCapture Stdout, StreamCapture Stderr,
    SortedDictionary<string,string> InputsAfter, SortedDictionary<string,string> OutputsBefore,
    SortedDictionary<string,string> OutputsAfter, string[] Notes);
public sealed record ReceiptEnvelope(ExecutionReceipt Receipt, string Sha256);
public sealed record PreviewResult(bool Ready, string[] Reasons);

public static class Notebook
{
    public const int FileLimit = 1024 * 1024, StreamLimit = 65536;
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false, MaxDepth = 24,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static T ReadFile<T>(string path)
    {
        CheckNoLinks(path);
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>2*FileLimit)throw new InvalidDataException("JSON exceeds 2 MiB limit.");
        var bytes=new byte[(int)stream.Length];stream.ReadExactly(bytes);
        if(stream.ReadByte()!=-1)throw new InvalidDataException("JSON changed while reading.");
        return Parse<T>(bytes);
    }
    public static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string CanonicalDigest<T>(T value)
    {
        using var output = new MemoryStream();
        using(var writer = new Utf8JsonWriter(output)) WriteCanonical(writer, JsonSerializer.SerializeToElement(value, Json));
        return Digest(output.ToArray());
    }
    static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch(value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach(var p in value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal)) { writer.WritePropertyName(p.Name); WriteCanonical(writer,p.Value); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array: writer.WriteStartArray(); foreach(var item in value.EnumerateArray())WriteCanonical(writer,item); writer.WriteEndArray(); break;
            default: value.WriteTo(writer); break;
        }
    }
    public static T Parse<T>(byte[] data)
    {
        if(data.Length > 2*FileLimit) throw new InvalidDataException("JSON exceeds 2 MiB limit.");
        var reader=new Utf8JsonReader(data,new JsonReaderOptions { MaxDepth=24 });
        var stack=new Stack<HashSet<string>?>();
        while(reader.Read())
        {
            if(reader.TokenType==JsonTokenType.StartObject)stack.Push(new(StringComparer.Ordinal));
            else if(reader.TokenType==JsonTokenType.StartArray)stack.Push(null);
            else if(reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)stack.Pop();
            else if(reader.TokenType==JsonTokenType.PropertyName)
            {
                var key=reader.GetString()!;
                if(!stack.Peek()!.Add(key))throw new InvalidDataException("Duplicate JSON property.");
            }
        }
        return JsonSerializer.Deserialize<T>(data,Json) ?? throw new InvalidDataException("JSON value is null.");
    }
    static bool WellFormed(string value)
    {
        try{new UTF8Encoding(false,true).GetByteCount(value);return true;}catch(EncoderFallbackException){return false;}
    }
    static bool IsHash(string? x)=>x is not null && Regex.IsMatch(x,"^[0-9a-f]{64}$",RegexOptions.CultureInvariant);
    public static string Relative(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||path.Length>240||path.Contains('\\')||path.Contains(':')||path.StartsWith('/')||path.Any(char.IsControl)||!WellFormed(path))throw new InvalidDataException("Expected bounded forward-slash relative path.");
        foreach(var segment in path.Split('/'))
        {
            if(segment is "" or "." or ".."||segment.EndsWith(' ')||segment.EndsWith('.')||segment.IndexOfAny(['<','>','"','|','?','*'])>=0 || Regex.IsMatch(segment,"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))throw new InvalidDataException("Unsafe path segment.");
        }
        return path;
    }
    public static string Resolve(string workspace,string relative)
    {
        Relative(relative);
        var root=Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!Directory.Exists(root))throw new DirectoryNotFoundException("Workspace is absent.");
        var full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Path escapes workspace.");
        CheckNoLinks(root); CheckNoLinks(full);
        return full;
    }
    static void CheckNoLinks(string path)
    {
        var current=Path.GetFullPath(path);
        while(!string.IsNullOrEmpty(current))
        {
            if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked/reparse paths are refused.");
            var parent=Path.GetDirectoryName(current); if(parent==current)break; current=parent!;
        }
    }
    public static string FileHash(string full,int limit=FileLimit)
    {
        CheckNoLinks(full);
        var before=new FileInfo(full);
        if(!before.Exists||before.Length>limit)throw new InvalidDataException("File absent or exceeds capture limit.");
        var length=before.Length;var time=before.LastWriteTimeUtc;
        using var stream=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read);
        var digest=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        before.Refresh();
        if(!before.Exists||before.Length!=length||before.LastWriteTimeUtc!=time)throw new InvalidDataException("File changed during hashing.");
        return digest;
    }
    static string ArtifactState(string workspace,string path)
    {
        var full=Resolve(workspace,path);
        if(!File.Exists(full)) { if(Directory.Exists(full))throw new InvalidDataException("Output is a directory.");return "absent"; }
        return FileHash(full);
    }
    public static CommandPlan Create(string workspace,string executable,string[] args,string[] inputs,string[] outputs,SortedDictionary<string,string> environment,int timeoutMs)
    {
        if(inputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=inputs.Length)throw new InvalidDataException("Duplicate input paths.");
        var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var path in inputs)hashes.Add(Relative(path),FileHash(Resolve(workspace,path)));
        var spec=new CommandSpec(1,Relative(executable),FileHash(Resolve(workspace,executable),256*FileLimit),args,hashes,outputs,environment,timeoutMs);
        Validate(spec);
        return new(spec,CanonicalDigest(spec));
    }
    public static void Validate(CommandSpec s)
    {
        if(s is null||s.Schema!=1||!IsHash(s.ExecutableSha256))throw new InvalidDataException("Unsupported command schema or executable digest.");
        Relative(s.Executable);
        if(s.Arguments is null||s.Arguments.Length>32||s.Arguments.Any(x=>x is null||x.Length>1024||x.Contains('\0')||!WellFormed(x))||s.Arguments.Sum(x=>x.Length)>12000)throw new InvalidDataException("Invalid argument vector.");
        if(s.TimeoutMs<100||s.TimeoutMs>30000)throw new InvalidDataException("Timeout must be 100..30000 ms.");
        if(s.Inputs is null||s.Outputs is null||s.Environment is null||s.Inputs.Count>16||s.Outputs.Length>16||s.Environment.Count>16)throw new InvalidDataException("Declaration limit exceeded.");
        foreach(var p in s.Inputs) { Relative(p.Key); if(!IsHash(p.Value))throw new InvalidDataException("Invalid input digest."); }
        foreach(var path in s.Outputs)Relative(path);
        var all=s.Inputs.Keys.Concat(s.Outputs).Append(s.Executable).ToArray();
        if(all.Any(x=>x.Split('/')[0].Equals(".rea-notebook",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Internal notebook paths are reserved.");
        if(all.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=all.Length)throw new InvalidDataException("Input/output/executable paths overlap or differ only by case.");
        for(var i=0;i<all.Length;i++)for(var j=i+1;j<all.Length;j++)if(all[i].StartsWith(all[j]+"/",StringComparison.OrdinalIgnoreCase)||all[j].StartsWith(all[i]+"/",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Ancestor path declarations overlap.");
        foreach(var item in s.Environment)
            if(!Regex.IsMatch(item.Key,"^REA_[A-Z0-9_]{1,50}$",RegexOptions.CultureInvariant)||Regex.IsMatch(item.Key,"SECRET|TOKEN|PASSWORD|KEY",RegexOptions.CultureInvariant)||item.Value is null||item.Value.Length>256||item.Value.Any(char.IsControl)||!WellFormed(item.Value))throw new InvalidDataException("Only bounded non-secret REA_ environment values are allowed.");
    }
    public static void VerifyPlan(CommandPlan plan)
    {
        if(plan is null)throw new InvalidDataException("Plan is null.");
        Validate(plan.Spec);
        if(!IsHash(plan.Sha256)||plan.Sha256!=CanonicalDigest(plan.Spec))throw new InvalidDataException("Plan digest mismatch.");
    }
    public static PreviewResult Preview(string workspace,CommandPlan plan)
    {
        VerifyPlan(plan); var errors=new List<string>();
        try{if(FileHash(Resolve(workspace,plan.Spec.Executable),256*FileLimit)!=plan.Spec.ExecutableSha256)errors.Add("Executable bytes changed.");}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){errors.Add("Executable unavailable or unsafe.");}
        foreach(var input in plan.Spec.Inputs)
            try{if(FileHash(Resolve(workspace,input.Key))!=input.Value)errors.Add("Input changed: "+input.Key);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){errors.Add("Input unavailable or unsafe: "+input.Key);}
        foreach(var output in plan.Spec.Outputs)
            try{ArtifactState(workspace,output);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){errors.Add("Output unavailable or unsafe: "+output);}
        return new(errors.Count==0,errors.ToArray());
    }
    public static async Task<ReceiptEnvelope> Run(string workspace,CommandPlan plan,CancellationToken cancellation=default)
    {
        plan=Parse<CommandPlan>(JsonSerializer.SerializeToUtf8Bytes(plan,Json));
        var stateRoot=Resolve(workspace,".rea-notebook");Directory.CreateDirectory(stateRoot);
        using var executionLock=new FileStream(Resolve(workspace,".rea-notebook/run.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var preflight=Preview(workspace,plan);
        if(!preflight.Ready)throw new InvalidDataException("Run refused: "+string.Join(" ",preflight.Reasons));
        cancellation.ThrowIfCancellationRequested();
        var spec=plan.Spec;
        var before=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var path in spec.Outputs)before[path]=ArtifactState(workspace,path);
        var runRelative=".rea-notebook/sessions/"+Guid.NewGuid().ToString("N");
        var runRoot=Resolve(workspace,runRelative);Directory.CreateDirectory(runRoot);
        foreach(var folder in new[]{"temp","profile","profile/AppData/Local","profile/AppData/Roaming"})Directory.CreateDirectory(Resolve(workspace,runRelative+"/"+folder));
        var psi=new ProcessStartInfo { FileName=Resolve(workspace,spec.Executable),WorkingDirectory=Path.GetFullPath(workspace),UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,CreateNoWindow=true };
        foreach(var arg in spec.Arguments)psi.ArgumentList.Add(arg);
        psi.Environment.Clear();
        var windows=System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
        psi.Environment["SystemRoot"]=windows;psi.Environment["WINDIR"]=windows;
        psi.Environment["PATH"]="";psi.Environment["TEMP"]=Path.Combine(runRoot,"temp");psi.Environment["TMP"]=psi.Environment["TEMP"];
        psi.Environment["USERPROFILE"]=Path.Combine(runRoot,"profile");psi.Environment["HOME"]=psi.Environment["USERPROFILE"];
        psi.Environment["APPDATA"]=Path.Combine(runRoot,"profile","AppData","Roaming");psi.Environment["LOCALAPPDATA"]=Path.Combine(runRoot,"profile","AppData","Local");
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";psi.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"]="false";
        foreach(var item in spec.Environment)psi.Environment[item.Key]=item.Value;
        using var process=new Process { StartInfo=psi };
        var notes=new System.Collections.Concurrent.ConcurrentQueue<string>();var outcome="failed";bool terminated=true;
        var watch=Stopwatch.StartNew();
        if(!process.Start())throw new InvalidOperationException("Process did not start.");
        process.StandardInput.Close();
        int killRequested=0;
        void Kill() { Interlocked.Exchange(ref killRequested,1);try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){notes.Enqueue("Process-tree termination could not be confirmed immediately.");} }
        var stdout=Capture(process.StandardOutput.BaseStream,Kill);
        var stderr=Capture(process.StandardError.BaseStream,Kill);
        using var timeout=new CancellationTokenSource(spec.TimeoutMs);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellation,timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            outcome=process.ExitCode==0?"completed":"failed";
        }
        catch(OperationCanceledException)
        {
            outcome=cancellation.IsCancellationRequested?"interrupted":"timed-out";Kill();
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));}catch(TimeoutException){terminated=false;outcome="termination-unconfirmed";}
        }
        // Even an exited parent may leave descendant-held pipes open. Never wait forever.
        StreamCapture outCapture,errCapture;
        try{await Task.WhenAll(stdout,stderr).WaitAsync(TimeSpan.FromSeconds(3));outCapture=await stdout;errCapture=await stderr;}
        catch(TimeoutException){Kill();terminated=false;outcome="termination-unconfirmed";process.StandardOutput.Dispose();process.StandardError.Dispose();outCapture=EmptyCapture(true);errCapture=EmptyCapture(true);notes.Enqueue("Output pipes remained open; complete output is unavailable.");}
        watch.Stop();
        if(outCapture.Truncated||errCapture.Truncated){if(outcome=="completed"||(outcome=="failed"&&Volatile.Read(ref killRequested)!=0))outcome="output-limit";notes.Enqueue("Output exceeded capture limit; output is partial.");}
        var afterInputs=new SortedDictionary<string,string>(StringComparer.Ordinal);var afterOutputs=new SortedDictionary<string,string>(StringComparer.Ordinal);
        try
        {
            foreach(var input in spec.Inputs)afterInputs[input.Key]=ArtifactState(workspace,input.Key);
            foreach(var output in spec.Outputs)afterOutputs[output]=ArtifactState(workspace,output);
            if(spec.Inputs.Any(x=>afterInputs[x.Key]!=x.Value)){notes.Enqueue("Declared input bytes changed during run.");if(outcome=="completed")outcome="input-changed";}
            if(FileHash(Resolve(workspace,spec.Executable),256*FileLimit)!=spec.ExecutableSha256){notes.Enqueue("Executable bytes changed after launch.");if(outcome=="completed")outcome="executable-changed";}
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){outcome="capture-refused";notes.Enqueue("Post-run file capture was unsafe or exceeded bounds.");}
        var receipt=new ExecutionReceipt(1,plan.Sha256,outcome,process.HasExited?process.ExitCode:null,watch.ElapsedMilliseconds,terminated,outCapture,errCapture,afterInputs,before,afterOutputs,notes.ToArray());
        return new(receipt,CanonicalDigest(receipt));
    }
    static StreamCapture EmptyCapture(bool truncated)=>new("",Digest([]),0,truncated);
    static async Task<StreamCapture> Capture(Stream source,Action kill)
    {
        using var bytes=new MemoryStream();var buffer=new byte[8192];bool truncated=false;
        try
        {
            while(true)
            {
                int count=await source.ReadAsync(buffer);if(count==0)break;
                int keep=Math.Min(count,StreamLimit-(int)bytes.Length);if(keep>0)bytes.Write(buffer,0,keep);
                if(keep<count){truncated=true;kill();}
            }
        }
        catch(IOException){truncated=true;}catch(ObjectDisposedException){truncated=true;}
        var data=bytes.ToArray();return new(Convert.ToBase64String(data),Digest(data),data.Length,truncated);
    }
    public static void VerifyReceipt(ReceiptEnvelope envelope,CommandPlan plan,string? independentDigest=null)
    {
        VerifyPlan(plan);
        if(envelope is null||envelope.Receipt is null||!IsHash(envelope.Sha256)||envelope.Sha256!=CanonicalDigest(envelope.Receipt))throw new InvalidDataException("Receipt digest mismatch.");
        if(independentDigest is not null&&independentDigest!=envelope.Sha256)throw new InvalidDataException("Independent receipt anchor differs.");
        var r=envelope.Receipt;
        if(r.Schema!=1||r.PlanSha256!=plan.Sha256||r.DurationMs<0||r.DurationMs>120000||!new[]{"completed","failed","interrupted","timed-out","termination-unconfirmed","output-limit","input-changed","executable-changed","capture-refused"}.Contains(r.Outcome)||r.Notes is null||r.Notes.Length>16||r.Notes.Any(x=>x is null||x.Length>1024))throw new InvalidDataException("Invalid receipt fields.");
        foreach(var capture in new[]{r.Stdout,r.Stderr})
        {
            if(capture is null||capture.Base64 is null||capture.Base64.Length>100000)throw new InvalidDataException("Invalid capture.");
            var data=Convert.FromBase64String(capture.Base64);
            if(data.Length>StreamLimit||capture.Bytes!=data.Length||!IsHash(capture.Sha256)||Digest(data)!=capture.Sha256)throw new InvalidDataException("Output commitment differs.");
        }
        if(r.InputsAfter is null||r.OutputsBefore is null||r.OutputsAfter is null||r.InputsAfter.Count>16||r.OutputsBefore.Count>16||r.OutputsAfter.Count>16)throw new InvalidDataException("Invalid artifact map.");
        foreach(var map in new[]{r.InputsAfter,r.OutputsBefore,r.OutputsAfter})foreach(var item in map){Relative(item.Key);if(!IsHash(item.Value)&&item.Value!="absent")throw new InvalidDataException("Invalid artifact state.");}
        if(!r.OutputsBefore.Keys.Order(StringComparer.Ordinal).SequenceEqual(plan.Spec.Outputs.Order(StringComparer.Ordinal)))throw new InvalidDataException("Output baseline keys differ.");
        if(r.InputsAfter.Keys.Except(plan.Spec.Inputs.Keys).Any()||r.OutputsAfter.Keys.Except(plan.Spec.Outputs).Any())throw new InvalidDataException("Undeclared receipt artifacts.");
        if(r.Outcome=="completed"&&(r.ExitCode!=0||!r.ParentExitConfirmed||r.Stdout.Truncated||r.Stderr.Truncated||r.InputsAfter.Count!=plan.Spec.Inputs.Count||plan.Spec.Inputs.Any(x=>!r.InputsAfter.TryGetValue(x.Key,out var v)||v!=x.Value)||!r.OutputsAfter.Keys.Order(StringComparer.Ordinal).SequenceEqual(plan.Spec.Outputs.Order(StringComparer.Ordinal))))throw new InvalidDataException("Completion contradicts captured state.");
    }
    public static void CheckNewDestination(string path)
    {
        var parent=Path.GetDirectoryName(Path.GetFullPath(path))!;CheckNoLinks(parent);CheckNoLinks(path);
        if(!Directory.Exists(parent))throw new DirectoryNotFoundException("Destination folder does not exist.");
        if(File.Exists(path)||Directory.Exists(path))throw new IOException("Evidence destination already exists.");
    }
    public static void SaveNew<T>(string path,T value)
    {
        CheckNewDestination(path);
        var parent=Path.GetDirectoryName(Path.GetFullPath(path))!;
        var temp=Path.Combine(parent,".rea-write-"+Guid.NewGuid().ToString("N"));
        try
        {
            using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(file,value,Json);file.Flush(true);}
            File.Move(temp,path,false);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
