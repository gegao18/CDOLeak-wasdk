using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CDOLeak_wasdk
{
    internal class HeuristicsView : UserControl
    {
        private ScrollViewer _scrollViewer;
        private TextBlock _header;
        private StackPanel _buttons;
        private StackPanel _contentPanel;   // Contains all HeuristicViews

        private StackTreeNodeView _currentRow; // The right-clicked row in the StackTreeView
        private HeuristicView _currentHeuristicView;    // Currently being built by the UI

        internal StackTreeView StackTreeView { get; set; }

        internal nint WindowHandle { get; set; }

        private MenuFlyout RightClickMenu { get; set; }
        private MenuFlyoutItem _addRefPatternFlyoutItem;
        private MenuFlyoutItem _releasePatternFlyoutItem;
        private MenuFlyoutItem _singleAddRefFlyoutItem;
        private MenuFlyoutItem _singleReleaseFlyoutItem;

        public string FileName { get; set; }

        public HeuristicsView()
        {
            StackPanel stackPanel = new StackPanel() { Margin = new Thickness(2, 0, 0, 0), };

            stackPanel.Children.Add(new TextBlock()
            {
                Text = "Heuristics",
                FontSize = 19,
            });

            stackPanel.Children.Add(new TextBlock()
            {
                Text = "Right click the tree view to define new AddRef/Release heuristics.",
                Margin = new Thickness(0, 10, 0, 10),
            });

            _buttons = new StackPanel() { Orientation = Orientation.Horizontal };
            Button load = new Button() { Content = "Load", };
            load.Tapped += Load_Tapped;
            _buttons.Children.Add(load);
            Button save = new Button() { Content = "Save", };
            save.Tapped += Save_Tapped;
            _buttons.Children.Add(save);
            Button collapseAll = new Button() { Content = "Collapse all", };
            collapseAll.Tapped += CollapseAll_Tapped;
            _buttons.Children.Add(collapseAll);
            Button expandAll = new Button() { Content = "Expand all", };
            expandAll.Tapped += ExpandAll_Tapped;
            _buttons.Children.Add(expandAll);
            Button clearAll = new Button() { Content = "Clear all", };
            clearAll.Tapped += ClearAll_Tapped;
            _buttons.Children.Add(clearAll);
            stackPanel.Children.Add(_buttons);

            _contentPanel = new StackPanel();
            stackPanel.Children.Add(_contentPanel);

            RightClickMenu = new MenuFlyout();
            _addRefPatternFlyoutItem = new MenuFlyoutItem();
            _addRefPatternFlyoutItem.Click += AddRefPattern_Click;
            RightClickMenu.Items.Add(_addRefPatternFlyoutItem);
            _releasePatternFlyoutItem = new MenuFlyoutItem();
            _releasePatternFlyoutItem.Click += ReleasePattern_Click;
            RightClickMenu.Items.Add(_releasePatternFlyoutItem);
            RightClickMenu.Items.Add(new MenuFlyoutSeparator());
            _singleAddRefFlyoutItem = new MenuFlyoutItem();
            _singleAddRefFlyoutItem.Click += SingleAddRef_Click;
            RightClickMenu.Items.Add(_singleAddRefFlyoutItem);
            _singleReleaseFlyoutItem = new MenuFlyoutItem();
            _singleReleaseFlyoutItem.Click += SingleRelease_Click;
            RightClickMenu.Items.Add(_singleReleaseFlyoutItem);
            ResetRightClickMenu();

            _scrollViewer = new ScrollViewer()
            {
                Content = stackPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            };
            Content = _scrollViewer;
        }

        #region Expand/collapse

        private void CollapseAll_Tapped(object sender, TappedRoutedEventArgs e)
        {
            CollapseAll();
        }

        private void CollapseAll()
        {
            foreach (HeuristicView view in _contentPanel.Children)
            {
                view.IsExpanded = false;
            }
        }

        private void ExpandAll_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ExpandAll();
        }

        private void ExpandAll()
        {
            foreach (HeuristicView view in _contentPanel.Children)
            {
                view.IsExpanded = true;
            }
        }

        #endregion

        #region Right click menu

        private void ResetRightClickMenu()
        {
            _addRefPatternFlyoutItem.Text = "This is an AddRef pattern";
            _addRefPatternFlyoutItem.IsEnabled = true;
            _releasePatternFlyoutItem.Text = "This is a Release pattern";
            _releasePatternFlyoutItem.IsEnabled = true;

            _singleAddRefFlyoutItem.Text = "This is a single AddRef";
            _singleAddRefFlyoutItem.IsEnabled = true;
            _singleReleaseFlyoutItem.Text = "This is a single Release";
            _singleReleaseFlyoutItem.IsEnabled = true;
        }

        private void DisableRightClickMenu()
        {
            _addRefPatternFlyoutItem.IsEnabled = false;
            _releasePatternFlyoutItem.IsEnabled = false;
            _singleAddRefFlyoutItem.IsEnabled = false;
            _singleReleaseFlyoutItem.IsEnabled = false;
        }

        internal void ShowRightClickMenu(StackTreeNodeView row, Point point)
        {
            _currentRow = row;
            RightClickMenu.ShowAt(row, point);
        }

        private void AddRefPattern_Click(object sender, RoutedEventArgs e)
        {
            bool createdNewHeuristic = false;
            if (_currentHeuristicView == null)
            {
                StartNewHeuristic();
                createdNewHeuristic = true;
            }

            _currentHeuristicView.Heuristic.Name = string.Format("{0} pattern{1}", _currentRow.Node.Function, _contentPanel.Children.Count);
            _currentHeuristicView.Heuristic.SetAddRef(_currentRow.Node.ModuleFunctionAndOffset);
            _currentHeuristicView.RedrawUI();

            if (createdNewHeuristic)
            {
                _releasePatternFlyoutItem.Text = "This is the matching Release pattern";
                _singleAddRefFlyoutItem.IsEnabled = false;
                _singleReleaseFlyoutItem.IsEnabled = false;
            }
            else if (!string.IsNullOrEmpty(_currentHeuristicView.Heuristic.ReleaseString))
            {
                CompleteCurrentHeuristic();
            }
        }

        private void ReleasePattern_Click(object sender, RoutedEventArgs e)
        {
            bool createdNewHeuristic = false;
            if (_currentHeuristicView == null)
            {
                StartNewHeuristic();
                createdNewHeuristic = true;
            }

            _currentHeuristicView.Heuristic.Name = string.Format("{0} pattern{1}", _currentRow.Node.Function, _contentPanel.Children.Count);
            _currentHeuristicView.Heuristic.SetRelease(_currentRow.Node.ModuleFunctionAndOffset);
            _currentHeuristicView.RedrawUI();

            if (createdNewHeuristic)
            {
                _addRefPatternFlyoutItem.Text = "This is the matching AddRef pattern";
                _singleAddRefFlyoutItem.IsEnabled = false;
                _singleReleaseFlyoutItem.IsEnabled = false;
            }
            else if (!string.IsNullOrEmpty(_currentHeuristicView.Heuristic.AddRefString))
            {
                CompleteCurrentHeuristic();
            }
        }

        private void StartNewHeuristic()
        {
            AddRefReleaseHeuristic currentHeuristic = new AddRefReleaseHeuristic();
            _currentHeuristicView = new HeuristicView(this, currentHeuristic);
            _contentPanel.Children.Add(_currentHeuristicView);
        }

        private void SingleAddRef_Click(object sender, RoutedEventArgs e)
        {
            bool createdNewHeuristic = false;
            if (_currentHeuristicView == null)
            {
                StartNewHeuristic();
                createdNewHeuristic = true;
            }

            _currentHeuristicView.Heuristic.Name = string.Format("{0} single line{1}", _currentRow.Node.Function, _contentPanel.Children.Count);
            _currentHeuristicView.Heuristic.SetAddRefLine(_currentRow.Node.LineNumber);
            _currentHeuristicView.RedrawUI();

            if (createdNewHeuristic)
            {
                _singleReleaseFlyoutItem.Text = "This is the matching single Release";
                _addRefPatternFlyoutItem.IsEnabled = false;
                _releasePatternFlyoutItem.IsEnabled = false;
            }
            else if (_currentHeuristicView.Heuristic.ReleaseLine > 0)
            {
                CompleteCurrentHeuristic();
            }
        }

        private void SingleRelease_Click(object sender, RoutedEventArgs e)
        {
            bool createdNewHeuristic = false;
            if (_currentHeuristicView == null)
            {
                StartNewHeuristic();
                createdNewHeuristic = true;
            }

            _currentHeuristicView.Heuristic.Name = string.Format("{0} single line{1}", _currentRow.Node.Function, _contentPanel.Children.Count);
            _currentHeuristicView.Heuristic.SetReleaseLine(_currentRow.Node.LineNumber);
            _currentHeuristicView.RedrawUI();

            if (createdNewHeuristic)
            {
                _singleAddRefFlyoutItem.Text = "This is the matching single AddRef";
                _addRefPatternFlyoutItem.IsEnabled = false;
                _releasePatternFlyoutItem.IsEnabled = false;
            }
            else if (_currentHeuristicView.Heuristic.AddRefLine > 0)
            {
                CompleteCurrentHeuristic();
            }
        }

        private void CompleteCurrentHeuristic()
        {
            CollapseAll();
            _currentHeuristicView.IsExpanded = true;

            _currentHeuristicView.RedrawUI();
            StackTreeView.ApplyHeuristic(_currentHeuristicView);

            _currentHeuristicView = null;
            ResetRightClickMenu();
        }

        #endregion

        #region Deleting

        public void DeleteHeuristic(HeuristicView heuristicView)
        {
            heuristicView.Undo();
            _contentPanel.Children.Remove(heuristicView);

            if (heuristicView == _currentHeuristicView)
            {
                _currentHeuristicView = null;
                ResetRightClickMenu();
            }

            StackTreeView.ExpandAll();
            StackTreeView.CollapseAnnotated();
            StackTreeView.UpdateUIState();
        }

        private async void ClearAll_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog()
            {
                XamlRoot = this.XamlRoot,
                Title = "Delete all heuristics",
                Content = "Delete all heuristics and reset the entire tree?",
                IsPrimaryButtonEnabled = true,
                IsSecondaryButtonEnabled = true,
                PrimaryButtonText = "Delete all",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                foreach (HeuristicView heuristicView in _contentPanel.Children)
                {
                    heuristicView.Undo();
                }

                ClearAll();
            }
        }

        public void ClearAll()
        {
            _contentPanel.Children.Clear();

            StackTreeView.ExpandAll();
            StackTreeView.UpdateUIState();
        }

        #endregion

        private async void Save_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FileSavePicker picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowHandle);
            picker.FileTypeChoices.Add("Text files", new List<string>() { ".txt" });
            picker.SuggestedFileName = FileName + "_heuristics.txt";

            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                using (StreamWriter sw = new StreamWriter(file.Path, false))
                {
                    foreach (HeuristicView heuristic in _contentPanel.Children)
                    {
                        heuristic.Heuristic.WriteToStream(sw);
                    }
                }
            }
        }

        private async void Load_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FileOpenPicker picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowHandle);
            picker.FileTypeFilter.Add(".txt");

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                List<AddRefReleaseHeuristic> heuristics = new List<AddRefReleaseHeuristic>();

                using (Stream stream = file.OpenStreamForReadAsync().Result)
                {
                    using (StreamReader sr = new StreamReader(stream))
                    {
                        string name = null;
                        string scope = null;
                        string addRef = null;
                        bool isLineMatch = false;
                        int addRefLine = 0;
                        int releaseLine = 0;
                        List<string> releases = new List<string>();

                        while (!sr.EndOfStream)
                        {
                            string line = sr.ReadLine().Trim();

                            if (line.StartsWith(AddRefReleaseHeuristic.CommentKeyword))
                            {
                                // Ignore - comment
                                continue;
                            }
                            else if (line.StartsWith(AddRefReleaseHeuristic.NameStartKeyword) && line.EndsWith(AddRefReleaseHeuristic.NameEndKeyword))
                            {
                                if (!string.IsNullOrEmpty(name))
                                {
                                    if (isLineMatch)
                                    {
                                        heuristics.Add(new AddRefReleaseHeuristic(name, addRefLine, releaseLine));
                                    }
                                    else if (!string.IsNullOrEmpty(addRef) && releases.Any())
                                    {
                                        heuristics.Add(new AddRefReleaseHeuristic(name, scope, addRef, releases));
                                    }
                                    name = null;
                                    scope = null;
                                    addRef = null;
                                    releases = new List<string>();
                                    isLineMatch = false;
                                }

                                name = line.Substring(1, line.Length - 2);
                                continue;
                            }
                            else if (line.StartsWith(AddRefReleaseHeuristic.ScopeKeyword))
                            {
                                scope = line.Substring(1).Trim();
                            }
                            else if (line.StartsWith(AddRefReleaseHeuristic.AddRefLineKeyword))
                            {
                                if (int.TryParse(line.Substring(AddRefReleaseHeuristic.AddRefLineKeyword.Length), out addRefLine))
                                {
                                    isLineMatch = true;
                                }
                            }
                            else if (line.StartsWith(AddRefReleaseHeuristic.AddRefKeyword))
                            {
                                addRef = line.Substring(1).Trim();
                            }
                            else if (line.StartsWith(AddRefReleaseHeuristic.ReleaseLineKeyword))
                            {
                                if (int.TryParse(line.Substring(AddRefReleaseHeuristic.ReleaseLineKeyword.Length), out releaseLine))
                                {
                                    isLineMatch = true;
                                }
                            }
                            else if (line.StartsWith(AddRefReleaseHeuristic.ReleaseKeyword))
                            {
                                releases.Add(line.Substring(1).Trim());
                            }
                        }

                        // At the end of the file, add the last heuristic. We won't have another name tag to add it.
                        if (!string.IsNullOrEmpty(name))
                        {
                            if (isLineMatch)
                            {
                                heuristics.Add(new AddRefReleaseHeuristic(name, addRefLine, releaseLine));
                            }
                            else if (!string.IsNullOrEmpty(addRef) && releases.Any())
                            {
                                heuristics.Add(new AddRefReleaseHeuristic(name, scope, addRef, releases));
                            }
                            name = null;
                            scope = null;
                            addRef = null;
                            releases = new List<string>();
                            isLineMatch = false;
                        }
                    }
                }

                foreach (AddRefReleaseHeuristic heuristic in heuristics)
                {
                    HeuristicView heuristicView = new HeuristicView(this, heuristic);
                    _contentPanel.Children.Add(heuristicView);

                    StackTreeView.ApplyHeuristic(heuristicView);
                }
            }
        }
    }
}
