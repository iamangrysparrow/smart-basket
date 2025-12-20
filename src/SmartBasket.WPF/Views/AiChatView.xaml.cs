using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SmartBasket.WPF.ViewModels;

namespace SmartBasket.WPF.Views;

public partial class AiChatView : UserControl
{
    public AiChatView()
    {
        InitializeComponent();

        // Auto-scroll when new messages are added
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AiChatViewModel viewModel)
        {
            viewModel.EnableCollectionSynchronization();

            // Subscribe to collection changes for auto-scroll
            if (viewModel.Messages is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged += (s, args) =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        MessagesScrollViewer.ScrollToEnd();
                    });
                };
            }

            // Subscribe to IsProcessing changes for auto-scroll when typing indicator appears
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiChatViewModel.IsProcessing))
        {
            Dispatcher.BeginInvoke(() =>
            {
                MessagesScrollViewer.ScrollToEnd();
            });
        }
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift+Enter = new line (allow default behavior for AcceptsReturn=True)
                return;
            }

            // Enter without Shift = send message
            if (DataContext is AiChatViewModel viewModel && viewModel.SendMessageCommand.CanExecute(null))
            {
                viewModel.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void PromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AiChatViewModel viewModel)
            return;

        var dialog = new PromptEditorDialog(viewModel.SystemPrompt)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            viewModel.SystemPrompt = dialog.PromptText;
            viewModel.ApplySystemPromptCommand.Execute(null);
        }
    }

    private void TogglePartExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is AssistantResponsePart part)
        {
            part.IsExpanded = !part.IsExpanded;
        }
    }

    private void CopyAnswerText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                // Ignore clipboard errors
            }
        }
    }

    private void CopyToolCallText_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is AssistantResponsePart part)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Tool: {part.ToolName}");
                if (!string.IsNullOrEmpty(part.ToolArgs))
                {
                    sb.AppendLine("Arguments:");
                    sb.AppendLine(part.ToolArgs);
                }
                if (!string.IsNullOrEmpty(part.ToolResult))
                {
                    sb.AppendLine("Result:");
                    sb.AppendLine(part.ToolResult);
                }
                Clipboard.SetText(sb.ToString());
            }
            catch
            {
                // Ignore clipboard errors
            }
        }
    }

    /// <summary>
    /// Пробрасывает MouseWheel из вложенных контролов в главный ScrollViewer.
    /// Это нужно чтобы прокрутка мышкой работала когда курсор над MarkdownScrollViewer
    /// или другими вложенными ScrollViewer-ами.
    /// </summary>
    private void OnChildPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Если вложенный ScrollViewer не может скроллить дальше, передаём событие родителю
        if (sender is ScrollViewer scrollViewer)
        {
            var atTop = scrollViewer.VerticalOffset <= 0 && e.Delta > 0;
            var atBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight && e.Delta < 0;

            if (atTop || atBottom || scrollViewer.ScrollableHeight <= 0)
            {
                // Пробрасываем событие в родительский ScrollViewer
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = MouseWheelEvent,
                    Source = sender
                };
                MessagesScrollViewer.RaiseEvent(eventArg);
            }
        }
        else
        {
            // Для MarkdownScrollViewer и других контролов просто пробрасываем всегда
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = sender
            };
            MessagesScrollViewer.RaiseEvent(eventArg);
        }
    }
}
