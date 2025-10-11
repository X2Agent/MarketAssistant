# 如何为 macOS 创建 ICNS 图标

本文档说明如何从 PNG 图片创建 macOS `.icns` 图标文件。

## 前提条件

- 一张高质量的 PNG 图片（建议至少 1024x1024 像素）
- macOS 系统（使用内置工具）或 Linux 系统（使用 ImageMagick）

---

## 方法 1: 使用 macOS 内置工具（推荐）

### 步骤 1: 准备源图片

确保你有一张至少 1024x1024 像素的 PNG 图片，命名为 `logo.png`。

### 步骤 2: 创建 iconset 目录

```bash
mkdir MarketAssistant.iconset
```

### 步骤 3: 生成各种尺寸的图标

使用 `sips` 命令（macOS 内置）：

```bash
# 16x16 和 @2x
sips -z 16 16 logo.png --out MarketAssistant.iconset/icon_16x16.png
sips -z 32 32 logo.png --out MarketAssistant.iconset/icon_16x16@2x.png

# 32x32 和 @2x
sips -z 32 32 logo.png --out MarketAssistant.iconset/icon_32x32.png
sips -z 64 64 logo.png --out MarketAssistant.iconset/icon_32x32@2x.png

# 128x128 和 @2x
sips -z 128 128 logo.png --out MarketAssistant.iconset/icon_128x128.png
sips -z 256 256 logo.png --out MarketAssistant.iconset/icon_128x128@2x.png

# 256x256 和 @2x
sips -z 256 256 logo.png --out MarketAssistant.iconset/icon_256x256.png
sips -z 512 512 logo.png --out MarketAssistant.iconset/icon_256x256@2x.png

# 512x512 和 @2x
sips -z 512 512 logo.png --out MarketAssistant.iconset/icon_512x512.png
sips -z 1024 1024 logo.png --out MarketAssistant.iconset/icon_512x512@2x.png
```

### 步骤 4: 转换为 ICNS

```bash
iconutil -c icns MarketAssistant.iconset -o MarketAssistant.icns
```

### 步骤 5: 清理临时文件

```bash
rm -rf MarketAssistant.iconset
```

### 一键脚本

创建一个脚本 `create-icon.sh`：

```bash
#!/bin/bash
set -e

SOURCE_PNG="$1"
OUTPUT_NAME="${2:-MarketAssistant}"

if [ ! -f "$SOURCE_PNG" ]; then
    echo "Usage: $0 <source.png> [output-name]"
    exit 1
fi

ICONSET="${OUTPUT_NAME}.iconset"

echo "Creating iconset directory..."
mkdir -p "$ICONSET"

echo "Generating icon sizes..."
sips -z 16 16     "$SOURCE_PNG" --out "$ICONSET/icon_16x16.png"
sips -z 32 32     "$SOURCE_PNG" --out "$ICONSET/icon_16x16@2x.png"
sips -z 32 32     "$SOURCE_PNG" --out "$ICONSET/icon_32x32.png"
sips -z 64 64     "$SOURCE_PNG" --out "$ICONSET/icon_32x32@2x.png"
sips -z 128 128   "$SOURCE_PNG" --out "$ICONSET/icon_128x128.png"
sips -z 256 256   "$SOURCE_PNG" --out "$ICONSET/icon_128x128@2x.png"
sips -z 256 256   "$SOURCE_PNG" --out "$ICONSET/icon_256x256.png"
sips -z 512 512   "$SOURCE_PNG" --out "$ICONSET/icon_256x256@2x.png"
sips -z 512 512   "$SOURCE_PNG" --out "$ICONSET/icon_512x512.png"
sips -z 1024 1024 "$SOURCE_PNG" --out "$ICONSET/icon_512x512@2x.png"

echo "Converting to ICNS..."
iconutil -c icns "$ICONSET" -o "${OUTPUT_NAME}.icns"

echo "Cleaning up..."
rm -rf "$ICONSET"

echo "✓ Created ${OUTPUT_NAME}.icns"
```

使用方法：

```bash
chmod +x create-icon.sh
./create-icon.sh logo.png MarketAssistant
```

---

## 方法 2: 使用 ImageMagick（跨平台）

### 安装 ImageMagick

```bash
# macOS
brew install imagemagick

# Ubuntu/Debian
sudo apt-get install imagemagick

# Fedora/RHEL
sudo dnf install ImageMagick
```

### 创建图标

```bash
# 方法 A: 简单转换
convert logo.png -resize 1024x1024 MarketAssistant.icns

# 方法 B: 创建多尺寸
mkdir -p iconset.iconset
for size in 16 32 128 256 512; do
    convert logo.png -resize ${size}x${size} iconset.iconset/icon_${size}x${size}.png
    convert logo.png -resize $((size*2))x$((size*2)) iconset.iconset/icon_${size}x${size}@2x.png
done
```

