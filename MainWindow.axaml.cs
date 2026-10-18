using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MojiTodo.Models;
using MojiTodo.Services;

namespace MojiTodo;

public partial class MainWindow : Window
{
    private readonly TodoStore _store = new();
    private FilterKind _filter = FilterKind.All;
    private readonly ObservableCollection<TodoRow> _rows = [];
    private bool _suppressCheck;

    public MainWindow()
    {
        InitializeComponent();
        var now = DateTime.Now;
        var weekdays = new[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
        TxtDate.Text = $"{now.Month}月{now.Day}日{weekdays[(int)now.DayOfWeek]}";
        TaskList.ItemsSource = _rows;
        _store.Load();
        RefreshList();
        UpdateFilterButtons();
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        _store.Add(TxtInput.Text ?? "");
        TxtInput.Text = "";
        TxtInput.Focus();
        RefreshList();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAdd(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnFilterAll(object? sender, RoutedEventArgs e) => SetFilter(FilterKind.All);
    private void OnFilterActive(object? sender, RoutedEventArgs e) => SetFilter(FilterKind.Active);
    private void OnFilterDone(object? sender, RoutedEventArgs e) => SetFilter(FilterKind.Done);

    private void SetFilter(FilterKind kind)
    {
        _filter = kind;
        UpdateFilterButtons();
        RefreshList();
    }

    private void UpdateFilterButtons()
    {
        StyleChip(BtnAll, _filter == FilterKind.All);
        StyleChip(BtnActive, _filter == FilterKind.Active);
        StyleChip(BtnDone, _filter == FilterKind.Done);
    }

    private static void StyleChip(Button btn, bool active)
    {
        btn.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
        btn.Background = active
            ? (IBrush?)Application.Current!.FindResource("ChipOnBrush")
            : (IBrush?)Application.Current!.FindResource("ChipOffBrush");
        btn.Foreground = active ? Brushes.White : (IBrush?)Application.Current!.FindResource("TitleBrush");
        btn.BorderBrush = active
            ? (IBrush?)Application.Current!.FindResource("AccentBrush")
            : (IBrush?)Application.Current!.FindResource("BorderBrush");
    }

    private void RefreshList()
    {
        _suppressCheck = true;
        _rows.Clear();
        foreach (var idx in _store.FilteredIndices(_filter))
        {
            var item = _store.Items[idx];
            _rows.Add(new TodoRow { StoreIndex = idx, Text = item.Text, Done = item.Done });
        }
        _suppressCheck = false;
        TxtStats.Text = _store.CountsLabel();
    }

    private TodoRow? SelectedRow() => TaskList.SelectedItem as TodoRow;

    private void OnTaskCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppressCheck) return;
        if (sender is not CheckBox cb || cb.DataContext is not TodoRow row) return;
        _store.SetDone(row.StoreIndex, cb.IsChecked == true);
        RefreshList();
    }

    private async void OnEditTask(object? sender, TappedEventArgs e)
    {
        var row = SelectedRow();
        if (row is null) return;
        var tb = new TextBox { Text = row.Text, Margin = new Thickness(0, 0, 0, 8) };
        var ok = new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(tb);
        panel.Children.Add(ok);
        var dlg = new Window
        {
            Title = "编辑待办",
            Width = 400,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
            Background = (IBrush)Application.Current!.FindResource("PageBgBrush")!
        };
        var saved = false;
        ok.Click += (_, _) => { saved = true; dlg.Close(); };
        await dlg.ShowDialog(this);
        if (!saved || string.IsNullOrWhiteSpace(tb.Text)) return;
        _store.UpdateText(row.StoreIndex, tb.Text);
        RefreshList();
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        var row = SelectedRow();
        if (row is null)
        {
            await ShowInfoAsync("请先选中一条待办");
            return;
        }
        if (!await ConfirmAsync("删除这条待办？")) return;
        _store.RemoveAt(row.StoreIndex);
        RefreshList();
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e) => MoveSelected(-1);
    private void OnMoveDown(object? sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        var row = SelectedRow();
        if (row is null) return;
        var newIdx = row.StoreIndex + delta;
        if (newIdx < 0 || newIdx >= _store.Items.Count) return;
        _store.MoveItem(row.StoreIndex, newIdx);
        RefreshList();
        foreach (var r in _rows)
        {
            if (r.StoreIndex == newIdx)
            {
                TaskList.SelectedItem = r;
                break;
            }
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var sp = StorageProvider;
        if (sp is null) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "墨迹清单-export.md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md", "*.txt"] }]
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(_store.ExportMarkdown());
        await ShowInfoAsync("已导出");
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var sp = StorageProvider;
        if (sp is null) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("文档") { Patterns = ["*.md", "*.txt", "*.json"] }
            ]
        });
        var pick = files.FirstOrDefault();
        if (pick is null) return;
        var path = pick.Path.LocalPath;
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            _store.ReplaceFromJsonFile(path);
        else
            _store.ImportMarkdown(await File.ReadAllTextAsync(path));
        RefreshList();
        await ShowInfoAsync("导入完成");
    }

    private async void OnPaste(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is null) return;
        var text = await top.Clipboard.GetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            await ShowInfoAsync("剪贴板为空");
            return;
        }
        _store.ImportMarkdown(text);
        RefreshList();
        await ShowInfoAsync("已从剪贴板导入");
    }

    private async Task ShowInfoAsync(string message)
    {
        var btn = new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var dlg = new Window
        {
            Title = "墨迹清单",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, btn }
            }
        };
        btn.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var result = false;
        var yes = new Button { Content = "是", Margin = new Thickness(0, 0, 8, 0) };
        var no = new Button { Content = "否" };
        var row = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { yes, no }
        };
        var dlg = new Window
        {
            Title = "墨迹清单",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children = { new TextBlock { Text = message }, row }
            }
        };
        yes.Click += (_, _) => { result = true; dlg.Close(); };
        no.Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
        return result;
    }
}

public sealed class TodoRow : INotifyPropertyChanged
{
    public int StoreIndex { get; set; }
    public string Text { get; set; } = "";

    private bool _done;
    public bool Done
    {
        get => _done;
        set
        {
            if (_done == value) return;
            _done = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
