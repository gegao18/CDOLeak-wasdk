using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CDOLeak_wasdk
{
    internal class AddRefReleaseHeuristic
    {
        private class HeuristicFrame
        {
            public bool IsImmediate { get; set; }
            public string Pattern { get; set; }

            public override string ToString()
            {
                return Pattern + (IsImmediate ? ", immediate" : null);
            }
        }

        public string Name { get; set; }

        public bool IsLineMatch { get; private set; }
        public int AddRefLine { get; private set; }
        public int ReleaseLine { get; private set; }

        public string AddRefString { get; set; }
        public string ReleaseString { get; set; }
        private IEnumerable<string> ReleaseStrings { get; set; }

        private HeuristicFrame[] _addRefPatterns;
        private List<HeuristicFrame[]> _releasePatterns;
        private bool _isScoped;
        private bool _isWildcardScoped;

        public const string WildcardScopeKeyword = "*";
        public const string CommentKeyword = "//";
        public const char ScopeKeyword = '=';
        public const char AddRefKeyword = '+';
        public const string AddRefLineKeyword = "+@";
        public const char ReleaseKeyword = '-';
        public const string ReleaseLineKeyword = "-@";
        public const char NameStartKeyword = '[';
        public const char NameEndKeyword = ']';

        public AddRefReleaseHeuristic()
        {
            _addRefPatterns = new HeuristicFrame[1];
            _releasePatterns = new List<HeuristicFrame[]>();
        }

        public void SetAddRef(string addRefPattern)
        {
            AddRefString = addRefPattern;
            _addRefPatterns = new HeuristicFrame[1] { new HeuristicFrame() { Pattern = addRefPattern } };
        }

        public void SetAddRefLine(int line)
        {
            AddRefLine = line;
            IsLineMatch = true;
        }

        public void SetRelease(string releasePattern)
        {
            ReleaseString = releasePattern;
            _releasePatterns = new List<HeuristicFrame[]>()
            {
                new HeuristicFrame[1] { new HeuristicFrame() { Pattern = releasePattern } }
            };
        }

        public void SetReleaseLine(int line)
        {
            ReleaseLine = line;
            IsLineMatch = true;
        }

        public void WriteToStream(StreamWriter sw)
        {
            // does not include scope

            sw.WriteLine(string.Format("[{0}]", Name));
            if (IsLineMatch)
            {
                sw.WriteLine(string.Format("{0} {1}", AddRefLineKeyword, AddRefLine));
                sw.WriteLine(string.Format("{0} {1}", ReleaseLineKeyword, ReleaseLine));
            }
            else
            {
                sw.WriteLine(string.Format("{0} {1}", AddRefKeyword, AddRefString));
                sw.WriteLine(string.Format("{0} {1}", ReleaseKeyword, ReleaseString));
            }

            sw.WriteLine();
        }

        public AddRefReleaseHeuristic(string name, int addRefLine, int releaseLine)
        {
            Name = name;
            AddRefLine = addRefLine;
            ReleaseLine = releaseLine;
            IsLineMatch = true;
        }

        public AddRefReleaseHeuristic(string name, string scope, string addRef, IEnumerable<string> releases)
        {
            Name = name;
            AddRefString = addRef;
            ReleaseStrings = releases;
            ReleaseString = string.Join(", ", ReleaseStrings);

            if (scope != null)
            {
                if (string.Equals(WildcardScopeKeyword, scope))
                {
                    _isWildcardScoped = true;
                    _isScoped = true;
                }
                else
                {
                    // If there's a specific scope, then append it to both the AddRef and Release as the first matched frame. 
                    _isScoped = true;
                    addRef = scope + "->" + addRef;
                    List<string> appendedReleases = new List<string>();
                    foreach (string release in releases)
                    {
                        appendedReleases.Add(scope + "->" + release);
                    }
                    releases = appendedReleases;
                }
            }

            _addRefPatterns = SplitHeuristicFrames(addRef);
            _releasePatterns = new List<HeuristicFrame[]>();
            foreach (string release in releases)
            {
                _releasePatterns.Add(SplitHeuristicFrames(release));
            }
        }

        private HeuristicFrame[] SplitHeuristicFrames(string line)
        {
            List<HeuristicFrame> heuristics = new List<HeuristicFrame>();

            string[] outerGroups = line.Split("-->");
            foreach (string outer in outerGroups)
            {
                //
                // These strings are divided by "-->", and they all start with non-immediate calls, with the exception
                // of the very first one, which should count as immediate.
                //
                // This normally doesn't matter - the first match doesn't follow anything so we don't normally check
                // that flag. The exception is for wildcard scoped matches, where the first explicit frame is actually
                // the second frame. The true first frame is the match for the wildcard scope itself. In that case it
                // matters whether the first explicit frame is immediate, and it needs to immediately follow the match
                // for the wildcard scope.
                //
                bool isImmediate = heuristics.Any() ? false : true;
                string[] innerGroups = outer.Split("->");
                foreach (string inner in innerGroups)
                {
                    heuristics.Add(new HeuristicFrame()
                    {
                        IsImmediate = isImmediate,
                        Pattern = inner.Trim(),
                    });

                    // These strings are divided by "->". The first one is a non-immediate call, and the rest are all immediate calls.
                    isImmediate = true;
                }
            }

            return heuristics.ToArray();
        }

        //
        // Can be done better. Make it all a top-down search
        // Add a range of [begin, end) to scope down the search and early exit. Child trees end at the parent's next sibling (or the end of the range).
        //
        public (StackTreeNodeView AddRef, StackTreeNodeView Release) FindMatchingUnannotatedAddRefRelease(List<StackTreeNodeView> rows, int startIndex)
        {
            // Line matches are done in StackTreeView.ApplyHeuristic directly
            Debug.Assert(!IsLineMatch);

            List<StackTreeNodeView> addRefMatches = FindUnannotatedMatch(rows, startIndex, _addRefPatterns, 1);

            if (addRefMatches != null)
            {
                List<StackTreeNodeView> releaseMatches = null;

                if (_isWildcardScoped)
                {
                    Debug.Assert(_isScoped);

                    //
                    // e.g.
                    //
                    // Rule is
                    //  =*
                    //    + -> AddRefCall
                    //    - -> ReleaseCall
                    //
                    // Stack is
                    //  AnyMethod
                    //    ParentMethod+0x10         <-- 3. Get parent of the wildcard frame. That's the scope. This is the grandparent of the topmost AddRef match.
                    //      AnyMethod
                    //    ParentMethod+0x20       <-- 2. The immediate parent of the AddRef match is the wildcard.
                    //      AddRefCall          <-- 1. AddRef match found here. This is the topmost frame in the match.
                    //    ParentMethod+0x30           <-- 4. Find wildcard's next sibling under the scope. Start looking for Release there.
                    //      AnyMethod
                    //    ParentMethod+0x40
                    //      AnyMethod
                    //    ParentMethod+0x50             <-- 5. Release found here (ParentMethod -> ReleaseCall). ParentMethod is the wildcard match.
                    //      ReleaseCall
                    //

                    // So get the topmost AddRef match, walk to its parent to get the wildcard, then do a scoped search from that.

                    // Update the release patterns to include the wildcard.
                    StackTreeNodeView wildcard = addRefMatches.First().ParentNodeView;

                    HeuristicFrame wildcardFrame = new HeuristicFrame()
                    {
                        Pattern = wildcard.Node.ModuleAndFunction,
                        IsImmediate = true /* doesn't matter */
                    };

                    foreach (HeuristicFrame[] releaseFrames in _releasePatterns)
                    {
                        List<HeuristicFrame> releaseFramesWithWildcard = new List<HeuristicFrame>() { wildcardFrame };
                        releaseFramesWithWildcard.AddRange(releaseFrames);

                        List<StackTreeNodeView> thisMatch = FindMatchingReleaseInScope(rows, wildcard, releaseFramesWithWildcard.ToArray());

                        if (thisMatch != null &&
                            (releaseMatches == null || thisMatch.Last().Node.LineNumber < releaseMatches.Last().Node.LineNumber))
                        {
                            releaseMatches = thisMatch;
                        }
                    }

                    //
                    // Note: It's possible for AddRef to find a match but for Release to find nothing. This happens if the
                    // wildcard matched something it shouldn't have. The caller should continue to iterate if it finds such
                    // a match, and specify "startIndex" to skip this partial match.
                    //
                }
                else if (_isScoped)
                {
                    //
                    // Specific scope matches look for the Release in the immediate siblings of the topmost AddRef match.
                    //
                    // e.g.
                    //
                    // Rule is
                    //  = ParentMethod
                    //    + -> AddRefCall
                    //    - -> ReleaseCall
                    //
                    // Stack is
                    //  AnyMethod                 <-- 2. Get parent of topmost AddRef match. That's the scope.
                    //    ParentMethod+0x10
                    //      AnyMethod
                    //    ParentMethod+0x20     <-- 1. AddRef match found here. This is the topmost frame in the match.
                    //      AddRefCall
                    //    ParentMethod+0x30         <-- 3. Find next sibling under the scope. Start looking for Release there.
                    //      AnyMethod
                    //    ParentMethod+0x40
                    //      AnyMethod
                    //    ParentMethod+0x50           <-- 4. Release found here (ParentMethod -> ReleaseCall)
                    //      ReleaseCall
                    //

                    foreach (HeuristicFrame[] releaseFrames in _releasePatterns)
                    {
                        List<StackTreeNodeView> thisMatch = FindMatchingReleaseInScope(rows, addRefMatches.First(), releaseFrames);

                        if (thisMatch != null &&
                            (releaseMatches == null || thisMatch.Last().Node.LineNumber < releaseMatches.Last().Node.LineNumber))
                        {
                            releaseMatches = thisMatch;
                        }
                    }
                }
                else
                {
                    int addRefMatch = addRefMatches.Last().Node.LineNumber;

                    foreach (HeuristicFrame[] releaseFrames in _releasePatterns)
                    {
                        List<StackTreeNodeView> thisMatch = FindUnannotatedMatch(rows, addRefMatch, releaseFrames, -1);

                        if (thisMatch != null &&
                            (releaseMatches == null || thisMatch.Last().Node.LineNumber < releaseMatches.Last().Node.LineNumber))
                        {
                            releaseMatches = thisMatch;
                        }
                    }
                }

                Debug.Assert(addRefMatches.Count == 1);
                Debug.Assert(releaseMatches == null || releaseMatches.Count == 1);

                return (addRefMatches.First(), releaseMatches?.FirstOrDefault());
            }
            else
            {
                return (null, null);
            }

        }

        /// <summary>
        /// Given a list of StackTreeNodeViews, find a matching entry starting at startIndex. Returns -1 if nothing is found.
        /// </summary>
        private static List<StackTreeNodeView> FindUnannotatedMatch(List<StackTreeNodeView> rows, int startIndex, HeuristicFrame[] searchHierarchy, int refCountDiff)
        {
            // searchHierarchy contains a call chain. Find the very last thing, then confirm that all required parents match something in the ancestor list.
            HeuristicFrame lastCall = searchHierarchy[searchHierarchy.Length - 1];

            for (int i = startIndex; i < rows.Count; i++)
            {
                StackTreeNodeView row = rows[i];
                if (row.Node.RefCountDiff == refCountDiff
                    && row.Node.MatchesHeuristic(lastCall.Pattern, true))   // The bottommost match must be a leaf
                {
                    // Found a match for the final frame.
                    // Check the ancestors if there are any. If everything matches then we've found the match. Otherwise keep looking.
                    if (searchHierarchy.Length > 1)
                    {
                        //
                        // Use lastCall.IsImmediate. Something defined as
                        //      firstCall --> secondCall -> lastCall
                        // will resolve to {dontCare, firstCall}, {non-immediate, secondCall}, {immediate, lastCall}.
                        //
                        // We found lastCall, and we're going to match secondCall. Since lastCall was marked as immediate, then secondCall
                        // must be the very next frame up. i.e. lastCall's immediate flag determines how we look for secondCall. In general,
                        // frame i's immediate flag determines how we look for frame i-1.
                        //
                        List<StackTreeNodeView> matches = CheckAncestors(rows, row, searchHierarchy, searchHierarchy.Length - 2, lastCall.IsImmediate);
                        if (matches != null)
                        {
                            matches.Add(row);
                            return matches;
                        }
                    }
                    else
                    {
                        return new List<StackTreeNodeView>() { row };
                    }
                }
            }

            return null;
        }

        private static List<StackTreeNodeView> CheckAncestors(List<StackTreeNodeView> rows, StackTreeNodeView child, HeuristicFrame[] patterns, int nextPatternIndex, bool isImmediate)
        {
            StackTreeNodeView current = child.ParentNodeView;
            HeuristicFrame matchingFrame = patterns[nextPatternIndex];

            if (current != null)
            {
                if (isImmediate)
                {
                    // isImmediate allows us to walk up a single frame. Look for the next unannotated match where we are, and don't walk
                    // up if we don't find a match.
                    // Annotated matches have all been accounted for, so we don't assume anything under them has any meaningful information.
                    if (!current.Node.MatchesHeuristic(matchingFrame.Pattern, false))
                    {
                        current = null;
                    }
                }
                else
                {
                    // non-immediate lets us walk up as many frames as needed.

                    // Look up the ancestor chain for the next unannotated match.
                    // Annotated matches have all been accounted for, so we don't assume anything under them has any meaningful information.
                    while (current != null
                        && !current.Node.MatchesHeuristic(matchingFrame.Pattern, false))
                    {
                        current = current.ParentNodeView;
                    }
                }
            }

            if (current != null)
            {
                // Successful match.

                if (nextPatternIndex == 0)
                {
                    // Base case - if we've matched every pattern, then we're done.
                    return new List<StackTreeNodeView>() { current };
                }
                else
                {
                    // There's more to match.
                    if (current.ParentNodeView != null)
                    {
                        // Match the rest of the pattern, starting at the next ancestor.
                        List<StackTreeNodeView> matched = CheckAncestors(rows, current, patterns, nextPatternIndex - 1, matchingFrame.IsImmediate);

                        if (matched != null)
                        {
                            // The rest of the chain matched. We're done.
                            matched.Add(current);
                            return matched;
                        }
                        else if (!isImmediate)
                        {
                            // The rest of the chain didn't match, but we're not done.
                            // It's possible that "current" isn't meant to match matchingFrame, and that something higher up on the
                            // ancestor chain can successfully match it, so try matching for the already matched pattern, but higher
                            // up on the ancestor chain. Note that we can only do this if we're not forced to make an immediate match.
                            return CheckAncestors(rows, current, patterns, nextPatternIndex, isImmediate);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="addRef">The reference row containing the topmost frame for the AddRef call. We look in its siblings for the matching Release.</param>
        /// <param name="searchHierarchy">The full match criteria for the Release stack</param>
        /// <returns>The matching Release stack frames</returns>
        private static List<StackTreeNodeView> FindMatchingReleaseInScope(List<StackTreeNodeView> rows, StackTreeNodeView addRefTopmost, HeuristicFrame[] frames)
        {
            StackTreeNodeView parent = addRefTopmost.ParentNodeView;

            // The current step in the searchHierarchy that we're matching. We'll be matching from the top down.
            HeuristicFrame firstHeuristicFrame = frames[0];
            bool isBottomHeuristicFrame = (frames.Length == 1);

            // Don't search in all children of "parent". Only start searching after we've passed the AddRef that we found.
            bool startSearching = false;
            foreach (StackTreeNodeView child in parent.ChildNodeViews)
            {
                if (child == addRefTopmost)
                {
                    startSearching = true;

                    // Exclude the AddRef branch itself and go to the next sibling.
                    continue;
                }

                if (startSearching)
                {
                    if (child.Node.MatchesHeuristic(firstHeuristicFrame.Pattern, isBottomHeuristicFrame))
                    {
                        if (frames.Length > 1)
                        {
                            List<StackTreeNodeView> matches = CheckDescendants(rows, child, frames, 1);
                            if (matches != null)
                            {
                                matches.Insert(0, child);
                                return matches;
                            }
                        }
                        else
                        {
                            return new List<StackTreeNodeView>() { child };
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parent">Exempt from the match. Only its descendants are checked.</param>
        /// <param name="patterns"></param>
        /// <param name="nextPatternIndex"></param>
        /// <param name="isImmediate"></param>
        /// <returns></returns>
        private static List<StackTreeNodeView> CheckDescendants(List<StackTreeNodeView> rows, StackTreeNodeView parent, HeuristicFrame[] patterns, int nextPatternIndex)
        {
            HeuristicFrame currentHeuristicFrame = patterns[nextPatternIndex];
            bool isImmediate = currentHeuristicFrame.IsImmediate;
            bool isBottomHeuristicFrame = (nextPatternIndex == patterns.Length - 1);

            foreach (StackTreeNodeView child in parent.ChildNodeViews)
            {
                bool childMatches = child.Node.MatchesHeuristic(currentHeuristicFrame.Pattern, isBottomHeuristicFrame);

                if (childMatches)
                {
                    // The immediate child stack frame matches the next heuristic.

                    if (nextPatternIndex == patterns.Length - 1)
                    {
                        // Base case - we've matched the last thing in the list. Return.
                        return new List<StackTreeNodeView>() { child };
                    }
                    else
                    {
                        // There's still more to search. Keep searching through the rest of the heuristics.
                        List<StackTreeNodeView> matches = CheckDescendants(rows, child, patterns, nextPatternIndex + 1);

                        // If the rest of the search succeeded, we're on the correct path. Add "child" to the list of results. Add at the
                        // head since the rest of the matches are lower down on the stack.
                        if (matches != null)
                        {
                            matches.Insert(0, child);
                            return matches;
                        }
                        // If the rest of the chain didn't match, we might still be able to continue.
                        // It's possible that "currentHeuristicFrame" isn't meant to match "child", and that something lower down on the
                        // call stack can successfully match it, so try matching again for the already matched pattern, but strictly under
                        // "child". Note that we can only do this if we're not forced to make an immediate match.
                        else if (!isImmediate)
                        {
                            // Return whatever the recursive call found. If the match succeeded, return the list as is. Don't add to the
                            // list of results, because "child" was meant to be skipped over. If the matched failed, return null as well.
                            return CheckDescendants(rows, child, patterns, nextPatternIndex);
                        }
                    }
                }
                else if (!isImmediate)
                {
                    // If the immediate child doesn't match, but we're not forced to make an immediate match, then search under child
                    // for the match instead.

                    return CheckDescendants(rows, child, patterns, nextPatternIndex);
                }
            }

            return null;
        }
    }
}
