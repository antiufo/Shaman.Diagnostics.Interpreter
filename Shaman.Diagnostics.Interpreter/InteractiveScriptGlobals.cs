using Microsoft.CodeAnalysis.Scripting.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Diagnostics
{
    public partial class Interpreter
    {
        

        public class InteractiveScriptGlobals
        {
            private readonly ObjectFormatter _objectFormatter;
            private readonly TextWriter _outputWriter;

            public InteractiveScriptGlobals(TextWriter outputWriter, ObjectFormatter objectFormatter)
            {
                this._objectFormatter = objectFormatter;
                this._outputWriter = outputWriter;
            }

            public void Print(object value)
            {
                _outputWriter.WriteLine(_objectFormatter.FormatObject(value));
            }
        }
        public class InteractiveScriptGlobals<TLastResult> : InteractiveScriptGlobals
        {
            public InteractiveScriptGlobals(TextWriter outputWriter, ObjectFormatter objectFormatter, TLastResult lastResult) : base(outputWriter, objectFormatter)
            {
                Last = lastResult;
            }
            public TLastResult Last { get; internal set; }
        }
    }
}
