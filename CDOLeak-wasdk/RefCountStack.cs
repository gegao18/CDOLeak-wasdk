using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CDOLeak_wasdk
{
    internal class StackLine
    {
        public int FrameNumber { get; private set; }
        public string Module { get; private set; }
        public string Function { get; private set; }
        public string Offset { get; private set; }
        public string SourceLine { get; private set; }
        public string Comments { get; private set; }

        public string EntireLine { get; private set; }

        private StackLine()
        {
        }

        public override string ToString()
        {
            return string.Format("{0:X} {1}!{2}{3} [{4}]",
                FrameNumber,
                Module,
                Function,
                Offset,
                SourceLine);
        }

        public static StackLine Create(string line, int frameNumber)
        {
            // e.g.
            // Microsoft_UI_Xaml!xref::details::optional_ref_count::AddRef+0x39 [G:\l-c\dxaml\xcp\components\base\inc\weakref_count.h @ 206] // annotations go here [including tags and stuff]

            string original = line;

            bool hasModule = false;
            string module = string.Empty;
            string function = string.Empty;
            string offset = string.Empty;
            string sourceLine = string.Empty;
            string comments = string.Empty;

            int bangIndex = line.IndexOf('!');
            if (bangIndex > -1)
            {
                hasModule = true;
                module = line.Substring(0, bangIndex).Trim();
                line = line.Substring(bangIndex + 1);
            }

            if (hasModule)
            {
                int commentIndex = line.IndexOf("//");
                if (commentIndex > -1)
                {
                    comments = line.Substring(commentIndex + 2).Trim();
                    line = line.Substring(0, commentIndex);
                }

                line = line.Trim();
                function = line;

                // Look in the entire function for the name, offset, and line number
                // Line like this are tricky because they contain [] without them being a line number:
                // Microsoft_UI_Xaml_Tests_External_Framework_XDefer!Microsoft::System::DispatcherQueueHandler::[Microsoft::System::DispatcherQueueHandler::__abi_IDelegate]::__abi_Microsoft_System_DispatcherQueueHandler___abi_IDelegate____abi_Invoke+0x41

                int rightBracketIndex = line.LastIndexOf(']');
                int leftBracketIndex = line.LastIndexOf('[');
                if (rightBracketIndex == line.Length - 1 && leftBracketIndex > -1)
                {
                    sourceLine = line.Substring(leftBracketIndex + 1, rightBracketIndex - leftBracketIndex - 1);

                    line = line.Substring(0, leftBracketIndex).Trim();
                    function = line;

                    int plusIndex = function.IndexOf('+');
                    if (plusIndex > -1)
                    {
                        offset = function.Substring(plusIndex);
                        function = function.Substring(0, plusIndex);
                    }
                }
            }

            if (hasModule)
            {
                return new StackLine()
                {
                    FrameNumber = frameNumber,
                    Module = module,
                    Function = function,
                    Offset = offset,
                    SourceLine = sourceLine,
                    Comments = comments,
                    EntireLine = original,
                };
            }
            else
            {
                return null;
            }
        }
    }

    internal class RefCountStack
    {
        public const string RefCountIs = "ref count is ";
        public const string StackBegin = "Stack begin";
        public const string StackEnd = "Stack end";
        public const string TimeTravelPosition = "Time Travel Position: ";

        public bool IsAddRef { get; set; }

        public int RefCount { get; private set; }

        public string Position { get; set; }

        private List<StackLine> _lines;

        public override string ToString()
        {
            if (this.IsAddRef)
            {
                return string.Format("AddRef stack, {0} lines", _lines.Count());
            }
            else
            {
                return string.Format("Release stack, {0} lines", _lines.Count());
            }
        }

        public RefCountStack(int refCount)
        {
            _lines = new List<StackLine>();
            RefCount = refCount;
        }

        public bool AddLine(StackLine stackLine)
        {
            bool canAdd = (!_lines.Any() && stackLine.FrameNumber == 0)
                || (_lines.Last().FrameNumber == stackLine.FrameNumber - 1);

            if (canAdd)
            {
                _lines.Add(stackLine);
            }

            return canAdd;
        }

        /// <summary>
        /// level 0 is "root"
        /// level 1 is the bottommost frame
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        public StackLine GetFrameFromBottom(int level)
        {
            int count = _lines.Count;
            if (level <= count)
            {
                return _lines[count - level];
            }
            else
            {
                return null;
            }
        }
    }
}
