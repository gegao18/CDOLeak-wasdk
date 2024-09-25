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
using Microsoft.UI.Input;

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
            _heuristicsView.WindowHandle = WindowNative.GetWindowHandle(this);
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

                        refCountStack = new RefCountStack(refCount);

                        // If the stack isn't identified yet and it's the first stack in the trace, then mark it as an AddRef stack.
                        // We explicitly look for some keywords when identifying stacks. The first stack is likely the ctor for the
                        // object, so it's going to be an AddRef. Depending on where in the ctor the stack gets dumped, it'll likely
                        // not have any of the keywords we're looking for.
                        if (refCountStacks.Count == 0)
                        {
                            refCountStack.DefaultToAddRefStack();
                        }

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
                        refCountStack = null;   // So the final "Time Travel Position:" doesn't stomp over the final stack
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
                    _stackTreeView.SetStackTree(root, _statusText, _virtualizationText, _heuristicsView);
                    _stackTreeView.ShowUnannotated();
                    _showingUnannotated = true;
                    _heuristicsView.FileName = file.DisplayName;
                    _heuristicsView.ClearAll();
                }
            }
        }

        private async void Save_Tapped(object sender, TappedRoutedEventArgs e)
        {
            FileSavePicker picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
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
                && InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
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
