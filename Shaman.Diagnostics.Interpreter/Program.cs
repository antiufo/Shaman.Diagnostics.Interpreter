using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Diagnostics
{
    class InterpreterProgram
    {
        static void Main(string[] args)
        {
            //        var compiler = new CSharpInteractiveCompiler(Path.Combine(AppContext.BaseDirectory, "csi.rsp"), Directory.GetCurrentDirectory(), CorLightup.Desktop.TryGetRuntimeDirectory(), args, new NotImplementedAnalyzerLoader());
            //      result = new CommandLineRunner(ConsoleIO.Default, compiler, CSharpScriptCompiler.Instance, CSharpObjectFormatter.Instance).RunInteractive();
            Shaman.Runtime.SingleThreadSynchronizationContext.Run(() => MainAsync());

        }

        private async static Task MainAsync()
        {
            var c = new Interpreter();

            LoopAsync();

            c.Options = c.Options.AddImports("Shaman", "Shaman.Runtime");
            await c.RunAsync();
        }

        private static async void LoopAsync()
        {
            while (true)
            {
                await Task.Delay(500);
                if (!Interpreter.IsComposingCommand)
                    Console.WriteLine(Guid.NewGuid().ToString());
            }
        }
    }
}
