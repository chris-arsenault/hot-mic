using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HotMic.App.ViewModels;

namespace HotMic.App.Controls;

public partial class ChannelStripControl : UserControl
{
    public ChannelStripControl()
    {
        InitializeComponent();
    }

    private void OnPluginListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (sender is not ListBox listBox)
        {
            return;
        }

        var item = GetListBoxItem(listBox, e.GetPosition(listBox));
        if (item?.DataContext is PluginViewModel plugin)
        {
            DragDrop.DoDragDrop(listBox, plugin, DragDropEffects.Move);
        }
    }

    private void OnPluginListDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (e.Data.GetData(typeof(PluginViewModel)) is not PluginViewModel source)
        {
            return;
        }

        var targetItem = GetListBoxItem(listBox, e.GetPosition(listBox));
        if (targetItem?.DataContext is not PluginViewModel target)
        {
            return;
        }

        if (DataContext is ChannelStripViewModel viewModel)
        {
            int from = viewModel.PluginSlots.IndexOf(source);
            int to = viewModel.PluginSlots.IndexOf(target);
            if (from != to && from >= 0 && to >= 0)
            {
                viewModel.MovePlugin(from, to);
            }
        }
    }

    private static ListBoxItem? GetListBoxItem(ItemsControl listBox, Point position)
    {
        var element = listBox.InputHitTest(position) as DependencyObject;
        while (element is not null && element is not ListBoxItem)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        return element as ListBoxItem;
    }
}
