using Rea.Notebook;
using System.Text.Json;
using var interrupt=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;interrupt.Cancel();};
try
{
    if(args.Length==0){Console.WriteLine("Research Notebook: demo WORKSPACE OUTDIR | preview WORKSPACE PLAN | run WORKSPACE PLAN NEW_RECEIPT | verify PLAN RECEIPT [INDEPENDENT_SHA256]");return 0;}
    if(args[0]=="demo"&&args.Length==3)
    {
        var workspace=Path.GetFullPath(args[1]);var output=Path.GetFullPath(args[2]);if(Directory.Exists(output)||File.Exists(output))throw new IOException("Demo output directory must be new.");Directory.CreateDirectory(output);
        var demoDir=Notebook.Resolve(workspace,"demo");Directory.CreateDirectory(demoDir);
        var input=Notebook.Resolve(workspace,"demo/input.json");if(!File.Exists(input))Notebook.SaveNew(input,new{Values=new[]{3,6,9}});
        foreach(var factor in new[]{2,3})
        {
            var plan=Notebook.Create(workspace,"NotebookFixture.exe",new[]{"compute","demo/input.json","demo/result.json"},new[]{"demo/input.json"},new[]{"demo/result.json"},new(){{"REA_FACTOR",factor.ToString()}},5000);
            Notebook.SaveNew(Path.Combine(output,"plan-"+factor+".json"),plan);
            var receipt=await Notebook.Run(workspace,plan,interrupt.Token);Notebook.VerifyReceipt(receipt,plan);
            Notebook.SaveNew(Path.Combine(output,"receipt-"+factor+".json"),receipt);
            Console.WriteLine(JsonSerializer.Serialize(new{factor,receipt.Receipt.Outcome,receipt.Sha256,stdout=receipt.Receipt.Stdout.Text}));
            if(receipt.Receipt.Outcome!="completed")return 1;
        }
        return 0;
    }
    if(args[0]=="preview"&&args.Length==3){var plan=Notebook.ReadFile<CommandPlan>(args[2]);var check=Notebook.Preview(args[1],plan);Console.WriteLine(JsonSerializer.Serialize(check,Notebook.Json));return check.Ready?0:1;}
    if(args[0]=="run"&&args.Length==4)
    {
        var plan=Notebook.ReadFile<CommandPlan>(args[2]);Notebook.CheckNewDestination(args[3]);var receiptPath=Path.GetFullPath(args[3]);if(plan.Spec.Inputs.Keys.Concat(plan.Spec.Outputs).Append(plan.Spec.Executable).Any(x=>Path.GetFullPath(Notebook.Resolve(args[1],x)).Equals(receiptPath,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Receipt destination overlaps a declared command path.");var result=await Notebook.Run(args[1],plan,interrupt.Token);Notebook.VerifyReceipt(result,plan);Notebook.SaveNew(args[3],result);Console.WriteLine(result.Receipt.Outcome);return result.Receipt.Outcome=="completed"?0:1;
    }
    if(args[0]=="verify"&&(args.Length==3||args.Length==4))
    {
        var plan=Notebook.ReadFile<CommandPlan>(args[1]);var receipt=Notebook.ReadFile<ReceiptEnvelope>(args[2]);Notebook.VerifyReceipt(receipt,plan,args.Length==4?args[3]:null);Console.WriteLine("Verified internal commitments"+(args.Length==4?" and independent anchor":"; authenticity not established"));return 0;
    }
    throw new InvalidDataException("Invalid command or argument count.");
}
catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or FormatException or System.ComponentModel.Win32Exception or OperationCanceledException){Console.Error.WriteLine(e.Message);return 2;}
