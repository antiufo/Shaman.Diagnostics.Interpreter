using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting.Hosting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.CodeAnalysis.Text;
using Shaman.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shaman.Diagnostics
{
    public partial class Interpreter
    {
        private static readonly CSharpParseOptions s_defaultOptions = new CSharpParseOptions(LanguageVersion.CSharp6, DocumentationMode.Parse, SourceCodeKind.Script, null);

        private static Func<object> CSharpScriptCompilerGetInstance = (Func<object>)ReflectionHelper.GetGetter(typeof(CSharpScript).GetTypeInfo().Assembly.GetType("Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScriptCompiler").GetField("Instance"), typeof(Func<object>));

        private volatile static bool isComposingCommand;
        public static bool IsComposingCommand => isComposingCommand;

        public Interpreter()
        {

#if CORECLR
            var loadedAssemblies = AppDomain_GetAssemblies(AppDomain_CurrentDomain());
#else
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
#endif
            Options =
                ScriptOptions.Default
                .WithImports("System", "System.Linq", "System.IO", "System.Threading", "System.Threading.Tasks")
                .WithReferences(loadedAssemblies.Where(x => !x.IsDynamic));

        }


#if CORECLR
        private static readonly Func<bool> Console_KeyAvailable = ReflectionHelper.GetWrapper<Func<bool>>(typeof(Console), "get_KeyAvailable");

        private static readonly Func<object> AppDomain_CurrentDomain = ReflectionHelper.GetWrapper<Func<object>>(typeof(int).GetTypeInfo().Assembly, "System.AppDomain", "get_CurrentDomain");
        private static readonly Func<object, Assembly[]> AppDomain_GetAssemblies = ReflectionHelper.GetWrapper<Func<object, Assembly[]>>(typeof(int).GetTypeInfo().Assembly, "System.AppDomain", "GetAssemblies");
#endif
        public ScriptOptions Options { get; set; }
        public async Task<Compilation> RunAsync()
        {
            /*Task.Run(() =>
            {
                while (true)
                {
                    if (Console.KeyAvailable) isComposingCommand = true;
                    Thread.Sleep(50);
                }
            }).GetAwaiter();*/
            //CSharpCompilation previousScriptCompilation = null;
            //if (script.Previous != null)
            //{
            //    previousScriptCompilation = (CSharpCompilation)script.Previous.GetCompilation();
            //}
            //DiagnosticBag instance = DiagnosticBag.GetInstance();
            //ImmutableArray<MetadataReference> referencesForCompilation = default(ImmutableArray<MetadataReference>); //= script.GetReferencesForCompilation(MessageProvider.Instance, instance, null);
            //instance.Free();
            // SyntaxTree syntaxTree = SyntaxFactory.ParseSyntaxTree(script.Code, CSharpScriptCompiler.s_defaultOptions, script.Options.FilePath, null, default(CancellationToken));
            //string assemblyName;
            //string scriptClassName;
            //script.GenerateSubmissionId(out assemblyName, out scriptClassName);
            //return CSharpCompilation.CreateScriptCompilation(, syntaxTree, referencesForCompilation, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, false, null, null, scriptClassName, script.Options.Imports, OptimizationLevel.Debug, false, true, null, null, default(ImmutableArray<byte>), null, Platform.AnyCpu, ReportDiagnostic.Default, 4, null, true, false, null, script.Options.SourceResolver, script.Options.MetadataResolver, DesktopAssemblyIdentityComparer.Default, null, false), previousScriptCompilation, script.ReturnType, script.GlobalsType);
            var cancellationToken = CancellationToken.None;
            //   var compiler = CSharpInteractiveCompilerCtor(null, Directory.GetCurrentDirectory(), Path.GetDirectoryName(typeof(int).GetTypeInfo().Assembly.Location), Array.Empty<string>(), new NotImplementedAnalyzerLoader());

            var box = new StateBox();
            object prevResult = null;
            box.options = Options;

            while (true)
            {
                Console.Write("> ");
                var input = new StringBuilder();
                string line;
                bool cancelSubmission = false;
                var globalsType = typeof(InteractiveScriptGlobals<>).MakeGenericType(prevResult?.GetType() ?? typeof(object));
                var globals = (InteractiveScriptGlobals)Activator.CreateInstance(globalsType, Console.Out, CSharpObjectFormatter.Instance, prevResult);
                //var globals = new InteractiveScriptGlobals(Console.Out, CSharpObjectFormatter.Instance);

                while (true)
                {
                    line = await Task.Run(() => ReadLine());
                    if (line == null)
                    {
                        if (input.Length == 0)
                        {
                            return null;
                        }

                        cancelSubmission = true;
                        break;
                    }

                    input.AppendLine(line);

                    var tree = ParseSubmission(SourceText.From(input.ToString()), cancellationToken);

                    if (SyntaxFactory.IsCompleteSubmission(tree))
                    {
                        break;
                    }

                    Console.Write(". ");
                }

                if (cancelSubmission)
                {
                    continue;
                }

                string code = input.ToString();



                Script<object> newScript;
                if (box.state == null)
                {

                    newScript = ScriptCreateInitialScript(CSharpScriptCompilerGetInstance(), code, box.options, globals.GetType(), null);
                }
                else
                {
                    newScript = box.state.Script.ContinueWith(code, box.options);
                }

                if (!await TryBuildAndRun(newScript, globals, box, cancellationToken))
                {
                    continue;
                }

                if (CompilationHasSubmissionResult(newScript.GetCompilation()))
                {
                    prevResult = box.state.ReturnValue;
                    globals.Print(box.state.ReturnValue);
                }
            }
        }

        private static string ReadLine()
        {
            var stdin = Console.In;
            StringBuilder stringBuilder = new StringBuilder();
            int num;
            try
            {
                while (true)
                {
                    if (isComposingCommand)
                    {
                        num = stdin.Read();
                    }
                    else
                    {
#if CORECLR
                        if (Console_KeyAvailable())
#else
                        if (Console.KeyAvailable)
#endif
                        {
                            isComposingCommand = true;
                            continue;
                        }
                        else
                        {
#if CORECLR
                            Task.Delay(Configuration_KeyAvailableCheckIntervalMs).Wait();   
#else
                            Thread.Sleep(Configuration_KeyAvailableCheckIntervalMs);
#endif
                            continue;
                        }

                    }

                    if (num == -1)
                    {
                        if (stringBuilder.Length > 0)
                        {
                            return stringBuilder.ToString();
                        }
                        return null;
                    }
                    if (num == 13 || num == 10)
                    {
                        break;
                    }
                    isComposingCommand = true;
                    stringBuilder.Append((char)num);
                }
                if (num == 13 && stdin.Peek() == 10)
                {
                    stdin.Read();
                }
                return stringBuilder.ToString();
            }
            finally
            {
                isComposingCommand = false;
            }
        }



        private static SyntaxTree ParseSubmission(SourceText text, CancellationToken cancellationToken)
        {
            return SyntaxFactory.ParseSyntaxTree(text, Interpreter.s_defaultOptions, "", cancellationToken);
        }

        // private readonly static Func<object, SourceText, CancellationToken, SyntaxTree> CompilerParseSubmission = ReflectionHelper.GetWrapper<Func<object, SourceText, CancellationToken, SyntaxTree>>(typeof(CSharpScript).GetTypeInfo().Assembly, "Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScriptCompiler", "ParseSubmission");
        internal static Func<string, string, string, string[], IAnalyzerAssemblyLoader, object> CSharpInteractiveCompilerCtor = ReflectionHelper.GetWrapper<Func<string, string, string, string[], IAnalyzerAssemblyLoader, object>>(typeof(CSharpObjectFormatter).GetTypeInfo().Assembly, "Microsoft.CodeAnalysis.CSharp.Scripting.Hosting.CSharpInteractiveCompiler", ".ctor");
        internal static Func<Compilation, bool> CompilationHasSubmissionResult = ReflectionHelper.GetWrapper<Func<Compilation, bool>>(typeof(Compilation), "HasSubmissionResult");

        internal static Func<object, string, ScriptOptions, Type, InteractiveAssemblyLoader, Script<object>> ScriptCreateInitialScript =
            ReflectionHelper.GetWrapper<Func<object, string, ScriptOptions, Type, InteractiveAssemblyLoader, Script<object>>>(typeof(Script), "CreateInitialScript", new[] { typeof(object) });
        private void DisplayDiagnostics(IEnumerable<Diagnostic> diagnostics)
        {
            const int MaxDisplayCount = 5;

            var errorsAndWarnings = diagnostics.ToArray();

            // by severity, then by location
            var ordered = errorsAndWarnings.OrderByDescending(d => d.Severity).ThenBy(x => x.Location.SourceSpan.Start);

            try
            {
                foreach (var diagnostic in ordered.Take(MaxDisplayCount))
                {
                    Console.ForegroundColor = (diagnostic.Severity == DiagnosticSeverity.Error) ? ConsoleColor.Red : ConsoleColor.Yellow;
                    Console.WriteLine(diagnostic.ToString());
                }

                if (errorsAndWarnings.Length > MaxDisplayCount)
                {
                    int notShown = errorsAndWarnings.Length - MaxDisplayCount;
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine(string.Format((notShown == 1) ? "+ 1 additional error" : "+ additional {0} errors", notShown));
                }
            }
            finally
            {
                Console.ResetColor();
            }
        }

        private static Func<Script<object>, ScriptState, CancellationToken, Task<ScriptState<object>>> ScriptContinueAsync = ReflectionHelper.GetWrapper<Func<Script<object>, ScriptState, CancellationToken, Task<ScriptState<object>>>>(typeof(Script<object>).GetTypeInfo().DeclaredMethods.Single(x => x.Name == "ContinueAsync"));

        private class StateBox
        {
            public ScriptState<object> state;
            public ScriptOptions options;
        }
        private async Task<bool> TryBuildAndRun(Script<object> newScript, InteractiveScriptGlobals globals, StateBox box, CancellationToken cancellationToken)
        {
            var diagnostics = newScript.Compile(cancellationToken);
            DisplayDiagnostics(diagnostics);
            if (HasAnyErrors(diagnostics))
            {
                return false;
            }

            try
            {
                var task = (box.state == null) ?
                    (Task<ScriptState<object>>)newScript.RunAsync(globals, cancellationToken) :
                    (Task<ScriptState<object>>)ScriptContinueAsync(newScript, box.state, cancellationToken);

                box.state = await task;
            }
            catch (FileLoadException e) when (e.InnerException?.GetType().Name == "InteractiveAssemblyLoaderException")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.InnerException.Message);
                Console.ResetColor();

                return false;
            }
            catch (Exception e)
            {
                DisplayException(e);
                return false;
            }

            // options = UpdateOptions(options, globals);

            return true;
        }

        internal static bool HasAnyErrors<T>(ImmutableArray<T> diagnostics) where T : Diagnostic
        {
            ImmutableArray<T>.Enumerator enumerator = diagnostics.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }
            return false;
        }

        static string FormatMethodSignature(MethodBase method)
        {
            // TODO: https://github.com/dotnet/roslyn/issues/5250 


            if (method.Name.IndexOfAny(s_generatedNameChars) >= 0 ||
                method.DeclaringType.Name.IndexOfAny(s_generatedNameChars) >= 0 ||
                method.GetCustomAttributes<DebuggerHiddenAttribute>().Any() ||
                method.DeclaringType.GetTypeInfo().GetCustomAttributes<DebuggerHiddenAttribute>().Any())
            {
                return null;
            }


            return $"{method.DeclaringType.ToString()}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ToString()))})";
        }
        private static readonly char[] s_generatedNameChars = { '$', '<' };
        private static Func<Exception, bool, StackTrace> StackTraceCtor = Shaman.Runtime.ReflectionHelper.GetWrapper<Func<Exception, bool, StackTrace>>(typeof(StackTrace), ".ctor");

        [Configuration]
        private static int Configuration_KeyAvailableCheckIntervalMs = 20;

        private void DisplayException(Exception e)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Out.WriteLine(e.Message);

                Console.ForegroundColor = ConsoleColor.DarkRed;

                var trace = StackTraceCtor(e, true);// new StackTrace(e, needFileInfo: true);
                foreach (var frame in trace.GetFrames())
                {
                    if (!frame.HasMethod())
                    {
                        continue;
                    }

                    var method = frame.GetMethod();
                    var type = method.DeclaringType;

                    if (type == typeof(Interpreter))
                    {
                        break;
                    }

                    string methodDisplay = FormatMethodSignature(method);

                    // TODO: we don't want to include awaiter helpers, shouldn't they be marked by DebuggerHidden in FX?
                    if (methodDisplay == null || IsTaskAwaiter(type) || IsTaskAwaiter(type.DeclaringType))
                    {
                        continue;
                    }

                    Console.Out.Write("  + ");
                    Console.Out.Write(methodDisplay);

                    if (frame.HasSource())
                    {
                        Console.Out.Write(string.Format(CultureInfo.CurrentUICulture, "at {0} : {1}", frame.GetFileName(), frame.GetFileLineNumber()));
                    }

                    Console.Out.WriteLine();
                }
            }
            finally
            {
                Console.ResetColor();
            }
        }


        private static bool IsTaskAwaiter(Type type)
        {
            if (type == typeof(TaskAwaiter) || type == typeof(ConfiguredTaskAwaitable))
            {
                return true;
            }

            if (type?.GetTypeInfo().IsGenericType == true)
            {
                var genericDef = type.GetTypeInfo().GetGenericTypeDefinition();
                return genericDef == typeof(TaskAwaiter<>) || type == typeof(ConfiguredTaskAwaitable<>);
            }

            return false;
        }





    }
}
