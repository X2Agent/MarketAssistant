using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 激活行为：让自绘的可点卡片同时响应「鼠标 / 触摸点击」与「键盘 Enter / Space」，并执行同一个命令。
///
/// <para>
/// 卡片是 Border / <see cref="AssetCardView"/> 而不是 Button，没有内置的键盘激活。
/// 页面不要再各写一对 Tapped + KeyDown 处理器：容易只写鼠标那一半，
/// 也无法保证各页的 Enter / Space 约定一致。
/// </para>
///
/// <para>
/// 被激活的元素需自己声明 <c>Focusable="True"</c> 才能拿到键盘焦点；
/// 激活后事件标记为已处理，不再向外层冒泡。命令不可执行时（<see cref="ICommand.CanExecute"/>）不触发。
/// </para>
/// </summary>
public static class Activation
{
    /// <summary>激活时执行的命令；设为 null 即退订。</summary>
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<InputElement, ICommand?>("Command", typeof(Activation));

    /// <summary>传给命令的参数，通常绑定到卡片自己的数据项。</summary>
    public static readonly AttachedProperty<object?> CommandParameterProperty =
        AvaloniaProperty.RegisterAttached<InputElement, object?>("CommandParameter", typeof(Activation));

    static Activation()
    {
        CommandProperty.Changed.AddClassHandler<InputElement>((element, change) =>
        {
            // 先退订再按需订阅：换命令不会重复挂处理器，清空命令则彻底摘除
            element.Tapped -= OnTapped;
            element.KeyDown -= OnKeyDown;

            if (change.NewValue is null)
            {
                return;
            }

            element.Tapped += OnTapped;
            element.KeyDown += OnKeyDown;
        });
    }

    public static ICommand? GetCommand(InputElement element) => element.GetValue(CommandProperty);

    public static void SetCommand(InputElement element, ICommand? value) => element.SetValue(CommandProperty, value);

    public static object? GetCommandParameter(InputElement element) => element.GetValue(CommandParameterProperty);

    public static void SetCommandParameter(InputElement element, object? value) =>
        element.SetValue(CommandParameterProperty, value);

    private static void OnTapped(object? sender, TappedEventArgs e) => Execute((InputElement)sender!, e);

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            Execute((InputElement)sender!, e);
        }
    }

    private static void Execute(InputElement element, RoutedEventArgs e)
    {
        var command = GetCommand(element)!;
        var parameter = GetCommandParameter(element);
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }

        e.Handled = true;
    }
}
