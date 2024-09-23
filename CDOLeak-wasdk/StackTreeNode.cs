using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CDOLeak_wasdk
{
    // A node in a tree holding the merged AddRef/Release stacks.
    // Each node represents one stack frame.
    internal class StackTreeNode
    {
        private readonly string _module;
        private readonly string _function;
        private readonly string _offset;

        public string Function { get { return _function; } }

        public string ModuleAndFunction
        {
            get
            {
                return string.IsNullOrEmpty(_module)
                    ? _function
                    : _module + '!' + _function;
            }
        }

        public string ModuleFunctionAndOffset
        {
            get
            {
                return ModuleAndFunction + _offset;
            }
        }

        public string DisplayString { get; private set; }
        public string Comments { get; set; }

        /// <summary>
        /// The ttdext command to jump to this point
        /// </summary>
        public string Position { get; private set; }
        private string RecursivePosition
        {
            get
            {
                if (!_children.Any())
                {
                    return Position;
                }
                else
                {
                    return _children.First().RecursivePosition;
                }
            }
        }

        private int _refCount;
        private int RefCount
        {
            get
            {
                if (!_children.Any())
                {
                    return _refCount;
                }
                else
                {
                    return _children.First().RefCount;
                }
            }
            set { _refCount = value; }
        }

        private StackTreeNode Parent { get; set; }

        private List<StackTreeNode> _children;
        public IEnumerable<StackTreeNode> Children
        {
            get { return _children; }
        }

        // Only set at the bottom of the stack (i.e. the StackTreeNodes without any children)
        private RefCountStack _stack;

        public int RefCountDiff { get; private set; }

        /// <summary>
        /// Leaf nodes have only one AddRef/Release under them.
        /// A non-leaf node can have multiple AddRef/Release branches under it, even though its net ref count might be 1 or -1.
        /// </summary>
        public bool IsLeafNode { get; private set; }

        public override string ToString()
        {
            return DisplayString + " // " + Comments;
        }

        private StackTreeNode(StackTreeNode parent, string module, string function, string offset, string displayString)
        {
            _children = new List<StackTreeNode>();
            Parent = parent;
            _module = module;
            _function = function;
            _offset = offset;
            DisplayString = displayString;
            IsLeafNode = false;
        }

        private void UpdateRefCountDiff()
        {
            if (_stack != null)
            {
                Debug.Assert(_children.Count == 0);

                RefCountDiff = _stack.IsAddRef ? 1 : -1;
                Position = _stack.Position;
                IsLeafNode = true;
            }
            else
            {
                int diff = 0;

                foreach (StackTreeNode child in _children)
                {
                    child.UpdateRefCountDiff();
                    diff += child.RefCountDiff;
                }

                if (_children.Count > 1)
                {
                    IsLeafNode = false;
                }
                else
                {
                    IsLeafNode = _children[0].IsLeafNode;
                }

                RefCountDiff = diff;
            }
        }

        #region Trimming

        private void TrimTop()
        {
            while (_children.Count() == 1)
            {
                StackTreeNode child = _children.First();
                _children.Clear();
                foreach (StackTreeNode grandchild in child.Children)
                {
                    grandchild.Parent = this;
                    _children.Add(grandchild);
                }
            }
        }

        private void TrimBottom()
        {
            if (IsLeafNode
                && RefCountDiff == 1
                && DisplayString.Contains("CDependencyObject::AddRef", StringComparison.Ordinal))
            {
                RefCount = _children.First().RefCount;
                Position = _children.First().RecursivePosition;
                _children.Clear();
            }
            else if (IsLeafNode
                && RefCountDiff == -1
                && DisplayString.Contains("CDependencyObject::Release", StringComparison.Ordinal))
            {
                RefCount = _children.First().RefCount;
                Position = _children.First().RecursivePosition;
                _children.Clear();
            }
            else
            {
                foreach (StackTreeNode child in Children)
                {
                    child.TrimBottom();
                }
            }
        }

        #endregion

        #region Building from RefCountStack

        private void MergeRefCountStack(RefCountStack stack, int level)
        {
            // Assume everything is merged up to level. We have to figure out where it goes in level+1.

            // Get the line at level+1. This is the child frame we have to merge.
            StackLine line = stack.GetFrameFromBottom(level + 1);

            // No more lines. The stack belongs here.
            if (line == null)
            {
                RefCount = stack.RefCount;
                if (_stack == null)
                {
                    _stack = stack;
                }
                else
                {
                    Debug.Assert(false, "stack has already been found, it shouldn't be found a second time");
                    // could be multiple AddRefs by the same stack
                }
            }
            else
            {
                // Look in the last entry in _children for something matching the same module/function.
                bool merged = false;
                if (_children.Any())
                {
                    StackTreeNode lastChild = _children.Last();
                    if (String.Equals(lastChild._module, line.Module)
                        && String.Equals(lastChild._function, line.Function)
                        && String.Equals(lastChild._offset, line.Offset)
                        && string.Equals(lastChild.Comments, line.Comments))
                    {
                        // If found, it goes in there.
                        lastChild.MergeRefCountStack(stack, level + 1);
                        merged = true;
                    }
                }

                if (!merged)
                {
                    string displayString = string.Format("{0}!{1}{2}", line.Module, line.Function, line.Offset);
                    if (!string.IsNullOrEmpty(line.SourceLine))
                    {
                        displayString = displayString + " [" + line.SourceLine + "]";
                    }

                    // If none found, make a new child. It goes in there.
                    StackTreeNode newNode = new StackTreeNode(this, line.Module, line.Function, line.Offset, displayString)
                    {
                        Comments = line.Comments,
                    };
                    _children.Add(newNode);
                    newNode.MergeRefCountStack(stack, level + 1);
                }
            }
        }

        /// <summary>
        /// Entrypoint into a StackTree. Given a bunch of stacks, stitch them into a tree.
        /// </summary>
        /// <param name="stacks"></param>
        /// <returns></returns>
        public static StackTreeNode StitchStacks(IEnumerable<RefCountStack> stacks)
        {
            StackTreeNode root = new StackTreeNode(null, null, null, null, "[Root]");

            // There won't be any if the input is bogus
            if (stacks.Any())
            {
                foreach (RefCountStack stack in stacks)
                {
                    root.MergeRefCountStack(stack, 0);
                }

                root.UpdateRefCountDiff();

                root.TrimTop();
                root.TrimBottom();
            }

            return root;
        }

        #endregion

        #region Writing to file

        public void WriteToStream(StreamWriter streamWriter)
        {
            // This needs to write the StackTreeNode. Its comment may have been updated.
            // There shouldn't even be a reference to _stack. RefCountStack is transient while the StackTreeNodes are being built.

            if (_children.Count == 0)
            {
                streamWriter.WriteLine(RefCountStack.RefCountIs + RefCount);
                streamWriter.WriteLine(RefCountStack.StackBegin);
                WriteStackFrame(streamWriter);
                streamWriter.WriteLine(RefCountStack.StackEnd);
                streamWriter.WriteLine();
            }
            else
            {
                foreach (StackTreeNode child in _children)
                {
                    child.WriteToStream(streamWriter);
                }
            }
        }

        private void WriteStackFrame(StreamWriter writer)
        {
            // The root element has nothing in it, so don't write anything.
            // The bottom of the stack is the root element's child.
            if (Parent != null)
            {
                writer.Write(DisplayString);

                if (!string.IsNullOrEmpty(Comments))
                {
                    writer.Write(" // ");
                    writer.Write(Comments);
                }

                writer.WriteLine();

                Parent.WriteStackFrame(writer);
            }
        }

        #endregion

        public bool MatchesHeuristic(string pattern, bool mustBeLeaf)
        {
            return _function != null    // Root node is all null and shouldn't match anything
                && (!mustBeLeaf || IsLeafNode)
                && string.IsNullOrEmpty(Comments)
                && (DisplayString.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        public void GetStats(StackTreeNodeStats stats, bool attributed)
        {
            if (!string.IsNullOrEmpty(Comments))
            {
                attributed = true;
            }

            if (_children.Count == 0)
            {
                if (attributed)
                {
                    if (RefCountDiff == 1)
                    {
                        stats.AttributedAddRef++;
                    }
                    else if (RefCountDiff == -1)
                    {
                        stats.AttributedRelease++;
                    }
                    else
                    {
                        Debug.Assert(false, "Should be a single AddRef/Release at the leaf StackTreeNode");
                    }
                }
                else
                {
                    if (RefCountDiff == 1)
                    {
                        stats.UnattributedAddRef++;
                    }
                    else if (RefCountDiff == -1)
                    {
                        stats.UnattributedRelease++;
                    }
                    else
                    {
                        Debug.Assert(false, "Should be a single AddRef/Release at the leaf StackTreeNode");
                    }
                }
            }
            else
            {
                foreach (StackTreeNode child in _children)
                {
                    child.GetStats(stats, attributed);
                }
            }
        }
    }

    public class StackTreeNodeStats
    {
        public int UnattributedAddRef { get; set; }
        public int UnattributedRelease { get; set; }

        public int AttributedAddRef { get; set; }
        public int AttributedRelease { get; set; }
    }
}
