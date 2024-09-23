﻿using Microsoft.UI.Xaml;
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

        private List<AddRefReleaseHeuristic> _heuristics = new List<AddRefReleaseHeuristic>();

        private StackTreeNodeView _currentRow; // The right-clicked row in the StackTreeView
        private AddRefReleaseHeuristic _currentHeuristic;   // Currently being built by the UI

        internal StackTreeView StackTreeView { get; set; }

        internal nint WindowHandle { get; set; }

        private MenuFlyout RightClickMenu { get; set; }

        public HeuristicsView()
        {
            _stackPanel = new StackPanel() { Margin = new Thickness(2, 0, 0, 0), };
            _scrollViewer = new ScrollViewer()
            {
                Content = _stackPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            };

            _header = new TextBlock()
            {
                Text = "Heuristics",
                FontSize = 19,
            };

            _buttons = new StackPanel() { Orientation = Orientation.Horizontal };
            Button load = new Button() { Content = "Load", };
            load.Tapped += Load_Tapped;
            _buttons.Children.Add(load);
            Button save = new Button() { Content = "Save", };
            save.Tapped += Save_Tapped;
            _buttons.Children.Add(save);

            RightClickMenu = new MenuFlyout();
            MenuFlyoutItem addRefThis = new MenuFlyoutItem() { Text = "AddRef this", };
            addRefThis.Click += AddRefThis_Click;
            RightClickMenu.Items.Add(addRefThis);
            MenuFlyoutItem releaseThis = new MenuFlyoutItem() { Text = "Release this", };
            releaseThis.Click += ReleaseThis_Click;
            RightClickMenu.Items.Add(releaseThis);

            ResetUI();
            Content = _scrollViewer;
        }

        internal void ShowRightClickMenu(StackTreeNodeView row, Point point)
        {
            _currentRow = row;
            RightClickMenu.ShowAt(row, point);
        }

        private void ReleaseThis_Click(object sender, RoutedEventArgs e)
        {
            if (_currentHeuristic != null)
            {
                _currentHeuristic.SetRelease(_currentRow.Node.ModuleFunctionAndOffset);

                _heuristics.Add(_currentHeuristic);
                HeuristicView newView = new HeuristicView(_currentHeuristic);
                _stackPanel.Children.Add(newView);
                StackTreeView.ApplyHeuristic(newView);
                _currentHeuristic = null;
            }
        }

        private void AddRefThis_Click(object sender, RoutedEventArgs e)
        {
            if (_currentHeuristic == null)
            {
                _currentHeuristic = new AddRefReleaseHeuristic(_currentRow.Node.Function);
                _currentHeuristic.SetAddRef(_currentRow.Node.ModuleFunctionAndOffset);
            }
        }

        private async void Save_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FileSavePicker picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowHandle);
            picker.FileTypeChoices.Add("Text files", new List<string>(){ ".txt" });

            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null)
            {
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
                    HeuristicView heuristicView = new HeuristicView(heuristic);
                    _stackPanel.Children.Add(heuristicView);
                    
                    StackTreeView.ApplyHeuristic(heuristicView);
                }
            }
        }

        internal void ResetUI()
        {
            _stackPanel.Children.Clear();
            _stackPanel.Children.Add(_header);
            _stackPanel.Children.Add(_buttons);
        }
    }
}
