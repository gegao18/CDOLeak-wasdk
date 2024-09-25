using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CDOLeak_wasdk
{
    internal class HeuristicView : StackPanel
    {
        private HeuristicsView ParentView { get; set; }
        public AddRefReleaseHeuristic Heuristic { get; private set; }

        private StackPanel _headerPanel;
        private StackPanel _bodyStackPanel;

        private TextBlock _expandCollapse;
        private const string CollapsedGlyph = "\uE76C";
        private const string ExpandedGlyph = "\uE70D";
        private const string DeleteGlyph = "\uE74D";
        private const int ExpandCollapseWidth = 20;

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get { return _isExpanded; }
            internal set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    _expandCollapse.Text = IsExpanded ? ExpandedGlyph : CollapsedGlyph;
                    _bodyStackPanel.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private TextBlock _header;

        private TextBlock _delete;

        private TextBlock _addRef;
        private TextBlock _release;
        private TextBlock _matches;

        private List<StackTreeNodeView> _addRefNodeViews = new List<StackTreeNodeView>();
        private List<StackTreeNodeView> _releaseNodeViews = new List<StackTreeNodeView>();

        public HeuristicView(HeuristicsView parent, AddRefReleaseHeuristic heuristic)
        {
            ParentView = parent;
            Heuristic = heuristic;

            _expandCollapse = new TextBlock()
            {
                Width = ExpandCollapseWidth,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Text = ExpandedGlyph,
            };
            _expandCollapse.Tapped += _expandCollapse_Tapped;

            _header = new TextBlock()
            {
                Text = heuristic.Name,
                FontWeight = FontWeights.Bold,
            };

            _delete = new TextBlock()
            {
                Width = ExpandCollapseWidth,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Text = DeleteGlyph,
            };
            _delete.Tapped += _delete_Tapped;

            _headerPanel = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 18, 0, 4),
            };
            _headerPanel.Children.Add(_expandCollapse);
            _headerPanel.Children.Add(_delete);
            _headerPanel.Children.Add(_header);

            _bodyStackPanel = new StackPanel()
            {
                Margin = new Thickness(20, 0, 0, 0),
            };

            _addRef = new TextBlock();
            _release = new TextBlock();
            _matches = new TextBlock() { Margin = new Thickness(0, 4, 0, 0), };

            _bodyStackPanel.Children.Add(_addRef);
            _bodyStackPanel.Children.Add(_release);
            _bodyStackPanel.Children.Add(_matches);

            Children.Add(_headerPanel);
            Children.Add(_bodyStackPanel);

            RedrawUI();
        }

        private async void _delete_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog()
            {
                XamlRoot = this.XamlRoot,
                Title = "Delete heuristic",
                Content = string.Format("Delete heuristic [{0}] and undo its matches?", Heuristic.Name),
                IsPrimaryButtonEnabled = true,
                IsSecondaryButtonEnabled = true,
                PrimaryButtonText = "Delete",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ParentView.DeleteHeuristic(this);
            }
        }

        private void _expandCollapse_Tapped(object sender, TappedRoutedEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }

        public void RedrawUI()
        {
            _addRef.Text = "AddRef: " + Heuristic.AddRefString;
            _release.Text = "Release: " + Heuristic.ReleaseString;
        }

        internal void AddAddRef(StackTreeView stackTreeView, List<MatchingStackTreeNodeView> addRefMatches)
        {
            TextBlock addRefLog = new TextBlock()
            {
                Text = String.Format("  - AddRef at line {0} - {1},", addRefMatches.Last().StackTreeNodeView.LineNumber, addRefMatches.Last().StackTreeNodeView.Node.DisplayString),
            };
            StackTreeNodeView addRefStackTreeNodeView = addRefMatches.Last().StackTreeNodeView;
            addRefLog.Tapped += (sender, e) => { stackTreeView.ScrollIntoView(addRefStackTreeNodeView); };
            _bodyStackPanel.Children.Add(addRefLog);
            _addRefNodeViews.Add(addRefStackTreeNodeView);
        }

        internal void AddRelease(StackTreeView stackTreeView, List<MatchingStackTreeNodeView> releaseMatches)
        {
            TextBlock releaseLog = new TextBlock()
            {
                Text = String.Format("       Release at {0} - {1}", releaseMatches.Last().StackTreeNodeView.LineNumber, releaseMatches.Last().StackTreeNodeView.Node.DisplayString),
            };
            StackTreeNodeView releaseStackTreeNodeView = releaseMatches.Last().StackTreeNodeView;
            releaseLog.Tapped += (sender, e) => { stackTreeView.ScrollIntoView(releaseStackTreeNodeView); };
            _bodyStackPanel.Children.Add(releaseLog);
            _releaseNodeViews.Add(releaseStackTreeNodeView);
        }

        internal void SetTotal(int total)
        {
            _header.Text = String.Format("({0}) {1}", total, Heuristic.Name);
            _matches.Text = String.Format("{0} matches found:", total);
        }

        public void Undo()
        {
            foreach (StackTreeNodeView addRef in _addRefNodeViews)
            {
                addRef.Node.Comments = null;
                addRef.UpdateCommentTextBlock();
                addRef.PropagateAnnotationsUp();
            }
            foreach (StackTreeNodeView release in _releaseNodeViews)
            {
                release.Node.Comments = null;
                release.UpdateCommentTextBlock();
                release.PropagateAnnotationsUp();
            }
        }
    }
}