**注意**: 在 Linux 上创建的 ICNS 可能与 macOS 原生工具创建的略有不同，建议在 macOS 上最终生成。

---

## 方法 3: 使用在线工具

如果没有 macOS 系统，可以使用在线工具：

1. **iConvert Icons** - https://iconverticons.com/online/
2. **CloudConvert** - https://cloudconvert.com/png-to-icns
3. **AnyConv** - https://anyconv.com/png-to-icns-converter/

步骤：
1. 上传 PNG 图片
2. 选择输出格式为 ICNS
3. 下载生成的文件

---

## 图标设计建议

### 尺寸和质量

- **源图片**: 至少 1024x1024 像素，PNG 格式
- **透明背景**: 建议使用透明背景，避免白色方框
- **简洁设计**: 图标应该在小尺寸下也清晰可辨

### 设计指南

macOS 图标设计遵循 [Apple Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines/app-icons):

1. **使用简单图形**: 避免过多细节
2. **圆角设计**: macOS 图标通常有圆角
3. **阴影和光泽**: 可以添加轻微的 3D 效果
4. **一致性**: 与其他 macOS 应用保持视觉一致

### 工具推荐

- **Sketch** - macOS 专业设计工具
- **Figma** - 跨平台在线设计工具
- **Affinity Designer** - 专业图形设计
- **GIMP** - 免费开源图像编辑器

---

## 验证图标

### 预览图标

```bash
# macOS
open MarketAssistant.icns

# 使用 Quick Look
qlmanage -p MarketAssistant.icns
```

### 检查图标内容

```bash
# 查看包含的图像尺寸
iconutil -c iconset MarketAssistant.icns -o temp.iconset
ls -lh temp.iconset/
rm -rf temp.iconset
```

### 在应用中测试

将图标复制到 `Resources` 目录并构建应用：

```bash
cp MarketAssistant.icns Release/macOS/MarketAssistant.app/Contents/Resources/
open Release/macOS/MarketAssistant.app
```

---

## 故障排除

### 问题: iconutil 命令找不到

确保安装了 Xcode Command Line Tools:

```bash
xcode-select --install
```

### 问题: 图标模糊或失真

- 确保源图片足够大（推荐 1024x1024）
- 使用矢量图形软件（如 Sketch、Illustrator）创建
- 在缩小时使用高质量的重采样算法

### 问题: 图标边缘有白色边框

- 确保源图片使用透明背景
- 检查 PNG 是否保存为 RGBA 模式

### 问题: 图标在 Finder 中不显示

- 清除图标缓存:
  ```bash
  sudo rm -rf /Library/Caches/com.apple.iconservices.store
  sudo find /private/var/folders/ -name com.apple.iconservices -exec rm -rf {} \;
  killall Finder
  ```

---

## 自动化集成

可以将图标生成集成到构建脚本中：

```bash
# 在 build-app-bundle.sh 中
if [ -f "$PROJECT_ROOT/src/Assets/logo.png" ]; then
    if [ ! -f "$PROJECT_ROOT/src/Assets/MarketAssistant.icns" ]; then
        echo "Converting PNG to ICNS..."
        ./scripts/macos/create-icon.sh "$PROJECT_ROOT/src/Assets/logo.png" "MarketAssistant"
        mv MarketAssistant.icns "$PROJECT_ROOT/src/Assets/"
    fi
fi
```

---

## 示例：从头创建

```bash
# 1. 准备源图片
# 将 1024x1024 的 logo.png 放在项目根目录

# 2. 创建图标
cd scripts/macos
./create-icon.sh ../../src/Assets/logo.png MarketAssistant

# 3. 移动到 Assets 目录
mv MarketAssistant.icns ../../src/Assets/

# 4. 验证
open ../../src/Assets/MarketAssistant.icns

# 5. 构建应用（图标会自动包含）
cd ../..
./scripts/macos/build-app-bundle.sh
```

---

## 参考资料

- [Apple Icon Guidelines](https://developer.apple.com/design/human-interface-guidelines/app-icons)
- [iconutil man page](https://ss64.com/osx/iconutil.html)
- [sips man page](https://ss64.com/osx/sips.html)
- [Creating Icons on Linux](https://www.omgubuntu.co.uk/2021/01/create-icns-icons-on-linux)

---

**提示**: 将生成的 `.icns` 文件放在 `src/Assets/` 目录中，构建脚本会自动将其包含到应用程序 bundle 中。

