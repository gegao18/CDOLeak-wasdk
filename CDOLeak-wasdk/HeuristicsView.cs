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
        private StackPanel _stackPanel;
        private TextBlock _header;
        private StackPanel _buttons;

        private List<HeuristicView> _heuristics = new List<HeuristicView>();

        private StackTreeNodeView _currentRow; // The right-clicked row in the StackTreeView
        private HeuristicView _currentHeuristicView;    // Currently being built by the UI

        internal StackTreeView StackTreeView { get; set; }

        internal nint WindowHandle { get; set; }

        private MenuFlyout RightClickMenu { get; set; }
        private MenuFlyoutItem _addRefFlyoutItem;
        private MenuFlyoutItem _releaseFlyoutItem;

        public string FileName { get; set; }

        public HeuristicsView()
        {
            _stackPanel = new StackPanel() { Margin = new Thickness(2, 0, 0, 0), };
            _scrollViewer = new ScrollViewer()
            {
                Content = _stackPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            };

            _stackPanel.Children.Add(new TextBlock()
            {
                Text = "Heuristics",
                FontSize = 19,
            });

            _stackPanel.Children.Add(new TextBlock()
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
            _stackPanel.Children.Add(_buttons);

            RightClickMenu = new MenuFlyout();
            _addRefFlyoutItem = new MenuFlyoutItem() { Text = "This is an AddRef", };
            _addRefFlyoutItem.Click += AddRefThis_Click;
            RightClickMenu.Items.Add(_addRefFlyoutItem);
            _releaseFlyoutItem = new MenuFlyoutItem() { Text = "This is a Release", };
            _releaseFlyoutItem.Click += ReleaseThis_Click;
            RightClickMenu.Items.Add(_releaseFlyoutItem);

            Content = _scrollViewer;
        }

        #region Expand/collapse

        private void CollapseAll_Tapped(object sender, TappedRoutedEventArgs e)
        {
            CollapseAll();
        }

        private void CollapseAll()
        {
            foreach (HeuristicView view in _heuristics)
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
            foreach (HeuristicView view in _heuristics)
            {
                view.IsExpanded = true;
            }
        }

        #endregion

        #region Right click menu

        internal void ResetRightClickMenu()
        {
            _addRefFlyoutItem.Text = "This is an AddRef";
            _addRefFlyoutItem.IsEnabled = true;
            _releaseFlyoutItem.Text = "This is a Release";
            _releaseFlyoutItem.IsEnabled = true;
        }

        internal void ShowRightClickMenu(StackTreeNodeView row, Point point)
        {
            _currentRow = row;
            RightClickMenu.ShowAt(row, point);
        }

        private void ReleaseThis_Click(object sender, RoutedEventArgs e)
        {
            if (_currentHeuristicView == null)
            {
                StartNewHeuristic();
                _currentHeuristicView.Heuristic.SetRelease(_currentRow.Node.ModuleFunctionAndOffset);
                _currentHeuristicView.RedrawUI();

                _addRefFlyoutItem.Text = "This is the matching AddRef";
                _releaseFlyoutItem.IsEnabled = false;
            }
            else if (!string.IsNullOrEmpty(_currentHeuristicView.Heuristic.AddRefString))
            {
                _currentHeuristicView.Heuristic.SetRelease(_currentRow.Node.ModuleFunctionAndOffset);
                CompleteCurrentHeuristic();
            }
        }

        private void AddRefThis_Click(object sender, RoutedEventArgs e)
        {
            if (_currentHeuristicView == null)
            {
                StartNewHeuristic();
                _currentHeuristicView.Heuristic.SetAddRef(_currentRow.Node.ModuleFunctionAndOffset);
                _currentHeuristicView.RedrawUI();

                _addRefFlyoutItem.IsEnabled = false;
                _releaseFlyoutItem.Text = "This is the matching Release";
            }
            else if (!string.IsNullOrEmpty(_currentHeuristicView.Heuristic.ReleaseString))
            {
                _currentHeuristicView.Heuristic.SetAddRef(_currentRow.Node.ModuleFunctionAndOffset);
                CompleteCurrentHeuristic();
            }
        }

        private void StartNewHeuristic()
        {
            AddRefReleaseHeuristic currentHeuristic = new AddRefReleaseHeuristic(_currentRow.Node.Function);
            _currentHeuristicView = new HeuristicView(this, currentHeuristic);
            _heuristics.Add(_currentHeuristicView);
            _stackPanel.Children.Add(_currentHeuristicView);
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

        private async void Save_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FileSavePicker picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowHandle);
            picker.FileTypeChoices.Add("Text files", new List<string>() { ".txt" });
            picker.SuggestedFileName = FileName + "_heuristics.txt";

            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                using (Stream stream = file.OpenStreamForWriteAsync().Result)
                {
                    using (StreamWriter sw = new StreamWriter(stream))
                    {
                        foreach (HeuristicView heuristic in _heuristics)
                        {
                            heuristic.Heuristic.WriteToStream(sw);
                        }
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
                                if (!string.IsNullOrEmpty(name)
                                    // scope can be null
                                    && !string.IsNullOrEmpty(addRef)
                                    && releases.Any())
                                {
                                    heuristics.Add(new AddRefReleaseHeuristic(name, scope, addRef, releases));
                                    name = null;
                                    scope = null;
                                    addRef = null;
                                    releases = new List<string>();
                                }

                                name = line.Substring(1, line.Length - 2);
                                continue;
                            }
                            else if (line.StartsWith(AddRefReleaseHeuristic.ScopeKeyword))
                            {
                                scope = line.Substring(1).Trim();
                            }
                            else if (line.StartsWith(AddRefReleaseHeuristic.AddRefKeyword))
                            {
                                addRef = line.Substring(1).Trim();
                            }
                            else if (line.StartsWith(AddRefReleaseHeuristic.ReleaseKeyword))
                            {
                                releases.Add(line.Substring(1).Trim());
                            }
                        }

                        // At the end of the file, add the last heuristic. We won't have another name tag for this add.
                        if (!string.IsNullOrEmpty(name)
                            // scope can be null
                            && !string.IsNullOrEmpty(addRef)
                            && releases.Any())
                        {
                            heuristics.Add(new AddRefReleaseHeuristic(name, scope, addRef, releases));
                            name = null;
                            scope = null;
                            addRef = null;
                            releases = new List<string>();
                        }
                    }
                }

                foreach (AddRefReleaseHeuristic heuristic in heuristics)
                {
                    HeuristicView heuristicView = new HeuristicView(this, heuristic);
                    _stackPanel.Children.Add(heuristicView);

                    StackTreeView.ApplyHeuristic(heuristicView);
                }
            }
        }

        public void DeleteHeuristic(HeuristicView heuristic)
        {
            heuristic.Undo();
            _stackPanel.Children.Remove(heuristic);

            if (heuristic == _currentHeuristicView)
            {
                _currentHeuristicView = null;
                ResetRightClickMenu();
            }

            StackTreeView.ExpandAll();
            StackTreeView.CollapseAnnotated();
            StackTreeView.UpdateUIState();
        }
    }
}
