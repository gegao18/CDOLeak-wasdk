using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace CDOLeak_wasdk
{
    internal class StackTreeView : UserControl
    {
        private StackTreeNodeView _root;
        public StackTreeNode Root
        {
            get { return _root.Node; }
        }

        public StackTreeNodeView SelectedRow { get; private set; }

        private ScrollViewer _scrollViewer;
        private StackPanel _stackPanel;

        // This is a flat list of all rows, with each row representing a stack frame.
        // The rows themselves have a tree structure (i.e. each frame can have a parent frame and multiple child frames),
        // but this is a flat list that contains everything in the tree.
        private List<StackTreeNodeView> _rows = new List<StackTreeNodeView>();

        private List<StackTreeNodeView> _rowsInView = new List<StackTreeNodeView>();

        private List<StackTreeNodeView> _highlightedRows = new List<StackTreeNodeView>();

        private TextBlock _statusText;
        private TextBlock _virtualizationText;
        private StackPanel _logPanel;

        public StackTreeView()
        {
            _stackPanel = new StackPanel();
            _scrollViewer = new ScrollViewer()
            {
                Content = _stackPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            };
            _scrollViewer.ViewChanged += _scrollViewer_ViewChanged;

            Content = _scrollViewer;
            IsTabStop = true;

            KeyDown += StackTreeView_KeyDown;
        }

        private void _scrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateVirtualization();
        }

        private void StackTreeView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case VirtualKey.Up:
                    SelectPrevious(false);
                    e.Handled = true;
                    break;

                case VirtualKey.Down:
                    SelectNext(false);
                    e.Handled = true;
                    break;

                case VirtualKey.Left:
                    CollapseSelected();
                    e.Handled = true;
                    break;

                case VirtualKey.Right:
                    ExpandSelected();
                    e.Handled = true;
                    break;

                case VirtualKey.Home:
                    SelectFirst();
                    e.Handled = true;
                    break;

                case VirtualKey.End:
                    SelectLast();
                    e.Handled = true;
                    break;

                case VirtualKey.F2:
                    SelectedRow.EditComments();
                    e.Handled = true;
                    break;

                case VirtualKey.F3:
                    FindNext(_searchString, _searchCollapsed);
                    e.Handled = true;
                    break;

                case VirtualKey.C:
                    if (!Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
                    {
                        CollapseDirectChildren(SelectedRow);
                        e.Handled = true;
                    }
                    break;

                case VirtualKey.P:
                    SelectParent(SelectedRow);
                    break;

                case (VirtualKey)219:
                case (VirtualKey)221:
                    SelectMatchingAddRefRelease();
                    break;
            }
        }

        #region Initialization

        public void SetStackTree(StackTreeNode root, TextBlock statusText, TextBlock virtualizationText, StackPanel logPanel)
        {
            _statusText = statusText;
            _virtualizationText = virtualizationText;
            _logPanel = logPanel;

            _rows.Clear();
            _rowsInView.Clear();
            _stackPanel.Children.Clear();

            _root = new StackTreeNodeView(null, root, 0, true, this); // Will add itself via AddRow

            _stackPanel.Children.Add(new Canvas() { Height = 50 });

            SelectedRow = _rows.First();

            UpdateLayout(); // Need layout positions so virtualization can work
            UpdateVirtualization();
            UpdateUIState();
        }

        internal void AddRow(StackTreeNodeView row)
        {
            _rows.Add(row);
            _stackPanel.Children.Add(row);
        }

        #endregion

        #region Expanding/Collapsing

        public bool ExpandAll()
        {
            bool wasExpanded = false;
            foreach (StackTreeNodeView row in _rows)
            {
                if (row.CanExpand)
                {
                    row.IsExpanded = true;
                    wasExpanded = true;
                }
            }
            return wasExpanded;
        }

        public void CollapseAnnotated()
        {
            foreach (StackTreeNodeView row in _rows)
            {
                if (row.CanExpand && !string.IsNullOrEmpty(row.Node.Comments))
                {
                    row.IsExpanded = false;
                }
            }
        }

        private void CollapseDirectChildren(StackTreeNodeView row)
        {
            foreach (StackTreeNodeView child in row.ChildNodeViews)
            {
                if (child.CanExpand)
                {
                    child.IsExpanded = false;
                }
            }
        }

        #endregion

        private void EnsureVisibleAndSelect(StackTreeNodeView row)
        {
            row.EnsureVisible();

            // In case the search result was just expanded, update the layout so SelectRow can bring it into view
            UpdateLayout();

            SelectRow(row);
        }

        internal void SelectRow(StackTreeNodeView row)
        {
            SelectedRow.IsSelected = false;
            SelectedRow = row;
            SelectedRow.IsSelected = true;

            // The StackPanel offset for the visible viewport
            double svTop = _scrollViewer.VerticalOffset;
            double svBottom = svTop + _scrollViewer.ViewportHeight;

            // The StackPanel offset for the selected item
            double rowTop = SelectedRow.ActualOffset.Y;
            double rowBottom = rowTop + SelectedRow.ActualHeight;

            if (svTop <= rowTop && rowBottom <= svBottom)
            {
                // Do nothing. The item is in view.
            }
            else if (rowTop < svTop)
            {
                // Move viewport up to the selected item
                _scrollViewer.ChangeView(null, rowTop, null);
            }
            else if (svBottom < rowBottom)
            {
                // Move viewport down to the selected item
                _scrollViewer.ChangeView(null, rowBottom - _scrollViewer.ViewportHeight, null);
            }

            Focus(FocusState.Keyboard);
        }

        public void UpdateVirtualization()
        {
            UpdateLayout();
            // Don't bother devirtualizing. Altering the width of the ScrollViewer messes with layout and causes a global re-layout.
            // Add a feature to not allow the width of the ScrollViewer to decrease, and then consider re-virtualizing.
            //List<StackTreeNodeView> oldRowsInView = new List<StackTreeNodeView>(_rowsInView);
            //_rowsInView.Clear();

            const double buffer = 250;
            double svTop = _scrollViewer.VerticalOffset - buffer;
            double svBottom = _scrollViewer.VerticalOffset + _scrollViewer.ViewportHeight + buffer;

            foreach (StackTreeNodeView row in _rows)
            {
                if (row.ActualOffset.Y > svTop
                    && row.ActualOffset.Y + row.ActualHeight < svBottom
                    && row.Visibility == Visibility.Visible // Skip collapsed rows
                    && !_rowsInView.Contains(row))
                {
                    //oldRowsInView.Remove(row);
                    _rowsInView.Add(row);
                }
            }

            //foreach (StackTreeNodeView row in oldRowsInView)
            //{
            //    row.Virtualize();
            //}

            foreach (StackTreeNodeView row in _rowsInView)
            {
                row.Devirtualize();
            }

            _virtualizationText.Text = string.Format(
                "Virtualization window [{0:F0}, {1:F0}], {2} rows devirtualized",
                svTop,
                svBottom,
                _rowsInView.Count()
                );
        }

        #region Keyboard selection

        private void SelectPrevious(bool canExpandOnly)
        {
            int selectedIndex = _rows.IndexOf(SelectedRow);
            for (int i = selectedIndex - 1; i >= 0; i--)
            {
                if (_rows[i].Visibility == Visibility.Visible
                    && (!canExpandOnly || i == 0 || _rows[i].CanExpand))
                {
                    SelectRow(_rows[i]);
                    break;
                }
            }
        }

        private void SelectNext(bool canExpandOnly)
        {
            int selectedIndex = _rows.IndexOf(SelectedRow);
            for (int i = selectedIndex + 1; i < _rows.Count; i++)
            {
                if (_rows[i].Visibility == Visibility.Visible
                    && (!canExpandOnly || i == _rows.Count - 1 || _rows[i].CanExpand))
                {
                    SelectRow(_rows[i]);
                    break;
                }
            }
        }

        private void CollapseSelected()
        {
            if (SelectedRow.CanExpand && SelectedRow.IsExpanded)
            {
                SelectedRow.IsExpanded = false;
            }
            else
            {
                SelectPrevious(true);
            }
        }

        private void ExpandSelected()
        {
            if (SelectedRow.CanExpand && !SelectedRow.IsExpanded)
            {
                SelectedRow.IsExpanded = true;
            }
            else
            {
                SelectNext(true);
            }
        }

        private void SelectFirst()
        {
            SelectRow(_rows[0]);
        }

        private void SelectLast()
        {
            for (int i = _rows.Count() - 1; i >= 0; i--)
            {
                if (_rows[i].Visibility == Visibility.Visible)
                {
                    SelectRow(_rows[i]);
                    break;
                }
            }
        }

        private void SelectParent(StackTreeNodeView row)
        {
            StackTreeNodeView parent = row.ParentNodeView;
            while (parent != null && !parent.CanExpand)
            {
                parent = parent.ParentNodeView;
            }
            SelectRow(parent);
        }

        private void SelectMatchingAddRefRelease()
        {
            if (SelectedRow != null)
            {
                string annotation = SelectedRow.Node.Comments;
                if (annotation.StartsWith("[AddRef", StringComparison.Ordinal))
                {
                    string matchingAnnotation = annotation.Replace("[AddRef", "[Release");

                    int selectedIndex = _rows.IndexOf(SelectedRow);
                    for (int i = selectedIndex + 1; i < _rows.Count; i++)
                    {
                        StackTreeNodeView row = _rows[i];
                        if (string.Equals(row.Node.Comments, matchingAnnotation))
                        {
                            EnsureVisibleAndSelect(row);
                            break;
                        }
                    }
                }
                else if (annotation.StartsWith("[Release", StringComparison.Ordinal))
                {
                    string matchingAnnotation = annotation.Replace("[Release", "[AddRef");

                    int selectedIndex = _rows.IndexOf(SelectedRow);
                    for (int i = selectedIndex - 1; i >= 0; i--)
                    {
                        StackTreeNodeView row = _rows[i];
                        if (string.Equals(row.Node.Comments, matchingAnnotation))
                        {
                            EnsureVisibleAndSelect(row);
                            break;
                        }
                    }
                }
            }
        }

        #endregion

        #region Search

        private string _searchString;
        private bool _searchCollapsed;

        public void FindNext(string searchString, bool searchCollapsed)
        {
            _searchString = searchString;
            _searchCollapsed = searchCollapsed;

            int selectedIndex = _rows.IndexOf(SelectedRow);
            for (int i = selectedIndex + 1; i < _rows.Count; i++)
            {
                if ((searchCollapsed
                        || _rows[i].Visibility == Visibility.Visible
                        )
                    && (_rows[i].Node.DisplayString.Contains(searchString, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(_rows[i].Node.Comments) && _rows[i].Node.Comments.Contains(searchString, StringComparison.OrdinalIgnoreCase))
                        )
                    )
                {
                    EnsureVisibleAndSelect(_rows[i]);
                    break;
                }
            }
        }

        #endregion

        #region Annotations

        private void UpdateStatusText()
        {
            StackTreeNodeStats stats = new StackTreeNodeStats();
            _root.Node.GetStats(stats, false);

            _statusText.Text = string.Format(
                "Unattributed: {0} AddRef, {1} Release | Attributed: {2} AddRef, {3} Release",
                stats.UnattributedAddRef,
                stats.UnattributedRelease,
                stats.AttributedAddRef,
                stats.AttributedRelease
                );
        }

        public void ClearAnnotations()
        {
            foreach (StackTreeNodeView row in _rows)
            {
                if (!string.IsNullOrEmpty(row.Node.Comments))
                {
                    row.ClearAnnotations();
                }
            }

            UpdateUIState();
        }

        private void PropagateAllAnnotationsUp()
        {
            foreach (StackTreeNodeView row in _rows)
            {
                if (!string.IsNullOrEmpty(row.Node.Comments))
                {
                    row.PropagateAnnotationsUp();
                }
            }
        }

        public void ApplyHeuristics(IEnumerable<AddRefReleaseHeuristic> heuristics)
        {
            (List<MatchingStackTreeNodeView> AddRef, List<MatchingStackTreeNodeView> Release) addRefReleaseMatches = (null, null);

            _logPanel.Children.Clear();

            bool isFirstLogEntry = true;

            foreach (AddRefReleaseHeuristic heuristic in heuristics)
            {
                TextBlock ruleTextBlock = new TextBlock()
                {
                    Text = heuristic.Name,
                    Margin = new Thickness(2, isFirstLogEntry ? 0 : 18, 0, 0),
                    FontSize = 7,
                };
                _logPanel.Children.Add(ruleTextBlock);
                isFirstLogEntry = false;

                int matchCount = 0;
                int startIndex = 0;
                do
                {
                    addRefReleaseMatches = heuristic.FindMatchingUnannotatedAddRefRelease(_rows, startIndex);
                    if (addRefReleaseMatches.AddRef != null)
                    {
                        // We found a match. If this was a wildcard scoped heuristic, it might be a partial match.
                        // Update the start search index to skip this match next time.
                        startIndex = addRefReleaseMatches.AddRef.Last().RowIndex + 1;

                        // Only update the annotations if it's a full match.
                        if (addRefReleaseMatches.Release != null)
                        {
                            matchCount++;
                            ruleTextBlock.FontSize = 14;

                            TextBlock addRefLog = new TextBlock()
                            {
                                Text = String.Format("  - AddRef at {0},", addRefReleaseMatches.AddRef.Last().StackTreeNodeView.Node.DisplayString),
                                Margin = new Thickness(2, 0, 0, 0),
                            };
                            StackTreeNodeView addRefStackTreeNodeView = addRefReleaseMatches.AddRef.Last().StackTreeNodeView;
                            addRefLog.Tapped += (sender, e) => { ScrollIntoView(addRefStackTreeNodeView); };
                            _logPanel.Children.Add(addRefLog);

                            TextBlock releaseLog = new TextBlock()
                            {
                                Text = String.Format("       Release at {0}", addRefReleaseMatches.Release.Last().StackTreeNodeView.Node.DisplayString),
                                Margin = new Thickness(2, 0, 0, 0),
                            };
                            StackTreeNodeView releaseStackTreeNodeView = addRefReleaseMatches.Release.Last().StackTreeNodeView;
                            releaseLog.Tapped += (sender, e) => { ScrollIntoView(releaseStackTreeNodeView); };
                            _logPanel.Children.Add(releaseLog);

                            addRefReleaseMatches.AddRef.Last().StackTreeNodeView.Node.Comments = "[AddRef for " + heuristic.Name + ": match #" + matchCount + "]";
                            addRefReleaseMatches.Release.Last().StackTreeNodeView.Node.Comments = "[Release for " + heuristic.Name + ": match #" + matchCount + "]";

                            addRefReleaseMatches.AddRef.Last().StackTreeNodeView.UpdateCommentTextBlock();
                            addRefReleaseMatches.Release.Last().StackTreeNodeView.UpdateCommentTextBlock();
                        }
                    }
                } while (addRefReleaseMatches.AddRef != null);

                if (matchCount > 0)
                {
                    _logPanel.Children.Add(new TextBlock()
                    {
                        Text = String.Format("  {0} matches found", matchCount),
                        Margin = new Thickness(2, 0, 0, 0),
                    });
                }

                // Propagate right away to mark completely covered branches. Otherwise the next
                // heuristic might mark something along an already covered branch.
                PropagateAllAnnotationsUp();
            }

            CollapseAnnotated();

            UpdateUIState();
        }

        void ScrollIntoView(StackTreeNodeView nodeView)
        {
            bool wasExpanded = ExpandAll();
            if (wasExpanded)
            {
                _stackPanel.UpdateLayout();
            }

            GeneralTransform transform = nodeView.TransformToVisual(_stackPanel);
            double offsetY = transform.TransformPoint(new Point(0, 0)).Y;
            _scrollViewer.ScrollToVerticalOffset(offsetY);
            nodeView.Highlight();
            _highlightedRows.Add(nodeView);
        }

        public void ClearHighlights()
        {
            foreach (var node in _highlightedRows)
            {
                node.RemoveHighlight();
            }
            _highlightedRows.Clear();
        }

        #endregion

        public void UpdateUIState()
        {
            UpdateStatusText();
            ApplyFilter();
        }

        #region Filtering

        Func<StackTreeNodeView, bool> _filterFunction;

        private void ApplyFilter()
        {
            if (_filterFunction != null)
            {
                foreach (StackTreeNodeView row in _rows)
                {
                    row.Filter(_filterFunction);
                }
            }
            else
            {
                foreach (StackTreeNodeView row in _rows)
                {
                    row.ResetFilter();
                }
            }
        }

        public void ShowUnannotated()
        {
            _filterFunction = (r => string.IsNullOrEmpty(r.Node.Comments));
            ApplyFilter();
        }

        public void ResetFilter()
        {
            _filterFunction = null;
            ApplyFilter();
        }

        #endregion
    }
}
