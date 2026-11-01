using System.Windows.Forms;
namespace Rea.Notebook.Desktop;
static class Program
{
    [STAThread] static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form=new NotebookForm();
        if(args.Length==2&&args[0]=="--ui-self-test")
        {
            int result=1;
            form.Shown+=async (_,_)=>
            {
                try{await form.SelfTest(Path.GetFullPath(args[1]));result=0;}
                catch(Exception e){File.WriteAllText(Path.GetFullPath(args[1])+".error.txt",e.ToString());}
                finally{form.Close();}
            };
            Application.Run(form);return result;
        }
        Application.Run(form);return 0;
    }
}
