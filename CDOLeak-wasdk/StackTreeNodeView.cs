using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace CDOLeak_wasdk
{
    internal class StackTreeNodeView : StackPanel
    {
        public StackTreeNode Node { get; private set; }

        private StackTreeView _stackTreeView;

        public bool CanExpand { get; private set; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get { return _isExpanded; }
            internal set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    UpdateVisibilityFromIsExpanded();
                    _stackTreeView.UpdateVirtualization();
                }
            }
        }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get { return _isHighlighted; }
            set
            {
                _isHighlighted = value;
                Background = _isSelected
                    ? SelectedBrush
                    : _isHighlighted ? HighlightedBrush : UnselectedBrush;
            }
        }
        private static readonly Brush HighlightedBrush = new SolidColorBrush(Colors.Yellow);

        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            internal set
            {
                _isSelected = value;
                Background = _isSelected
                    ? SelectedBrush
                    : _isHighlighted ? HighlightedBrush : UnselectedBrush;
            }
        }
        private static readonly Brush SelectedBrush = new SolidColorBrush(Colors.DarkBlue);
        private static readonly Brush UnselectedBrush = new SolidColorBrush(Colors.Transparent);

        private bool _isVirtualized;
        public bool IsVirtualized { get { return _isVirtualized; } }

        public int LineNumber { get; private set; }
        private TextBlock _lineNumberTextBlock;

        private TextBlock _expandCollapse;
        private const string CollapsedGlyph = "\uE76C";
        private const string ExpandedGlyph = "\uE70D";
        private const int ExpandCollapseWidth = 20;

        private int _level;
        private UIElement _indent;

        private TextBlock _refCount;
        private static readonly Brush PositiveBrush = new SolidColorBrush(Colors.Red);
        private static readonly Brush NegativeBrush = new SolidColorBrush(Colors.Green);

        private TextBlock _displayText;

        public const string TTCommand = "!ttdext.tt ";
        private TextBlock _tt;
        private static readonly Brush TTBrush = new SolidColorBrush(Colors.Blue);
        CoreCursor _handCursor = new CoreCursor(CoreCursorType.Hand, 1);
        CoreCursor _oldCursor;

        private TextBlock _comments;
        private TextBox _commentsEdit;
        private static readonly Brush CommentsBrush = new SolidColorBrush(Colors.Green);

        private StackTreeNodeView _parent;
        internal StackTreeNodeView ParentNodeView { get { return _parent; } }

        // These are all parented to the StackTreeView, not to this panel. There's a reference to these things to collapse them
        private List<StackTreeNodeView> _childNodeViews;
        internal IEnumerable<StackTreeNodeView> ChildNodeViews { get { return _childNodeViews; } }


        internal StackTreeNodeView(StackTreeNodeView parent, StackTreeNode node, int level, bool canExpand, StackTreeView stackTreeView)
        {
            _stackTreeView = stackTreeView;
            _parent = parent;
            Node = node;
            CanExpand = canExpand;
            _isExpanded = true;
            _level = level;
            IsSelected = false; // Also sets the brush
            _isVirtualized = true;
            Height = 24;
            Orientation = Orientation.Horizontal;
            Tapped += StackTreeNodeView_Tapped;
            RightTapped += StackTreeNodeView_RightTapped;

            EnsureControls();

            LineNumber = stackTreeView.AddRow(this);

            CreateChildViews(_level);

            if (CanExpand)
            {
                IsExpanded = string.IsNullOrEmpty(Node.Comments);
            }
        }

        private void EnsureControls()
        {
            // Instead of storing a "was initialized" flag, check for a control that's unconditionally created.
            if (!IsVirtualized
                && _indent == null)
            {
                _lineNumberTextBlock = new TextBlock()
                {
                    Text = LineNumber.ToString(),
                    FontSize = 12,
                    Margin = new Thickness(4, 0, 7, 0),
                    Width = 30,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Right,
                };
                Children.Add(_lineNumberTextBlock);

                if (CanExpand)
                {
                    _expandCollapse = new TextBlock()
                    {
                        Width = ExpandCollapseWidth,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        Text = ExpandedGlyph,
                    };
                    _expandCollapse.Tapped += _expandCollapse_Tapped;

                    Children.Add(_expandCollapse);
                }

                AddIndent(_level);

                AddRefCountText();

                _displayText = new TextBlock()
                {
                    Text = Node.DisplayString,
                    Margin = new Thickness(0, 2, 0, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsTextSelectionEnabled = false,
                };
                if (!Node.Children.Any())
                {
                    _displayText.Foreground = Node.RefCountDiff > 0 ? PositiveBrush : NegativeBrush;
                }
                _displayText.Tapped += StackTreeNodeView_Tapped;
                _displayText.RightTapped += StackTreeNodeView_RightTapped;
                Children.Add(_displayText);

                if (!string.IsNullOrEmpty(Node.Position))
                {
                    _tt = new TextBlock()
                    {
                        Text = TTCommand + Node.Position,
                        Margin = new Thickness(2, 0, 2, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = TTBrush,
                    };
                    _tt.PointerEntered += _tt_PointerEntered;
                    _tt.PointerExited += _tt_PointerExited;
                    _tt.Tapped += _tt_Tapped;
                    Children.Add(_tt);
                }

                if (!string.IsNullOrEmpty(Node.Comments))
                {
                    EnsureCommentsControls();
                }
            }
        }

        private void _tt_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        }

        private void _tt_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        }

        private void _tt_Tapped(object sender, TappedRoutedEventArgs e)
        {
            DataPackage data = new DataPackage();
            data.SetText(((TextBlock)sender).Text);
            Clipboard.SetContent(data);
        }

        public override string ToString()
        {
            if (Node != null)
            {
                return Node.ToString();
            }
            else
            {
                return base.ToString();
            }
        }

        private void StackTreeNodeView_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _stackTreeView.SelectRow(this);
        }

        private void StackTreeNodeView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            _stackTreeView.SelectRow(this);
            _stackTreeView.RightClickRow(this, e);
        }

        public void UpdateLabel()
        {
            _displayText.Text = Node.DisplayString;
        }

        #region Construction

        private void AddIndent(int level)
        {
            StringBuilder sb = new StringBuilder();

            if (level > 0)
            {
                for (int i = 0; i < level - 1; i++)
                {
                    sb.Append("|    ");
                }

                if (CanExpand)
                {
                    sb.Append("|- ");
                }
                else
                {
                    sb.Append("|    ");
                }
            }
            sb.Append('\ufeff');    // Zero-width no-break space

            _indent = new TextBlock()
            {
                Text = sb.ToString(),
                Margin = new Thickness(CanExpand ? 0 : ExpandCollapseWidth, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Children.Add(_indent);
        }

        private void AddRefCountText()
        {
            _refCount = new TextBlock()
            {
                Width = 25,
                Text = Node.RefCountDiff.ToString(),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
            };
            if (Node.RefCountDiff > 0)
            {
                _refCount.Foreground = PositiveBrush;
            }
            else if (Node.RefCountDiff < 0)
            {
                _refCount.Foreground = NegativeBrush;
            }
            Children.Add(_refCount);
        }

        private void CreateChildViews(int level)
        {
            _childNodeViews = new List<StackTreeNodeView>();

            //
            // Copy the formatting from WPA:
            //
            // 1 |- win32u.dll!<Symbols disabled>
            // 2 |    |- ntoskrnl.exe!<Symbols disabled>
            // 3 |    |    win32kfull.sys!<Symbols disabled>
            // 4 |    |    |- win32kfull.sys!<Symbols disabled>
            // 5 |    |    |- win32kbase.sys!<Symbols disabled>
            // 6 |    |- ntdll.dll!<Symbols disabled>
            // 7 |- CoreMessaging.dll!<Symbols disabled>
            //
            // A node can expand if its parent has multiple children.
            //  - Lines 2 and 6 can expand because line 1 has two children.
            //  - Line 3 can't expand because line 2 has a single child. It's always expanded.
            //  - Lines 4 and 5 can expand because line 3 has two children.
            //
            // A node with a single child doesn't add to the indent level. Line 3 and line 2 have the same indent level.
            //
            int newLevel = this.Node.Children.Count() == 1 ? level : level + 1;
            bool newCanExpand = (this.Node.Children.Count() > 1);

            foreach (StackTreeNode child in this.Node.Children)
            {
                _childNodeViews.Add(new StackTreeNodeView(this, child, newLevel, newCanExpand, _stackTreeView));
            }
        }

        #endregion

        #region Expand/Collapse

        private void _expandCollapse_Tapped(object sender, TappedRoutedEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }

        private void UpdateVisibilityFromIsExpanded()
        {
            // This row always stays visible, even if it's collapsed. It's the child rows that get removed.
            foreach (StackTreeNodeView child in _childNodeViews)
            {
                child.SetAndPropagateVisibility(IsExpanded ? Visibility.Visible : Visibility.Collapsed);
            }

            if (!IsVirtualized)
            {
                _expandCollapse.Text = IsExpanded ? ExpandedGlyph : CollapsedGlyph;
            }
        }

        //
        // Updates whether this row is visible or collapsed
        // Whether a row is visible is separate from its IsExpanded property. A row can also become collapsed an ancestor was collapsed.
        //
        // Cases include:
        //  - This row was expanded by the user. Set visible and propagate.
        //  - An ancestor was expanded by the user. Set visible, and propagate if this view is also expanded.
        //  - This row was collapsed by the user. Set collapsed and propagate.
        //  - An ancestor was collapsed by the user. Set collapsed and propagate.
        //
        private void SetAndPropagateVisibility(Visibility newVisibility)
        {
            if (Visibility != newVisibility)
            {
                Visibility = newVisibility;

                if (IsExpanded                                  // Making this row visible propagates iff this row is expanded
                    || newVisibility == Visibility.Collapsed)   // Making this row collapsed always propagates
                {
                    foreach (StackTreeNodeView child in _childNodeViews)
                    {
                        child.SetAndPropagateVisibility(newVisibility);
                    }
                }
            }
        }

        public void EnsureVisible()
        {
            if (_parent != null)
            {
                _parent.ExpandAndEnsureVisible();
            }
        }

        private void ExpandAndEnsureVisible()
        {
            if (CanExpand && !IsExpanded)
            {
                IsExpanded = true;
            }

            if (_parent != null)
            {
                _parent.ExpandAndEnsureVisible();
            }
        }

        #endregion

        #region Virtualization

        public void Virtualize()
        {
            if (!_isVirtualized)
            {
                _isVirtualized = true;

                _refCount.Visibility = Visibility.Collapsed;
                if (_expandCollapse != null)
                {
                    _expandCollapse.Visibility = Visibility.Collapsed;
                }
                _indent.Visibility = Visibility.Collapsed;
                _displayText.Visibility = Visibility.Collapsed;
                if (_comments != null)
                {
                    _comments.Visibility = Visibility.Collapsed;
                }
                if (_tt != null)
                {
                    _tt.Visibility = Visibility.Collapsed;
                }
            }
        }

        public void Devirtualize()
        {
            if (_isVirtualized)
            {
                _isVirtualized = false;
                EnsureControls();

                _refCount.Visibility = Visibility.Visible;
                if (_expandCollapse != null)
                {
                    _expandCollapse.Visibility = Visibility.Visible;
                    UpdateVisibilityFromIsExpanded();
                }
                _indent.Visibility = Visibility.Visible;
                _displayText.Visibility = Visibility.Visible;
                if (_comments != null)
                {
                    _comments.Visibility = Visibility.Visible;
                    UpdateCommentTextBlock();
                }
                if (_tt != null)
                {
                    _tt.Visibility = Visibility.Visible;
                }
            }
        }

        #endregion

        #region Annotations

        // Needs some reworking. I want to comment on something without marking it resolved.
        private bool _isFullyAnnotated;

        private void EnsureCommentsControls()
        {
            // If we are virtualized, do nothing. We'll create and update the comment controls when devirtualizing.
            if (!IsVirtualized)
            {
                if (_commentsEdit == null)
                {
                    _commentsEdit = new TextBox()
                    {
                        Text = Node.Comments,
                    };
                    _commentsEdit.KeyDown += _commentsEdit_KeyDown;
                    _commentsEdit.LostFocus += _commentsEdit_LostFocus;
                }

                if (_comments == null)
                {
                    _comments = new TextBlock()
                    {
                        Foreground = CommentsBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                    };

                    Children.Add(_comments);
                    UpdateCommentTextBlock();
                }
            }
        }

        public void UpdateCommentTextBlock()
        {
            // If we are virtualized, do nothing. We'll create and update the comment controls when devirtualizing.
            if (!IsVirtualized)
            {
                if (string.IsNullOrEmpty(Node.Comments))
                {
                    _comments.Text = string.Empty;
                }
                else
                {
                    EnsureCommentsControls();
                    _comments.Text = "// " + Node.Comments;
                }
            }
        }

        public void ClearAnnotations()
        {
            Node.Comments = null;
            _isFullyAnnotated = false;
            UpdateCommentTextBlock();
        }

        private void SaveComments()
        {
            string newComments = _commentsEdit.Text;
            Node.Comments = newComments;
            UpdateCommentTextBlock();
            PropagateAnnotationsUp();

            if (_commentsEdit.Parent == this)
            {
                Children.Remove(_commentsEdit);
                Children.Add(_comments);
                _stackTreeView.Focus(FocusState.Keyboard);
            }

            _stackTreeView.UpdateUIState();
        }

        public void EditComments()
        {
            EnsureCommentsControls();
            Children.Remove(_comments);
            Children.Add(_commentsEdit);

            _commentsEdit.Focus(FocusState.Keyboard);
        }

        public void PropagateAnnotationsUp()
        {
            _isFullyAnnotated = !string.IsNullOrEmpty(Node.Comments);
            // If the root becomes fully annotated, it has no parent to propagate up to
            if (_parent != null)
            {
                _parent.CountChildrenAndPropagateAnnotationsUp();
            }
        }

        public const string AllChildrenAccountedFor = "[All children accounted for]";
        private void CountChildrenAndPropagateAnnotationsUp()
        {
            int childAnnotationCount = 0;
            foreach (StackTreeNodeView child in _childNodeViews)
            {
                if (child._isFullyAnnotated)
                {
                    childAnnotationCount++;
                }
            }

            if (!_isFullyAnnotated && childAnnotationCount == _childNodeViews.Count)
            {
                _isFullyAnnotated = true;
                if (string.IsNullOrEmpty(Node.Comments))
                {
                    Node.Comments = AllChildrenAccountedFor;
                    UpdateCommentTextBlock();
                }
                // If the root becomes fully annotated, it has no parent to propagate up to
                if (_parent != null)
                {
                    _parent.CountChildrenAndPropagateAnnotationsUp();
                }
            }
            else if (_isFullyAnnotated && childAnnotationCount < _childNodeViews.Count)
            {
                _isFullyAnnotated = false;
                if (string.Equals(Node.Comments, AllChildrenAccountedFor))
                {
                    Node.Comments = null;
                    UpdateCommentTextBlock();
                    _parent.CountChildrenAndPropagateAnnotationsUp();
                }
            }
        }

        private void _commentsEdit_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                SaveComments();
            }
            else
            {
                e.Handled = true;
            }
        }

        private void _commentsEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            SaveComments();
        }

        #endregion

        #region Filtering

        public void Filter(Func<StackTreeNodeView, bool> filterFunction)
        {
            if (filterFunction(this))
            {
                ShowFilteredIn();
            }
            else
            {
                ShowFilteredOut();
            }
        }

        public void ResetFilter()
        {
            ShowFilteredIn();
        }

        private void ShowFilteredIn()
        {
            Opacity = 1;
        }

        private void ShowFilteredOut()
        {
            Opacity = 0.5;
        }

        #endregion
    }
}
