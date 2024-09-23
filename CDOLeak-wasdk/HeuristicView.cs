using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CDOLeak_wasdk
{
    internal class HeuristicView : StackPanel
    {
        public AddRefReleaseHeuristic Heuristic { get; private set; }

        private TextBlock _header;
        private TextBlock _total;

        // expand/collapse

        public HeuristicView(AddRefReleaseHeuristic heuristic)
        {
            Heuristic = heuristic;

            _header = new TextBlock()
            {
                Text = heuristic.Name,
                Margin = new Thickness(0, 18, 0, 0),
                FontSize = 7
            };

            _total = new TextBlock();

            ResetUI();
        }

        internal void ResetUI()
        {
            Children.Clear();
            Children.Add(_header);
        }


        internal void AddAddRef(StackTreeView stackTreeView, List<MatchingStackTreeNodeView> addRefMatches)
        {
            TextBlock addRefLog = new TextBlock()
            {
                Text = String.Format("  - AddRef at line {0} - {1},", addRefMatches.Last().StackTreeNodeView.LineNumber, addRefMatches.Last().StackTreeNodeView.Node.DisplayString),
            };
            StackTreeNodeView addRefStackTreeNodeView = addRefMatches.Last().StackTreeNodeView;
            addRefLog.Tapped += (sender, e) => { stackTreeView.ScrollIntoView(addRefStackTreeNodeView); };
            Children.Add(addRefLog);
        }

        internal void AddRelease(StackTreeView stackTreeView, List<MatchingStackTreeNodeView> releaseMatches)
        {
            TextBlock releaseLog = new TextBlock()
            {
                Text = String.Format("       Release at {0} - {1}", releaseMatches.Last().StackTreeNodeView.LineNumber, releaseMatches.Last().StackTreeNodeView.Node.DisplayString),
            };
            StackTreeNodeView releaseStackTreeNodeView = releaseMatches.Last().StackTreeNodeView;
            releaseLog.Tapped += (sender, e) => { stackTreeView.ScrollIntoView(releaseStackTreeNodeView); };
            Children.Add(releaseLog);
        }

        internal void SetTotal(int total)
        {
            _total.Text = String.Format("  {0} matches found", total);
            _header.FontSize = 14;
            Children.Add(_total);
        }
    }
}
