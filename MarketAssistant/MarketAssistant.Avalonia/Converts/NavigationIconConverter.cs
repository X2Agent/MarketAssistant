using System.Globalization;
using Avalonia.Data.Converters;
using MarketAssistant.Avalonia.ViewModels;
using Avalonia.Platform;
using Svg.Skia;
using SkiaSharp;
using Avalonia.Media.Imaging;
using Avalonia;

namespace MarketAssistant.Avalonia.Converts
{
    /// <summary>
    /// 导航图标转换器，根据选中状态显示不同的图标
    /// </summary>
    public class NavigationIconConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count != 2 || 
                values[0] is not NavigationItemViewModel navigationItem ||
                values[1] is not bool isSelected)
            {
                return null;
            }

            var iconPath = isSelected ? navigationItem.SelectedIconPath : navigationItem.IconPath;
            
            try
            {
                // 使用Svg.Skia加载SVG图标并转换为位图
                var uri = new Uri(iconPath);
                using var stream = AssetLoader.Open(uri);
                var svg = new SKSvg();
                svg.Load(stream);
                
                if (svg.Picture != null)
                {
                    var bounds = svg.Picture.CullRect;
                    var bitmap = new SKBitmap((int)bounds.Width, (int)bounds.Height);
                    using var canvas = new SKCanvas(bitmap);
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawPicture(svg.Picture);
                    
                    // 转换为Avalonia位图
                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var memoryStream = new MemoryStream(data.ToArray());
                    return new Bitmap(memoryStream);
                }
            }
            catch (Exception)
            {
                // 如果SVG加载失败，返回null
            }

            return null;
        }
    }
}
