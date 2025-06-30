using Microsoft.Maui.Controls.Shapes;
using System.Windows.Input;

namespace MarketAssistant.Controls
{
    public class WatermarkView : ContentView
    {
        // 水印文本
        public static readonly BindableProperty TextProperty =
            BindableProperty.Create(nameof(Text), typeof(string), typeof(WatermarkView), "水印", propertyChanged: OnWatermarkPropertyChanged);

        // 水印透明度
        public static readonly BindableProperty OpacityValueProperty =
            BindableProperty.Create(nameof(OpacityValue), typeof(double), typeof(WatermarkView), 0.2, propertyChanged: OnWatermarkPropertyChanged);

        // 水印角度
        public static readonly BindableProperty AngleProperty =
            BindableProperty.Create(nameof(Angle), typeof(double), typeof(WatermarkView), -45.0, propertyChanged: OnWatermarkPropertyChanged);

        // 水印颜色
        public static readonly BindableProperty TextColorProperty =
            BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(WatermarkView), Colors.Gray, propertyChanged: OnWatermarkPropertyChanged);

        // 水印字体大小
        public static readonly BindableProperty FontSizeProperty =
            BindableProperty.Create(nameof(FontSize), typeof(double), typeof(WatermarkView), 24.0, propertyChanged: OnWatermarkPropertyChanged);

        // 水印重复次数（水平）
        public static readonly BindableProperty HorizontalRepeatCountProperty =
            BindableProperty.Create(nameof(HorizontalRepeatCount), typeof(int), typeof(WatermarkView), 3, propertyChanged: OnWatermarkPropertyChanged);

        // 水印重复次数（垂直）
        public static readonly BindableProperty VerticalRepeatCountProperty =
            BindableProperty.Create(nameof(VerticalRepeatCount), typeof(int), typeof(WatermarkView), 5, propertyChanged: OnWatermarkPropertyChanged);

        // 属性访问器
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public double OpacityValue
        {
            get => (double)GetValue(OpacityValueProperty);
            set => SetValue(OpacityValueProperty, value);
        }

        public double Angle
        {
            get => (double)GetValue(AngleProperty);
            set => SetValue(AngleProperty, value);
        }

        public Color TextColor
        {
            get => (Color)GetValue(TextColorProperty);
            set => SetValue(TextColorProperty, value);
        }

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public int HorizontalRepeatCount
        {
            get => (int)GetValue(HorizontalRepeatCountProperty);
            set => SetValue(HorizontalRepeatCountProperty, value);
        }

        public int VerticalRepeatCount
        {
            get => (int)GetValue(VerticalRepeatCountProperty);
            set => SetValue(VerticalRepeatCountProperty, value);
        }

        // 水印网格
        private Grid _watermarkGrid;

        public WatermarkView()
        {
            // 初始化控件
            InitializeWatermark();

            // 设置控件属性
            InputTransparent = true; // 允许点击穿透
            HorizontalOptions = LayoutOptions.Fill;
            VerticalOptions = LayoutOptions.Fill;
        }

        private void InitializeWatermark()
        {
            // 创建水印网格
            _watermarkGrid = new Grid
            {
                InputTransparent = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            // 更新水印内容
            UpdateWatermark();

            // 设置内容
            Content = _watermarkGrid;
        }

        private void UpdateWatermark()
        {
            // 清空现有水印
            _watermarkGrid.Clear();

            // 创建水印文本的网格布局
            for (int row = 0; row < VerticalRepeatCount; row++)
            {
                for (int col = 0; col < HorizontalRepeatCount; col++)
                {
                    // 创建水印标签
                    var label = new Label
                    {
                        Text = Text,
                        TextColor = TextColor,
                        FontSize = FontSize,
                        Opacity = OpacityValue,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        Rotation = Angle
                    };

                    // 添加到网格的指定位置
                    _watermarkGrid.Add(label);
                    Grid.SetRow(label, row);
                    Grid.SetColumn(label, col);
                }
            }

            // 定义行和列
            _watermarkGrid.RowDefinitions.Clear();
            _watermarkGrid.ColumnDefinitions.Clear();

            for (int i = 0; i < VerticalRepeatCount; i++)
            {
                _watermarkGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
            }

            for (int i = 0; i < HorizontalRepeatCount; i++)
            {
                _watermarkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            }
        }

        private static void OnWatermarkPropertyChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is WatermarkView watermarkView)
            {
                watermarkView.UpdateWatermark();
            }
        }
    }
}