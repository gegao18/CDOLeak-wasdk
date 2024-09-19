using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace CDOLeak_wasdk
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
        }

        private async void Load_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FileOpenPicker picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".txt");

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                Stream stream = file.OpenStreamForReadAsync().Result;
                StreamReader sr = new StreamReader(stream);

                List<RefCountStack> refCountStacks = new List<RefCountStack>();
                RefCountStack refCountStack = null;
                int frameNumber = 0;
                bool canAdd = false;
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();

                    if (line.StartsWith(RefCountStack.RefCountIs))
                    {
                        int refCount = Convert.ToInt32(line.Replace(RefCountStack.RefCountIs, string.Empty));

                        // If the stack isn't identified yet and it's the first stack in the trace, then mark it as an AddRef stack.
                        // We explicitly look for some keywords when identifying stacks. The first stack is likely the ctor for the
                        // object, so it's going to be an AddRef. Depending on where in the ctor the stack gets dumped, it'll likely
                        // not have any of the keywords we're looking for.
                        if (refCountStacks.Count == 1)
                        {
                            refCountStack.DefaultToAddRefStack();
                        }

                        refCountStack = new RefCountStack(refCount);
                        refCountStacks.Add(refCountStack);
                        frameNumber = 0;
                        continue;
                    }
                    else if (refCountStack != null  // Filters out "Time Travel Position: " lines in debugger output before ref count stacks start
                        && line.StartsWith(RefCountStack.TimeTravelPosition))
                    {
                        string position = line.Substring(RefCountStack.TimeTravelPosition.Length);
                        refCountStack.Position = position;
                    }
                    else if (line.Equals(RefCountStack.StackBegin))
                    {
                        canAdd = true;
                        continue;
                    }
                    else if (line.Equals(RefCountStack.StackEnd))
                    {
                        canAdd = false;
                        continue;
                    }

                    if (canAdd)
                    {
                        StackLine stackLine = StackLine.Create(line, frameNumber);
                        if (stackLine != null)
                        {
                            bool added = refCountStack.AddLine(stackLine);
                            frameNumber++;
                            if (!added)
                            {
                                Debug.Fail("Stack frame couldn't be added to the stack.");
                            }
                        }
                    }
                }
                sr.Close();
                stream.Close();

                StackTreeNode root = StackTreeNode.StitchStacks(refCountStacks);
                if (root.Children.Any())
                {
                    _stackTreeView.SetStackTree(root, _statusText, _virtualizationText, _logPanel);
                    _stackTreeView.ShowUnannotated();
                    _showingUnannotated = true;
                }
            }
        }

        private async void Save_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FileSavePicker picker = new FileSavePicker();
            picker.FileTypeChoices.Add("Text file", new List<string>() { ".txt" });

            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                Stream stream = file.OpenStreamForWriteAsync().Result;
                stream.SetLength(0);
                StreamWriter sw = new StreamWriter(stream);

                _stackTreeView.Root.WriteToStream(sw);

                sw.Close();
                stream.Close();
            }
        }

        private void ExpandAll_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _stackTreeView.ExpandAll();
        }

        private void CollapseAnnotated_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _stackTreeView.CollapseAnnotated();
        }

        private void ClearAnnotations_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _stackTreeView.ClearAnnotations();
        }

        private async void Heuristics_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FileOpenPicker picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
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

                _stackTreeView.ApplyHeuristics(heuristics);
            }
        }

        private void SearchText_GotFocus(object sender, RoutedEventArgs e)
        {
            SearchText.SelectAll();
        }

        private void SearchText_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                bool searchCollapsed = SearchCollapsed.IsChecked.Value;
                string searchString = SearchText.Text;
                _stackTreeView.FindNext(searchString, searchCollapsed);
            }
        }

        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.F
                && Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
            {
                SearchText.Focus(FocusState.Keyboard);
            }
        }

        bool _showingUnannotated;
        private void StatusText_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_showingUnannotated)
            {
                _stackTreeView.ResetFilter();
            }
            else
            {
                _stackTreeView.ShowUnannotated();
            }
            _showingUnannotated = !_showingUnannotated;
        }

        private void ClearHighlights_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _stackTreeView.ClearHighlights();
        }
    }
}
